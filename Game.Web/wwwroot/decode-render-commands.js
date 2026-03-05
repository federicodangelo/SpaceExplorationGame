// decode-render-commands.js — Shared binary decoder for the RenderCommandBuffer.
//
// Owns all DataView / LEB128 / typed-array reading so neither renderer needs
// to duplicate it.  Calls methods on a 'handler' object with fully-parsed args:
//
//   beginFrame()
//   endFrame()
//   setTitle(title)
//   setClipRect(x, y, w, h)
//   clearClipRect()
//   fillRect(x, y, w, h, r, g, b, a)              r/g/b/a: 0-255 bytes
//   drawCircle(cx, cy, radius, r, g, b, a)
//   fillCircle(cx, cy, radius, r, g, b, a)
//   fillCircleGradient(cx, cy, radius, ir,ig,ib,ia, or_,og,ob,oa, transitionStartRadius)
//   solidRing(cx, cy, innerRadius, outerRadius, r, g, b, a)
//   drawLine(x1, y1, x2, y2, r, g, b, a)
//   drawTriangle(x1,y1, x2,y2, x3,y3, r, g, b, a)
//   fillTriangle(x1,y1, x2,y2, x3,y3, r, g, b, a)
//   drawTexture(texId, x, y, w, h, rotDeg, alpha)
//   drawTextureRect(texId, dx, dy, dw, dh, alpha)
//   drawTextureSrcDst(texId, sx,sy,sw,sh, dx,dy,dw,dh, alpha)
//   drawTextureColor(texId, x, y, w, h, r, g, b, a, rotDeg)
//
//   beginQuadBatch(texId, tr,tg,tb,ta, atlasW, atlasH, count) → bool
//     Return true to receive quad() calls, false to skip (decoder advances past data).
//   quad(u0, v0, u1, v1, dx0, dy0, dx1, dy1)
//   endQuadBatch()
//
//   beginTileMap(screenX, screenY, scaledTileSize, tilesW, tilesH, colorCount)
//   tileColor(index, r, g, b, a)
//   endTileMap()
//
// Command type constants (RenderCommandType enum in C#):
//   0=Update  1=BeginFrame  2=EndFrame  3=SetTitle
//   10=SetClipRect  11=ClearClipRect
//   20=FillRect  21=DrawCircle  22=FillCircle  23=FillCircleGradient
//   24=SolidRing  25=DrawLine  26=Triangle  27=FilledTriangle
//   30=Texture  31=TextureRect  32=TextureSrcDst  33=TextureColor
//   40=QuadBatch  50=TileMap
//
// Binary layout: little-endian IEEE-754 floats, Int32/Int64.
// Textures are Int64 handles; only the low 32 bits (nint on WASM32) are used.

const _textDecoder = new TextDecoder();

export function decodeRenderCommands(buffer, length, handler) {
    const dv = new DataView(buffer.buffer, buffer.byteOffset, length);
    let pos = 0;

    const ri32 = () => { const v = dv.getInt32(pos, true); pos += 4; return v; };
    const rf32 = () => { const v = dv.getFloat32(pos, true); pos += 4; return v; };
    const ru8 = () => buffer[pos++];
    // Int64 texture handle — read low 32-bit unsigned word, skip high 32 bits
    const rtex = () => { const lo = dv.getUint32(pos, true); pos += 8; return lo; };
    const rstr = () => {
        // BinaryWriter 7-bit LEB128 length prefix + UTF-8 bytes
        let len = 0, shift = 0, b;
        do { b = ru8(); len |= (b & 0x7f) << shift; shift += 7; } while (b & 0x80);
        const bytes = new Uint8Array(buffer.buffer, buffer.byteOffset + pos, len);
        pos += len;
        return _textDecoder.decode(bytes);
    };

    while (pos < length) {
        switch (ri32()) {

            case 0: break;  // Update — no payload

            case 1: handler.beginFrame(); break;
            case 2: handler.endFrame(); break;
            case 3: handler.setTitle(rstr()); break;

            case 10: handler.setClipRect(rf32(), rf32(), rf32(), rf32()); break;
            case 11: handler.clearClipRect(); break;

            case 20: { // FillRect
                const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                handler.fillRect(x, y, w, h, r, g, b, a);
                break;
            }
            case 21: { // DrawCircle (outline)
                const cx = rf32(), cy = rf32(), radius = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                ri32(); // segments — ignored by all current renderers
                handler.drawCircle(cx, cy, radius, r, g, b, a);
                break;
            }
            case 22: { // FillCircle
                const cx = rf32(), cy = rf32(), radius = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                ri32(); // segments
                handler.fillCircle(cx, cy, radius, r, g, b, a);
                break;
            }
            case 23: { // FillCircleGradient
                const cx = rf32(), cy = rf32(), radius = rf32();
                const ir = ru8(), ig = ru8(), ib = ru8(), ia = ru8();
                const or_ = ru8(), og = ru8(), ob = ru8(), oa = ru8();
                const transitionStartRadius = rf32();
                ri32(); // segments
                handler.fillCircleGradient(cx, cy, radius, ir, ig, ib, ia, or_, og, ob, oa, transitionStartRadius);
                break;
            }
            case 24: { // SolidRing
                const cx = rf32(), cy = rf32();
                const innerRadius = rf32(), outerRadius = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                ri32(); // segments
                handler.solidRing(cx, cy, innerRadius, outerRadius, r, g, b, a);
                break;
            }
            case 25: { // DrawLine
                const x1 = rf32(), y1 = rf32(), x2 = rf32(), y2 = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                handler.drawLine(x1, y1, x2, y2, r, g, b, a);
                break;
            }
            case 26: { // DrawTriangle (outline)
                const x1 = rf32(), y1 = rf32(), x2 = rf32(), y2 = rf32(), x3 = rf32(), y3 = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                handler.drawTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a);
                break;
            }
            case 27: { // FilledTriangle
                const x1 = rf32(), y1 = rf32(), x2 = rf32(), y2 = rf32(), x3 = rf32(), y3 = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                handler.fillTriangle(x1, y1, x2, y2, x3, y3, r, g, b, a);
                break;
            }

            case 30: { // DrawTexture (center-positioned)
                const texId = rtex();
                const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                const rotDeg = rf32();
                const alpha = ru8();
                handler.drawTexture(texId, x, y, w, h, rotDeg, alpha);
                break;
            }
            case 31: { // DrawTextureRect (top-left positioned)
                const texId = rtex();
                const dx = rf32(), dy = rf32(), dw = rf32(), dh = rf32();
                const alpha = ru8();
                handler.drawTextureRect(texId, dx, dy, dw, dh, alpha);
                break;
            }
            case 32: { // DrawTextureSrcDst
                const texId = rtex();
                const sx = rf32(), sy = rf32(), sw = rf32(), sh = rf32();
                const dx = rf32(), dy = rf32(), dw = rf32(), dh = rf32();
                const alpha = ru8();
                handler.drawTextureSrcDst(texId, sx, sy, sw, sh, dx, dy, dw, dh, alpha);
                break;
            }
            case 33: { // DrawTextureColor (tinted, center-positioned)
                const texId = rtex();
                const x = rf32(), y = rf32(), w = rf32(), h = rf32();
                const r = ru8(), g = ru8(), b = ru8(), a = ru8();
                const rotDeg = rf32();
                handler.drawTextureColor(texId, x, y, w, h, r, g, b, a, rotDeg);
                break;
            }

            case 40: { // QuadBatch (font / glyph atlas)
                const texId = rtex();
                const tr = ru8(), tg = ru8(), tb = ru8(), ta = ru8();
                const atlasW = ri32(), atlasH = ri32();
                const count = ri32();
                if (handler.beginQuadBatch(texId, tr, tg, tb, ta, atlasW, atlasH, count)) {
                    for (let i = 0; i < count; i++) {
                        handler.quad(rf32(), rf32(), rf32(), rf32(), rf32(), rf32(), rf32(), rf32());
                    }
                    handler.endQuadBatch();
                } else {
                    pos += count * 32; // 8 floats × 4 bytes — skip
                }
                break;
            }

            case 50: { // TileMap  (colors column-major: index = tileX * tilesH + tileY)
                const screenX = rf32(), screenY = rf32(), scaledTileSize = rf32();
                const tilesW = ri32(), tilesH = ri32();
                const colorCount = ri32();
                handler.beginTileMap(screenX, screenY, scaledTileSize, tilesW, tilesH, colorCount);
                for (let i = 0; i < colorCount; i++) {
                    handler.tileColor(i, ru8(), ru8(), ru8(), ru8());
                }
                handler.endTileMap();
                break;
            }
        }
    }
}
