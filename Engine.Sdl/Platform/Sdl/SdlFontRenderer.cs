using SDL3;
using Engine.Core;
using Engine.Rendering.Base;

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
    private SDL.Vertex[] _vertexBuf = new SDL.Vertex[256];
    private int[] _indexBuf = new int[384];

    // Per-batch state set in BeginGlyphBatch and consumed in DrawGlyph / EndGlyphBatch.
    private nint _batchTexture;
    private SDL.FColor _batchFColor;
    private int _vi;
    private int _ii;

    public SdlFontRenderer(nint renderer, SdlTextureManager textures)
        : base(textures)
    {
        _renderer = renderer;
    }

    protected override void BeginGlyphBatch(nint texture, Color4 color, int atlasWidth, int atlasHeight, int maxGlyphs)
    {
        _batchTexture = texture;
        _batchFColor = new SDL.FColor { R = color.R / 255f, G = color.G / 255f, B = color.B / 255f, A = color.A / 255f };
        if (_vertexBuf.Length < maxGlyphs * 4) _vertexBuf = new SDL.Vertex[maxGlyphs * 4];
        if (_indexBuf.Length < maxGlyphs * 6) _indexBuf = new int[maxGlyphs * 6];
        _vi = 0;
        _ii = 0;
    }

    protected override void DrawGlyph(float visLeft, float snappedY, float visRight, int snappedDrawH,
                                      float u0, float v0, float u1, float v1)
    {
        float ry1 = snappedY + snappedDrawH;
        int baseVertex = _vi;
        _vertexBuf[_vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = visLeft, Y = snappedY }, Color = _batchFColor, TexCoord = new SDL.FPoint { X = u0, Y = v0 } };
        _vertexBuf[_vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = visRight, Y = snappedY }, Color = _batchFColor, TexCoord = new SDL.FPoint { X = u1, Y = v0 } };
        _vertexBuf[_vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = visRight, Y = ry1 }, Color = _batchFColor, TexCoord = new SDL.FPoint { X = u1, Y = v1 } };
        _vertexBuf[_vi++] = new SDL.Vertex { Position = new SDL.FPoint { X = visLeft, Y = ry1 }, Color = _batchFColor, TexCoord = new SDL.FPoint { X = u0, Y = v1 } };
        _indexBuf[_ii++] = baseVertex; _indexBuf[_ii++] = baseVertex + 1; _indexBuf[_ii++] = baseVertex + 2;
        _indexBuf[_ii++] = baseVertex; _indexBuf[_ii++] = baseVertex + 2; _indexBuf[_ii++] = baseVertex + 3;
    }

    protected override void EndGlyphBatch()
    {
        if (_vi > 0)
            SDL.RenderGeometry(_renderer, _batchTexture, _vertexBuf, _vi, _indexBuf, _ii);
    }

}
