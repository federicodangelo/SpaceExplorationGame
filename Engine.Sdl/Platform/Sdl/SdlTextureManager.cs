using SDL3;
using Engine.Platform.Base;

namespace Engine.Platform.Sdl;

/// <summary>
/// SDL3 implementation of texture creation utilities.
/// Each renderer owns its own textures; this class only wraps the SDL renderer handle.
/// </summary>
public class SdlTextureManager : BaseTextureManager
{
    private readonly nint _renderer;

    public SdlTextureManager(nint renderer)
    {
        _renderer = renderer;
    }

    /// <summary>Creates an SDL texture from a raw RGBA pixel array. Used by entity renderers to generate their own textures.</summary>
    public override nint CreateTextureFromPixels(byte[] pixels, int width, int height,
        TextureScaleMode scaleMode = TextureScaleMode.Linear)
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
                SDL.SetTextureScaleMode(texture, scaleMode == TextureScaleMode.Linear ? SDL.ScaleMode.Linear : SDL.ScaleMode.Nearest);

                return texture;
            }
        }
    }

    /// <summary>Destroys an SDL texture. Safe to call with <see cref="nint.Zero"/>.</summary>
    public override void DestroyTexture(nint texture)
    {
        if (texture != nint.Zero)
            SDL.DestroyTexture(texture);
    }
}
