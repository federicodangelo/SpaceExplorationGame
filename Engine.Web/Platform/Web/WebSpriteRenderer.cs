using System.Numerics;
using Engine.Core;

namespace Engine.Platform.Web;

/// <summary>
/// Sprite renderer backed by Canvas2D via JavaScript interop.
/// </summary>
public class WebSpriteRenderer : ISpriteRenderer
{
    private readonly WebFontRenderer _fontRenderer;
    private readonly WebTextureManager _textures;

    // Cached circle texture for small filled circles
    private nint _cachedCircleTexture = nint.Zero;
    private const int CachedCircleSize = 64;

    public WebSpriteRenderer(WebTextureManager textures)
    {
        _textures = textures;
        _fontRenderer = new WebFontRenderer(textures);
        _cachedCircleTexture = CreateCachedCircleTexture();
    }

    // ── Clip ──────────────────────────────────────────────────────────

    public void SetClipRect(float x, float y, float w, float h)
        => JsCanvas.SetClipRect(x, y, w, h);

    public void ClearClipRect()
        => JsCanvas.ClearClipRect();

    // ── Rectangles ───────────────────────────────────────────────────

    public void DrawRect(Camera camera, Vector2 worldPos, int width, int height, Color4 color)
    {
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;
        JsCanvas.FillRect(screenPos.X - scaledW / 2f, screenPos.Y - scaledH / 2f, scaledW, scaledH,
            color.R, color.G, color.B, color.A);
    }

    public void DrawRectScreen(float x, float y, float w, float h, Color4 color)
        => JsCanvas.FillRect(x, y, w, h, color.R, color.G, color.B, color.A);

    // ── Circles & rings (world) ──────────────────────────────────────

    public void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        float radius = worldRadius * camera.Zoom;
        JsCanvas.StrokeCircle(center.X, center.Y, radius, color.R, color.G, color.B, color.A);
    }

    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color)
    {
        var center = camera.WorldToScreen(worldCenter);
        float radius = worldRadius * camera.Zoom;
        DrawFilledCircleScreen(center.X, center.Y, radius, color);
    }

    public void DrawSolidRing(Camera camera, Vector2 worldCenter, float innerRadius, float outerRadius,
        Color4 color, int segments = 48)
    {
        var center = camera.WorldToScreen(worldCenter);
        float inner = innerRadius * camera.Zoom;
        float outer = outerRadius * camera.Zoom;
        DrawSolidRingScreen(center.X, center.Y, inner, outer, color, segments);
    }

    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius,
        Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        float radius = worldRadius * camera.Zoom;
        float transitionRadius = transitionStartRadius * camera.Zoom;
        DrawFilledCircleScreen(center.X, center.Y, radius, innerColor, outerColor, transitionRadius, segments);
    }

    // ── Circles & rings (screen) ─────────────────────────────────────

    public void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32)
    {
        float diameter = radius * 2f;
        if (diameter <= CachedCircleSize)
        {
            DrawTextureScreen(_cachedCircleTexture, cx, cy, diameter, diameter, color, 0f);
            return;
        }
        JsCanvas.FillCircle(cx, cy, radius, color.R, color.G, color.B, color.A);
    }

    public void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius,
        Color4 color, int segments = 48)
    {
        if (outerRadius <= 0f) return;
        float inner = MathF.Max(0f, MathF.Min(innerRadius, outerRadius));
        if (inner <= 0f)
        {
            DrawFilledCircleScreen(cx, cy, outerRadius, color, segments);
            return;
        }
        JsCanvas.FillRing(cx, cy, inner, outerRadius, color.R, color.G, color.B, color.A);
    }

    public void DrawFilledCircleScreen(float cx, float cy, float radius,
        Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32)
    {
        if (radius <= 0f) return;
        float tRadius = Math.Clamp(transitionStartRadius, 0f, radius);

        if (tRadius >= radius ||
            (innerColor.R == outerColor.R && innerColor.G == outerColor.G &&
             innerColor.B == outerColor.B && innerColor.A == outerColor.A))
        {
            DrawFilledCircleScreen(cx, cy, radius, innerColor, segments);
            return;
        }

        JsCanvas.FillCircleGradient(cx, cy, radius,
            innerColor.R, innerColor.G, innerColor.B, innerColor.A,
            outerColor.R, outerColor.G, outerColor.B, outerColor.A,
            tRadius);
    }

    // ── Lines ────────────────────────────────────────────────────────

    public void DrawLine(Camera camera, Vector2 worldStart, Vector2 worldEnd, Color4 color)
    {
        var start = camera.WorldToScreen(worldStart);
        var end = camera.WorldToScreen(worldEnd);
        JsCanvas.DrawLine(start.X, start.Y, end.X, end.Y, color.R, color.G, color.B, color.A);
    }

    public void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color)
        => JsCanvas.DrawLine(x1, y1, x2, y2, color.R, color.G, color.B, color.A);

    // ── Text ─────────────────────────────────────────────────────────

    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f)
        => _fontRenderer.DrawText(camera, worldPos, text, color, scale);

    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f)
        => _fontRenderer.DrawTextScreen(x, y, text, color, scale);

    public float MeasureText(string text, float scale = 1f)
        => _fontRenderer.MeasureText(text, scale);

    // ── Textures (world) ─────────────────────────────────────────────

    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height,
        float rotationDeg = 0f, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;
        JsCanvas.DrawTexture((int)texture, screenPos.X, screenPos.Y, scaledW, scaledH, rotationDeg, alpha);
    }

    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height,
        Color4 color, float rotationDeg = 0f)
    {
        if (texture == nint.Zero) return;
        var screenPos = camera.WorldToScreen(worldPos);
        float scaledW = width * camera.Zoom;
        float scaledH = height * camera.Zoom;
        JsCanvas.DrawTextureTinted((int)texture, screenPos.X, screenPos.Y, scaledW, scaledH,
            color.R, color.G, color.B, color.A, rotationDeg);
    }

    // ── Textures (screen) ────────────────────────────────────────────

    public void DrawTextureScreen(nint texture, float x, float y, float w, float h,
        float rotationDeg = 0f, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTexture((int)texture, x, y, w, h, rotationDeg, alpha);
    }

    public void DrawTextureScreen(nint texture, Rect dst, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTextureRect((int)texture, dst.X, dst.Y, dst.W, dst.H, alpha);
    }

    public void DrawTextureScreen(nint texture, Rect src, Rect dst, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTextureSrcDst((int)texture, src.X, src.Y, src.W, src.H,
            dst.X, dst.Y, dst.W, dst.H, alpha);
    }

    public void DrawTextureScreen(nint texture, float x, float y, float w, float h,
        Color4 color, float rotationDeg = 0f)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTextureTinted((int)texture, x, y, w, h,
            color.R, color.G, color.B, color.A, rotationDeg);
    }

    // ── Triangles ────────────────────────────────────────────────────

    public void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
        => JsCanvas.StrokeTriangle(x1, y1, x2, y2, x3, y3, color.R, color.G, color.B, color.A);

    public void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
        => JsCanvas.FillTriangle(x1, y1, x2, y2, x3, y3, color.R, color.G, color.B, color.A);

    // ── Frame lifecycle ──────────────────────────────────────────────

    public void BeginFrame()
    {
        JsCanvas.BeginFrame(
            JsInput.GetCanvasWidth(),
            JsInput.GetCanvasHeight());
    }

    public void EndFrame()
        => JsCanvas.EndFrame();

    public void SetTitle(string title)
        => JsCanvas.SetTitle(title);

    public string? TakeScreenshot()
    {
        // Screenshots not supported in the web build
        return null;
    }

    // ── Tile map ─────────────────────────────────────────────────────

    public void RenderTiles(Camera camera, int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor,
        Action<int, int, Vector2, int>? renderDetail = null)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startX = Math.Max(0, (int)(topLeft.X / tileSize) - 1);
        int startY = Math.Max(0, (int)(topLeft.Y / tileSize) - 1);
        int endX = Math.Min(mapWidth - 1, (int)(bottomRight.X / tileSize) + 1);
        int endY = Math.Min(mapHeight - 1, (int)(bottomRight.Y / tileSize) + 1);

        float halfTile = tileSize / 2f;
        float scaledSize = tileSize * camera.Zoom;

        // Pass 1: draw background tiles
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                var color = getColor(x, y);
                if (color == null) continue;

                var worldPos = new Vector2(x * tileSize + halfTile, y * tileSize + halfTile);
                var screenPos = camera.WorldToScreen(worldPos);

                float left = screenPos.X - scaledSize / 2f;
                float top = screenPos.Y - scaledSize / 2f;

                var c = color.Value;
                JsCanvas.FillRect(left, top, scaledSize, scaledSize, c.R, c.G, c.B, 255);
            }
        }

        // Pass 2: render per-tile details
        if (renderDetail != null)
        {
            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    var color = getColor(x, y);
                    if (color == null) continue;

                    int hash = (x * 374761393 + y * 668265263) ^ (x * y);
                    var worldPos = new Vector2(x * tileSize + halfTile, y * tileSize + halfTile);
                    renderDetail(x, y, worldPos, hash);
                }
            }
        }
    }

    // ── Dispose ──────────────────────────────────────────────────────

    public void Dispose()
    {
        _textures.DestroyTexture(_cachedCircleTexture);
        _cachedCircleTexture = nint.Zero;
        _fontRenderer.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Private ──────────────────────────────────────────────────────

    private nint CreateCachedCircleTexture()
    {
        int w = CachedCircleSize;
        int h = CachedCircleSize;
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
                    pixels[idx + 0] = 255;
                    pixels[idx + 1] = 255;
                    pixels[idx + 2] = 255;
                    pixels[idx + 3] = 255;
                }
            }
        }

        return _textures.CreateTextureFromPixels(pixels, w, h, TextureScaleMode.Nearest);
    }
}
