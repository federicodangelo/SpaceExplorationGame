using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Creates and renders star textures. Tracks all created textures
/// so they can be bulk-destroyed when leaving a solar system or closing the galaxy map.
/// </summary>
public class StarRenderer : IDisposable
{
    private readonly TextureManager _textures;
    private readonly List<nint> _createdTextures = [];

    public StarRenderer(TextureManager textures)
    {
        _textures = textures;
    }

    /// <summary>Creates a star texture with glow gradient. The texture is tracked for later cleanup.</summary>
    public nint CreateTexture(int size, byte r, byte g, byte b)
    {
        var pixels = new byte[size * size * 4];
        float center = size / 2f;
        float coreRadius = size * 0.2f;
        float glowRadius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int idx = (y * size + x) * 4;

                if (dist <= coreRadius)
                {
                    // Bright core (white to star color)
                    float t = dist / coreRadius;
                    pixels[idx + 0] = (byte)(255 - (255 - r) * t * 0.5f);
                    pixels[idx + 1] = (byte)(255 - (255 - g) * t * 0.5f);
                    pixels[idx + 2] = (byte)(255 - (255 - b) * t * 0.5f);
                    pixels[idx + 3] = 255;
                }
                else if (dist <= glowRadius)
                {
                    // Glow falloff
                    float t = (dist - coreRadius) / (glowRadius - coreRadius);
                    float intensity = MathF.Pow(1f - t, 2.5f);
                    pixels[idx + 0] = (byte)(r * intensity);
                    pixels[idx + 1] = (byte)(g * intensity);
                    pixels[idx + 2] = (byte)(b * intensity);
                    pixels[idx + 3] = (byte)(255 * intensity);
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

    /// <summary>Renders a star at its world position.</summary>
    public void Render(SpriteRenderer renderer, Camera camera,
        nint starTexture, Vector2 starCenter, float starDisplayRadius)
    {
        renderer.DrawTexture(camera, starTexture, starCenter,
            (int)(starDisplayRadius * 3), (int)(starDisplayRadius * 3));
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

    /// <summary>Destroys all tracked textures. Call when leaving a solar system or closing the galaxy map.</summary>
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
