using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders asteroids using primitive geometry.
/// </summary>
public class AsteroidRenderer : IDisposable
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

            DrawAsteroidPrimitives(renderer, camera, transform.Position, rot, visualSize, hpRatio);
        }
    }

    private static void DrawAsteroidPrimitives(SpriteRenderer renderer, Camera camera,
        Vector2 position, float rotationDeg, float size, float hpRatio)
    {
        float radius = size * 0.5f;
        byte tone = (byte)(110 + 40 * hpRatio);
        var fill = new Color4((byte)(tone + 18), tone, (byte)(tone - 12), 255);
        var edge = new Color4((byte)(tone - 22), (byte)(tone - 28), (byte)(tone - 36), 255);

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
        Vector2 crater1 = position + Rotate(new Vector2(-radius * 0.22f, -radius * 0.08f), rotationDeg);
        Vector2 crater2 = position + Rotate(new Vector2(radius * 0.18f, radius * 0.16f), rotationDeg + 35f);
        renderer.DrawFilledCircle(camera, crater1, craterR, new Color4((byte)(tone - 35), (byte)(tone - 35), (byte)(tone - 40), 210));
        renderer.DrawFilledCircle(camera, crater2, craterR * 0.8f, new Color4((byte)(tone - 28), (byte)(tone - 30), (byte)(tone - 35), 190));
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
