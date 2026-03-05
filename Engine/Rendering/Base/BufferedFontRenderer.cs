using Engine.Core;
using Engine.Platform;

namespace Engine.Rendering.Base;

/// <summary>
/// Font renderer that performs text layout at write time and serializes
/// pre-computed <see cref="TexturedQuad"/> arrays into the shared <see cref="RenderCommandBuffer"/>
/// as a single <see cref="RenderCommandType.DrawTexturedQuadBatchScreen"/> command per string.
/// This avoids serializing raw strings and deferring layout to replay time.
/// </summary>
public class BufferedFontRenderer : BaseFontRenderer
{
    /// <summary>The command buffer shared with the paired <see cref="BufferedSpriteRenderer"/>.</summary>
    internal RenderCommandBuffer Buffer { get; set; } = null!; // set by the owning BufferedSpriteRenderer after construction

    // Reused across calls to avoid per-frame allocations.
    private TexturedQuad[] _quadBuf = new TexturedQuad[128];

    internal BufferedFontRenderer(ITextureManager textures)
        : base(textures)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Performs the full glyph layout (identical to <c>SdlFontRenderer.DrawTextScreen</c>) and
    /// writes a single <see cref="RenderCommandType.DrawTexturedQuadBatchScreen"/> command so that replay
    /// only needs a batched textured draw — no string parsing or font atlas lookup at replay time.
    /// </remarks>
    public override void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f, float maxWidth = 0f)
    {
        if (_fontAtlases.Length == 0 || text.Length == 0) return;

        ref var atlas = ref PickAtlas(scale);
        var glyphUV = atlas.GlyphUV;

        int gw = MiniBitmapFont.GlyphWidth;
        int gh = MiniBitmapFont.GlyphHeight;

        int snappedAdvance = (int)MathF.Round((gw + 1) * scale);
        int snappedDrawW = (int)MathF.Round(gw * scale);
        int snappedDrawH = (int)MathF.Round(gh * scale);
        float snappedY = MathF.Round(y);
        float startX = MathF.Round(x);
        float clipRight = maxWidth > 0f ? startX + maxWidth : float.MaxValue;

        float totalTextW = (text.Length - 1) * snappedAdvance + snappedDrawW;
        float scrollOffset = ComputeScrollOffset(totalTextW, maxWidth);

        if (_quadBuf.Length < text.Length)
            _quadBuf = new TexturedQuad[text.Length];

        float cursorX = startX - scrollOffset;
        int count = 0;

        foreach (char c in text)
        {
            float charLeft = MathF.Round(cursorX);
            float charRight = charLeft + snappedDrawW;

            cursorX += snappedAdvance;

            // Skip invisible characters
            if (charRight <= startX || charLeft >= clipRight || c == ' ')
                continue;

            if (!glyphUV.TryGetValue(c, out var uv))
                continue;

            // Crop to visible window; adjust UV proportionally for partial edge glyphs.
            float visLeft = Math.Max(charLeft, startX);
            float visRight = Math.Min(charRight, clipRight);
            float uvRange = uv.U1 - uv.U0;
            float u0 = uv.U0 + (visLeft - charLeft) / snappedDrawW * uvRange;
            float u1 = uv.U0 + (visRight - charLeft) / snappedDrawW * uvRange;

            ref var q = ref _quadBuf[count++];
            q.U0 = u0; q.V0 = uv.V0;
            q.U1 = u1; q.V1 = uv.V1;
            q.DstX0 = MathF.Round(visLeft); q.DstY0 = snappedY;
            q.DstX1 = MathF.Round(visRight); q.DstY1 = snappedY + snappedDrawH;
        }

        if (count > 0)
            Buffer.WriteDrawTexturedQuadBatchScreen(atlas.Texture, color, _quadBuf, count);
    }
}
