using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders the player avatar using primitive shapes for a retro look.
/// </summary>
public class AvatarRenderer : IDisposable
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

        float s = AvatarSize / 28f;

        renderer.DrawRect(camera, position + new Vector2(0f, -10f * s), (int)(6f * s), (int)(6f * s), new Color4(200, 180, 150, 255));
        renderer.DrawRect(camera, position + new Vector2(0f, -6f * s), (int)(6f * s), (int)(2f * s), new Color4(60, 180, 100, 255));
        renderer.DrawRect(camera, position + new Vector2(0f, -1f * s), (int)(8f * s), (int)(8f * s), new Color4(60, 180, 100, 255));

        renderer.DrawRect(camera, position + new Vector2(-5f * s, -1f * s), (int)(3f * s), (int)(6f * s), new Color4(60, 180, 100, 255));
        renderer.DrawRect(camera, position + new Vector2(5f * s, -1f * s), (int)(3f * s), (int)(6f * s), new Color4(60, 180, 100, 255));

        renderer.DrawRect(camera, position + new Vector2(-2f * s, 7f * s), (int)(3f * s), (int)(8f * s), new Color4(50, 50, 140, 255));
        renderer.DrawRect(camera, position + new Vector2(2f * s, 7f * s), (int)(3f * s), (int)(8f * s), new Color4(50, 50, 140, 255));

        renderer.DrawRect(camera, position + new Vector2(-2.5f * s, 12f * s), (int)(4f * s), (int)(2f * s), new Color4(80, 60, 40, 255));
        renderer.DrawRect(camera, position + new Vector2(2.5f * s, 12f * s), (int)(4f * s), (int)(2f * s), new Color4(80, 60, 40, 255));

        renderer.DrawFilledCircle(camera, position + new Vector2(0f, -10f * s), 1.3f * s, new Color4(100, 180, 255, 240));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
