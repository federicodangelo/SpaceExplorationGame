using Engine.Platform;
using Engine.Rendering.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Font renderer for the web platform. Inherits <see cref="BufferedFontRenderer"/> so that
/// glyph layout and serialization are handled by the base class; the actual drawing is
/// dispatched to the JavaScript Canvas2D API inside
/// <see cref="WebSpriteRenderer.OnFlushBuffer"/>.
/// </summary>
public class WebFontRenderer : BufferedFontRenderer
{
    public WebFontRenderer(WebTextureManager textures)
        : base(textures)
    {
    }
}
