using System.Numerics;

namespace SpaceExplorationGame.Core;

/// <summary>
/// 2D camera with position, zoom, and viewport support.
/// Converts between world coordinates and screen coordinates.
/// </summary>
public class Camera
{
    public Vector2 Position { get; set; }
    public float Zoom { get; set; } = 1.0f;
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }

    public Camera(int viewportWidth, int viewportHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        Position = Vector2.Zero;
    }

    /// <summary>
    /// Convert world coordinates to screen coordinates.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var offset = worldPos - Position;
        offset *= Zoom;
        offset += new Vector2(ViewportWidth / 2f, ViewportHeight / 2f);
        return offset;
    }

    /// <summary>
    /// Convert screen coordinates to world coordinates.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var offset = screenPos - new Vector2(ViewportWidth / 2f, ViewportHeight / 2f);
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

    public void ClampZoom()
    {
        Zoom = Math.Clamp(Zoom, GameConfig.CameraZoomMin, GameConfig.CameraZoomMax);
    }

    /// <summary>
    /// Center the camera on a position smoothly.
    /// </summary>
    public void LerpTo(Vector2 target, float t)
    {
        Position = Vector2.Lerp(Position, target, t);
    }
}
