// main.js — Bootstrap for .NET WASM runtime + game module implementations
import { dotnet } from './_framework/dotnet.js';

// ────────────────────────────────────────────────────────────────
// Auto-update: fetch version.json (written at CI publish time)
// with no-cache so the browser always hits the network. If the
// build hash changed since the last visit, flush all browser
// caches and reload — completely transparent to the user.
// version.json is absent in local dev builds, so errors are silently ignored.
// ────────────────────────────────────────────────────────────────
async function checkForUpdate() {
    try {
        const res = await fetch('./version.json', { cache: 'no-cache' });
        if (!res.ok) return;
        const { hash } = await res.json();
        const stored = localStorage.getItem('seg_buildHash');
        if (stored && stored !== hash) {
            // New version detected — flush all caches and force a fresh load.
            // Store the new hash first to avoid an infinite reload loop if
            // something goes wrong after the page comes back.
            localStorage.setItem('seg_buildHash', hash);
            if ('caches' in window) {
                const keys = await caches.keys();
                await Promise.all(keys.map(k => caches.delete(k)));
            }
            location.reload();
            return;
        }
        localStorage.setItem('seg_buildHash', hash);
    } catch {
        // version.json absent (local dev build) or network error — proceed normally.
    }
}
await checkForUpdate();

// ────────────────────────────────────────────────────────────────
// Canvas state
// ────────────────────────────────────────────────────────────────
const gameCanvas = document.getElementById('gameCanvas');
const ctx = gameCanvas.getContext('2d');
let canvasWidth = 0;
let canvasHeight = 0;

function resizeCanvas() {
    const dpr = window.devicePixelRatio || 1;
    const w = window.innerWidth;
    const h = window.innerHeight;
    gameCanvas.width = w;   // Use CSS pixels (not DPR-scaled) for 1:1 mapping with game coords
    gameCanvas.height = h;
    canvasWidth = w;
    canvasHeight = h;
}
resizeCanvas();
window.addEventListener('resize', resizeCanvas);

// ────────────────────────────────────────────────────────────────
// Texture storage
// ────────────────────────────────────────────────────────────────
const textures = new Map();
let nextTextureId = 1;

// Cache for color-tinted versions of textures (used by font atlas, etc.)
const tintedCache = new Map();

function getTintedTexture(texId, r, g, b) {
    const key = `${texId}_${r}_${g}_${b}`;
    let cached = tintedCache.get(key);
    if (cached) return cached;

    const tex = textures.get(texId);
    if (!tex) return null;

    const c = new OffscreenCanvas(tex.canvas.width, tex.canvas.height);
    const tctx = c.getContext('2d');
    tctx.drawImage(tex.canvas, 0, 0);
    tctx.globalCompositeOperation = 'source-in';
    tctx.fillStyle = `rgb(${r},${g},${b})`;
    tctx.fillRect(0, 0, c.width, c.height);
    tctx.globalCompositeOperation = 'source-over';

    tintedCache.set(key, c);
    return c;
}

// Temp canvas for per-draw tinting (textures not in cache yet)
let tmpCanvas = new OffscreenCanvas(64, 64);
let tmpCtx = tmpCanvas.getContext('2d');
function ensureTmpCanvas(w, h) {
    if (tmpCanvas.width < w || tmpCanvas.height < h) {
        tmpCanvas = new OffscreenCanvas(Math.max(w, tmpCanvas.width), Math.max(h, tmpCanvas.height));
        tmpCtx = tmpCanvas.getContext('2d');
    }
}

// ────────────────────────────────────────────────────────────────
// Input state
// ────────────────────────────────────────────────────────────────
let mouseX = 0, mouseY = 0, mouseWheelAccum = 0;
const inputEvents = [];
let textInputBuffer = '';

gameCanvas.addEventListener('mousemove', e => { mouseX = e.offsetX; mouseY = e.offsetY; });
gameCanvas.addEventListener('mousedown', e => {
    inputEvents.push(`MD:${e.button}`);
    resumeAudio();
});
gameCanvas.addEventListener('mouseup', e => { inputEvents.push(`MU:${e.button}`); });
gameCanvas.addEventListener('wheel', e => { mouseWheelAccum -= e.deltaY / 120; e.preventDefault(); }, { passive: false });
gameCanvas.addEventListener('contextmenu', e => e.preventDefault());

// ── Gamepad state ──────────────────────────────────────────────
let gamepadConnected = false;
window.addEventListener('gamepadconnected', () => { gamepadConnected = true; });
window.addEventListener('gamepaddisconnected', () => {
    const gps = navigator.getGamepads();
    gamepadConnected = gps && Array.from(gps).some(g => g !== null);
});

document.addEventListener('keydown', e => {
    // Allow F12 for dev tools, F5 for refresh
    if (e.code === 'F12' || e.code === 'F5') return;

    inputEvents.push(`KD:${e.code}`);
    resumeAudio();

    // Text input handling
    if (e.key === 'Backspace') {
        textInputBuffer += '\b';
    } else if (e.key === 'Enter') {
        textInputBuffer += '\n';
    } else if (e.key.length === 1) {
        textInputBuffer += e.key;
    }

    e.preventDefault();
});
document.addEventListener('keyup', e => {
    if (e.code === 'F12' || e.code === 'F5') return;
    inputEvents.push(`KU:${e.code}`);
    e.preventDefault();
});

// ────────────────────────────────────────────────────────────────
// Audio state
// ────────────────────────────────────────────────────────────────
let audioCtx = null;
let audioNextTime = 0;
let audioResumed = false;

function resumeAudio() {
    if (audioCtx && audioCtx.state === 'suspended') {
        audioCtx.resume();
        audioResumed = true;
    }
}

// ────────────────────────────────────────────────────────────────
// Module imports for C# [JSImport]
// ────────────────────────────────────────────────────────────────
const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet
    .withDiagnosticTracing(false)
    .create();

document.getElementById('loadingStatus').textContent = 'Initializing runtime...';

// Shared decoder for SetTitle string in flushCommandBuffer
const _textDecoder = new TextDecoder();

setModuleImports('game.js', {
    // ── Canvas rendering ─────────────────────────────────────────
    canvas: {
        // ── Buffered command replay ──────────────────────────────
        // Decodes the entire RenderCommandBuffer binary payload in one JS call,
        // eliminating per-draw-call interop marshaling overhead.
        // Command type constants match RenderCommandType enum (C#):
        //   0=Update 1=BeginFrame 2=EndFrame 3=SetTitle
        //   10=SetClipRect 11=ClearClipRect
        //   20=FillRect 21=DrawCircle 22=FillCircle 23=FillCircleGradient
        //   24=SolidRing 25=DrawLine 26=Triangle 27=FilledTriangle
        //   30=Texture 31=TextureRect 32=TextureSrcDst 33=TextureColor
        //   40=QuadBatch  50=TileMap
        //
        // Binary layout uses little-endian IEEE-754 floats and Int32/Int64;
        // textures are stored as Int64 (low 32 bits = texture ID).
        flushCommandBuffer(buffer, length, cachedCircleTexId) {
            const dv = new DataView(buffer.buffer, buffer.byteOffset, length);
            let pos = 0;

            // ── Primitive readers ────────────────────────────────
            const ri32 = () => { const v = dv.getInt32(pos, true); pos += 4; return v; };
            const rf32 = () => { const v = dv.getFloat32(pos, true); pos += 4; return v; };
            const ru8 = () => buffer[pos++];
            // Int64 texture handle: use low 32-bit unsigned word (nint on WASM32)
            const rtex = () => { const lo = dv.getUint32(pos, true); pos += 8; return lo; };
            const rstr = () => {
                // BinaryWriter 7-bit LEB128 length prefix + UTF-8 bytes
                let len = 0, shift = 0, b;
                do { b = ru8(); len |= (b & 0x7f) << shift; shift += 7; } while (b & 0x80);
                const bytes = new Uint8Array(buffer.buffer, buffer.byteOffset + pos, len);
                pos += len;
                return _textDecoder.decode(bytes);
            };

            const CACHED_CIRCLE_SIZE = 64;

            // ── Command dispatch loop ────────────────────────────
            while (pos < length) {
                switch (ri32()) {

                    case 0: break;  // Update — no payload, no-op on replay

                    case 1: // BeginFrame
                        ctx.setTransform(1, 0, 0, 1, 0, 0);
                        ctx.clearRect(0, 0, canvasWidth, canvasHeight);
                        ctx.fillStyle = '#000';
                        ctx.fillRect(0, 0, canvasWidth, canvasHeight);
                        ctx.imageSmoothingEnabled = false;
                        break;

                    case 2: break;  // EndFrame — Canvas2D presents immediately

                    case 3: document.title = rstr(); break;  // SetTitle

                    case 10: {  // SetClipRect
                        const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                        ctx.save(); ctx.beginPath(); ctx.rect(x, y, w, h); ctx.clip();
                        break;
                    }
                    case 11: ctx.restore(); break;  // ClearClipRect

                    case 20: {  // DrawRectScreen
                        const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
                        ctx.fillRect(x, y, w, h);
                        break;
                    }

                    case 21: {  // DrawCircleScreen (screen-space)
                        const cx = rf32(), cy = rf32(), radius = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ri32(); // segments — canvas arcs are always smooth
                        ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
                        ctx.lineWidth = 1;
                        ctx.beginPath();
                        ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
                        ctx.stroke();
                        break;
                    }

                    case 22: {  // DrawFilledCircleScreen
                        const cx = rf32(), cy = rf32(), radius = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ri32(); // segments
                        const diameter = radius * 2;
                        if (diameter <= CACHED_CIRCLE_SIZE && cachedCircleTexId > 0) {
                            const tinted = getTintedTexture(cachedCircleTexId, r, g, b);
                            if (tinted) {
                                ctx.save();
                                ctx.globalAlpha = a / 255;
                                ctx.imageSmoothingEnabled = false;
                                ctx.drawImage(tinted, cx - radius, cy - radius, diameter, diameter);
                                ctx.restore();
                                break;
                            }
                        }
                        ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
                        ctx.beginPath();
                        ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
                        ctx.fill();
                        break;
                    }

                    case 23: {  // DrawFilledCircleScreenGradient
                        const cx = rf32(), cy = rf32(), radius = rf32();
                        const ir = ru8(), ig = ru8(), ib = ru8(), ia = ru8();
                        const or_ = ru8(), og = ru8(), ob = ru8(), oa = ru8();
                        const transitionStartRadius = rf32();
                        ri32(); // segments
                        if (radius <= 0) break;
                        const tRadius = Math.max(0, Math.min(transitionStartRadius, radius));
                        const solid = tRadius >= radius || (ir === or_ && ig === og && ib === ob && ia === oa);
                        if (solid) {
                            const diameter = radius * 2;
                            if (diameter <= CACHED_CIRCLE_SIZE && cachedCircleTexId > 0) {
                                const tinted = getTintedTexture(cachedCircleTexId, ir, ig, ib);
                                if (tinted) {
                                    ctx.save();
                                    ctx.globalAlpha = ia / 255;
                                    ctx.imageSmoothingEnabled = false;
                                    ctx.drawImage(tinted, cx - radius, cy - radius, diameter, diameter);
                                    ctx.restore();
                                    break;
                                }
                            }
                            ctx.fillStyle = `rgba(${ir},${ig},${ib},${ia / 255})`;
                            ctx.beginPath();
                            ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
                            ctx.fill();
                        } else {
                            const grad = ctx.createRadialGradient(cx, cy, tRadius, cx, cy, radius);
                            grad.addColorStop(0, `rgba(${ir},${ig},${ib},${ia / 255})`);
                            grad.addColorStop(1, `rgba(${or_},${og},${ob},${oa / 255})`);
                            ctx.fillStyle = `rgba(${ir},${ig},${ib},${ia / 255})`;
                            ctx.beginPath();
                            ctx.arc(cx, cy, tRadius, 0, Math.PI * 2);
                            ctx.fill();
                            ctx.fillStyle = grad;
                            ctx.beginPath();
                            ctx.arc(cx, cy, radius, 0, Math.PI * 2);
                            ctx.arc(cx, cy, tRadius, 0, Math.PI * 2, true);
                            ctx.fill();
                        }
                        break;
                    }

                    case 24: {  // DrawSolidRingScreen
                        const cx = rf32(), cy = rf32();
                        const innerRadius = rf32(), outerRadius = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ri32(); // segments
                        if (outerRadius <= 0) break;
                        const inner = Math.max(0, Math.min(innerRadius, outerRadius));
                        ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
                        if (inner <= 0) {
                            const diameter = outerRadius * 2;
                            if (diameter <= CACHED_CIRCLE_SIZE && cachedCircleTexId > 0) {
                                const tinted = getTintedTexture(cachedCircleTexId, r, g, b);
                                if (tinted) {
                                    ctx.save();
                                    ctx.globalAlpha = a / 255;
                                    ctx.imageSmoothingEnabled = false;
                                    ctx.drawImage(tinted, cx - outerRadius, cy - outerRadius, diameter, diameter);
                                    ctx.restore();
                                    break;
                                }
                            }
                            ctx.beginPath();
                            ctx.arc(cx, cy, Math.max(0.5, outerRadius), 0, Math.PI * 2);
                            ctx.fill();
                        } else {
                            ctx.beginPath();
                            ctx.arc(cx, cy, outerRadius, 0, Math.PI * 2);
                            ctx.arc(cx, cy, inner, 0, Math.PI * 2, true);
                            ctx.fill();
                        }
                        break;
                    }

                    case 25: {  // DrawLineScreen
                        const x1 = rf32(), y1 = rf32(), x2 = rf32(), y2 = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
                        ctx.lineWidth = 1;
                        ctx.beginPath();
                        ctx.moveTo(x1, y1);
                        ctx.lineTo(x2, y2);
                        ctx.stroke();
                        break;
                    }

                    case 26: {  // DrawTriangleScreen (outline)
                        const x1 = rf32(), y1 = rf32(), x2 = rf32(), y2 = rf32(), x3 = rf32(), y3 = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
                        ctx.lineWidth = 1;
                        ctx.beginPath();
                        ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.lineTo(x3, y3);
                        ctx.closePath(); ctx.stroke();
                        break;
                    }

                    case 27: {  // DrawFilledTriangleScreen
                        const x1 = rf32(), y1 = rf32(), x2 = rf32(), y2 = rf32(), x3 = rf32(), y3 = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
                        ctx.beginPath();
                        ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.lineTo(x3, y3);
                        ctx.closePath(); ctx.fill();
                        break;
                    }

                    case 30: {  // DrawTextureScreen (center-positioned)
                        const texId = rtex();
                        const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                        const rotDeg = rf32();
                        const alpha = ru8();
                        if (texId === 0) break;
                        const tex = textures.get(texId);
                        if (!tex) break;
                        ctx.save();
                        ctx.globalAlpha = alpha / 255;
                        ctx.imageSmoothingEnabled = tex.smooth;
                        if (rotDeg !== 0) {
                            ctx.translate(x, y);
                            ctx.rotate(rotDeg * Math.PI / 180);
                            ctx.drawImage(tex.canvas, -w / 2, -h / 2, w, h);
                        } else {
                            ctx.drawImage(tex.canvas, x - w / 2, y - h / 2, w, h);
                        }
                        ctx.restore();
                        break;
                    }

                    case 31: {  // DrawTextureScreenRect (top-left positioned)
                        const texId = rtex();
                        const dx = rf32(), dy = rf32(), dw = rf32(), dh = rf32();
                        const alpha = ru8();
                        if (texId === 0) break;
                        const tex = textures.get(texId);
                        if (!tex) break;
                        ctx.save();
                        ctx.globalAlpha = alpha / 255;
                        ctx.imageSmoothingEnabled = tex.smooth;
                        ctx.drawImage(tex.canvas, dx, dy, dw, dh);
                        ctx.restore();
                        break;
                    }

                    case 32: {  // DrawTextureScreenSrcDst
                        const texId = rtex();
                        const sx = rf32(), sy = rf32(), sw = rf32(), sh = rf32();
                        const dx = rf32(), dy = rf32(), dw = rf32(), dh = rf32();
                        const alpha = ru8();
                        if (texId === 0) break;
                        const tex = textures.get(texId);
                        if (!tex) break;
                        ctx.save();
                        ctx.globalAlpha = alpha / 255;
                        ctx.imageSmoothingEnabled = tex.smooth;
                        ctx.drawImage(tex.canvas, sx, sy, sw, sh, dx, dy, dw, dh);
                        ctx.restore();
                        break;
                    }

                    case 33: {  // DrawTextureScreenColor (tinted, center-positioned)
                        const texId = rtex();
                        const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                        const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                        const rotDeg = rf32();
                        if (texId === 0) break;
                        const tinted = getTintedTexture(texId, r, g, b);
                        if (!tinted) break;
                        const tex = textures.get(texId);
                        ctx.save();
                        ctx.globalAlpha = a / 255;
                        ctx.imageSmoothingEnabled = tex ? tex.smooth : false;
                        if (rotDeg !== 0) {
                            ctx.translate(x, y);
                            ctx.rotate(rotDeg * Math.PI / 180);
                            ctx.drawImage(tinted, -w / 2, -h / 2, w, h);
                        } else {
                            ctx.drawImage(tinted, x - w / 2, y - h / 2, w, h);
                        }
                        ctx.restore();
                        break;
                    }

                    case 40: {  // DrawTexturedQuadBatchScreen (font / glyph atlas)
                        const texId = rtex();
                        const tr = ru8(), tg = ru8(), tb = ru8(), ta = ru8();
                        const atlasW = ri32(), atlasH = ri32();
                        const count = ri32();
                        if (texId === 0) { pos += count * 32; break; }
                        const tinted = getTintedTexture(texId, tr, tg, tb);
                        if (!tinted) { pos += count * 32; break; }
                        const tex = textures.get(texId);
                        ctx.save();
                        ctx.globalAlpha = ta / 255;
                        ctx.imageSmoothingEnabled = tex ? tex.smooth : false;
                        for (let i = 0; i < count; i++) {
                            const u0 = rf32(), v0 = rf32(), u1 = rf32(), v1 = rf32();
                            const dx0 = rf32(), dy0 = rf32(), dx1 = rf32(), dy1 = rf32();
                            ctx.drawImage(tinted,
                                u0 * atlasW, v0 * atlasH, (u1 - u0) * atlasW, (v1 - v0) * atlasH,
                                dx0, dy0, dx1 - dx0, dy1 - dy0);
                        }
                        ctx.restore();
                        break;
                    }

                    case 50: {  // DrawTileMapScreen
                        // Colors stored column-major: index = tileX * tilesH + tileY
                        const screenX = rf32(), screenY = rf32(), scaledTileSize = rf32();
                        const tilesW = ri32(), tilesH = ri32();
                        const colorCount = ri32();
                        let lastColor = -1;
                        for (let i = 0; i < colorCount; i++) {
                            const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                            if (a === 0) continue; // A=0 sentinel = empty tile
                            const tx = (i / tilesH) | 0;
                            const ty = i % tilesH;
                            const left = Math.floor(screenX + tx * scaledTileSize);
                            const top = Math.floor(screenY + ty * scaledTileSize);
                            const right = Math.floor(screenX + (tx + 1) * scaledTileSize);
                            const bottom = Math.floor(screenY + (ty + 1) * scaledTileSize);
                            // All tile colors are opaque (Color3→Color4 = a=255),
                            // so rgb() avoids alpha string math and is faster.
                            const color = (r << 16) | (g << 8) | b;
                            if (color !== lastColor) {
                                ctx.fillStyle = `rgb(${r},${g},${b})`;
                                lastColor = color;
                            }
                            ctx.fillRect(left, top, right - left, bottom - top);
                        }
                        break;
                    }
                }
            }
        },
    },

    // ── Texture management ───────────────────────────────────────
    texture: {
        create(pixels, width, height, scaleMode) {
            const id = nextTextureId++;
            const c = new OffscreenCanvas(width, height);
            const tctx = c.getContext('2d');
            const imageData = new ImageData(width, height);

            // Copy RGBA pixels — pixels is a Uint8Array view from WASM
            const src = new Uint8Array(pixels);
            imageData.data.set(src);
            tctx.putImageData(imageData, 0, 0);

            textures.set(id, {
                canvas: c,
                width,
                height,
                smooth: scaleMode === 1, // 0 = Nearest, 1 = Linear
            });
            return id;
        },

        destroy(id) {
            textures.delete(id);
            // Also clear tinted cache entries for this texture
            for (const [key] of tintedCache) {
                if (key.startsWith(`${id}_`)) {
                    tintedCache.delete(key);
                }
            }
        },
    },

    // ── Input ────────────────────────────────────────────────────
    input: {
        getMouseX() { return mouseX; },
        getMouseY() { return mouseY; },
        getMouseWheel() {
            const v = mouseWheelAccum;
            mouseWheelAccum = 0;
            return v;
        },
        flushEvents() {
            if (inputEvents.length === 0) return '';
            const result = inputEvents.join('|');
            inputEvents.length = 0;
            return result;
        },
        getCanvasWidth() { return canvasWidth; },
        getCanvasHeight() { return canvasHeight; },
        getTextInput() {
            const result = textInputBuffer;
            textInputBuffer = '';
            return result;
        },
        // Returns: "connected|b0,b1,...|a0,a1,..." or "" if no gamepad
        // b = button states (0/1), a = axis values (float)
        pollGamepad() {
            if (!gamepadConnected) return '';
            const gamepads = navigator.getGamepads();
            if (!gamepads) return '';
            let gp = null;
            for (let i = 0; i < gamepads.length; i++) {
                if (gamepads[i] && gamepads[i].connected) { gp = gamepads[i]; break; }
            }
            if (!gp) return '';
            resumeAudio();
            const btns = [];
            for (let i = 0; i < Math.min(gp.buttons.length, 17); i++) {
                btns.push(gp.buttons[i].pressed ? '1' : '0');
            }
            const axes = [];
            for (let i = 0; i < Math.min(gp.axes.length, 4); i++) {
                axes.push(gp.axes[i].toFixed(5));
            }
            return `1|${btns.join(',')}|${axes.join(',')}`;
        },
    },

    // ── Audio ────────────────────────────────────────────────────
    audio: {
        init(sampleRate) {
            try {
                audioCtx = new AudioContext({ sampleRate });
                audioNextTime = 0;
                return true;
            } catch (e) {
                console.warn('Web Audio init failed:', e);
                return false;
            }
        },

        pushChunk(buffer, frames) {
            if (!audioCtx || audioCtx.state === 'suspended') return;

            const audioBuf = audioCtx.createBuffer(2, frames, audioCtx.sampleRate);
            const left = audioBuf.getChannelData(0);
            const right = audioBuf.getChannelData(1);

            // buffer is a Float64Array view (double[]) — interleaved L/R
            for (let i = 0; i < frames; i++) {
                left[i] = buffer[i * 2];
                right[i] = buffer[i * 2 + 1];
            }

            const source = audioCtx.createBufferSource();
            source.buffer = audioBuf;
            source.connect(audioCtx.destination);

            const now = audioCtx.currentTime;
            if (audioNextTime < now) audioNextTime = now;
            source.start(audioNextTime);
            audioNextTime += frames / audioCtx.sampleRate;
        },

        getBufferedDuration() {
            if (!audioCtx) return 0;
            return Math.max(0, audioNextTime - audioCtx.currentTime);
        },
    },

    // ── Settings ─────────────────────────────────────────────────
    settings: {
        save(key, value) {
            try { localStorage.setItem('seg_' + key, value); } catch { }
        },
        load(key) {
            try { return localStorage.getItem('seg_' + key); } catch { return null; }
        },
    },

    // ── Launch options ────────────────────────────────────────────
    // Mirrors the SDL CLI argument parser: read named URL query parameters so
    // the web build can be launched with e.g. ?seed=42&location=planet&sublocation=on-foot
    launchOptions: {
        getUrlParam(name) {
            try {
                const params = new URLSearchParams(window.location.search);
                return params.get(name);
            } catch {
                return null;
            }
        },
    },
});

// ────────────────────────────────────────────────────────────────
// Boot the .NET runtime
// ────────────────────────────────────────────────────────────────
document.getElementById('loadingStatus').textContent = 'Loading assemblies...';

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

document.getElementById('loadingStatus').textContent = 'Starting game...';

// Run C# Main() to initialize the game
try {
    console.log('[SEG] Calling runMain()...');
    await runMain();
    console.log('[SEG] runMain() completed successfully');
} catch (e) {
    console.error('[SEG] runMain() failed:', e);
    document.getElementById('loadingStatus').textContent = 'Error: ' + e.message;
    throw e;
}

// Hide loading overlay
document.getElementById('loading').classList.add('hidden');
console.log('[SEG] Game loop starting...');

// Start the game loop
let frameCount = 0;
function gameLoop() {
    try {
        exports.SpaceExplorationGame.WebMain.RunOneFrame();
        frameCount++;
        if (frameCount <= 3) console.log(`[SEG] Frame ${frameCount} completed`);
    } catch (e) {
        console.error('[SEG] Game error in frame ' + frameCount + ':', e);
        document.getElementById('loading').classList.remove('hidden');
        document.getElementById('loadingStatus').textContent = 'Runtime error: ' + e.message;
        return; // Stop the loop on error
    }
    requestAnimationFrame(gameLoop);
}
requestAnimationFrame(gameLoop);
