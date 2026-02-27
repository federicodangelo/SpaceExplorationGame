using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player avatar using primitive shapes for a retro look.
/// </summary>
public class AvatarRenderer
{
    private const float AvatarSize = 28f;

    public AvatarRenderer()
    {
    }

    /// <summary>Renders the avatar at its world position (planet surface or interior).</summary>
    public void Render(SpriteRenderer renderer, Camera camera, Vector2 position)
    {
        // Shadow beneath feet
        var shadowPos = position + new Vector2(0, AvatarSize / 2f - 1f);
        renderer.DrawRect(camera, shadowPos, 16, 4, new Color4(0, 0, 0, 60));

        float scale = AvatarSize / 28f;

        renderer.DrawRect(camera, position + new Vector2(0f, -10f * scale), (int)(6f * scale), (int)(6f * scale), new Color4(200, 180, 150, 255));
        renderer.DrawRect(camera, position + new Vector2(0f, -6f * scale), (int)(6f * scale), (int)(2f * scale), new Color4(60, 180, 100, 255));
        renderer.DrawRect(camera, position + new Vector2(0f, -1f * scale), (int)(8f * scale), (int)(8f * scale), new Color4(60, 180, 100, 255));

        renderer.DrawRect(camera, position + new Vector2(-5f * scale, -1f * scale), (int)(3f * scale), (int)(6f * scale), new Color4(60, 180, 100, 255));
        renderer.DrawRect(camera, position + new Vector2(5f * scale, -1f * scale), (int)(3f * scale), (int)(6f * scale), new Color4(60, 180, 100, 255));

        renderer.DrawRect(camera, position + new Vector2(-2f * scale, 7f * scale), (int)(3f * scale), (int)(8f * scale), new Color4(50, 50, 140, 255));
        renderer.DrawRect(camera, position + new Vector2(2f * scale, 7f * scale), (int)(3f * scale), (int)(8f * scale), new Color4(50, 50, 140, 255));

        renderer.DrawRect(camera, position + new Vector2(-2.5f * scale, 12f * scale), (int)(4f * scale), (int)(2f * scale), new Color4(80, 60, 40, 255));
        renderer.DrawRect(camera, position + new Vector2(2.5f * scale, 12f * scale), (int)(4f * scale), (int)(2f * scale), new Color4(80, 60, 40, 255));

        renderer.DrawFilledCircle(camera, position + new Vector2(0f, -10f * scale), 1.3f * scale, new Color4(100, 180, 255, 240));
    }
}

