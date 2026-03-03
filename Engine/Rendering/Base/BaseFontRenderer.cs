using System.Numerics;
using Engine.Core;
using Engine.Platform;

namespace Engine.Rendering.Base;

/// <summary>
/// Platform-agnostic base for bitmap font renderers.
/// Builds multi-scale glyph atlas textures from <see cref="MiniBitmapFont"/>,
/// handles atlas selection, world-space text delegation, and text measurement.
/// Subclasses provide platform-specific <see cref="DrawTextScreen"/> implementations.
/// </summary>
public abstract class BaseFontRenderer : IFontRenderer
{
    // ── Multi-scale font atlases ──────────────────────────────────────
    protected const float AtlasScaleMin = 0.5f;
    protected const float AtlasScaleMax = 2.0f;
    protected const float AtlasScaleStep = 0.1f;

    protected struct FontAtlasEntry
    {
        public float Scale;
        public nint Texture;
        public int ScaledGlyphW;
        public int ScaledGlyphH;
        public int AtlasWidth;
        public int AtlasHeight;
        public Dictionary<char, (float U0, float V0, float U1, float V1)> GlyphUV;
    }

    protected FontAtlasEntry[] _fontAtlases = [];
    private readonly ITextureManager _textures;

    protected BaseFontRenderer(ITextureManager textures)
    {
        _textures = textures;
        BuildFontAtlases();
    }

    // ── Font atlas construction ───────────────────────────────────────

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
    protected ref FontAtlasEntry PickAtlas(float scale)
    {
        float clamped = Math.Clamp(scale, AtlasScaleMin, AtlasScaleMax);
        int idx = (int)MathF.Round((clamped - AtlasScaleMin) / AtlasScaleStep);
        idx = Math.Clamp(idx, 0, _fontAtlases.Length - 1);
        return ref _fontAtlases[idx];
    }

    // ── IFontRenderer ─────────────────────────────────────────────────

    /// <summary>Draw text in world space (transformed by camera).</summary>
    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f, float maxWidth = 0f)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        DrawTextScreen(screenPos.X, screenPos.Y, text, color, scale, maxWidth);
    }

    public abstract void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f, float maxWidth = 0f);

    /// <summary>Measure the width of text in screen pixels.</summary>
    public float MeasureText(string text, float scale = 1f) => text.Length * 6f * scale;

    public virtual void Dispose()
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
