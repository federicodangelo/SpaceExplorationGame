// renderer-canvas2d.js — Canvas 2D implementation of the render-command interface.
// Mirrors the API of renderer-webgl.js so main.js can swap between them via
// the ?renderer= URL parameter.

import { decodeRenderCommands } from './decode-render-commands.js';

export function createCanvas2DRenderer(canvas) {
    const ctx = canvas.getContext('2d');

    // ── Texture storage ───────────────────────────────────────────
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

    // ── Handler state ─────────────────────────────────────────────
    let _cachedCircleTexId = 0;
    const CACHED_CIRCLE_SIZE = 64;
    // Quad-batch state
    let _qTinted = null, _qAtlasW = 0, _qAtlasH = 0;
    // Tile-map state
    let _tmScreenX = 0, _tmScreenY = 0, _tmScale = 0, _tmTilesH = 0, _tmLastColor = -1;

    // ── Render-command handler ────────────────────────────────────
    const handler = {
        beginFrame() {
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            ctx.fillStyle = '#000';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.imageSmoothingEnabled = false;
        },
        endFrame() { },
        setTitle(title) { document.title = title; },
        setClipRect(x, y, w, h) { ctx.save(); ctx.beginPath(); ctx.rect(x, y, w, h); ctx.clip(); },
        clearClipRect() { ctx.restore(); },

        fillRect(x, y, w, h, r, g, b, a) {
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.fillRect(x, y, w, h);
        },
        drawCircle(cx, cy, radius, r, g, b, a) {
            ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
            ctx.stroke();
        },
        fillCircle(cx, cy, radius, r, g, b, a) {
            const diameter = radius * 2;
            if (diameter <= CACHED_CIRCLE_SIZE && _cachedCircleTexId > 0) {
                const tinted = getTintedTexture(_cachedCircleTexId, r, g, b);
                if (tinted) {
                    ctx.save();
                    ctx.globalAlpha = a / 255;
                    ctx.imageSmoothingEnabled = false;
                    ctx.drawImage(tinted, cx - radius, cy - radius, diameter, diameter);
                    ctx.restore();
                    return;
                }
            }
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.beginPath();
            ctx.arc(cx, cy, Math.max(0.5, radius), 0, Math.PI * 2);
            ctx.fill();
        },
        fillCircleGradient(cx, cy, radius, ir, ig, ib, ia, or_, og, ob, oa, transitionStartRadius) {
            if (radius <= 0) return;
            const tRadius = Math.max(0, Math.min(transitionStartRadius, radius));
            const solid = tRadius >= radius || (ir === or_ && ig === og && ib === ob && ia === oa);
            if (solid) {
                const diameter = radius * 2;
                if (diameter <= CACHED_CIRCLE_SIZE && _cachedCircleTexId > 0) {
                    const tinted = getTintedTexture(_cachedCircleTexId, ir, ig, ib);
                    if (tinted) {
                        ctx.save();
                        ctx.globalAlpha = ia / 255;
                        ctx.imageSmoothingEnabled = false;
                        ctx.drawImage(tinted, cx - radius, cy - radius, diameter, diameter);
                        ctx.restore();
                        return;
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
        },
        solidRing(cx, cy, innerRadius, outerRadius, r, g, b, a) {
            if (outerRadius <= 0) return;
            const inner = Math.max(0, Math.min(innerRadius, outerRadius));
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            if (inner <= 0) {
                const diameter = outerRadius * 2;
                if (diameter <= CACHED_CIRCLE_SIZE && _cachedCircleTexId > 0) {
                    const tinted = getTintedTexture(_cachedCircleTexId, r, g, b);
                    if (tinted) {
                        ctx.save();
                        ctx.globalAlpha = a / 255;
                        ctx.imageSmoothingEnabled = false;
                        ctx.drawImage(tinted, cx - outerRadius, cy - outerRadius, diameter, diameter);
                        ctx.restore();
                        return;
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
        },
        drawLine(x1, y1, x2, y2, r, g, b, a) {
            ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
            ctx.stroke();
        },
        drawTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a) {
            ctx.strokeStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.lineTo(x3, y3);
            ctx.closePath(); ctx.stroke();
        },
        fillTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a) {
            ctx.fillStyle = `rgba(${r},${g},${b},${a / 255})`;
            ctx.beginPath();
            ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.lineTo(x3, y3);
            ctx.closePath(); ctx.fill();
        },
        drawTexture(texId, x, y, w, h, rotDeg, alpha) {
            if (texId === 0) return;
            const tex = textures.get(texId);
            if (!tex) return;
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
        },
        drawTextureRect(texId, dx, dy, dw, dh, alpha) {
            if (texId === 0) return;
            const tex = textures.get(texId);
            if (!tex) return;
            ctx.save();
            ctx.globalAlpha = alpha / 255;
            ctx.imageSmoothingEnabled = tex.smooth;
            ctx.drawImage(tex.canvas, dx, dy, dw, dh);
            ctx.restore();
        },
        drawTextureSrcDst(texId, sx, sy, sw, sh, dx, dy, dw, dh, alpha) {
            if (texId === 0) return;
            const tex = textures.get(texId);
            if (!tex) return;
            ctx.save();
            ctx.globalAlpha = alpha / 255;
            ctx.imageSmoothingEnabled = tex.smooth;
            ctx.drawImage(tex.canvas, sx, sy, sw, sh, dx, dy, dw, dh);
            ctx.restore();
        },
        drawTextureColor(texId, x, y, w, h, r, g, b, a, rotDeg) {
            if (texId === 0) return;
            const tinted = getTintedTexture(texId, r, g, b);
            if (!tinted) return;
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
        },

        // ── QuadBatch ─────────────────────────────────────────────
        beginQuadBatch(texId, tr, tg, tb, ta, atlasW, atlasH, _count) {
            if (texId === 0) return false;
            const tinted = getTintedTexture(texId, tr, tg, tb);
            if (!tinted) return false;
            const tex = textures.get(texId);
            _qTinted = tinted;
            _qAtlasW = atlasW;
            _qAtlasH = atlasH;
            ctx.save();
            ctx.globalAlpha = ta / 255;
            ctx.imageSmoothingEnabled = tex ? tex.smooth : false;
            return true;
        },
        quad(u0, v0, u1, v1, dx0, dy0, dx1, dy1) {
            ctx.drawImage(_qTinted,
                u0 * _qAtlasW, v0 * _qAtlasH, (u1 - u0) * _qAtlasW, (v1 - v0) * _qAtlasH,
                dx0, dy0, dx1 - dx0, dy1 - dy0);
        },
        endQuadBatch() { ctx.restore(); },

        // ── TileMap ───────────────────────────────────────────────
        // Colors stored column-major: index = tileX * tilesH + tileY
        beginTileMap(screenX, screenY, scaledTileSize, _tilesW, tilesH, _colorCount) {
            _tmScreenX = screenX;
            _tmScreenY = screenY;
            _tmScale = scaledTileSize;
            _tmTilesH = tilesH;
            _tmLastColor = -1;
        },
        tileColor(i, r, g, b, a) {
            if (a === 0) return; // A=0 sentinel = empty tile
            const tx = (i / _tmTilesH) | 0;
            const ty = i % _tmTilesH;
            const left = Math.floor(_tmScreenX + tx * _tmScale);
            const top = Math.floor(_tmScreenY + ty * _tmScale);
            const right = Math.floor(_tmScreenX + (tx + 1) * _tmScale);
            const bot = Math.floor(_tmScreenY + (ty + 1) * _tmScale);
            // All tile colors are opaque (Color3→Color4 = a=255),
            // so rgb() avoids alpha string math and is faster.
            const color = (r << 16) | (g << 8) | b;
            if (color !== _tmLastColor) {
                ctx.fillStyle = `rgb(${r},${g},${b})`;
                _tmLastColor = color;
            }
            ctx.fillRect(left, top, right - left, bot - top);
        },
        endTileMap() { },
    };

    function flushCommandBuffer(buffer, length, cachedCircleTexId) {
        _cachedCircleTexId = cachedCircleTexId;
        decodeRenderCommands(buffer, length, handler);
    }

    // ── Texture API ───────────────────────────────────────────────

    function textureCreate(pixels, width, height, scaleMode) {
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
    }

    function textureDestroy(id) {
        textures.delete(id);
        // Also clear tinted cache entries for this texture
        for (const [key] of tintedCache) {
            if (key.startsWith(`${id}_`)) {
                tintedCache.delete(key);
            }
        }
    }

    return {
        flushCommandBuffer,
        textureCreate,
        textureDestroy,
        rendererType: 'canvas2d',
    };
}
