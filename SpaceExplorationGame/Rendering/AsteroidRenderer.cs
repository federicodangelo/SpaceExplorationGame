using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders asteroids using primitive geometry.
/// </summary>
public class AsteroidRenderer
{
    private static readonly Vector2[] BaseShape =
    [
        new Vector2(1.00f, 0.00f),
        new Vector2(0.62f, 0.52f),
        new Vector2(0.12f, 0.92f),
        new Vector2(-0.55f, 0.72f),
        new Vector2(-0.96f, 0.12f),
        new Vector2(-0.72f, -0.56f),
        new Vector2(-0.18f, -0.92f),
        new Vector2(0.68f, -0.62f)
    ];

    public AsteroidRenderer()
    {
    }

    /// <summary>Renders asteroids from ECS entities (with Transform, Health, AsteroidField).</summary>
    public void RenderAsteroids(SpriteRenderer renderer, Camera camera,
        World ecsWorld, List<Entity> asteroidEntities)
    {
        foreach (var entity in asteroidEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;

            ref var transform = ref ecsWorld.Get<Transform>(entity);
            ref var health = ref ecsWorld.Get<Health>(entity);
            ref var asteroid = ref ecsWorld.Get<AsteroidField>(entity);

            if (health.IsDead) continue;

            float rot = MathF.Atan2(transform.Position.Y, transform.Position.X) * 180f / MathF.PI * 2f;

            // Scale down visual size as HP drops
            float hpRatio = health.HullPercent;
            float visualSize = (asteroid.Size + 4) * (0.5f + 0.5f * hpRatio);
            var resourceColor = ResourceCatalog.Get(asteroid.Resource).Color;

            DrawAsteroidPrimitives(renderer, camera, transform.Position, rot, visualSize, hpRatio, resourceColor);
        }
    }

    private static void DrawAsteroidPrimitives(SpriteRenderer renderer, Camera camera,
        Vector2 position, float rotationDeg, float size, float hpRatio, Color3 resourceColor)
    {
        float radius = size * 0.5f;
        byte tone = (byte)(110 + 40 * hpRatio);
        var fill = new Color4(
            BlendToByte((byte)(tone + 18), resourceColor.R, 0.36f),
            BlendToByte(tone, resourceColor.G, 0.36f),
            BlendToByte((byte)(tone - 12), resourceColor.B, 0.36f),
            255);
        var edge = new Color4(
            BlendToByte((byte)(tone - 22), resourceColor.R, 0.24f),
            BlendToByte((byte)(tone - 28), resourceColor.G, 0.24f),
            BlendToByte((byte)(tone - 36), resourceColor.B, 0.24f),
            255);
        var veinColor = new Color4(
            BlendToByte((byte)(tone - 20), resourceColor.R, 0.82f),
            BlendToByte((byte)(tone - 20), resourceColor.G, 0.82f),
            BlendToByte((byte)(tone - 20), resourceColor.B, 0.82f),
            235);
        var coreGlow = new Color4(
            BlendToByte((byte)(tone + 14), resourceColor.R, 0.74f),
            BlendToByte((byte)(tone + 14), resourceColor.G, 0.74f),
            BlendToByte((byte)(tone + 14), resourceColor.B, 0.74f),
            225);
        var feedbackOutlineOuter = new Color4(12, 12, 16, 220);
        var feedbackOutlineInner = new Color4(235, 235, 245, 135);

        Vector2[] points = new Vector2[BaseShape.Length];
        for (int i = 0; i < BaseShape.Length; i++)
        {
            float localRot = rotationDeg + i * 6f;
            points[i] = position + Rotate(BaseShape[i] * radius, localRot);
        }

        var centerScreen = camera.WorldToScreen(position);
        for (int i = 0; i < points.Length; i++)
        {
            var p1 = camera.WorldToScreen(points[i]);
            var p2 = camera.WorldToScreen(points[(i + 1) % points.Length]);
            renderer.DrawFilledTriangleScreen(centerScreen.X, centerScreen.Y, p1.X, p1.Y, p2.X, p2.Y, fill);
            renderer.DrawLineScreen(p1.X, p1.Y, p2.X, p2.Y, edge);
        }

        float craterR = MathF.Max(0.8f, size * 0.11f);
        float outlinePad = MathF.Max(0.25f, size * 0.014f);
        Vector2 crater1 = position + Rotate(new Vector2(-radius * 0.22f, -radius * 0.08f), rotationDeg);
        Vector2 crater2 = position + Rotate(new Vector2(radius * 0.18f, radius * 0.16f), rotationDeg + 35f);
        renderer.DrawFilledCircle(camera, crater1, craterR + outlinePad, feedbackOutlineOuter);
        renderer.DrawFilledCircle(camera, crater1, craterR + outlinePad * 0.55f, feedbackOutlineInner);
        renderer.DrawFilledCircle(camera, crater1, craterR, veinColor);
        float crater2R = craterR * 0.8f;
        renderer.DrawFilledCircle(camera, crater2, crater2R + outlinePad, feedbackOutlineOuter);
        renderer.DrawFilledCircle(camera, crater2, crater2R + outlinePad * 0.55f, feedbackOutlineInner);
        renderer.DrawFilledCircle(camera, crater2, crater2R, veinColor);

        float coreR = MathF.Max(0.6f, size * 0.07f);
        Vector2 core = position + Rotate(new Vector2(radius * 0.02f, -radius * 0.03f), rotationDeg - 12f);
        renderer.DrawFilledCircle(camera, core, coreR + outlinePad * 0.9f, feedbackOutlineOuter);
        renderer.DrawFilledCircle(camera, core, coreR + outlinePad * 0.45f, feedbackOutlineInner);
        renderer.DrawFilledCircle(camera, core, coreR, coreGlow);
    }

    private static byte BlendToByte(byte from, byte to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return (byte)Math.Clamp(float.Lerp(from, to, t), 0f, 255f);
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
}
