using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders NPC ships (pirates, traders, patrols) using primitive geometry.
/// </summary>
public class EnemyShipRenderer
{
    public EnemyShipRenderer()
    {
    }

    /// <summary>Render an NPC ship at a world position with rotation.</summary>
    public void Render(SpriteRenderer renderer, Camera camera, Vector2 position, float rotation,
        Faction faction, int size)
    {
        float scale = size / 30f;

        switch (faction)
        {
            case Faction.Trader:
                DrawTrader(renderer, camera, position, rotation, scale);
                break;
            case Faction.Patrol:
                DrawPatrol(renderer, camera, position, rotation, scale);
                break;
            case Faction.Pirate:
            default:
                DrawPirate(renderer, camera, position, rotation, scale);
                break;
        }
    }

    private static void DrawPirate(SpriteRenderer renderer, Camera camera, Vector2 pos, float rot, float s)
    {
        var hull = new Color4(170, 55, 55, 255);
        var accent = new Color4(255, 110, 60, 255);
        var wing = new Color4(120, 35, 35, 255);

        // Aggressive dagger profile
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(-11f * s, -3.8f * s), new Vector2(13f * s, 0f), new Vector2(-11f * s, 3.8f * s), hull);
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(-1f * s, -3.8f * s), new Vector2(-15f * s, -10f * s), new Vector2(-7f * s, -1.2f * s), wing);
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(-1f * s, 3.8f * s), new Vector2(-15f * s, 10f * s), new Vector2(-7f * s, 1.2f * s), wing);
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(5f * s, -2.3f * s), new Vector2(16f * s, 0f), new Vector2(5f * s, 2.3f * s), accent.WithAlpha(220));

        Vector2 cockpit = Rotate(new Vector2(4.2f * s, 0f), rot);
        Vector2 engine = Rotate(new Vector2(-13.2f * s, 0f), rot);
        renderer.DrawFilledCircle(camera, pos + cockpit, 1.8f * s, accent.WithAlpha(220));
        renderer.DrawFilledCircle(camera, pos + engine, 1.9f * s, new Color4(255, 150, 60, 180));
    }

    private static void DrawTrader(SpriteRenderer renderer, Camera camera, Vector2 pos, float rot, float s)
    {
        var hull = new Color4(175, 140, 80, 255);
        var trim = new Color4(145, 115, 65, 255);
        var accent = new Color4(120, 200, 220, 255);

        // Bulky cargo body with side pods
        DrawRotatedQuad(renderer, camera, pos, rot,
            new Vector2(-13f * s, -6f * s), new Vector2(7f * s, -6f * s),
            new Vector2(7f * s, 6f * s), new Vector2(-13f * s, 6f * s), hull);
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(7f * s, -6f * s), new Vector2(14f * s, 0f), new Vector2(7f * s, 6f * s), trim);

        DrawRotatedQuad(renderer, camera, pos, rot,
            new Vector2(-8f * s, -10.5f * s), new Vector2(1f * s, -10.5f * s),
            new Vector2(1f * s, -6f * s), new Vector2(-8f * s, -6f * s), trim.WithAlpha(235));
        DrawRotatedQuad(renderer, camera, pos, rot,
            new Vector2(-8f * s, 6f * s), new Vector2(1f * s, 6f * s),
            new Vector2(1f * s, 10.5f * s), new Vector2(-8f * s, 10.5f * s), trim.WithAlpha(235));

        Vector2 cockpit = Rotate(new Vector2(4.8f * s, 0f), rot);
        Vector2 engineL = Rotate(new Vector2(-13.5f * s, -3f * s), rot);
        Vector2 engineR = Rotate(new Vector2(-13.5f * s, 3f * s), rot);
        renderer.DrawFilledCircle(camera, pos + cockpit, 2.1f * s, accent.WithAlpha(220));
        renderer.DrawFilledCircle(camera, pos + engineL, 1.3f * s, new Color4(255, 180, 80, 160));
        renderer.DrawFilledCircle(camera, pos + engineR, 1.3f * s, new Color4(255, 180, 80, 160));
    }

    private static void DrawPatrol(SpriteRenderer renderer, Camera camera, Vector2 pos, float rot, float s)
    {
        var hull = new Color4(80, 140, 220, 255);
        var wing = new Color4(60, 100, 190, 255);
        var accent = new Color4(200, 220, 255, 255);

        // Sleek interceptor profile
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(-10f * s, -3.6f * s), new Vector2(14f * s, 0f), new Vector2(-10f * s, 3.6f * s), hull);
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(-3f * s, -3.6f * s), new Vector2(-15f * s, -8.5f * s), new Vector2(-8f * s, -0.7f * s), wing);
        DrawRotatedTriangle(renderer, camera, pos, rot,
            new Vector2(-3f * s, 3.6f * s), new Vector2(-15f * s, 8.5f * s), new Vector2(-8f * s, 0.7f * s), wing);
        DrawRotatedQuad(renderer, camera, pos, rot,
            new Vector2(-1f * s, -1.5f * s), new Vector2(7f * s, -1.5f * s),
            new Vector2(7f * s, 1.5f * s), new Vector2(-1f * s, 1.5f * s), accent.WithAlpha(220));

        Vector2 cockpit = Rotate(new Vector2(5.6f * s, 0f), rot);
        Vector2 engine = Rotate(new Vector2(-13f * s, 0f), rot);
        renderer.DrawFilledCircle(camera, pos + cockpit, 1.8f * s, accent.WithAlpha(225));
        renderer.DrawFilledCircle(camera, pos + engine, 1.5f * s, new Color4(100, 180, 255, 170));
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
}
