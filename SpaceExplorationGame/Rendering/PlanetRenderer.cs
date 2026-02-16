using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Creates and renders planet/moon textures. Tracks all created textures
/// so they can be bulk-destroyed when leaving a solar system.
/// </summary>
public class PlanetRenderer : IDisposable
{
    private readonly TextureManager _textures;
    private readonly List<nint> _createdTextures = [];

    public PlanetRenderer(TextureManager textures)
    {
        _textures = textures;
    }

    /// <summary>Creates a planet texture with shading and surface detail. The texture is tracked for later cleanup.</summary>
    public nint CreateTexture(int size, Color3 color, uint detailSeed)
    {
        var (r, g, b) = color;
        var pixels = new byte[size * size * 4]; // RGBA
        float center = size / 2f;
        float radius = size / 2f - 1;
        var rng = new SeededRandom(detailSeed);

        // Generate some surface noise
        var noise = new float[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                noise[x, y] = rng.NextFloat(-0.15f, 0.15f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int idx = (y * size + x) * 4;

                if (dist <= radius)
                {
                    // Sphere shading: light from top-left
                    float nx = dx / radius;
                    float ny = dy / radius;
                    float nz = MathF.Sqrt(MathF.Max(0, 1f - nx * nx - ny * ny));

                    // Diffuse lighting (light from top-left-front)
                    float lightX = -0.4f, lightY = -0.5f, lightZ = 0.7f;
                    float len = MathF.Sqrt(lightX * lightX + lightY * lightY + lightZ * lightZ);
                    lightX /= len; lightY /= len; lightZ /= len;
                    float diffuse = MathF.Max(0, nx * lightX + ny * lightY + nz * lightZ);

                    // Ambient + diffuse
                    float brightness = 0.25f + 0.75f * diffuse;

                    // Surface noise variation
                    float n = noise[x, y];

                    float fr = Math.Clamp(r * brightness + r * n, 0, 255);
                    float fg = Math.Clamp(g * brightness + g * n, 0, 255);
                    float fb = Math.Clamp(b * brightness + b * n, 0, 255);

                    // Edge darkening (atmosphere effect)
                    float edge = 1f - MathF.Pow(dist / radius, 4);
                    fr *= edge + (1 - edge) * 0.3f;
                    fg *= edge + (1 - edge) * 0.3f;
                    fb *= edge + (1 - edge) * 0.3f;

                    // Specular highlight
                    float specular = MathF.Pow(MathF.Max(0, nz * lightZ + nx * lightX + ny * lightY), 16);
                    fr = Math.Min(255, fr + 60 * specular);
                    fg = Math.Min(255, fg + 60 * specular);
                    fb = Math.Min(255, fb + 60 * specular);

                    pixels[idx + 0] = (byte)fr;  // R
                    pixels[idx + 1] = (byte)fg;  // G
                    pixels[idx + 2] = (byte)fb;  // B
                    pixels[idx + 3] = 255;        // A
                }
                else if (dist <= radius + 1)
                {
                    // Anti-alias edge
                    float alpha = Math.Clamp(radius + 1 - dist, 0, 1);
                    pixels[idx + 0] = (byte)(r * 0.3f);
                    pixels[idx + 1] = (byte)(g * 0.3f);
                    pixels[idx + 2] = (byte)(b * 0.3f);
                    pixels[idx + 3] = (byte)(alpha * 120);
                }
                else
                {
                    pixels[idx + 0] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                }
            }
        }

        var tex = _textures.CreateTextureFromPixels(pixels, size, size);
        _createdTextures.Add(tex);
        return tex;
    }

    /// <summary>Renders planets with textures, settlement indicators, rings, moon orbits, and moons.</summary>
    public void RenderPlanetsAndMoons(SpriteRenderer renderer, Camera camera,
        World ecsWorld, List<PlanetData> planets,
        List<Entity> planetEntities, List<List<Entity>> moonEntities,
        List<nint> planetTextures, List<List<nint>> moonTextures)
    {
        for (int i = 0; i < planets.Count; i++)
        {
            if (i >= planetEntities.Count) break;
            var pTransform = ecsWorld.Get<Transform>(planetEntities[i]);
            var p = planets[i];
            int texRenderSize = (int)(p.Radius * 2) + 4;

            // Planet texture
            if (i < planetTextures.Count)
            {
                renderer.DrawTexture(camera, planetTextures[i], pTransform.Position,
                    texRenderSize, texRenderSize);
            }

            // Settlement indicator (small diamond below planet)
            if (p.HasSettlement)
            {
                var indicatorPos = pTransform.Position + new Vector2(0, p.Radius + 6);
                renderer.DrawFilledCircle(camera, indicatorPos, 3f, new Color4(255, 210, 200, 220));
            }

            // Rings
            if (p.HasRings)
            {
                renderer.DrawCircle(camera, pTransform.Position, p.Radius * 1.5f,
                    p.Color.WithAlpha(120), 48);
                renderer.DrawCircle(camera, pTransform.Position, p.Radius * 1.8f,
                    p.Color.WithAlpha(80), 48);
            }

            // Moon orbit lines
            foreach (var moon in p.Moons)
            {
                renderer.DrawCircle(camera, pTransform.Position, moon.OrbitRadius, new Color3(20, 20, 40), 24);
            }

            // Moon textures
            if (i < moonEntities.Count)
            {
                for (int m = 0; m < moonEntities[i].Count; m++)
                {
                    var moonTransform = ecsWorld.Get<Transform>(moonEntities[i][m]);
                    var moon = p.Moons[m];
                    int moonTexSize = (int)(moon.Radius * 2) + 2;
                    if (i < moonTextures.Count && m < moonTextures[i].Count)
                    {
                        renderer.DrawTexture(camera, moonTextures[i][m], moonTransform.Position,
                            moonTexSize, moonTexSize);
                    }
                }
            }
        }
    }

    /// <summary>Destroys a specific texture and removes it from tracking.</summary>
    public void DestroyTexture(nint texture)
    {
        if (texture != nint.Zero)
        {
            SDL.DestroyTexture(texture);
            _createdTextures.Remove(texture);
        }
    }

    /// <summary>Destroys all tracked textures. Call when leaving a solar system.</summary>
    public void DestroyAll()
    {
        foreach (var tex in _createdTextures)
        {
            SDL.DestroyTexture(tex);
        }
        _createdTextures.Clear();
    }

    public void Dispose()
    {
        DestroyAll();
        GC.SuppressFinalize(this);
    }
}
