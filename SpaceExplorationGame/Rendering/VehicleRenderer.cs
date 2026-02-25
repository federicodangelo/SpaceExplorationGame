using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player's ground vehicle using primitive geometry.
/// </summary>
public class VehicleRenderer : IDisposable
{
    private const float VehicleSize = 40f;

    public VehicleRenderer()
    {
    }

    /// <summary>Renders the vehicle with texture and optional label when not mounted.</summary>
    public void Render(SpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, bool isMounted)
    {
        DrawVehiclePrimitives(renderer, camera, position, rotation + 90f);
        if (!isMounted)
        {
            renderer.DrawText(camera, position + new Vector2(-20, 14), "VEHICLE", new Color3(180, 160, 100));
        }
    }

    private static void DrawVehiclePrimitives(SpriteRenderer renderer, Camera camera, Vector2 position, float rotationDeg)
    {
        float s = VehicleSize / 20f;

        DrawRotatedQuad(renderer, camera, position, rotationDeg,
            new Vector2(-7f * s, -10f * s),
            new Vector2(7f * s, -10f * s),
            new Vector2(7f * s, 10f * s),
            new Vector2(-7f * s, 10f * s),
            new Color4(180, 140, 80, 255));

        DrawRotatedQuad(renderer, camera, position, rotationDeg,
            new Vector2(-5f * s, -7f * s),
            new Vector2(5f * s, -7f * s),
            new Vector2(5f * s, -3f * s),
            new Vector2(-5f * s, -3f * s),
            new Color4(100, 180, 230, 255));

        DrawRotatedQuad(renderer, camera, position, rotationDeg,
            new Vector2(-7f * s, -1f * s),
            new Vector2(7f * s, -1f * s),
            new Vector2(7f * s, 1f * s),
            new Vector2(-7f * s, 1f * s),
            new Color4(100, 100, 110, 255));

        DrawWheel(renderer, camera, position, rotationDeg, new Vector2(-8f * s, -7f * s), 2.2f * s);
        DrawWheel(renderer, camera, position, rotationDeg, new Vector2(8f * s, -7f * s), 2.2f * s);
        DrawWheel(renderer, camera, position, rotationDeg, new Vector2(-8f * s, 7f * s), 2.2f * s);
        DrawWheel(renderer, camera, position, rotationDeg, new Vector2(8f * s, 7f * s), 2.2f * s);

        Vector2 headL = Rotate(new Vector2(-3f * s, -11f * s), rotationDeg);
        Vector2 headR = Rotate(new Vector2(3f * s, -11f * s), rotationDeg);
        renderer.DrawFilledCircle(camera, position + headL, 1f * s, new Color4(255, 255, 200, 255));
        renderer.DrawFilledCircle(camera, position + headR, 1f * s, new Color4(255, 255, 200, 255));

        Vector2 tailL = Rotate(new Vector2(-3f * s, 11f * s), rotationDeg);
        Vector2 tailR = Rotate(new Vector2(3f * s, 11f * s), rotationDeg);
        renderer.DrawFilledCircle(camera, position + tailL, 1f * s, new Color4(255, 80, 80, 255));
        renderer.DrawFilledCircle(camera, position + tailR, 1f * s, new Color4(255, 80, 80, 255));
    }

    private static void DrawWheel(SpriteRenderer renderer, Camera camera, Vector2 center, float rotationDeg, Vector2 offset, float radius)
    {
        Vector2 w = center + Rotate(offset, rotationDeg);
        renderer.DrawFilledCircle(camera, w, radius, new Color4(50, 50, 50, 255));
        renderer.DrawCircle(camera, w, radius, new Color4(80, 80, 80, 255), 12);
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static void DrawRotatedTriangle(SpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Color4 color)
    {
        var w1 = center + Rotate(p1, rotationDeg);
        var w2 = center + Rotate(p2, rotationDeg);
        var w3 = center + Rotate(p3, rotationDeg);

        var s1 = camera.WorldToScreen(w1);
        var s2 = camera.WorldToScreen(w2);
        var s3 = camera.WorldToScreen(w3);
        renderer.DrawFilledTriangleScreen(s1.X, s1.Y, s2.X, s2.Y, s3.X, s3.Y, color);
    }

    private static void DrawRotatedQuad(SpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, Color4 color)
    {
        DrawRotatedTriangle(renderer, camera, center, rotationDeg, p1, p2, p3, color);
        DrawRotatedTriangle(renderer, camera, center, rotationDeg, p1, p3, p4, color);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
