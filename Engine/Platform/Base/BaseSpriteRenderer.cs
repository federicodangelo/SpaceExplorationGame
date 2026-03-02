using System.Numerics;
using Engine.Core;
using Engine.Rendering.Base;

namespace Engine.Platform;

/// <summary>
/// Platform-agnostic base for sprite renderers.
/// Handles window-dimension state, the pre-rendered circle texture, and all
/// world-space → screen-space coordinate delegations.
/// Subclasses provide platform-specific screen-space drawing implementations.
/// </summary>
public abstract class BaseSpriteRenderer : ISpriteRenderer
{
    protected const int CachedCircleSize = 64;
    protected nint _cachedCircleTexture = nint.Zero;
    private readonly ITextureManager _textures;
    protected BaseFontRenderer _fontRenderer;

    protected int _windowWidth;
    protected int _windowHeight;
    public int WindowWidth => _windowWidth;
    public int WindowHeight => _windowHeight;

    protected BaseSpriteRenderer(BaseFontRenderer fontRenderer, ITextureManager textures)
    {
        _fontRenderer = fontRenderer;
        _textures = textures;
        _cachedCircleTexture = CreateCachedCircleTexture(textures);
    }

    // ── Window / frame ────────────────────────────────────────────────
    public abstract void Update();
    public abstract void BeginFrame();
    public abstract void EndFrame();
    public abstract void SetTitle(string title);
    public abstract string? TakeScreenshot();

    // ── Clip ──────────────────────────────────────────────────────────
    public abstract void SetClipRect(float x, float y, float w, float h);
    public abstract void ClearClipRect();

    // ── Rectangles ───────────────────────────────────────────────────

    /// <summary>Draw a filled rectangle in world space (transformed by camera).</summary>
    public void DrawRect(Camera camera, Vector2 worldPos, int width, int height, Color4 color)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;
        DrawRectScreen(screenPos.X - scaledW / 2f, screenPos.Y - scaledH / 2f, scaledW, scaledH, color);
    }

    /// <summary>Draw a filled rectangle directly in screen space.</summary>
    public abstract void DrawRectScreen(float x, float y, float w, float h, Color4 color);

    // ── Circles & rings (world) ──────────────────────────────────────

    public abstract void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color, int segments = 32);

    /// <summary>Draw a filled circle in world space.</summary>
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color)
    {
        var center = camera.WorldToScreen(worldCenter);
        float radius = worldRadius * camera.Zoom;
        DrawFilledCircleScreen(center.X, center.Y, radius, color);
    }

    /// <summary>Draw a solid ring (annulus) in world space.</summary>
    public void DrawSolidRing(Camera camera, Vector2 worldCenter, float innerRadius, float outerRadius,
        Color4 color, int segments = 48)
    {
        var center = camera.WorldToScreen(worldCenter);
        float inner = innerRadius * camera.Zoom;
        float outer = outerRadius * camera.Zoom;
        DrawSolidRingScreen(center.X, center.Y, inner, outer, color, segments);
    }

    /// <summary>
    /// Draw a filled circle in world space with a radial gradient.
    /// </summary>
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius,
        Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        float radius = worldRadius * camera.Zoom;
        float transitionRadius = transitionStartRadius * camera.Zoom;
        DrawFilledCircleScreen(center.X, center.Y, radius, innerColor, outerColor, transitionRadius, segments);
    }

    // ── Circles & rings (screen) ─────────────────────────────────────
    public abstract void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32);
    public abstract void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius, Color4 color, int segments = 48);
    public abstract void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32);

    // ── Lines ────────────────────────────────────────────────────────

    /// <summary>Draw a line in world space.</summary>
    public void DrawLine(Camera camera, Vector2 worldStart, Vector2 worldEnd, Color4 color)
    {
        var start = camera.WorldToScreen(worldStart);
        var end = camera.WorldToScreen(worldEnd);
        DrawLineScreen(start.X, start.Y, end.X, end.Y, color);
    }

    /// <summary>Draw a line directly in screen space.</summary>
    public abstract void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color);

    // ── Text ─────────────────────────────────────────────────────────

    /// <summary>Draw text in world space (delegates to FontRenderer).</summary>
    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f)
        => _fontRenderer.DrawText(camera, worldPos, text, color, scale);

    /// <summary>Draw text in screen space (delegates to FontRenderer).</summary>
    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
        => _fontRenderer.DrawTextScreen(x, y, text, color, scale);

    /// <summary>Measure the width of text in screen pixels.</summary>
    public float MeasureText(string text, float scale = 1f)
        => _fontRenderer.MeasureText(text, scale);

    // ── Textures (world) ─────────────────────────────────────────────

    /// <summary>Draw a texture in world space, centered on the position, with rotation.</summary>
    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height,
        float rotationDeg = 0f, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;
        DrawTextureScreen(texture, screenPos.X, screenPos.Y, scaledW, scaledH, rotationDeg, alpha);
    }

    /// <summary>Draw a texture in world space with a color tint (RGBA).</summary>
    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height,
        Color4 color, float rotationDeg = 0f)
    {
        if (texture == nint.Zero) return;
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;
        DrawTextureScreen(texture, screenPos.X, screenPos.Y, scaledW, scaledH, color, rotationDeg);
    }

    // ── Textures (screen) ────────────────────────────────────────────
    public abstract void DrawTextureScreen(nint texture, float x, float y, float w, float h, float rotationDeg = 0f, byte alpha = 255);
    public abstract void DrawTextureScreen(nint texture, Rect dst, byte alpha = 255);
    public abstract void DrawTextureScreen(nint texture, Rect src, Rect dst, byte alpha = 255);
    public abstract void DrawTextureScreen(nint texture, float x, float y, float w, float h, Color4 color, float rotationDeg = 0f);

    // ── Triangles ────────────────────────────────────────────────────
    public abstract void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color);
    public abstract void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color);

    // ── Tile map ─────────────────────────────────────────────────────
    public abstract void RenderTiles(Camera camera, int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor,
        Action<int, int, Vector2, int>? renderDetail = null);

    // ── Dispose ──────────────────────────────────────────────────────
    public virtual void Dispose()
    {
        _textures.DestroyTexture(_cachedCircleTexture);
        _cachedCircleTexture = nint.Zero;
        _fontRenderer.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private ──────────────────────────────────────────────────────

    private static nint CreateCachedCircleTexture(ITextureManager textures)
    {
        const int w = CachedCircleSize;
        const int h = CachedCircleSize;
        byte[] pixels = new byte[w * h * 4];
        float cx = w / 2f;
        float cy = h / 2f;
        float r = w / 2f;
        float r2 = r * r;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float dist2 = dx * dx + dy * dy;
                int idx = (y * w + x) * 4;
                if (dist2 <= r2)
                {
                    pixels[idx + 0] = 255; // R
                    pixels[idx + 1] = 255; // G
                    pixels[idx + 2] = 255; // B
                    pixels[idx + 3] = 255; // A
                }
                else
                {
                    pixels[idx + 0] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, w, h, TextureScaleMode.Nearest);
    }
}
