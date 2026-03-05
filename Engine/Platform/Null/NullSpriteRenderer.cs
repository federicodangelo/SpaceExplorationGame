using System.Numerics;
using Engine.Core;

namespace Engine.Platform.Null;

/// <summary>
/// No-op sprite renderer for headless/server use. All draw calls are silently discarded.
/// </summary>
public sealed class NullSpriteRenderer : ISpriteRenderer
{
    public int WindowWidth { get; }
    public int WindowHeight { get; }

    public NullSpriteRenderer(int width, int height)
    {
        WindowWidth = width;
        WindowHeight = height;
    }

    public void Update() { }
    public void BeginFrame() { }
    public void EndFrame() { }
    public void SetTitle(string title) { }
    public string? TakeScreenshot() => null;
    public void Dispose() { }

    // Clip
    public void SetClipRect(float x, float y, float w, float h) { }
    public void ClearClipRect() { }

    // Rectangles
    public void DrawRect(Camera camera, Vector2 worldPos, int width, int height, Color4 color) { }
    public void DrawRectScreen(float x, float y, float w, float h, Color4 color) { }

    // Circles (world)
    public void DrawCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color, int segments = 32) { }
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 color) { }
    public void DrawSolidRing(Camera camera, Vector2 worldCenter, float innerRadius, float outerRadius, Color4 color, int segments = 48) { }
    public void DrawFilledCircle(Camera camera, Vector2 worldCenter, float worldRadius, Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32) { }

    // Circles (screen)
    public void DrawCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32) { }
    public void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 color, int segments = 32) { }
    public void DrawSolidRingScreen(float cx, float cy, float innerRadius, float outerRadius, Color4 color, int segments = 48) { }
    public void DrawFilledCircleScreen(float cx, float cy, float radius, Color4 innerColor, Color4 outerColor, float transitionStartRadius, int segments = 32) { }

    // Lines
    public void DrawLine(Camera camera, Vector2 worldStart, Vector2 worldEnd, Color4 color) { }
    public void DrawLineScreen(float x1, float y1, float x2, float y2, Color4 color) { }

    // Text
    public void DrawText(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f, float maxWidth = 0f) { }
    public void DrawTextCentered(Camera camera, Vector2 worldPos, string text, Color4 color, float scale = 1f, float maxWidth = 0f) { }
    public void DrawTextScreen(float x, float y, string text, Color4 color, float scale = 1f, float maxWidth = 0f) { }
    public float MeasureText(string text, float scale = 1f) => text.Length * 8f * scale;

    // Textures (world)
    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height, float rotationDeg = 0f, byte alpha = 255) { }
    public void DrawTexture(Camera camera, nint texture, Vector2 worldPos, int width, int height, Color4 color, float rotationDeg = 0f) { }

    // Textures (screen)
    public void DrawTextureScreen(nint texture, float x, float y, float w, float h, float rotationDeg = 0f, byte alpha = 255) { }
    public void DrawTextureScreen(nint texture, Rect dst, byte alpha = 255) { }
    public void DrawTextureScreen(nint texture, Rect src, Rect dst, byte alpha = 255) { }
    public void DrawTextureScreen(nint texture, float x, float y, float w, float h, Color4 color, float rotationDeg = 0f) { }

    // Triangles
    public void DrawTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color) { }
    public void DrawFilledTriangleScreen(float x1, float y1, float x2, float y2, float x3, float y3, Color4 color) { }

    // Tile map
    public void RenderTiles(Camera camera, int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor,
        Action<int, int, Vector2, int>? renderDetail = null)
    { }
}
