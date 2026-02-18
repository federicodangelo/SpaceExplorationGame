using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders asteroids. Owns the singleton asteroid texture.
/// </summary>
public class AsteroidRenderer : IDisposable
{
    private readonly TextureManager _textures;
    private nint _texture;

    public AsteroidRenderer(TextureManager textures)
    {
        _textures = textures;
        _texture = GenerateAsteroidTexture(textures);
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

            renderer.DrawTexture(camera, _texture, transform.Position, (int)visualSize, (int)visualSize, rot);
        }
    }

    private static nint GenerateAsteroidTexture(TextureManager textures)
    {
        const int size = 12;
        var pixels = new byte[size * size * 4];

        // Irregular rocky blob
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 6;
                int cy = y - 6;
                float dist = MathF.Sqrt(cx * cx + cy * cy);

                // Irregular radius
                float angle = MathF.Atan2(cy, cx);
                float r = 4f + MathF.Sin(angle * 3) * 1f + MathF.Cos(angle * 5) * 0.5f;

                if (dist <= r)
                {
                    float shade = 0.5f + 0.5f * (1f - dist / r);
                    // Gray-brown rock with variation
                    float vary = MathF.Sin(x * 2.5f + y * 1.7f) * 0.15f;
                    pixels[idx + 0] = (byte)(140 * (shade + vary));
                    pixels[idx + 1] = (byte)(120 * (shade + vary));
                    pixels[idx + 2] = (byte)(100 * (shade + vary));
                    pixels[idx + 3] = 255;
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, size, size);
    }

    public void Dispose()
    {
        _textures.DestroyTexture(_texture);
        _texture = nint.Zero;
        GC.SuppressFinalize(this);
    }
}
