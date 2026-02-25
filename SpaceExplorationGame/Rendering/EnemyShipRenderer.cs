using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders NPC ships (pirates, traders, patrols) using primitive geometry.
/// </summary>
public class EnemyShipRenderer : IDisposable
{
    public EnemyShipRenderer()
    {
    }

    /// <summary>Render an NPC ship at a world position with rotation.</summary>
    public void Render(SpriteRenderer renderer, Camera camera, Vector2 position, float rotation,
        Faction faction, int size)
    {
        (Color4 hull, Color4 accent, Color4 wing) = faction switch
        {
            Faction.Pirate => (new Color4(170, 55, 55, 255), new Color4(255, 110, 60, 255), new Color4(120, 35, 35, 255)),
            Faction.Trader => (new Color4(175, 140, 80, 255), new Color4(120, 200, 220, 255), new Color4(145, 115, 65, 255)),
            Faction.Patrol => (new Color4(80, 140, 220, 255), new Color4(200, 220, 255, 255), new Color4(60, 100, 190, 255)),
            _ => (new Color4(170, 55, 55, 255), new Color4(255, 110, 60, 255), new Color4(120, 35, 35, 255))
        };

        float scale = size / 30f;

        DrawRotatedQuad(renderer, camera, position, rotation,
            new Vector2(-10f * scale, -4f * scale),
            new Vector2(9f * scale, -4f * scale),
            new Vector2(11f * scale, 4f * scale),
            new Vector2(-10f * scale, 4f * scale),
            hull);

        DrawRotatedTriangle(renderer, camera, position, rotation,
            new Vector2(9f * scale, -4f * scale),
            new Vector2(15f * scale, 0f),
            new Vector2(9f * scale, 4f * scale),
            accent);

        DrawRotatedTriangle(renderer, camera, position, rotation,
            new Vector2(-4f * scale, -4f * scale),
            new Vector2(-14f * scale, -9f * scale),
            new Vector2(-8f * scale, -1f * scale),
            wing);

        DrawRotatedTriangle(renderer, camera, position, rotation,
            new Vector2(-4f * scale, 4f * scale),
            new Vector2(-14f * scale, 9f * scale),
            new Vector2(-8f * scale, 1f * scale),
            wing);

        Vector2 cockpitOffset = Rotate(new Vector2(5f * scale, 0f), rotation);
        renderer.DrawFilledCircle(camera, position + cockpitOffset, 2f * scale, accent.WithAlpha(220));

        Vector2 engineOffset = Rotate(new Vector2(-13f * scale, 0f), rotation);
        renderer.DrawFilledCircle(camera, position + engineOffset, 1.8f * scale, new Color4(255, 170, 70, 170));
    }

    /// <summary>Render a health bar above an NPC ship.</summary>
    public void RenderHealthBar(SpriteRenderer renderer, Camera camera, Vector2 position,
        float hullPercent, float shieldPercent, float maxShield, int shipSize)
    {
        float barWidth = shipSize * 1.2f;
        float barHeight = 3f;
        var barPos = position - new Vector2(barWidth / 2f, shipSize / 2f + 8f);

        // Hull bar (red/green)
        var screenPos = camera.WorldToScreen(barPos);
        float zoom = camera.Zoom;
        float w = barWidth * zoom;
        float h = barHeight * zoom;

        // Background
        renderer.DrawRectScreen(screenPos.X, screenPos.Y, w, h, new Color4(40, 40, 40, 180));
        // Hull fill
        byte hullR = hullPercent > 0.5f ? (byte)(255 * (1 - hullPercent) * 2) : (byte)255;
        byte hullG = hullPercent > 0.5f ? (byte)255 : (byte)(255 * hullPercent * 2);
        renderer.DrawRectScreen(screenPos.X, screenPos.Y, w * hullPercent, h, new Color4(hullR, hullG, 0, 200));

        // Shield bar (if has shields)
        if (maxShield > 0)
        {
            float shieldY = screenPos.Y - h - 1;
            renderer.DrawRectScreen(screenPos.X, shieldY, w, h, new Color4(40, 40, 60, 180));
            renderer.DrawRectScreen(screenPos.X, shieldY, w * shieldPercent, h, new Color4(80, 160, 255, 200));
        }
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * (MathF.PI / 180f);
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static void DrawRotatedTriangle(SpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Color4 color)
    {
        var w1 = center + Rotate(p1, rotationDeg);
        var w2 = center + Rotate(p2, rotationDeg);
        var w3 = center + Rotate(p3, rotationDeg);

        var s1 = camera.WorldToScreen(w1);
        var s2 = camera.WorldToScreen(w2);
        var s3 = camera.WorldToScreen(w3);
        renderer.DrawFilledTriangleScreen(s1.X, s1.Y, s2.X, s2.Y, s3.X, s3.Y, color);
    }

    private static void DrawRotatedQuad(SpriteRenderer renderer, Camera camera, Vector2 center,
        float rotationDeg, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, Color4 color)
    {
        DrawRotatedTriangle(renderer, camera, center, rotationDeg, p1, p2, p3, color);
        DrawRotatedTriangle(renderer, camera, center, rotationDeg, p1, p3, p4, color);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
