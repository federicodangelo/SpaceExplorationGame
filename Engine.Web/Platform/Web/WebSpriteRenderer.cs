using System.Numerics;
using Engine.Core;
using Engine.Platform;
using Engine.Rendering.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Sprite renderer for the web platform. All draw calls are serialized into a
/// <see cref="RenderCommandBuffer"/> by <see cref="BufferedSpriteRenderer"/> and then
/// dispatched to the JavaScript Canvas2D API as a batch inside <see cref="OnFlushBuffer"/>.
/// </summary>
public class WebSpriteRenderer : BufferedSpriteRenderer
{
    public WebSpriteRenderer(WebTextureManager textures)
        : base(new WebFontRenderer(textures), textures)
    {
        _windowWidth = JsInput.GetCanvasWidth();
        _windowHeight = JsInput.GetCanvasHeight();
    }

    // ── Immediate operations (never buffered) ────────────────────────────

    public override void Update()
    {
        _windowWidth = JsInput.GetCanvasWidth();
        _windowHeight = JsInput.GetCanvasHeight();
    }

    // ── Buffer replay ─────────────────────────────────────────────────────

    protected override void OnFlushBuffer()
    {
        Buffer.GetRawBuffer(out byte[] rawBuffer, out int length);
        JsCanvas.FlushCommandBuffer(rawBuffer, length, (int)_cachedCircleTexture);
    }

    // ── Dispose ──────────────────────────────────────────────────────────

    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
