using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders space stations. Owns the singleton station texture.
/// </summary>
public class StationRenderer : IDisposable
{
    private nint _texture;

    public StationRenderer(TextureManager textures)
    {
        _texture = GenerateStationTexture(textures);
    }

    /// <summary>Renders all stations with a slowly rotating texture.</summary>
    public void RenderStations(SpriteRenderer renderer, Camera camera,
        World ecsWorld, List<Entity> stationEntities, double globalTime)
    {
        for (int i = 0; i < stationEntities.Count; i++)
        {
            var stTransform = ecsWorld.Get<Transform>(stationEntities[i]);
            float stRotation = (float)(globalTime * 10) % 360f;
            renderer.DrawTexture(camera, _texture, stTransform.Position, 280, 280, stRotation);
        }
    }

    private static nint GenerateStationTexture(TextureManager textures)
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 16;
                int cy = y - 16;

                // Central hub (circle)
                float dist = MathF.Sqrt(cx * cx + cy * cy);
                if (dist <= 5)
                {
                    pixels[idx + 0] = 180;
                    pixels[idx + 1] = 180;
                    pixels[idx + 2] = 220;
                    pixels[idx + 3] = 255;
                }
                // Outer ring
                else if (dist >= 9 && dist <= 12)
                {
                    pixels[idx + 0] = 150;
                    pixels[idx + 1] = 150;
                    pixels[idx + 2] = 200;
                    pixels[idx + 3] = 255;
                }
                // Solar panel arms (cross shape)
                else if ((Math.Abs(cx) <= 1 && Math.Abs(cy) <= 14) || (Math.Abs(cy) <= 1 && Math.Abs(cx) <= 14))
                {
                    if (dist > 5 && dist < 9)
                    {
                        // Struts
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 100;
                        pixels[idx + 2] = 130;
                        pixels[idx + 3] = 255;
                    }
                    else if (dist >= 12)
                    {
                        // Panel areas
                        pixels[idx + 0] = 60;
                        pixels[idx + 1] = 80;
                        pixels[idx + 2] = 180;
                        pixels[idx + 3] = 255;
                    }
                }
                // Solar panels (rectangles at cross ends)
                else if (Math.Abs(cx) <= 3 && Math.Abs(cy) >= 12 && Math.Abs(cy) <= 15)
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 160;
                    pixels[idx + 3] = 255;
                }
                else if (Math.Abs(cy) <= 3 && Math.Abs(cx) >= 12 && Math.Abs(cx) <= 15)
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 70;
                    pixels[idx + 2] = 160;
                    pixels[idx + 3] = 255;
                }
                // Docking ring indicators
                if (dist >= 11.5f && dist <= 12.5f)
                {
                    float angle = MathF.Atan2(cy, cx);
                    if ((int)(angle * 8 / MathF.PI) % 2 == 0)
                    {
                        pixels[idx + 0] = 255;
                        pixels[idx + 1] = 200;
                        pixels[idx + 2] = 100;
                        pixels[idx + 3] = 255;
                    }
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
