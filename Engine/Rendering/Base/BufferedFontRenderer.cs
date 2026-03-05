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

    // Per-batch state set in BeginGlyphBatch and consumed in DrawGlyph / EndGlyphBatch.
    private nint _batchTexture;
    private Color4 _batchColor;
    private int _atlasWidth;
    private int _atlasHeight;
    private int _count;

    protected internal BufferedFontRenderer(ITextureManager textures)
        : base(textures)
    {
    }

    protected override void BeginGlyphBatch(nint texture, Color4 color, int atlasWidth, int atlasHeight, int maxGlyphs)
    {
        _batchTexture = texture;
        _batchColor = color;
        _atlasWidth = atlasWidth;
        _atlasHeight = atlasHeight;
        if (_quadBuf.Length < maxGlyphs) _quadBuf = new TexturedQuad[maxGlyphs];
        _count = 0;
    }

    protected override void DrawGlyph(float visLeft, float snappedY, float visRight, int snappedDrawH,
                                      float u0, float v0, float u1, float v1)
    {
        ref var q = ref _quadBuf[_count++];
        q.U0 = u0; q.V0 = v0;
        q.U1 = u1; q.V1 = v1;
        q.DstX0 = visLeft; q.DstY0 = snappedY;
        q.DstX1 = visRight; q.DstY1 = snappedY + snappedDrawH;
    }

    protected override void EndGlyphBatch()
    {
        if (_count > 0)
            Buffer.WriteDrawTexturedQuadBatchScreen(_batchTexture, _batchColor, _atlasWidth, _atlasHeight, _quadBuf, _count);
    }
}
