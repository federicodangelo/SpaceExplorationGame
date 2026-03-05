using Engine.Core;
using Engine.Rendering.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Font renderer that builds multi-scale texture atlases from <see cref="MiniBitmapFont"/>
/// and draws text as textured quads via the Canvas2D texture drawing API.
/// </summary>
public class WebFontRenderer : BaseFontRenderer
{
    // Per-batch state set in BeginGlyphBatch and consumed in DrawGlyph.
    private nint _texture;
    private Color4 _color;
    private int _atlasWidth;
    private int _atlasHeight;

    public WebFontRenderer(WebTextureManager textures)
        : base(textures)
    {
    }

    protected override void BeginGlyphBatch(nint texture, Color4 color, int atlasWidth, int atlasHeight, int maxGlyphs)
    {
        _texture = texture;
        _color = color;
        _atlasWidth = atlasWidth;
        _atlasHeight = atlasHeight;
    }

    protected override void DrawGlyph(float visLeft, float snappedY, float visRight, int snappedDrawH,
                                      float u0, float v0, float u1, float v1)
    {
        float srcX = u0 * _atlasWidth;
        float srcW = (u1 - u0) * _atlasWidth;
        float sy = v0 * _atlasHeight;
        float atlasGlyphH = (v1 - v0) * _atlasHeight;
        int dstW = (int)(visRight - visLeft);

        JsCanvas.DrawTextureSrcDstTinted((int)_texture,
            srcX, sy, srcW, atlasGlyphH,
            visLeft, snappedY, dstW, snappedDrawH,
            _color.R, _color.G, _color.B, _color.A);
    }

    protected override void EndGlyphBatch() { }

}
