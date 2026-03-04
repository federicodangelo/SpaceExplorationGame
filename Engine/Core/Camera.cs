using System.Numerics;

namespace Engine.Core;

/// <summary>
/// 2D camera with position, zoom, and viewport support.
/// Converts between world coordinates and screen coordinates.
/// </summary>
public class Camera
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; } = 1.0f;
    public float ZoomMin { get; set; }
    public float ZoomMax { get; set; }
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }

    /// <summary>
    /// Screen-space offset added to WorldToScreen output.
    /// Use to render into a sub-region of the screen (e.g. a map panel).
    /// </summary>
    public float ViewportOffsetX { get; set; }
    public float ViewportOffsetY { get; set; }

    public Camera(int viewportWidth, int viewportHeight, float zoomMin = 0.025f, float zoomMax = 4.0f)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        ZoomMin = zoomMin;
        ZoomMax = zoomMax;
        Position = Vector2.Zero;
        ClampZoom();
    }

    /// <summary>
    /// Convert world coordinates to screen coordinates.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var offset = worldPos - Position;
        offset *= Zoom;
        offset += new Vector2(ViewportOffsetX + ViewportWidth / 2f, ViewportOffsetY + ViewportHeight / 2f);
        return offset;
    }

    /// <summary>
    /// Convert screen coordinates to world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var offset = screenPos - new Vector2(ViewportOffsetX + ViewportWidth / 2f, ViewportOffsetY + ViewportHeight / 2f);
        offset /= Zoom;
        offset += Position;
        return offset;
    }

    /// <summary>
    /// Get the world-space rectangle visible on screen.
    /// </summary>
    public VisibleBounds GetVisibleBounds()
    {
        var halfW = ViewportWidth / (2f * Zoom);
        var halfH = ViewportHeight / (2f * Zoom);
        return new VisibleBounds(
            new Vector2(Position.X - halfW, Position.Y - halfH),
            new Vector2(Position.X + halfW, Position.Y + halfH)
        );
    }

    /// <summary>
    /// Call once per frame before rendering to sync the viewport dimensions.
    /// </summary>
    public void Update(int viewportWidth, int viewportHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    public void ClampZoom()
    {
        Zoom = Math.Clamp(Zoom, ZoomMin, ZoomMax);
    }

    /// <summary>
    /// Returns true when a world-space circle (ring border at <paramref name="radius"/> around
    /// <paramref name="center"/>) overlaps the camera viewport, which is approximated as a circle
    /// whose radius equals the half-diagonal of the visible world area.
    /// Test: |distance(camera.Position, center) - radius| &lt;= cameraRadius
    /// </summary>
    public bool CircleBorderOverlapsCamera(Vector2 center, float radius)
    {
        float halfW = ViewportWidth / (2f * Zoom);
        float halfH = ViewportHeight / (2f * Zoom);
        float cameraRadius = MathF.Sqrt(halfW * halfW + halfH * halfH);
        float dist = Vector2.Distance(Position, center);
        return MathF.Abs(dist - radius) <= cameraRadius;
    }

    /// <summary>
    /// Returns true when a world-space filled circle (centered at <paramref name="center"/> with
    /// <paramref name="radius"/>) overlaps the camera viewport, approximated as a circle whose
    /// radius equals the half-diagonal of the visible world area.
    /// Test: distance(camera.Position, center) &lt;= cameraRadius + diskRadius
    /// </summary>
    public bool CircleOverlapsCamera(Vector2 center, float radius)
    {
        float halfW = ViewportWidth / (2f * Zoom);
        float halfH = ViewportHeight / (2f * Zoom);
        float cameraRadius = MathF.Sqrt(halfW * halfW + halfH * halfH);
        return Vector2.Distance(Position, center) <= cameraRadius + radius;
    }

    /// <summary>
    /// Center the camera on a position smoothly.
    /// </summary>
    public void LerpTo(Vector2 target, float t)
    {
        Position = Vector2.Lerp(Position, target, t);
    }
}
