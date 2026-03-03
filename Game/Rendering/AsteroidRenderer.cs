using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using Engine.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders asteroids using primitive geometry.
/// </summary>
public class AsteroidRenderer
{
    // Cached shapes keyed by seed (entity id). Generated once, reused every frame.
    // Max vertex count is 10; reuse this buffer every draw call to avoid per-frame allocs.
    private readonly Vector2[] _pointsBuffer = new Vector2[10];
    private readonly Vector2[] _shapeBuffer = new Vector2[10];

    // Shapes are generated procedurally per entity so every asteroid looks unique.
    // Fills the provided buffer and returns the vertex count.
    private static int GenerateAsteroidShape(int seed, Vector2[] buffer)
    {
        uint rng = (uint)seed;
        // Vary vertex count: 6–10
        rng = rng * 1664525u + 1013904223u;
        int vertexCount = 6 + (int)(rng % 5u);
        float segAngle = MathF.Tau / vertexCount;
        for (int i = 0; i < vertexCount; i++)
        {
            float baseAngle = i * segAngle;
            // Angular jitter ±30 % of one segment
            rng = rng * 1664525u + 1013904223u;
            float angleJitter = ((rng & 0xFFFFu) / 65535f - 0.5f) * 0.6f * segAngle;
            // Radius jitter: 0.55 – 1.0
            rng = rng * 1664525u + 1013904223u;
            float r = 0.55f + (rng & 0xFFFFu) / 65535f * 0.45f;
            float a = baseAngle + angleJitter;
            buffer[i] = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
        }
        return vertexCount;
    }

    public AsteroidRenderer()
    {
    }

    /// <summary>Renders asteroids from ECS entities (with Transform, Health, AsteroidField).</summary>
    public void RenderAsteroids(ISpriteRenderer renderer, Camera camera,
        World ecsWorld, List<Entity> asteroidEntities, float globalTime)
    {
        foreach (var entity in asteroidEntities)
        {
            if (!ecsWorld.IsAlive(entity)) continue;

            ref var transform = ref ecsWorld.Get<Transform>(entity);
            ref var health = ref ecsWorld.Get<Health>(entity);
            ref var asteroid = ref ecsWorld.Get<AsteroidField>(entity);

            if (health.IsDead) continue;

            // Spinning rotation based on time + unique per-asteroid speed
            float posHash = entity.Id;
            float spinSpeed = 1.5f + MathF.Abs(posHash % 3f); // 1.5-4.5 deg/sec
            float rot = posHash + globalTime * spinSpeed;

            // Scale down visual size as HP drops
            float hpRatio = health.HullPercent;
            float visualSize = (asteroid.Size + 4) * (0.5f + 0.5f * hpRatio);

            // Cull asteroids outside the camera viewport
            if (!camera.DiskOverlapsCamera(transform.Position, visualSize * 0.5f)) continue;

            var resourceColor = ResourceCatalog.Get(asteroid.Resource).Color;

            DrawAsteroidPrimitives(renderer, camera, transform.Position, rot, visualSize,
                hpRatio, resourceColor, globalTime, entity.Id);
        }
    }

    private void DrawAsteroidPrimitives(ISpriteRenderer renderer, Camera camera,
        Vector2 position, float rotationDeg, float size, float hpRatio, Color3 resourceColor,
        float globalTime, int seed)
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

        int vertexCount = GenerateAsteroidShape(seed, _shapeBuffer);
        for (int i = 0; i < vertexCount; i++)
            _pointsBuffer[i] = position + Rotate(_shapeBuffer[i] * radius, rotationDeg);

        var centerScreen = camera.WorldToScreen(position);
        for (int i = 0; i < vertexCount; i++)
        {
            var p1 = camera.WorldToScreen(_pointsBuffer[i]);
            var p2 = camera.WorldToScreen(_pointsBuffer[(i + 1) % vertexCount]);
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

        // Resource shimmer pulse
        float shimmer = (MathF.Sin(globalTime * 2.5f + position.X * 0.01f + position.Y * 0.013f) + 1f) * 0.5f;
        if (shimmer > 0.6f)
        {
            byte shimmerA = (byte)((shimmer - 0.6f) * 2.5f * 40);
            renderer.DrawFilledCircle(camera, position, radius * 0.6f,
                new Color4(resourceColor.R, resourceColor.G, resourceColor.B, shimmerA));
        }
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
