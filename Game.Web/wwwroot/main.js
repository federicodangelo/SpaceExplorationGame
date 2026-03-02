// main.js — Bootstrap for .NET WASM runtime + game module implementations
import { dotnet } from './_framework/dotnet.js';

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

setModuleImports('game.js', {
    // ── Canvas rendering ─────────────────────────────────────────
    canvas: {
        beginFrame(w, h) {
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            ctx.clearRect(0, 0, canvasWidth, canvasHeight);
            ctx.fillStyle = '#000';
            ctx.fillRect(0, 0, canvasWidth, canvasHeight);
            ctx.imageSmoothingEnabled = false;
        },

        endFrame() {
            // No-op — Canvas2D presents immediately
        },

        setClipRect(x, y, w, h) {
            ctx.save();
            ctx.beginPath();
            ctx.rect(x, y, w, h);
            ctx.clip();
        },

        clearClipRect() {
            ctx.restore();
        },

        fillRect(x, y, w, h, r, g, b, a) {
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.fillRect(x, y, w, h);
        },

        drawLine(x1, y1, x2, y2, r, g, b, a) {
            ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.stroke();
        },

        strokeCircle(cx, cy, radius, r, g, b, a) {
            ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
            ctx.stroke();
        },

        fillCircle(cx, cy, radius, r, g, b, a) {
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.beginPath();
            ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
            ctx.fill();
        },

        fillCircleGradient(cx, cy, radius, ir, ig, ib, ia, or_, og, ob, oa, transitionRadius) {
            const grad = ctx.createRadialGradient(cx, cy, transitionRadius, cx, cy, radius);
            grad.addColorStop(0, `rgba(${ir},${ig},${ib},${ia / 255})`);
            grad.addColorStop(1, `rgba(${or_},${og},${ob},${oa / 255})`);
            ctx.fillStyle = `rgba(${ir},${ig},${ib},${ia / 255})`;
            ctx.beginPath();
            ctx.arc(cx, cy, transitionRadius, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = grad;
            ctx.beginPath();
            ctx.arc(cx, cy, radius, 0, Math.PI * 2);
            ctx.arc(cx, cy, transitionRadius, 0, Math.PI * 2, true);
            ctx.fill();
        },

        fillRing(cx, cy, innerR, outerR, r, g, b, a) {
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.beginPath();
            ctx.arc(cx, cy, outerR, 0, Math.PI * 2);
            ctx.arc(cx, cy, innerR, 0, Math.PI * 2, true);
            ctx.fill();
        },

        drawTexture(texId, cx, cy, w, h, rotDeg, alpha) {
            const tex = textures.get(texId);
            if (!tex) return;
            ctx.save();
            ctx.globalAlpha = alpha / 255;
            ctx.imageSmoothingEnabled = tex.smooth;
            if (rotDeg !== 0) {
                ctx.translate(cx, cy);
                ctx.rotate(rotDeg * Math.PI / 180);
                ctx.drawImage(tex.canvas, -w / 2, -h / 2, w, h);
            } else {
                ctx.drawImage(tex.canvas, cx - w / 2, cy - h / 2, w, h);
            }
            ctx.restore();
        },

        drawTextureTinted(texId, cx, cy, w, h, r, g, b, a, rotDeg) {
            const tinted = getTintedTexture(texId, r, g, b);
            if (!tinted) return;
            ctx.save();
            ctx.globalAlpha = a / 255;
            const tex = textures.get(texId);
            ctx.imageSmoothingEnabled = tex ? tex.smooth : false;
            if (rotDeg !== 0) {
                ctx.translate(cx, cy);
                ctx.rotate(rotDeg * Math.PI / 180);
                ctx.drawImage(tinted, -w / 2, -h / 2, w, h);
            } else {
                ctx.drawImage(tinted, cx - w / 2, cy - h / 2, w, h);
            }
            ctx.restore();
        },

        drawTextureRect(texId, dx, dy, dw, dh, alpha) {
            const tex = textures.get(texId);
            if (!tex) return;
            ctx.save();
            ctx.globalAlpha = alpha / 255;
            ctx.imageSmoothingEnabled = tex.smooth;
            ctx.drawImage(tex.canvas, dx, dy, dw, dh);
            ctx.restore();
        },

        drawTextureSrcDst(texId, sx, sy, sw, sh, dx, dy, dw, dh, alpha) {
            const tex = textures.get(texId);
            if (!tex) return;
            ctx.save();
            ctx.globalAlpha = alpha / 255;
            ctx.imageSmoothingEnabled = tex.smooth;
            ctx.drawImage(tex.canvas, sx, sy, sw, sh, dx, dy, dw, dh);
            ctx.restore();
        },

        drawTextureSrcDstTinted(texId, sx, sy, sw, sh, dx, dy, dw, dh, r, g, b, a) {
            const tinted = getTintedTexture(texId, r, g, b);
            if (!tinted) return;
            ctx.save();
            ctx.globalAlpha = a / 255;
            const tex = textures.get(texId);
            ctx.imageSmoothingEnabled = tex ? tex.smooth : false;
            ctx.drawImage(tinted, sx, sy, sw, sh, dx, dy, dw, dh);
            ctx.restore();
        },

        strokeTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a) {
            ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.lineTo(x3, y3);
            ctx.closePath();
            ctx.stroke();
        },

        fillTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a) {
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.lineTo(x3, y3);
            ctx.closePath();
            ctx.fill();
        },

        setTitle(title) {
            document.title = title;
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
