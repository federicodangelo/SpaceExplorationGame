using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player avatar sprite. Owns the avatar texture so future
/// customisation (equipped suit/helmet/boots visuals) can be handled in one place.
/// </summary>
public class AvatarRenderer : IDisposable
{
    private const int AvatarSize = 28;
    private nint _texture;

    public AvatarRenderer(TextureManager textures)
    {
        _texture = GenerateAvatarTexture(textures);
    }

    /// <summary>Renders the avatar at its world position (planet surface or interior).</summary>
    public void Render(SpriteRenderer renderer, Camera camera, Vector2 position)
    {
        // Shadow beneath feet
        var shadowPos = position + new Vector2(0, AvatarSize / 2f - 1);
        renderer.DrawRect(camera, shadowPos, 16, 4, new Color4(0, 0, 0, 60));

        renderer.DrawTexture(camera, _texture, position, AvatarSize, AvatarSize);
    }

    private static nint GenerateAvatarTexture(TextureManager textures)
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];

        // Tiny humanoid sprite
        TextureManager.SetPixelBlock(pixels, size, 6, 1, 4, 3, new Color4(200, 180, 150, 255));   // Head
        TextureManager.SetPixelBlock(pixels, size, 6, 4, 4, 1, new Color4(60, 180, 100, 255));    // Neck
        TextureManager.SetPixelBlock(pixels, size, 5, 5, 6, 4, new Color4(60, 180, 100, 255));    // Torso (green suit)
        TextureManager.SetPixelBlock(pixels, size, 3, 6, 2, 3, new Color4(60, 180, 100, 255));    // Left arm
        TextureManager.SetPixelBlock(pixels, size, 11, 6, 2, 3, new Color4(60, 180, 100, 255));   // Right arm
        TextureManager.SetPixelBlock(pixels, size, 6, 9, 2, 4, new Color4(50, 50, 140, 255));     // Left leg
        TextureManager.SetPixelBlock(pixels, size, 8, 9, 2, 4, new Color4(50, 50, 140, 255));     // Right leg
        TextureManager.SetPixelBlock(pixels, size, 5, 13, 3, 1, new Color4(80, 60, 40, 255));     // Left boot
        TextureManager.SetPixelBlock(pixels, size, 8, 13, 3, 1, new Color4(80, 60, 40, 255));     // Right boot
        // Visor
        TextureManager.SetPixelBlock(pixels, size, 7, 2, 2, 1, new Color4(100, 180, 255, 255));

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
