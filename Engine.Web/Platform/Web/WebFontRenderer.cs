using Engine.Core;
using Engine.Rendering.Base;
using System.Numerics;

namespace Engine.Platform.Web;

/// <summary>
/// Font renderer that builds multi-scale texture atlases from <see cref="MiniBitmapFont"/>
/// and draws text as textured quads via the Canvas2D texture drawing API.
/// </summary>
public class WebFontRenderer : IFontRenderer
{
    private readonly WebTextureManager _textures;

    private const float AtlasScaleMin = 0.5f;
    private const float AtlasScaleMax = 2.0f;
    private const float AtlasScaleStep = 0.1f;

    private struct FontAtlasEntry
    {
        public float Scale;
        public int TextureId;
        public int ScaledGlyphW;
        public int ScaledGlyphH;
        public int AtlasWidth;
        public int AtlasHeight;
        public Dictionary<char, (float U0, float V0, float U1, float V1)> GlyphUV;
    }

    private FontAtlasEntry[] _fontAtlases = [];

    public WebFontRenderer(WebTextureManager textures)
    {
        _textures = textures;
        BuildFontAtlases();
    }

    private void BuildFontAtlases()
    {
        var glyphs = MiniBitmapFont.GetAllGlyphs();
        int count = glyphs.Count;
        if (count == 0) return;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;

        var charList = new List<char>(glyphs.Keys);

        int steps = (int)MathF.Round((AtlasScaleMax - AtlasScaleMin) / AtlasScaleStep) + 1;
        _fontAtlases = new FontAtlasEntry[steps];

        for (int si = 0; si < steps; si++)
        {
            float scale = AtlasScaleMin + si * AtlasScaleStep;
            int scaledGW = Math.Max(1, (int)MathF.Round(gw * scale));
            int scaledGH = Math.Max(1, (int)MathF.Round(gh * scale));
            int cellW = scaledGW + 1;
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
                TextureId = (int)texture,
                ScaledGlyphW = scaledGW,
                ScaledGlyphH = scaledGH,
                AtlasWidth = atlasW,
                AtlasHeight = atlasH,
                GlyphUV = glyphUV
            };
        }
    }

    private ref FontAtlasEntry PickAtlas(float scale)
    {
        float clamped = Math.Clamp(scale, AtlasScaleMin, AtlasScaleMax);
        int idx = (int)MathF.Round((clamped - AtlasScaleMin) / AtlasScaleStep);
        idx = Math.Clamp(idx, 0, _fontAtlases.Length - 1);
        return ref _fontAtlases[idx];
    }

    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        DrawTextScreen(screenPos.X, screenPos.Y, text, color, scale);
    }

    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
    {
        if (_fontAtlases.Length == 0 || text.Length == 0) return;

        ref var atlas = ref PickAtlas(scale);
        var glyphUV = atlas.GlyphUV;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;
        float charAdvance = (gw + 1) * scale;
        float drawW = gw * scale;
        float drawH = gh * scale;

        float cursorX = MathF.Round(x);
        float snappedY = MathF.Round(y);
        int snappedDrawW = (int)MathF.Round(drawW);
        int snappedDrawH = (int)MathF.Round(drawH);
        int snappedAdvance = (int)MathF.Round(charAdvance);

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

            float rx = MathF.Round(cursorX);

            // Source rect in the atlas (pixel coordinates)
            float sx = uv.U0 * atlas.AtlasWidth;
            float sy = uv.V0 * atlas.AtlasHeight;
            float sw = (uv.U1 - uv.U0) * atlas.AtlasWidth;
            float sh = (uv.V1 - uv.V0) * atlas.AtlasHeight;

            // Draw the glyph sub-rect from the atlas, tinted with color
            JsCanvas.DrawTextureSrcDstTinted(atlas.TextureId,
                sx, sy, sw, sh,
                rx, snappedY, snappedDrawW, snappedDrawH,
                color.R, color.G, color.B, color.A);

            cursorX += snappedAdvance;
        }
    }

    public float MeasureText(string text, float scale = 1f)
    {
        return text.Length * 6f * scale;
    }

    public void Dispose()
    {
        foreach (ref var atlas in _fontAtlases.AsSpan())
        {
            _textures.DestroyTexture((nint)atlas.TextureId);
            atlas.TextureId = 0;
        }
        _fontAtlases = [];
        GC.SuppressFinalize(this);
    }
}
