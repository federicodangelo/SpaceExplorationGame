using SDL3;
using Engine.Core;
using Engine.Rendering.Base;
using System.Numerics;

namespace Engine.Platform.Sdl;

/// <summary>
/// Renders text using pre-built multi-scale font atlas textures.
/// Each atlas contains every glyph rasterized at a specific scale for crisp pixel-perfect rendering.
/// Text is drawn in a single batched SDL.RenderGeometry call per string.
/// </summary>
public class SdlFontRenderer : IFontRenderer
{
    private readonly nint _renderer;
    private readonly SdlTextureManager _textures;

    // ── Multi-scale font atlases ──────────────────────────────────────
    private const float AtlasScaleMin = 0.5f;
    private const float AtlasScaleMax = 2.0f;
    private const float AtlasScaleStep = 0.1f;

    private struct FontAtlasEntry
    {
        public float Scale;
        public nint Texture;
        public int ScaledGlyphW; // pixel width of one glyph in this atlas
        public int ScaledGlyphH; // pixel height of one glyph in this atlas
        public int AtlasWidth;
        public int AtlasHeight;
        public Dictionary<char, (float U0, float V0, float U1, float V1)> GlyphUV;
    }

    private FontAtlasEntry[] _fontAtlases = [];
    // Reusable buffers for SDL.RenderGeometry batching (avoids per-call allocs).
    private SDL.Vertex[] _vertexBuf = new SDL.Vertex[256]; // grows as needed
    private int[] _indexBuf = new int[384];                  // grows as needed

    public SdlFontRenderer(nint renderer, SdlTextureManager textures)
    {
        _renderer = renderer;
        _textures = textures;
        BuildFontAtlases();
    }

    // ── Font atlas construction ─────────────────────────────────────────
    /// <summary>Builds font atlas textures at multiple pre-defined scales for crisp rendering.</summary>
    private void BuildFontAtlases()
    {
        var glyphs = MiniBitmapFont.GetAllGlyphs();
        int count = glyphs.Count;
        if (count == 0) return;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;

        // Build a stable ordered list of characters so column index is consistent
        var charList = new List<char>(glyphs.Keys);

        int steps = (int)MathF.Round((AtlasScaleMax - AtlasScaleMin) / AtlasScaleStep) + 1;
        _fontAtlases = new FontAtlasEntry[steps];

        for (int si = 0; si < steps; si++)
        {
            float scale = AtlasScaleMin + si * AtlasScaleStep;
            int scaledGW = Math.Max(1, (int)MathF.Round(gw * scale));
            int scaledGH = Math.Max(1, (int)MathF.Round(gh * scale));
            int cellW = scaledGW + 1; // 1px padding to avoid bleeding
            int atlasW = cellW * count;
            int atlasH = scaledGH;

            var pixels = new byte[atlasW * atlasH * 4];

            int col = 0;
            var glyphUV = new Dictionary<char, (float U0, float V0, float U1, float V1)>(count);
            float invW = 1f / atlasW;
            float invH = 1f / atlasH;

            foreach (var ch in charList)
            {
                var data = glyphs[ch];
                int baseX = col * cellW;

                // Rasterize: each source pixel becomes a scale×scale block
                for (int sy = 0; sy < gh; sy++)
                {
                    for (int sx = 0; sx < gw; sx++)
                    {
                        if (!data[sy * gw + sx]) continue;

                        int destX0 = (int)(sx * scale);
                        int destY0 = (int)(sy * scale);
                        int destX1 = (int)((sx + 1) * scale);
                        int destY1 = (int)((sy + 1) * scale);

                        for (int py = destY0; py < destY1 && py < scaledGH; py++)
                        {
                            for (int px = destX0; px < destX1 && px < scaledGW; px++)
                            {
                                int idx = ((py * atlasW) + baseX + px) * 4;
                                pixels[idx + 0] = 255;
                                pixels[idx + 1] = 255;
                                pixels[idx + 2] = 255;
                                pixels[idx + 3] = 255;
                            }
                        }
                    }
                }

                glyphUV[ch] = (
                    baseX * invW,
                    0f,
                    (baseX + scaledGW) * invW,
                    scaledGH * invH
                );
                col++;
            }

            // Create SDL texture via TextureManager
            nint texture = _textures.CreateTextureFromPixels(pixels, atlasW, atlasH, TextureScaleMode.Nearest);

            _fontAtlases[si] = new FontAtlasEntry
            {
                Scale = scale,
                Texture = texture,
                ScaledGlyphW = scaledGW,
                ScaledGlyphH = scaledGH,
                AtlasWidth = atlasW,
                AtlasHeight = atlasH,
                GlyphUV = glyphUV
            };
        }
    }

    /// <summary>Picks the atlas whose pre-rendered scale is closest to the requested scale.</summary>
    private ref FontAtlasEntry PickAtlas(float scale)
    {
        // Clamp to valid range, then compute index directly
        float clamped = Math.Clamp(scale, AtlasScaleMin, AtlasScaleMax);
        int idx = (int)MathF.Round((clamped - AtlasScaleMin) / AtlasScaleStep);
        idx = Math.Clamp(idx, 0, _fontAtlases.Length - 1);
        return ref _fontAtlases[idx];
    }

    /// <summary>Draw text in world space (transformed by camera).</summary>
    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        DrawTextScreen(screenPos.X, screenPos.Y, text, color, scale);
    }

    /// <summary>Draw text in screen space using a pre-scaled font atlas and a single batched draw call.</summary>
    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
    {
        if (_fontAtlases.Length == 0 || text.Length == 0) return;

        ref var atlas = ref PickAtlas(scale);
        var glyphUV = atlas.GlyphUV;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;
        float charAdvance = (gw + 1) * scale; // 6px logical advance scaled
        float drawW = gw * scale;
        float drawH = gh * scale;

        // Count visible (non-space) characters that have glyphs
        int visibleCount = 0;
        foreach (char c in text)
        {
            if (c != ' ' && glyphUV.ContainsKey(c))
                visibleCount++;
        }

        if (visibleCount == 0) return;

        // Ensure buffers are large enough (4 verts + 6 indices per quad)
        int requiredVerts = visibleCount * 4;
        int requiredIndices = visibleCount * 6;
        if (_vertexBuf.Length < requiredVerts)
            _vertexBuf = new SDL.Vertex[requiredVerts];
        if (_indexBuf.Length < requiredIndices)
            _indexBuf = new int[requiredIndices];

        // Vertex color (SDL.Vertex uses FColor: 0-1 floats)
        var fcolor = new SDL.FColor
        {
            R = color.R / 255f,
            G = color.G / 255f,
            B = color.B / 255f,
            A = color.A / 255f
        };

        // Snap starting position to integer pixels for crisp rendering
        float cursorX = MathF.Round(x);
        float snappedY = MathF.Round(y);
        int snappedDrawW = (int)MathF.Round(drawW);
        int snappedDrawH = (int)MathF.Round(drawH);
        int snappedAdvance = (int)MathF.Round(charAdvance);
        int vi = 0; // vertex write index
        int ii = 0; // index write index

        foreach (char c in text)
        {
            if (c == ' ')
            {
                cursorX += snappedAdvance;
                continue;
            }

            if (!glyphUV.TryGetValue(c, out var uv))
            {
                cursorX += snappedAdvance;
                continue;
            }

            int baseVertex = vi;
            float rx = MathF.Round(cursorX);

            // Top-left
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx, Y = snappedY },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U0, Y = uv.V0 }
            };
            // Top-right
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx + snappedDrawW, Y = snappedY },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U1, Y = uv.V0 }
            };
            // Bottom-right
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx + snappedDrawW, Y = snappedY + snappedDrawH },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U1, Y = uv.V1 }
            };
            // Bottom-left
            _vertexBuf[vi++] = new SDL.Vertex
            {
                Position = new SDL.FPoint { X = rx, Y = snappedY + snappedDrawH },
                Color = fcolor,
                TexCoord = new SDL.FPoint { X = uv.U0, Y = uv.V1 }
            };

            // Two triangles: 0-1-2, 0-2-3
            _indexBuf[ii++] = baseVertex;
            _indexBuf[ii++] = baseVertex + 1;
            _indexBuf[ii++] = baseVertex + 2;
            _indexBuf[ii++] = baseVertex;
            _indexBuf[ii++] = baseVertex + 2;
            _indexBuf[ii++] = baseVertex + 3;

            cursorX += snappedAdvance;
        }

        // Single draw call for the entire string
        SDL.RenderGeometry(_renderer, atlas.Texture, _vertexBuf, vi, _indexBuf, ii);
    }

    /// <summary>Measure the width of text in screen pixels.</summary>
    public float MeasureText(string text, float scale = 1f)
    {
        return text.Length * 6f * scale;
    }

    public void Dispose()
    {
        foreach (ref var atlas in _fontAtlases.AsSpan())
        {
            _textures.DestroyTexture(atlas.Texture);
            atlas.Texture = nint.Zero;
        }
        _fontAtlases = [];
        GC.SuppressFinalize(this);
    }
}
