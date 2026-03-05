using System.Numerics;
using Engine.Core;

namespace Engine.Platform.Web;

/// <summary>
/// Sprite renderer backed by Canvas2D via JavaScript interop.
/// </summary>
public class WebSpriteRenderer : BaseSpriteRenderer
{

    public WebSpriteRenderer(WebTextureManager textures)
        : base(new WebFontRenderer(textures), textures)
    {
        _windowWidth = JsInput.GetCanvasWidth();
        _windowHeight = JsInput.GetCanvasHeight();
    }

    public override void Update()
    {
        _windowWidth = JsInput.GetCanvasWidth();
        _windowHeight = JsInput.GetCanvasHeight();
    }

    // ── Clip ──────────────────────────────────────────────────────────

    public override void SetClipRect(float x, float y, float w, float h)
        => JsCanvas.SetClipRect(x, y, w, h);

    public override void ClearClipRect()
        => JsCanvas.ClearClipRect();

    // ── Rectangles ───────────────────────────────────────────────────

    public override void DrawRectScreen(float x, float y, float w, float h, Color4 color)
        => JsCanvas.FillRect(x, y, w, h, color.R, color.G, color.B, color.A);

    // ── Circles & rings (world) ──────────────────────────────────────

    public override void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color, int segments = 32)
    {
        var center = camera.WorldToScreen(worldCenter);
        float radius = worldRadius * camera.Zoom;
        JsCanvas.StrokeCircle(center.X, center.Y, radius, color.R, color.G, color.B, color.A);
    }

    // ── Circles & rings (screen) ─────────────────────────────────────

    public override void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32)
    {
        float diameter = radius * 2f;
        if (diameter <= CachedCircleSize)
        {
            DrawTextureScreen(_cachedCircleTexture, cx, cy, diameter, diameter, color, 0f);
            return;
        }
        JsCanvas.FillCircle(cx, cy, radius, color.R, color.G, color.B, color.A);
    }

    public override void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius,
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

    public override void DrawFilledCircleScreen(float cx, float cy, float radius,
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

    public override void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color)
        => JsCanvas.DrawLine(x1, y1, x2, y2, color.R, color.G, color.B, color.A);

    // ── Textures (screen) ────────────────────────────────────────────

    public override void DrawTextureScreen(nint texture, float x, float y, float w, float h,
        float rotationDeg = 0f, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTexture((int)texture, x, y, w, h, rotationDeg, alpha);
    }

    public override void DrawTextureScreen(nint texture, Rect dst, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTextureRect((int)texture, dst.X, dst.Y, dst.W, dst.H, alpha);
    }

    public override void DrawTextureScreen(nint texture, Rect src, Rect dst, byte alpha = 255)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTextureSrcDst((int)texture, src.X, src.Y, src.W, src.H,
            dst.X, dst.Y, dst.W, dst.H, alpha);
    }

    public override void DrawTextureScreen(nint texture, float x, float y, float w, float h,
        Color4 color, float rotationDeg = 0f)
    {
        if (texture == nint.Zero) return;
        JsCanvas.DrawTextureTinted((int)texture, x, y, w, h,
            color.R, color.G, color.B, color.A, rotationDeg);
    }

    // ── Triangles ────────────────────────────────────────────────────

    public override void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
        => JsCanvas.StrokeTriangle(x1, y1, x2, y2, x3, y3, color.R, color.G, color.B, color.A);

    public override void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color)
        => JsCanvas.FillTriangle(x1, y1, x2, y2, x3, y3, color.R, color.G, color.B, color.A);

    // ── Frame lifecycle ──────────────────────────────────────────────

    public override void BeginFrame()
    {
        JsCanvas.BeginFrame(
            JsInput.GetCanvasWidth(),
            JsInput.GetCanvasHeight());
    }

    public override void EndFrame()
        => JsCanvas.EndFrame();

    public override void SetTitle(string title)
        => JsCanvas.SetTitle(title);

    public override string? TakeScreenshot()
    {
        // Screenshots not supported in the web build
        return null;
    }

    // ── Tile map ─────────────────────────────────────────────────────

    public override void RenderTiles(Camera camera, int mapWidth, int mapHeight, float tileSize,
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

                // Floor both edges independently so adjacent tiles share the same
                // boundary pixel and leave no sub-pixel gaps (black lines).
                float left = MathF.Floor(screenPos.X - scaledSize / 2f);
                float top = MathF.Floor(screenPos.Y - scaledSize / 2f);
                float right = MathF.Floor(screenPos.X + scaledSize / 2f);
                float bottom = MathF.Floor(screenPos.Y + scaledSize / 2f);

                var c = color.Value;
                JsCanvas.FillRect(left, top, right - left, bottom - top, c.R, c.G, c.B, 255);
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

                    int hash = GetTileHash(x, y);
                    var worldPos = new Vector2(x * tileSize + halfTile, y * tileSize + halfTile);
                    renderDetail(x, y, worldPos, hash);
                }
            }
        }
    }

    // ── Dispose ──────────────────────────────────────────────────────

    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
