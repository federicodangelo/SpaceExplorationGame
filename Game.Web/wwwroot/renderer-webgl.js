// renderer-webgl.js — WebGL-backed implementation of the render-command interface.
// Implements the same flushCommandBuffer + texture API as renderer-canvas2d.js so
// main.js can swap them transparently via ?renderer=webgl.

import { decodeRenderCommands } from './decode-render-commands.js';
//
// Design notes:
//  • All geometry (rects, circles, triangles, lines, quads) is batched into a
//    single interleaved VBO flushed per-frame (or whenever a texture switch forces it).
//  • Each vertex carries: x,y, u,v, r,g,b,a  (xy=position, uv=texcoord,rgba=color)
//  • A 1×1 white texture is used for solid-color draws so the same shader handles both.
//  • Textures are uploaded as WebGLTexture objects; the same integer IDs as Canvas2D.
//  • Tint-blending (font/glyph atlas) is done in the shader: output = texture * vertexColor.
//  • Clip rects are implemented with gl.scissor().
//  • Circles are tessellated into triangles (fan), rings into triangle strips.

export function createWebGLRenderer(canvas) {
    const gl = canvas.getContext('webgl', {
        alpha: false,
        antialias: false,
        depth: false,
        stencil: false,
        premultipliedAlpha: false,
    });
    if (!gl) throw new Error('WebGL not available');

    // ── Shader ────────────────────────────────────────────────────
    const VS = `
        attribute vec2 a_pos;
        attribute vec2 a_uv;
        attribute vec4 a_color;
        uniform vec2 u_resolution;
        varying vec2 v_uv;
        varying vec4 v_color;
        void main () {
            // Convert from pixel coords to clip space
            vec2 clip = (a_pos / u_resolution) * 2.0 - 1.0;
            gl_Position = vec4(clip.x, -clip.y, 0.0, 1.0);
            v_uv    = a_uv;
            v_color = a_color;
        }
    `;
    const FS = `
        precision mediump float;
        uniform sampler2D u_texture;
        varying vec2 v_uv;
        varying vec4 v_color;
        void main () {
            gl_FragColor = texture2D(u_texture, v_uv) * v_color;
        }
    `;

    function compileShader(type, src) {
        const s = gl.createShader(type);
        gl.shaderSource(s, src);
        gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS))
            throw new Error('Shader: ' + gl.getShaderInfoLog(s));
        return s;
    }
    const prog = gl.createProgram();
    gl.attachShader(prog, compileShader(gl.VERTEX_SHADER, VS));
    gl.attachShader(prog, compileShader(gl.FRAGMENT_SHADER, FS));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS))
        throw new Error('Link: ' + gl.getProgramInfoLog(prog));
    gl.useProgram(prog);

    const a_pos = gl.getAttribLocation(prog, 'a_pos');
    const a_uv = gl.getAttribLocation(prog, 'a_uv');
    const a_color = gl.getAttribLocation(prog, 'a_color');
    const u_res = gl.getUniformLocation(prog, 'u_resolution');
    const u_tex = gl.getUniformLocation(prog, 'u_texture');

    // ── Batch buffer ─────────────────────────────────────────────
    // Floats per vertex: x, y, u, v, r, g, b, a  → 8 floats = 32 bytes
    const FLOATS_PER_VERT = 8;
    const MAX_VERTS = 65536;
    const vertData = new Float32Array(MAX_VERTS * FLOATS_PER_VERT);
    const idxData = new Uint16Array(MAX_VERTS * 3); // generous upper bound
    let vertCount = 0;
    let idxCount = 0;

    const vbo = gl.createBuffer();
    const ibo = gl.createBuffer();

    function setupAttribs() {
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        const stride = FLOATS_PER_VERT * 4;
        gl.enableVertexAttribArray(a_pos);
        gl.vertexAttribPointer(a_pos, 2, gl.FLOAT, false, stride, 0);
        gl.enableVertexAttribArray(a_uv);
        gl.vertexAttribPointer(a_uv, 2, gl.FLOAT, false, stride, 8);
        gl.enableVertexAttribArray(a_color);
        gl.vertexAttribPointer(a_color, 4, gl.FLOAT, false, stride, 16);
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
    }

    // ── 1×1 white texture for solid color draws ──────────────────
    const whiteTex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, whiteTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE,
        new Uint8Array([255, 255, 255, 255]));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);

    // ── Texture map: id → WebGLTexture ───────────────────────────
    const textures = new Map();      // id → { glTex, width, height, smooth }
    let nextTextureId = 1;
    let currentTexId = 0;            // tracks bound texture for batching

    // ── State ─────────────────────────────────────────────────────
    let canvasWidth = canvas.width;
    let canvasHeight = canvas.height;
    let scissorStack = [];           // stack of {x,y,w,h} for clip rect
    let globalAlpha = 1.0;          // not used (per-vertex), kept for API compat

    // ── Circle tessellation cache ─────────────────────────────────
    const CIRCLE_SEGMENTS = 32;
    const circleVerts = (() => {
        const v = [];
        for (let i = 0; i < CIRCLE_SEGMENTS; i++) {
            const a = (i / CIRCLE_SEGMENTS) * Math.PI * 2;
            v.push(Math.cos(a), Math.sin(a));
        }
        return v;
    })();

    // ── Helpers ───────────────────────────────────────────────────

    function setTexture(id) {
        if (id === currentTexId) return;
        flush();
        currentTexId = id;
        const entry = textures.get(id);
        if (entry) {
            gl.bindTexture(gl.TEXTURE_2D, entry.glTex);
            const filter = entry.smooth ? gl.LINEAR : gl.NEAREST;
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter);
        } else {
            gl.bindTexture(gl.TEXTURE_2D, whiteTex);
        }
    }

    function ensureCapacity(verts, tris) {
        if (vertCount + verts > MAX_VERTS || idxCount + tris * 3 > idxData.length) {
            flush();
        }
    }

    // Push a vertex: pos(x,y), uv(u,v), color(r,g,b,a)  0-1 floats
    function pushVert(x, y, u, v, r, g, b, a) {
        const off = vertCount * FLOATS_PER_VERT;
        vertData[off] = x;
        vertData[off + 1] = y;
        vertData[off + 2] = u;
        vertData[off + 3] = v;
        vertData[off + 4] = r;
        vertData[off + 5] = g;
        vertData[off + 6] = b;
        vertData[off + 7] = a;
        return vertCount++;
    }

    // Push a quad (2 triangles) from 4 vertex positions with shared uv/color
    function pushQuadColored(x, y, w, h, r, g, b, a) {
        ensureCapacity(4, 2);
        const base = vertCount;
        pushVert(x, y, 0, 0, r, g, b, a);
        pushVert(x + w, y, 1, 0, r, g, b, a);
        pushVert(x + w, y + h, 1, 1, r, g, b, a);
        pushVert(x, y + h, 0, 1, r, g, b, a);
        pushTri(base, base + 1, base + 2);
        pushTri(base, base + 2, base + 3);
    }

    function pushTri(a, b, c) {
        idxData[idxCount++] = a;
        idxData[idxCount++] = b;
        idxData[idxCount++] = c;
    }

    function flush() {
        if (vertCount === 0) return;
        gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
        gl.bufferData(gl.ARRAY_BUFFER, vertData.subarray(0, vertCount * FLOATS_PER_VERT), gl.DYNAMIC_DRAW);
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
        gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, idxData.subarray(0, idxCount), gl.DYNAMIC_DRAW);
        setupAttribs();
        gl.drawElements(gl.TRIANGLES, idxCount, gl.UNSIGNED_SHORT, 0);
        vertCount = 0;
        idxCount = 0;
    }

    // ── Circle helpers ────────────────────────────────────────────

    function pushFilledCircle(cx, cy, radius, r, g, b, a) {
        const segs = CIRCLE_SEGMENTS;
        ensureCapacity(segs + 1, segs);
        const centerIdx = vertCount;
        pushVert(cx, cy, 0.5, 0.5, r, g, b, a);
        for (let i = 0; i < segs; i++) {
            const ux = circleVerts[i * 2], uy = circleVerts[i * 2 + 1];
            pushVert(cx + ux * radius, cy + uy * radius, 0.5 + ux * 0.5, 0.5 + uy * 0.5, r, g, b, a);
        }
        for (let i = 0; i < segs; i++) {
            pushTri(centerIdx, centerIdx + 1 + i, centerIdx + 1 + (i + 1) % segs);
        }
    }

    function pushRing(cx, cy, inner, outer, r, g, b, a) {
        const segs = CIRCLE_SEGMENTS;
        ensureCapacity(segs * 2, segs * 2);
        const base = vertCount;
        for (let i = 0; i < segs; i++) {
            const ux = circleVerts[i * 2], uy = circleVerts[i * 2 + 1];
            pushVert(cx + ux * inner, cy + uy * inner, 0, 0, r, g, b, a);
            pushVert(cx + ux * outer, cy + uy * outer, 0, 0, r, g, b, a);
        }
        for (let i = 0; i < segs; i++) {
            const a0 = base + i * 2;
            const a1 = base + ((i + 1) % segs) * 2;
            pushTri(a0, a0 + 1, a1 + 1);
            pushTri(a0, a1 + 1, a1);
        }
    }

    // Radial gradient circle: inner color fades to outer color
    function pushFilledCircleGradient(cx, cy, radius, tRadius,
        ir, ig, ib, ia, or_, og, ob, oa) {
        const segs = CIRCLE_SEGMENTS;
        // Draw solid inner core
        if (tRadius > 0) {
            pushFilledCircle(cx, cy, tRadius, ir, ig, ib, ia);
        }
        // Draw gradient ring from tRadius to radius
        ensureCapacity(segs * 2, segs * 2);
        const base = vertCount;
        for (let i = 0; i < segs; i++) {
            const ux = circleVerts[i * 2], uy = circleVerts[i * 2 + 1];
            pushVert(cx + ux * tRadius, cy + uy * tRadius, 0, 0, ir, ig, ib, ia);
            pushVert(cx + ux * radius, cy + uy * radius, 0, 0, or_, og, ob, oa);
        }
        for (let i = 0; i < segs; i++) {
            const a0 = base + i * 2;
            const a1 = base + ((i + 1) % segs) * 2;
            pushTri(a0, a0 + 1, a1 + 1);
            pushTri(a0, a1 + 1, a1);
        }
    }

    // ── Line via thin quad ────────────────────────────────────────
    function pushLine(x1, y1, x2, y2, r, g, b, a) {
        // Expand line to a thin quad 1px wide
        const dx = x2 - x1, dy = y2 - y1;
        const len = Math.sqrt(dx * dx + dy * dy);
        if (len < 0.0001) return;
        const nx = -dy / len * 0.5, ny = dx / len * 0.5;
        ensureCapacity(4, 2);
        const base = vertCount;
        pushVert(x1 + nx, y1 + ny, 0, 0, r, g, b, a);
        pushVert(x2 + nx, y2 + ny, 0, 0, r, g, b, a);
        pushVert(x2 - nx, y2 - ny, 0, 0, r, g, b, a);
        pushVert(x1 - nx, y1 - ny, 0, 0, r, g, b, a);
        pushTri(base, base + 1, base + 2);
        pushTri(base, base + 2, base + 3);
    }

    // ── Handler state ─────────────────────────────────────────────
    // Quad-batch float color components
    let _qRF = 0, _qGF = 0, _qBF = 0, _qAF = 0;
    // Tile-map position / scale
    let _tmScreenX = 0, _tmScreenY = 0, _tmScale = 0, _tmTilesH = 0;

    // ── Render-command handler ────────────────────────────────────
    const handler = {
        beginFrame() {
            canvasWidth = canvas.width;
            canvasHeight = canvas.height;
            gl.viewport(0, 0, canvasWidth, canvasHeight);
            gl.uniform2f(u_res, canvasWidth, canvasHeight);
            gl.uniform1i(u_tex, 0);
            gl.clearColor(0, 0, 0, 1);
            gl.clear(gl.COLOR_BUFFER_BIT);
            gl.enable(gl.BLEND);
            gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
            gl.disable(gl.SCISSOR_TEST);
            scissorStack = [];
            currentTexId = -1;
            setTexture(0);
        },
        endFrame() { flush(); },
        setTitle(title) { document.title = title; },
        setClipRect(x, y, w, h) {
            flush();
            scissorStack.push({ x, y, w, h });
            gl.enable(gl.SCISSOR_TEST);
            gl.scissor(
                Math.round(x),
                Math.round(canvasHeight - y - h),
                Math.round(w),
                Math.round(h)
            );
        },
        clearClipRect() {
            flush();
            scissorStack.pop();
            if (scissorStack.length === 0) {
                gl.disable(gl.SCISSOR_TEST);
            } else {
                const { x, y, w, h } = scissorStack[scissorStack.length - 1];
                gl.scissor(
                    Math.round(x),
                    Math.round(canvasHeight - y - h),
                    Math.round(w),
                    Math.round(h)
                );
            }
        },

        fillRect(x, y, w, h, r, g, b, a) {
            setTexture(0);
            pushQuadColored(x, y, w, h, r / 255, g / 255, b / 255, a / 255);
        },
        drawCircle(cx, cy, radius, r, g, b, a) {
            setTexture(0);
            pushRing(cx, cy, Math.max(0, radius - 1), radius,
                r / 255, g / 255, b / 255, a / 255);
        },
        fillCircle(cx, cy, radius, r, g, b, a) {
            setTexture(0);
            pushFilledCircle(cx, cy, radius, r / 255, g / 255, b / 255, a / 255);
        },
        fillCircleGradient(cx, cy, radius, ir, ig, ib, ia, or_, og, ob, oa, transitionStartRadius) {
            if (radius <= 0) return;
            const tRadius = Math.max(0, Math.min(transitionStartRadius, radius));
            setTexture(0);
            if (tRadius >= radius || (ir === or_ && ig === og && ib === ob && ia === oa)) {
                pushFilledCircle(cx, cy, radius, ir / 255, ig / 255, ib / 255, ia / 255);
            } else {
                pushFilledCircleGradient(cx, cy, radius, tRadius,
                    ir / 255, ig / 255, ib / 255, ia / 255,
                    or_ / 255, og / 255, ob / 255, oa / 255);
            }
        },
        solidRing(cx, cy, innerRadius, outerRadius, r, g, b, a) {
            if (outerRadius <= 0) return;
            const inner = Math.max(0, Math.min(innerRadius, outerRadius));
            setTexture(0);
            if (inner <= 0) {
                pushFilledCircle(cx, cy, outerRadius, r / 255, g / 255, b / 255, a / 255);
            } else {
                pushRing(cx, cy, inner, outerRadius, r / 255, g / 255, b / 255, a / 255);
            }
        },
        drawLine(x1, y1, x2, y2, r, g, b, a) {
            setTexture(0);
            pushLine(x1, y1, x2, y2, r / 255, g / 255, b / 255, a / 255);
        },
        drawTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a) {
            setTexture(0);
            const rf = r / 255, gf = g / 255, bf = b / 255, af = a / 255;
            pushLine(x1, y1, x2, y2, rf, gf, bf, af);
            pushLine(x2, y2, x3, y3, rf, gf, bf, af);
            pushLine(x3, y3, x1, y1, rf, gf, bf, af);
        },
        fillTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a) {
            setTexture(0);
            ensureCapacity(3, 1);
            const base = vertCount;
            const rf = r / 255, gf = g / 255, bf = b / 255, af = a / 255;
            pushVert(x1, y1, 0, 0, rf, gf, bf, af);
            pushVert(x2, y2, 0, 0, rf, gf, bf, af);
            pushVert(x3, y3, 0, 0, rf, gf, bf, af);
            pushTri(base, base + 1, base + 2);
        },
        drawTexture(texId, x, y, w, h, rotDeg, alpha) {
            if (texId === 0) return;
            const entry = textures.get(texId);
            if (!entry) return;
            setTexture(texId);
            const a = alpha / 255;
            if (rotDeg !== 0) {
                const rad = rotDeg * Math.PI / 180;
                const cos = Math.cos(rad), sin = Math.sin(rad);
                const hw = w / 2, hh = h / 2;
                const corners = [[-hw, -hh], [hw, -hh], [hw, hh], [-hw, hh]];
                const uvs = [[0, 0], [1, 0], [1, 1], [0, 1]];
                ensureCapacity(4, 2);
                const base = vertCount;
                for (let i = 0; i < 4; i++) {
                    const [lx, ly] = corners[i];
                    const [u, v] = uvs[i];
                    pushVert(x + lx * cos - ly * sin, y + lx * sin + ly * cos, u, v, 1, 1, 1, a);
                }
                pushTri(base, base + 1, base + 2);
                pushTri(base, base + 2, base + 3);
            } else {
                ensureCapacity(4, 2);
                const base = vertCount;
                pushVert(x - w / 2, y - h / 2, 0, 0, 1, 1, 1, a);
                pushVert(x + w / 2, y - h / 2, 1, 0, 1, 1, 1, a);
                pushVert(x + w / 2, y + h / 2, 1, 1, 1, 1, 1, a);
                pushVert(x - w / 2, y + h / 2, 0, 1, 1, 1, 1, a);
                pushTri(base, base + 1, base + 2);
                pushTri(base, base + 2, base + 3);
            }
        },
        drawTextureRect(texId, dx, dy, dw, dh, alpha) {
            if (texId === 0) return;
            const entry = textures.get(texId);
            if (!entry) return;
            setTexture(texId);
            const a = alpha / 255;
            ensureCapacity(4, 2);
            const base = vertCount;
            pushVert(dx, dy, 0, 0, 1, 1, 1, a);
            pushVert(dx + dw, dy, 1, 0, 1, 1, 1, a);
            pushVert(dx + dw, dy + dh, 1, 1, 1, 1, 1, a);
            pushVert(dx, dy + dh, 0, 1, 1, 1, 1, a);
            pushTri(base, base + 1, base + 2);
            pushTri(base, base + 2, base + 3);
        },
        drawTextureSrcDst(texId, sx, sy, sw, sh, dx, dy, dw, dh, alpha) {
            if (texId === 0) return;
            const entry = textures.get(texId);
            if (!entry) return;
            setTexture(texId);
            const a = alpha / 255;
            const iw = entry.width, ih = entry.height;
            const u0 = sx / iw, v0 = sy / ih, u1 = (sx + sw) / iw, v1 = (sy + sh) / ih;
            ensureCapacity(4, 2);
            const base = vertCount;
            pushVert(dx, dy, u0, v0, 1, 1, 1, a);
            pushVert(dx + dw, dy, u1, v0, 1, 1, 1, a);
            pushVert(dx + dw, dy + dh, u1, v1, 1, 1, 1, a);
            pushVert(dx, dy + dh, u0, v1, 1, 1, 1, a);
            pushTri(base, base + 1, base + 2);
            pushTri(base, base + 2, base + 3);
        },
        drawTextureColor(texId, x, y, w, h, r, g, b, a, rotDeg) {
            if (texId === 0) return;
            const entry = textures.get(texId);
            if (!entry) return;
            setTexture(texId);
            const rf = r / 255, gf = g / 255, bf = b / 255, af = a / 255;
            if (rotDeg !== 0) {
                const rad = rotDeg * Math.PI / 180;
                const cos = Math.cos(rad), sin = Math.sin(rad);
                const hw = w / 2, hh = h / 2;
                const corners = [[-hw, -hh], [hw, -hh], [hw, hh], [-hw, hh]];
                const uvs = [[0, 0], [1, 0], [1, 1], [0, 1]];
                ensureCapacity(4, 2);
                const base = vertCount;
                for (let i = 0; i < 4; i++) {
                    const [lx, ly] = corners[i];
                    const [u, v] = uvs[i];
                    pushVert(x + lx * cos - ly * sin, y + lx * sin + ly * cos, u, v, rf, gf, bf, af);
                }
                pushTri(base, base + 1, base + 2);
                pushTri(base, base + 2, base + 3);
            } else {
                ensureCapacity(4, 2);
                const base = vertCount;
                pushVert(x - w / 2, y - h / 2, 0, 0, rf, gf, bf, af);
                pushVert(x + w / 2, y - h / 2, 1, 0, rf, gf, bf, af);
                pushVert(x + w / 2, y + h / 2, 1, 1, rf, gf, bf, af);
                pushVert(x - w / 2, y + h / 2, 0, 1, rf, gf, bf, af);
                pushTri(base, base + 1, base + 2);
                pushTri(base, base + 2, base + 3);
            }
        },

        // ── QuadBatch ─────────────────────────────────────────────
        beginQuadBatch(texId, tr, tg, tb, ta, _atlasW, _atlasH, _count) {
            if (texId === 0 || !textures.has(texId)) return false;
            setTexture(texId);
            _qRF = tr / 255; _qGF = tg / 255; _qBF = tb / 255; _qAF = ta / 255;
            return true;
        },
        quad(u0, v0, u1, v1, dx0, dy0, dx1, dy1) {
            ensureCapacity(4, 2);
            const base = vertCount;
            pushVert(dx0, dy0, u0, v0, _qRF, _qGF, _qBF, _qAF);
            pushVert(dx1, dy0, u1, v0, _qRF, _qGF, _qBF, _qAF);
            pushVert(dx1, dy1, u1, v1, _qRF, _qGF, _qBF, _qAF);
            pushVert(dx0, dy1, u0, v1, _qRF, _qGF, _qBF, _qAF);
            pushTri(base, base + 1, base + 2);
            pushTri(base, base + 2, base + 3);
        },
        endQuadBatch() { },

        // ── TileMap ───────────────────────────────────────────────
        // Colors stored column-major: index = tileX * tilesH + tileY
        beginTileMap(screenX, screenY, scaledTileSize, _tilesW, tilesH, _colorCount) {
            setTexture(0);
            _tmScreenX = screenX;
            _tmScreenY = screenY;
            _tmScale = scaledTileSize;
            _tmTilesH = tilesH;
        },
        tileColor(i, r, g, b, a) {
            if (a === 0) return;
            const tx = (i / _tmTilesH) | 0;
            const ty = i % _tmTilesH;
            const left = _tmScreenX + tx * _tmScale;
            const top = _tmScreenY + ty * _tmScale;
            const right = left + _tmScale;
            const bottom = top + _tmScale;
            ensureCapacity(4, 2);
            const base = vertCount;
            const rf = r / 255, gf = g / 255, bf = b / 255, af = a / 255;
            pushVert(left, top, 0, 0, rf, gf, bf, af);
            pushVert(right, top, 1, 0, rf, gf, bf, af);
            pushVert(right, bottom, 1, 1, rf, gf, bf, af);
            pushVert(left, bottom, 0, 1, rf, gf, bf, af);
            pushTri(base, base + 1, base + 2);
            pushTri(base, base + 2, base + 3);
        },
        endTileMap() { },
    };

    function flushCommandBuffer(buffer, length, _cachedCircleTexId) {
        decodeRenderCommands(buffer, length, handler);
    }

    // ── Texture API ───────────────────────────────────────────────

    function textureCreate(pixels, width, height, scaleMode) {
        const id = nextTextureId++;
        const glTex = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, glTex);
        const src = new Uint8Array(pixels);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0,
            gl.RGBA, gl.UNSIGNED_BYTE, src);
        const filter = scaleMode === 1 ? gl.LINEAR : gl.NEAREST;
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        // Restore previously bound texture
        const prev = textures.get(currentTexId);
        if (prev) gl.bindTexture(gl.TEXTURE_2D, prev.glTex);
        else gl.bindTexture(gl.TEXTURE_2D, whiteTex);
        textures.set(id, { glTex, width, height, smooth: scaleMode === 1 });
        return id;
    }

    function textureDestroy(id) {
        const entry = textures.get(id);
        if (entry) {
            gl.deleteTexture(entry.glTex);
            textures.delete(id);
        }
    }

    return {
        flushCommandBuffer,
        textureCreate,
        textureDestroy,
        rendererType: 'webgl',
    };
}
