using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player's ground vehicle. Owns the vehicle texture so future
/// customisation (chassis/lights visuals) can be handled in one place.
/// </summary>
public class VehicleRenderer : IDisposable
{
    private const int VehicleSize = 40;
    private nint _texture;

    public VehicleRenderer(TextureManager textures)
    {
        _texture = GenerateVehicleTexture(textures);
    }

    /// <summary>Renders the vehicle with texture and optional label when not mounted.</summary>
    public void Render(SpriteRenderer renderer, Camera camera,
        Vector2 position, float rotation, bool isMounted)
    {
        // Vehicle texture points up (north) so add 90° offset to align with 0°=right convention
        renderer.DrawTexture(camera, _texture, position, VehicleSize, VehicleSize, rotation + 90f);
        if (!isMounted)
        {
            renderer.DrawText(camera, position + new Vector2(-20, 14), "VEHICLE", 180, 160, 100);
        }
    }

    private static nint GenerateVehicleTexture(TextureManager textures)
    {
        const int size = 20;
        var pixels = new byte[size * size * 4];

        // Top-down 4-wheel rover with roll cage
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = (y * size + x) * 4;
                int cx = x - 10;
                int cy = y - 10;

                // Main body (rounded rectangle)
                if (Math.Abs(cx) <= 5 && Math.Abs(cy) <= 7)
                {
                    // Body color: warm gray-orange
                    float shade = 1f - Math.Abs(cy) / 10f * 0.2f;
                    pixels[idx + 0] = (byte)(180 * shade);
                    pixels[idx + 1] = (byte)(140 * shade);
                    pixels[idx + 2] = (byte)(80 * shade);
                    pixels[idx + 3] = 255;

                    // Cockpit windshield (top)
                    if (cy <= -3 && Math.Abs(cx) <= 3)
                    {
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 180;
                        pixels[idx + 2] = 230;
                        pixels[idx + 3] = 255;
                    }
                    // Roll cage bars
                    else if (Math.Abs(cx) == 5 || (cy == 0 && Math.Abs(cx) <= 5))
                    {
                        pixels[idx + 0] = 100;
                        pixels[idx + 1] = 100;
                        pixels[idx + 2] = 110;
                        pixels[idx + 3] = 255;
                    }
                }
                // Wheels (4 corners)
                else if (Math.Abs(cx) >= 5 && Math.Abs(cx) <= 8 &&
                         (Math.Abs(cy - 5) <= 2 || Math.Abs(cy + 5) <= 2))
                {
                    pixels[idx + 0] = 50;
                    pixels[idx + 1] = 50;
                    pixels[idx + 2] = 50;
                    pixels[idx + 3] = 255;

                    // Wheel tread highlight
                    if (Math.Abs(cx) == 6 || Math.Abs(cx) == 7)
                    {
                        pixels[idx + 0] = 70;
                        pixels[idx + 1] = 70;
                        pixels[idx + 2] = 70;
                    }
                }
                // Headlights (front)
                else if (cy == -8 && (Math.Abs(cx) == 3 || Math.Abs(cx) == 4))
                {
                    pixels[idx + 0] = 255;
                    pixels[idx + 1] = 255;
                    pixels[idx + 2] = 200;
                    pixels[idx + 3] = 255;
                }
                // Tail lights (rear)
                else if (cy == 8 && (Math.Abs(cx) == 3 || Math.Abs(cx) == 4))
                {
                    pixels[idx + 0] = 255;
                    pixels[idx + 1] = 50;
                    pixels[idx + 2] = 50;
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
