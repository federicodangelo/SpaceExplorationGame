using SDL3;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Rendering.Base;

/// <summary>
/// Provides low-level texture creation utilities used by entity renderers.
/// Each renderer owns its own textures; this class only wraps the SDL renderer handle.
/// </summary>
public class TextureManager : IDisposable
{
    private readonly nint _renderer;

    public TextureManager(nint renderer)
    {
        _renderer = renderer;
    }

    /// <summary>Creates an SDL texture from a raw RGBA pixel array. Used by entity renderers to generate their own textures.</summary>
    public nint CreateTextureFromPixels(byte[] pixels, int width, int height,
        SDL.ScaleMode scaleMode = SDL.ScaleMode.Linear)
    {
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                var surface = SDL.CreateSurfaceFrom(width, height,
                    SDL.PixelFormat.ABGR8888, (nint)ptr, width * 4);

                if (surface == nint.Zero)
                    throw new Exception($"Failed to create surface: {SDL.GetError()}");

                var texture = SDL.CreateTextureFromSurface(_renderer, surface);
                SDL.DestroySurface(surface);

                if (texture == nint.Zero)
                    throw new Exception($"Failed to create texture: {SDL.GetError()}");

                // Enable alpha blending and configure filtering on the texture
                SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
                SDL.SetTextureScaleMode(texture, scaleMode);

                return texture;
            }
        }
    }

    /// <summary>Fills a rectangular block of pixels in a pixel array.</summary>
    public static void SetPixelBlock(byte[] pixels, int stride, int x, int y, int w, int h,
        Color4 color)
    {
        for (int py = y; py < y + h; py++)
        {
            for (int px = x; px < x + w; px++)
            {
                if (px >= 0 && px < stride && py >= 0 && py < stride)
                {
                    int idx = (py * stride + px) * 4;
                    pixels[idx + 0] = color.R;
                    pixels[idx + 1] = color.G;
                    pixels[idx + 2] = color.B;
                    pixels[idx + 3] = color.A;
                }
            }
        }
    }

    /// <summary>Destroys an SDL texture. Safe to call with <see cref="nint.Zero"/>.</summary>
    public void DestroyTexture(nint texture)
    {
        if (texture != nint.Zero)
            SDL.DestroyTexture(texture);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
