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

    /// <summary>Draw text in screen space.
    /// When <paramref name="maxWidth"/> is set and the text overflows, the text auto-scrolls
    /// horizontally (ping-pong) driven by <see cref="DateTime.Now"/>. Partial glyphs at either
    /// edge are rendered by adjusting the source atlas rect rather than hard clipping.</summary>
    public override void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f, float maxWidth = 0f)
    {
        if (_fontAtlases.Length == 0 || text.Length == 0) return;

        ref var atlas = ref PickAtlas(scale);
        var glyphUV = atlas.GlyphUV;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;
        float charAdvance = (gw + 1) * scale;
        float drawW = gw * scale;
        float drawH = gh * scale;

        int snappedAdvance = (int)MathF.Round(charAdvance);
        int snappedDrawW = (int)MathF.Round(drawW);
        int snappedDrawH = (int)MathF.Round(drawH);
        float snappedY = MathF.Round(y);
        float startX = MathF.Round(x);
        float clipRight = maxWidth > 0f ? startX + maxWidth : float.MaxValue;

        // Scroll offset: ping-pong animation when text overflows maxWidth
        float totalTextW = (text.Length - 1) * snappedAdvance + snappedDrawW;
        float scrollOffset = ComputeScrollOffset(totalTextW, maxWidth);

        float cursorX = startX - scrollOffset; // shift all glyphs by scroll

        foreach (char c in text)
        {
            float charLeft = MathF.Round(cursorX);
            float charRight = charLeft + snappedDrawW;

            // Outside visible window — advance and skip
            if (charRight <= startX || charLeft >= clipRight)
            {
                cursorX += snappedAdvance;
                continue;
            }

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

            // Source atlas rect for this glyph (pixel coordinates)
            float atlasGlyphW = (uv.U1 - uv.U0) * atlas.AtlasWidth;
            float atlasGlyphH = (uv.V1 - uv.V0) * atlas.AtlasHeight;
            float sx = uv.U0 * atlas.AtlasWidth;
            float sy = uv.V0 * atlas.AtlasHeight;

            // Crop source and dest rects to the visible window so partial glyphs at
            // the left/right edge render correctly without any hard clipping.
            float visLeft = Math.Max(charLeft, startX);
            float visRight = Math.Min(charRight, clipRight);
            float cropLeftFrac = (visLeft - charLeft) / snappedDrawW;
            float visWidthFrac = (visRight - visLeft) / snappedDrawW;
            float srcX = sx + cropLeftFrac * atlasGlyphW;
            float srcW = visWidthFrac * atlasGlyphW;
            int dstW = (int)MathF.Round(visRight - visLeft);

            JsCanvas.DrawTextureSrcDstTinted((int)atlas.Texture,
                srcX, sy, srcW, atlasGlyphH,
                MathF.Round(visLeft), snappedY, dstW, snappedDrawH,
                color.R, color.G, color.B, color.A);

            cursorX += snappedAdvance;
        }
    }

}
