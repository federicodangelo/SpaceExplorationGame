using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders settlement visuals (buildings, streets, lights, fences) on the planet surface.
/// </summary>
public static class SettlementRenderer
{
    public static void Render(SpriteRenderer renderer, Camera camera, PlanetSurfaceData surfaceData)
    {
        foreach (var settlement in surfaceData.Settlements)
        {
            RenderSettlement(renderer, camera, surfaceData, settlement);
        }
    }

    private static void RenderSettlement(SpriteRenderer renderer, Camera camera,
        PlanetSurfaceData surfaceData, SettlementData settlement)
    {
        var layout = settlement.Layout;
        var (px, py, pw, ph) = layout.Perimeter;

        // Ground/plaza (slightly lighter base)
        for (int sx = settlement.TileRect.X; sx < settlement.TileRect.X + settlement.TileRect.Width && sx < surfaceData.Width; sx++)
        {
            for (int sy = settlement.TileRect.Y; sy < settlement.TileRect.Y + settlement.TileRect.Height && sy < surfaceData.Height; sy++)
            {
                var worldPos = new Vector2(sx * GameConfig.TileSize + GameConfig.TileSize / 2f,
                                           sy * GameConfig.TileSize + GameConfig.TileSize / 2f);
                renderer.DrawRect(camera, worldPos, GameConfig.TileSize, GameConfig.TileSize, new Color3(80, 82, 88));
            }
        }

        // Streets (lighter paths)
        foreach (var (stX, stY, stW, stH) in layout.Streets)
        {
            var streetCenter = new Vector2(stX + stW / 2f, stY + stH / 2f);
            renderer.DrawRect(camera, streetCenter, (int)stW, (int)stH, new Color3(100, 98, 90));

            // Dashed center line
            if (stW > stH) // horizontal street
            {
                for (float dx = stX + 8; dx < stX + stW - 8; dx += 16)
                {
                    var dashPos = new Vector2(dx + 4, stY + stH / 2f);
                    renderer.DrawRect(camera, dashPos, 8, 2, new Color4(160, 155, 120, 120));
                }
            }
            else // vertical street
            {
                for (float dy = stY + 8; dy < stY + stH - 8; dy += 16)
                {
                    var dashPos = new Vector2(stX + stW / 2f, dy + 4);
                    renderer.DrawRect(camera, dashPos, 2, 8, new Color4(160, 155, 120, 120));
                }
            }
        }

        // Buildings
        foreach (var b in layout.Buildings)
        {
            RenderBuilding(renderer, camera, b);
        }

        // Street lights (small yellow dots with glow)
        foreach (var lightPos in layout.Lights)
        {
            renderer.DrawRect(camera, lightPos, 6, 6, new Color3(60, 60, 65)); // post
            renderer.DrawRect(camera, lightPos + new Vector2(0, -2), 4, 4, new Color4(255, 230, 140, 140)); // glow
            renderer.DrawRect(camera, lightPos + new Vector2(0, -2), 2, 2, new Color3(255, 245, 180)); // bulb
        }

        // Perimeter fence (four edges)
        renderer.DrawRect(camera, new Vector2(px + pw / 2f, py), (int)pw, 2, new Color3(140, 140, 150));       // top
        renderer.DrawRect(camera, new Vector2(px + pw / 2f, py + ph), (int)pw, 2, new Color3(140, 140, 150));   // bottom
        renderer.DrawRect(camera, new Vector2(px, py + ph / 2f), 2, (int)ph, new Color3(140, 140, 150));        // left
        renderer.DrawRect(camera, new Vector2(px + pw, py + ph / 2f), 2, (int)ph, new Color3(140, 140, 150));   // right

        // Entrance gate marker (gap in top fence with pillars)
        float gateX = px + pw / 2f;
        renderer.DrawRect(camera, new Vector2(gateX, py), 24, 2, new Color3(80, 82, 88)); // erase fence section
        renderer.DrawRect(camera, new Vector2(gateX - 13, py - 2), 4, 6, new Color3(160, 160, 170)); // left pillar
        renderer.DrawRect(camera, new Vector2(gateX + 13, py - 2), 4, 6, new Color3(160, 160, 170)); // right pillar

        // Settlement label (above gate)
        var labelPos = new Vector2(
            (settlement.TileRect.X + settlement.TileRect.Width / 2f) * GameConfig.TileSize,
            settlement.TileRect.Y * GameConfig.TileSize - 18
        );
        renderer.DrawText(camera, labelPos, settlement.Name, new Color3(255, 255, 200));
    }

    private static void RenderBuilding(SpriteRenderer renderer, Camera camera, SettlementBuilding b)
    {
        var bCenter = new Vector2(b.X + b.W / 2f, b.Y + b.H / 2f);

        // Building body
        renderer.DrawRect(camera, bCenter, (int)b.W, (int)b.H, b.Color);

        // Roof edge (darker top strip)
        byte roofR = (byte)Math.Clamp(b.Color.R - 25, 0, 255);
        byte roofG = (byte)Math.Clamp(b.Color.G - 25, 0, 255);
        byte roofB = (byte)Math.Clamp(b.Color.B - 25, 0, 255);
        var roofPos = new Vector2(b.X + b.W / 2f, b.Y + 2);
        renderer.DrawRect(camera, roofPos, (int)b.W, 4, new Color3(roofR, roofG, roofB));

        // Shadow on bottom/right
        var shadowBottom = new Vector2(b.X + b.W / 2f, b.Y + b.H - 1);
        renderer.DrawRect(camera, shadowBottom, (int)b.W, 3, new Color4(40, 40, 45, 100));
        var shadowRight = new Vector2(b.X + b.W - 1, b.Y + b.H / 2f);
        renderer.DrawRect(camera, shadowRight, 3, (int)b.H, new Color4(40, 40, 45, 80));

        // Windows
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
                        var winPos = new Vector2(wx + ww / 2f, wy + wh / 2f);
                        renderer.DrawRect(camera, winPos, (int)ww, (int)wh, new Color4(180, 200, 140, 180));
                    }
                }
            }
        }

        // Antenna
        if (b.HasAntenna)
        {
            var antennaBase = new Vector2(b.X + b.W * 0.7f, b.Y);
            renderer.DrawRect(camera, antennaBase + new Vector2(0, -6), 2, 12, new Color3(170, 170, 180));
            renderer.DrawRect(camera, antennaBase + new Vector2(0, -11), 4, 2, new Color3(200, 60, 60)); // red tip
        }

        // Chimney / vent
        if (b.HasChimney)
        {
            var chimneyPos = new Vector2(b.X + b.W * 0.3f, b.Y - 2);
            renderer.DrawRect(camera, chimneyPos, 6, 6, new Color3(roofR, roofG, roofB));
        }
    }
}
