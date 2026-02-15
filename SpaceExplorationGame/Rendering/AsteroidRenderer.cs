using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders asteroids. Owns the singleton asteroid texture.
/// </summary>
public class AsteroidRenderer : IDisposable
{
    private nint _texture;

    public AsteroidRenderer(TextureManager textures)
    {
        _texture = GenerateAsteroidTexture(textures);
    }

    /// <summary>Computes the world position of an asteroid at a given time.</summary>
    public static Vector2 GetAsteroidPosition(MineableAsteroid asteroid, Vector2 center, double globalTime)
    {
        float angle = asteroid.BaseAngle + asteroid.Speed * (float)globalTime;
        return center + new Vector2(MathF.Cos(angle) * asteroid.Radius, MathF.Sin(angle) * asteroid.Radius);
    }

    /// <summary>Renders asteroids orbiting around a center point.</summary>
    public void RenderAsteroids(SpriteRenderer renderer, Camera camera,
        List<MineableAsteroid> asteroids,
        Vector2 starCenter, double globalTime)
    {
        float asteroidTime = (float)globalTime;
        foreach (var asteroid in asteroids)
        {
            if (asteroid.Depleted) continue;

            float angle = asteroid.BaseAngle + asteroid.Speed * asteroidTime;
            var pos = starCenter + new Vector2(MathF.Cos(angle) * asteroid.Radius, MathF.Sin(angle) * asteroid.Radius);
            float rot = angle * 180f / MathF.PI * 2f;

            // Scale down visual size as HP drops
            float hpRatio = asteroid.Hp / asteroid.MaxHp;
            float visualSize = (asteroid.Size + 4) * (0.5f + 0.5f * hpRatio);

            renderer.DrawTexture(camera, _texture, pos, (int)visualSize, (int)visualSize, rot);
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
        if (_texture != nint.Zero)
        {
            SDL.DestroyTexture(_texture);
            _texture = nint.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
