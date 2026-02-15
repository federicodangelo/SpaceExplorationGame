using SDL3;

namespace SpaceExplorationGame.Rendering;

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
    public nint CreateTextureFromPixels(byte[] pixels, int width, int height)
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

                // Enable alpha blending on the texture
                SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);

                return texture;
            }
        }
    }

    /// <summary>Fills a rectangular block of pixels in a pixel array.</summary>
    public static void SetPixelBlock(byte[] pixels, int stride, int x, int y, int w, int h,
        byte r, byte g, byte b, byte a)
    {
        for (int py = y; py < y + h; py++)
        {
            for (int px = x; px < x + w; px++)
            {
                if (px >= 0 && px < stride && py >= 0 && py < stride)
                {
                    int idx = (py * stride + px) * 4;
                    pixels[idx + 0] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b;
                    pixels[idx + 3] = a;
                }
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
