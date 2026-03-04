using System.Numerics;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Core.Config;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders settlement visuals (buildings, streets, lights, fences) on the planet surface.
/// </summary>
public static class SettlementRenderer
{
    public delegate Rect ProjectRectDelegate(float worldCenterX, float worldCenterY,
        float worldWidth, float worldHeight);

    public delegate Vector2 ProjectPointDelegate(Vector2 worldPos);

    public static void Render(ISpriteRenderer renderer, Camera camera, PlanetSurfaceData surfaceData)
    {
        RenderProjected(renderer, surfaceData,
            (worldCenterX, worldCenterY, worldWidth, worldHeight) =>
            {
                var center = camera.WorldToScreen(new Vector2(worldCenterX, worldCenterY));
                float screenW = worldWidth * camera.Zoom;
                float screenH = worldHeight * camera.Zoom;
                return new Rect(center.X - screenW / 2f, center.Y - screenH / 2f, screenW, screenH);
            },
            worldPos => camera.WorldToScreen(worldPos));
    }

    public static void RenderProjected(ISpriteRenderer renderer, PlanetSurfaceData surfaceData,
        ProjectRectDelegate projectRect, ProjectPointDelegate projectPoint,
        byte alpha = 255)
    {
        foreach (var settlement in surfaceData.Settlements)
        {
            RenderSettlementProjected(renderer, settlement, projectRect, projectPoint, alpha);
        }
    }

    private static void RenderSettlementProjected(ISpriteRenderer renderer,
        SettlementData settlement, ProjectRectDelegate projectRect,
        ProjectPointDelegate projectPoint, byte alpha)
    {
        var layout = settlement.Layout;
        var (px, py, pw, ph) = layout.Perimeter;

        float tileSize = WindowConfig.TileSize;

        DrawProjectedRect(renderer, projectRect,
            (settlement.TileRect.X + settlement.TileRect.Width * 0.5f) * tileSize,
            (settlement.TileRect.Y + settlement.TileRect.Height * 0.5f) * tileSize,
            settlement.TileRect.Width * tileSize,
            settlement.TileRect.Height * tileSize,
            WithAlpha(new Color3(80, 82, 88), alpha));

        foreach (var (stX, stY, stW, stH) in layout.Streets)
        {
            DrawProjectedRect(renderer, projectRect,
                stX + stW / 2f, stY + stH / 2f,
                stW, stH,
                WithAlpha(new Color3(100, 98, 90), alpha));

            if (stW > stH)
            {
                for (float dx = stX + 8; dx < stX + stW - 8; dx += 16)
                {
                    DrawProjectedRect(renderer, projectRect,
                        dx + 4, stY + stH / 2f,
                        8, 2,
                        ScaleAlpha(new Color4(160, 155, 120, 120), alpha));
                }
            }
            else
            {
                for (float dy = stY + 8; dy < stY + stH - 8; dy += 16)
                {
                    DrawProjectedRect(renderer, projectRect,
                        stX + stW / 2f, dy + 4,
                        2, 8,
                        ScaleAlpha(new Color4(160, 155, 120, 120), alpha));
                }
            }
        }

        foreach (var b in layout.Buildings)
        {
            DrawProjectedBuilding(renderer, projectRect, b, alpha);
        }

        foreach (var lightPos in layout.Lights)
        {
            DrawProjectedRect(renderer, projectRect,
                lightPos.X, lightPos.Y,
                6, 6,
                WithAlpha(new Color3(60, 60, 65), alpha));
            DrawProjectedRect(renderer, projectRect,
                lightPos.X, lightPos.Y - 2,
                4, 4,
                ScaleAlpha(new Color4(255, 230, 140, 140), alpha));
            DrawProjectedRect(renderer, projectRect,
                lightPos.X, lightPos.Y - 2,
                2, 2,
                WithAlpha(new Color3(255, 245, 180), alpha));
        }

        DrawProjectedRect(renderer, projectRect,
            px + pw / 2f, py,
            pw, 2,
            WithAlpha(new Color3(140, 140, 150), alpha));
        DrawProjectedRect(renderer, projectRect,
            px + pw / 2f, py + ph,
            pw, 2,
            WithAlpha(new Color3(140, 140, 150), alpha));
        DrawProjectedRect(renderer, projectRect,
            px, py + ph / 2f,
            2, ph,
            WithAlpha(new Color3(140, 140, 150), alpha));
        DrawProjectedRect(renderer, projectRect,
            px + pw, py + ph / 2f,
            2, ph,
            WithAlpha(new Color3(140, 140, 150), alpha));

        float gateX = px + pw / 2f;
        DrawProjectedRect(renderer, projectRect,
            gateX, py,
            24, 2,
            WithAlpha(new Color3(80, 82, 88), alpha));
        DrawProjectedRect(renderer, projectRect,
            gateX - 13, py - 2,
            4, 6,
            WithAlpha(new Color3(160, 160, 170), alpha));
        DrawProjectedRect(renderer, projectRect,
            gateX + 13, py - 2,
            4, 6,
            WithAlpha(new Color3(160, 160, 170), alpha));

        var labelPos = new Vector2(
            (settlement.TileRect.X + settlement.TileRect.Width / 2f) * tileSize,
            settlement.TileRect.Y * tileSize - 18f);

        var labelScreen = projectPoint(labelPos);
        renderer.DrawTextScreen(labelScreen.X, labelScreen.Y,
            settlement.Name, WithAlpha(new Color3(255, 255, 200), alpha));
    }

    private static void DrawProjectedBuilding(ISpriteRenderer renderer,
        ProjectRectDelegate projectRect, SettlementBuilding b, byte alpha)
    {
        DrawProjectedRect(renderer, projectRect,
            b.X + b.W / 2f, b.Y + b.H / 2f,
            b.W, b.H,
            b.Color.WithAlpha(alpha));

        byte roofR = (byte)Math.Clamp(b.Color.R - 25, 0, 255);
        byte roofG = (byte)Math.Clamp(b.Color.G - 25, 0, 255);
        byte roofB = (byte)Math.Clamp(b.Color.B - 25, 0, 255);
        DrawProjectedRect(renderer, projectRect,
            b.X + b.W / 2f, b.Y + 2,
            b.W, 4,
            new Color4(roofR, roofG, roofB, alpha));

        DrawProjectedRect(renderer, projectRect,
            b.X + b.W / 2f, b.Y + b.H - 1,
            b.W, 3,
            ScaleAlpha(new Color4(40, 40, 45, 100), alpha));
        DrawProjectedRect(renderer, projectRect,
            b.X + b.W - 1, b.Y + b.H / 2f,
            3, b.H,
            ScaleAlpha(new Color4(40, 40, 45, 80), alpha));

        if (b.WindowRows > 0 && b.WindowCols > 0)
        {
            float winMarginX = b.W * 0.15f;
            float winMarginY = b.H * 0.2f;
            float winSpaceW = (b.W - winMarginX * 2) / b.WindowCols;
            float winSpaceH = (b.H - winMarginY * 2) / b.WindowRows;

            for (int wr = 0; wr < b.WindowRows; wr++)
            {
                for (int wc = 0; wc < b.WindowCols; wc++)
                {
                    float wx = b.X + winMarginX + wc * winSpaceW + winSpaceW * 0.3f;
                    float wy = b.Y + winMarginY + wr * winSpaceH + winSpaceH * 0.3f;
                    float ww = winSpaceW * 0.4f;
                    float wh = winSpaceH * 0.4f;
                    if (ww >= 2 && wh >= 2)
                    {
                        DrawProjectedRect(renderer, projectRect,
                            wx + ww / 2f, wy + wh / 2f,
                            ww, wh,
                            ScaleAlpha(new Color4(180, 200, 140, 180), alpha));
                    }
                }
            }
        }

        if (b.HasAntenna)
        {
            float baseX = b.X + b.W * 0.7f;
            float baseY = b.Y;

            DrawProjectedRect(renderer, projectRect,
                baseX, baseY - 6,
                2, 12,
                WithAlpha(new Color3(170, 170, 180), alpha));
            DrawProjectedRect(renderer, projectRect,
                baseX, baseY - 11,
                4, 2,
                WithAlpha(new Color3(200, 60, 60), alpha));
        }

        if (b.HasChimney)
        {
            DrawProjectedRect(renderer, projectRect,
                b.X + b.W * 0.3f, b.Y - 2,
                6, 6,
                new Color4(roofR, roofG, roofB, alpha));
        }
    }

    private static void DrawProjectedRect(ISpriteRenderer renderer,
        ProjectRectDelegate projectRect,
        float worldCenterX, float worldCenterY, float worldW, float worldH,
        Color4 color)
    {
        var rect = projectRect(worldCenterX, worldCenterY, worldW, worldH);
        renderer.DrawRectScreen(rect.X, rect.Y, rect.W, rect.H, color);
    }

    private static Color4 WithAlpha(Color3 color, byte alpha) => new(color.R, color.G, color.B, alpha);

    private static Color4 ScaleAlpha(Color4 color, byte alpha)
    {
        byte scaled = (byte)(color.A * (alpha / 255f));
        return new Color4(color.R, color.G, color.B, scaled);
    }
}
