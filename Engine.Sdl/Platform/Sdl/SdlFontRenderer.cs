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
public class SdlFontRenderer : BaseFontRenderer
{
    private readonly nint _renderer;

    // Reusable buffers for SDL.RenderGeometry batching (avoids per-call allocs).
    private SDL.Vertex[] _vertexBuf = new SDL.Vertex[256]; // grows as needed
    private int[] _indexBuf = new int[384];                  // grows as needed

    public SdlFontRenderer(nint renderer, SdlTextureManager textures)
        : base(textures)
    {
        _renderer = renderer;
    }

    /// <summary>Draw text in screen space using a pre-scaled font atlas and a single batched draw call.
    /// When <paramref name="maxWidth"/> is set and the text overflows, the text auto-scrolls
    /// horizontally (ping-pong) driven by <see cref="DateTime.Now"/>. Partial glyphs at either
    /// edge are rendered using UV coordinate cropping rather than hard clipping.</summary>
    public override void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f, float maxWidth = 0f)
    {
        if (_fontAtlases.Length == 0 || text.Length == 0) return;

        ref var atlas = ref PickAtlas(scale);
        var glyphUV = atlas.GlyphUV;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;
        float charAdvance = (gw + 1) * scale; // 6px logical advance scaled
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

        // Pre-size buffers using text.Length as upper bound (partial edge glyphs each still one quad)
        int maxQuads = text.Length;
        if (_vertexBuf.Length < maxQuads * 4)
            _vertexBuf = new SDL.Vertex[maxQuads * 4];
        if (_indexBuf.Length < maxQuads * 6)
            _indexBuf = new int[maxQuads * 6];

        // Vertex color (SDL.Vertex uses FColor: 0-1 floats)
        var fcolor = new SDL.FColor
        {
            R = color.R / 255f,
            G = color.G / 255f,
            B = color.B / 255f,
            A = color.A / 255f
        };

        float cursorX = startX - scrollOffset; // shift all glyphs by scroll
        int vi = 0; // vertex write index
        int ii = 0; // index write index

        foreach (char c in text)
        {
            float charLeft = MathF.Round(cursorX);
            float charRight = charLeft + snappedDrawW;

            // Outside visible window — advance and skip (no drawing)
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

            // Crop screen rect to visible window; adjust UV proportionally so partial
            // glyphs at the left/right edge show correctly without hard clipping.
            float visLeft = Math.Max(charLeft, startX);
            float visRight = Math.Min(charRight, clipRight);
            float uvRange = uv.U1 - uv.U0;
            float u0 = uv.U0 + (visLeft - charLeft) / snappedDrawW * uvRange;
            float u1 = uv.U0 + (visRight - charLeft) / snappedDrawW * uvRange;

            float rx0 = MathF.Round(visLeft);
            float rx1 = MathF.Round(visRight);
            float ry0 = snappedY;
            float ry1 = snappedY + snappedDrawH;

            int baseVertex = vi;
            _vertexBuf[vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = rx0, Y = ry0 }, Color = fcolor, TexCoord = new SDL.FPoint { X = u0, Y = uv.V0 } };
            _vertexBuf[vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = rx1, Y = ry0 }, Color = fcolor, TexCoord = new SDL.FPoint { X = u1, Y = uv.V0 } };
            _vertexBuf[vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = rx1, Y = ry1 }, Color = fcolor, TexCoord = new SDL.FPoint { X = u1, Y = uv.V1 } };
            _vertexBuf[vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = rx0, Y = ry1 }, Color = fcolor, TexCoord = new SDL.FPoint { X = u0, Y = uv.V1 } };

            _indexBuf[ii++] = baseVertex; _indexBuf[ii++] = baseVertex + 1; _indexBuf[ii++] = baseVertex + 2;
            _indexBuf[ii++] = baseVertex; _indexBuf[ii++] = baseVertex + 2; _indexBuf[ii++] = baseVertex + 3;

            cursorX += snappedAdvance;
        }

        // Single draw call for the entire string
        if (vi > 0)
            SDL.RenderGeometry(_renderer, atlas.Texture, _vertexBuf, vi, _indexBuf, ii);
    }

}
