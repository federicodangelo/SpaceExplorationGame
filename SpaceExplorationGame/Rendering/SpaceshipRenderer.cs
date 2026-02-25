using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player's spaceship in all contexts (solar system flight and planet surface).
/// Uses primitive geometry (triangles/rectangles/circles) for a retro look.
/// </summary>
public class SpaceshipRenderer : IDisposable
{
    public SpaceshipRenderer()
    {
    }

    /// <summary>Gets the in-flight texture for a ship type.</summary>
    public nint GetSolarTexture(string shipTypeId) => nint.Zero;

    /// <summary>Gets the landed texture for a ship type.</summary>
    public nint GetLandedTexture(string shipTypeId) => nint.Zero;

    /// <summary>Renders the ship in flight with optional engine flame effect.</summary>
    public void RenderFlying(SpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, string shipTypeId, int spriteSize)
    {
        var screen = camera.WorldToScreen(position);
        DrawShipPrimitivesScreen(renderer, screen.X, screen.Y, camera.Zoom, rotation, shipTypeId, spriteSize);
    }

    /// <summary>Renders the ship in flight directly in screen space.</summary>
    public void RenderFlyingScreen(SpriteRenderer renderer,
        float screenX, float screenY, float rotation, string shipTypeId, int spriteSize)
    {
        DrawShipPrimitivesScreen(renderer, screenX, screenY, 1f, rotation, shipTypeId, spriteSize);
    }

    /// <summary>Renders the landed ship on a planet surface with a label.</summary>
    public void RenderLanded(SpriteRenderer renderer, Camera camera,
        Vector2 position, string shipTypeId, int spriteSize)
    {
        int landedSize = (int)(spriteSize * 1.5f);
        var screen = camera.WorldToScreen(position);
        DrawShipPrimitivesScreen(renderer, screen.X, screen.Y, camera.Zoom, 0f, shipTypeId, landedSize);
        renderer.DrawText(camera, position + new Vector2(-12, 14), "SHIP", new Color3(180, 180, 200));
    }

    private static void DrawShipPrimitivesScreen(SpriteRenderer renderer, float screenX, float screenY,
        float zoom, float rotationDeg, string shipTypeId, int spriteSize)
    {
        float scale = (spriteSize / 32f) * zoom;

        (Color4 hull, Color4 accent) = shipTypeId switch
        {
            "fighter" => (new Color4(190, 70, 70, 255), new Color4(255, 220, 120, 255)),
            "freighter" => (new Color4(170, 150, 90, 255), new Color4(130, 120, 80, 255)),
            "explorer" => (new Color4(90, 150, 220, 255), new Color4(140, 210, 255, 255)),
            _ => (new Color4(90, 200, 90, 255), new Color4(150, 220, 255, 255))
        };

        DrawRotatedQuadScreen(renderer, screenX, screenY, rotationDeg,
            new Vector2(-11f * scale, -4f * scale),
            new Vector2(10f * scale, -4f * scale),
            new Vector2(12f * scale, 4f * scale),
            new Vector2(-11f * scale, 4f * scale),
            hull);

        DrawRotatedTriangleScreen(renderer, screenX, screenY, rotationDeg,
            new Vector2(10f * scale, -4f * scale),
            new Vector2(16f * scale, 0f),
            new Vector2(10f * scale, 4f * scale),
            accent);

        DrawRotatedTriangleScreen(renderer, screenX, screenY, rotationDeg,
            new Vector2(-6f * scale, -4f * scale),
            new Vector2(-16f * scale, -9f * scale),
            new Vector2(-10f * scale, -1f * scale),
            new Color4((byte)(hull.R * 0.7f), (byte)(hull.G * 0.7f), (byte)(hull.B * 0.7f), 255));

        DrawRotatedTriangleScreen(renderer, screenX, screenY, rotationDeg,
            new Vector2(-6f * scale, 4f * scale),
            new Vector2(-16f * scale, 9f * scale),
            new Vector2(-10f * scale, 1f * scale),
            new Color4((byte)(hull.R * 0.7f), (byte)(hull.G * 0.7f), (byte)(hull.B * 0.7f), 255));

        Vector2 cockpitOffset = Rotate(new Vector2(6f * scale, 0f), rotationDeg);
        renderer.DrawFilledCircleScreen(screenX + cockpitOffset.X, screenY + cockpitOffset.Y, 2.2f * scale, accent.WithAlpha(220));
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static void DrawRotatedTriangleScreen(SpriteRenderer renderer, float cx, float cy,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Color4 color)
    {
        var r1 = Rotate(p1, rotationDeg);
        var r2 = Rotate(p2, rotationDeg);
        var r3 = Rotate(p3, rotationDeg);
        renderer.DrawFilledTriangleScreen(cx + r1.X, cy + r1.Y, cx + r2.X, cy + r2.Y, cx + r3.X, cy + r3.Y, color);
    }

    private static void DrawRotatedQuadScreen(SpriteRenderer renderer, float cx, float cy,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, Color4 color)
    {
        DrawRotatedTriangleScreen(renderer, cx, cy, rotationDeg, p1, p2, p3, color);
        DrawRotatedTriangleScreen(renderer, cx, cy, rotationDeg, p1, p3, p4, color);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
