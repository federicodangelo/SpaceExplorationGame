using System.Numerics;
using Engine.Core;

namespace Engine.Platform;

/// <summary>
/// Abstraction for 2D sprite, shape, and text rendering.
/// </summary>
public interface ISpriteRenderer : IDisposable
{
    // ── Window dimensions ─────────────────────────────────────────────
    int WindowWidth { get; }
    int WindowHeight { get; }
    /// <summary>Called once per frame by the platform to refresh cached window dimensions.</summary>
    void Update();

    // ── Clip ──────────────────────────────────────────────────────────
    void SetClipRect(float x, float y, float w, float h);
    void ClearClipRect();

    // ── Rectangles ───────────────────────────────────────────────────
    void DrawRect(Camera camera, Vector2 worldPos, int width, int height, Color4 color);
    void DrawRectScreen(float x, float y, float w, float h, Color4 color);

    // ── Circles & rings (world) ──────────────────────────────────────
    void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color, int segments = 32);
    void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color);
    void DrawSolidRing(Camera camera, Vector2 worldCenter, float innerRadius, float outerRadius, Color4 color, int segments = 48);
    void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32);

    // ── Circles & rings (screen) ─────────────────────────────────────
    void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32);
    void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius, Color4 color, int segments = 48);
    void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32);

    // ── Lines ────────────────────────────────────────────────────────
    void DrawLine(Camera camera, Vector2 worldStart, Vector2 worldEnd, Color4 color);
    void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color);

    // ── Text ─────────────────────────────────────────────────────────
    void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f);
    void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f);
    float MeasureText(string text, float scale = 1f);

    // ── Textures (world) ─────────────────────────────────────────────
    void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height, float rotationDeg = 0f, byte alpha = 255);
    void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height, Color4 color, float rotationDeg = 0f);

    // ── Textures (screen) ────────────────────────────────────────────
    void DrawTextureScreen(nint texture, float x, float y, float w, float h, float rotationDeg = 0f, byte alpha = 255);
    void DrawTextureScreen(nint texture, Rect dst, byte alpha = 255);
    void DrawTextureScreen(nint texture, Rect src, Rect dst, byte alpha = 255);
    void DrawTextureScreen(nint texture, float x, float y, float w, float h, Color4 color, float rotationDeg = 0f);

    // ── Triangles ────────────────────────────────────────────────────
    void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color);
    void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color);

    // ── Frame lifecycle ──────────────────────────────────────────────
    void BeginFrame();
    void EndFrame();
    void SetTitle(string title);

    /// <summary>
    /// Captures the current frame and saves it to disk.
    /// </summary>
    /// <returns>The file path of the saved screenshot, or <c>null</c> if the capture failed.</returns>
    string? TakeScreenshot();

    // ── Tile map ─────────────────────────────────────────────────────
    void RenderTiles(Camera camera, int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor,
        Action<int, int, Vector2, int>? renderDetail = null);
}
