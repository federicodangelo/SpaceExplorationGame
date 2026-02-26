using System.Numerics;
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

        switch (shipTypeId)
        {
            case "fighter":
                DrawFighter(renderer, screenX, screenY, rotationDeg, scale);
                break;
            case "freighter":
                DrawFreighter(renderer, screenX, screenY, rotationDeg, scale);
                break;
            case "explorer":
                DrawExplorer(renderer, screenX, screenY, rotationDeg, scale);
                break;
            default:
                DrawScout(renderer, screenX, screenY, rotationDeg, scale);
                break;
        }
    }

    private static void DrawScout(SpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hull = new Color4(90, 200, 90, 255);
        var accent = new Color4(150, 220, 255, 255);
        var wing = new Color4(65, 140, 75, 255);

        DrawRotatedQuadScreen(renderer, cx, cy, rot,
            new Vector2(-10f * s, -3.2f * s), new Vector2(9f * s, -3.2f * s),
            new Vector2(10.5f * s, 3.2f * s), new Vector2(-10f * s, 3.2f * s), hull);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(9f * s, -3.2f * s), new Vector2(15f * s, 0f), new Vector2(9f * s, 3.2f * s), accent);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-6f * s, -3.2f * s), new Vector2(-13f * s, -7f * s), new Vector2(-9f * s, -0.8f * s), wing);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-6f * s, 3.2f * s), new Vector2(-13f * s, 7f * s), new Vector2(-9f * s, 0.8f * s), wing);

        Vector2 cockpitOffset = Rotate(new Vector2(5f * s, 0f), rot);
        renderer.DrawFilledCircleScreen(cx + cockpitOffset.X, cy + cockpitOffset.Y, 1.9f * s, accent.WithAlpha(220));
    }

    private static void DrawFighter(SpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hull = new Color4(190, 70, 70, 255);
        var accent = new Color4(255, 220, 120, 255);
        var wing = new Color4(140, 50, 50, 255);

        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-9f * s, -3.6f * s), new Vector2(13f * s, 0f), new Vector2(-9f * s, 3.6f * s), hull);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-1f * s, -3.8f * s), new Vector2(-14f * s, -10f * s), new Vector2(-7f * s, -1.2f * s), wing);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-1f * s, 3.8f * s), new Vector2(-14f * s, 10f * s), new Vector2(-7f * s, 1.2f * s), wing);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(5f * s, -2.2f * s), new Vector2(16f * s, 0f), new Vector2(5f * s, 2.2f * s), accent.WithAlpha(210));

        Vector2 cockpitOffset = Rotate(new Vector2(4f * s, 0f), rot);
        renderer.DrawFilledCircleScreen(cx + cockpitOffset.X, cy + cockpitOffset.Y, 1.7f * s, accent.WithAlpha(220));
    }

    private static void DrawFreighter(SpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hull = new Color4(170, 150, 90, 255);
        var trim = new Color4(130, 120, 80, 255);
        var accent = new Color4(165, 205, 235, 240);

        DrawRotatedQuadScreen(renderer, cx, cy, rot,
            new Vector2(-13f * s, -6.2f * s), new Vector2(8f * s, -6.2f * s),
            new Vector2(8f * s, 6.2f * s), new Vector2(-13f * s, 6.2f * s), hull);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(8f * s, -6.2f * s), new Vector2(15f * s, 0f), new Vector2(8f * s, 6.2f * s), trim);

        DrawRotatedQuadScreen(renderer, cx, cy, rot,
            new Vector2(-8f * s, -11f * s), new Vector2(3f * s, -11f * s),
            new Vector2(3f * s, -6.2f * s), new Vector2(-8f * s, -6.2f * s), trim.WithAlpha(230));
        DrawRotatedQuadScreen(renderer, cx, cy, rot,
            new Vector2(-8f * s, 6.2f * s), new Vector2(3f * s, 6.2f * s),
            new Vector2(3f * s, 11f * s), new Vector2(-8f * s, 11f * s), trim.WithAlpha(230));

        Vector2 cockpitOffset = Rotate(new Vector2(5f * s, 0f), rot);
        renderer.DrawFilledCircleScreen(cx + cockpitOffset.X, cy + cockpitOffset.Y, 2.1f * s, accent);
    }

    private static void DrawExplorer(SpriteRenderer renderer, float cx, float cy, float rot, float s)
    {
        var hull = new Color4(90, 150, 220, 255);
        var wing = new Color4(70, 120, 190, 255);
        var accent = new Color4(140, 210, 255, 255);

        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-10f * s, -4f * s), new Vector2(15f * s, 0f), new Vector2(-10f * s, 4f * s), hull);

        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-2f * s, -4f * s), new Vector2(-14f * s, -11f * s), new Vector2(-6f * s, -1.6f * s), wing);
        DrawRotatedTriangleScreen(renderer, cx, cy, rot,
            new Vector2(-2f * s, 4f * s), new Vector2(-14f * s, 11f * s), new Vector2(-6f * s, 1.6f * s), wing);

        DrawRotatedQuadScreen(renderer, cx, cy, rot,
            new Vector2(-1f * s, -1.6f * s), new Vector2(7f * s, -1.6f * s),
            new Vector2(7f * s, 1.6f * s), new Vector2(-1f * s, 1.6f * s), accent.WithAlpha(210));

        Vector2 sensorOffset = Rotate(new Vector2(-5f * s, 0f), rot);
        renderer.DrawFilledCircleScreen(cx + sensorOffset.X, cy + sensorOffset.Y, 1.1f * s, accent.WithAlpha(200));
        Vector2 cockpitOffset = Rotate(new Vector2(6f * s, 0f), rot);
        renderer.DrawFilledCircleScreen(cx + cockpitOffset.X, cy + cockpitOffset.Y, 1.9f * s, accent.WithAlpha(225));
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
