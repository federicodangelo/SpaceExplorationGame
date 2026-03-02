using Engine.Core;

namespace Engine.Platform;

/// <summary>
/// Abstraction for texture creation and lifecycle management.
/// </summary>
public interface ITextureManager
{
    /// <summary>Creates a texture from a raw RGBA pixel array.</summary>
    nint CreateTextureFromPixels(byte[] pixels, int width, int height,
        TextureScaleMode scaleMode = TextureScaleMode.Linear);

    /// <summary>Destroys a texture. Safe to call with <see cref="nint.Zero"/>.</summary>
    void DestroyTexture(nint texture);

    /// <summary>Fills a rectangular block of pixels in a pixel array.</summary>
    static void SetPixelBlock(byte[] pixels, int stride, int x, int y, int w, int h,
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
}
