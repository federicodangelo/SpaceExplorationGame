using Engine.Core;
using Engine.Rendering.Base;
using System.Numerics;

namespace Engine.Platform.Web;

/// <summary>
/// Font renderer that builds multi-scale texture atlases from <see cref="MiniBitmapFont"/>
/// and draws text as textured quads via the Canvas2D texture drawing API.
/// </summary>
public class WebFontRenderer : BaseFontRenderer
{
    public WebFontRenderer(WebTextureManager textures)
        : base(textures)
    {
    }

    public override void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
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
            JsCanvas.DrawTextureSrcDstTinted((int)atlas.Texture,
                sx, sy, sw, sh,
                rx, snappedY, snappedDrawW, snappedDrawH,
                color.R, color.G, color.B, color.A);

            cursorX += snappedAdvance;
        }
    }

}
