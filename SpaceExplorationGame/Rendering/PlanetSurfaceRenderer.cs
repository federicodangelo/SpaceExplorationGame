using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders planet surface visuals: terrain tiles, entities (ship, vehicle, avatar),
/// interaction prompts, HUD, and minimap.
/// </summary>
public static class PlanetSurfaceRenderer
{
    /// <summary>Renders the terrain tiles with per-tile detail overlays.</summary>
    public static void RenderTerrain(SpriteRenderer renderer, Camera camera, PlanetSurfaceData surfaceData)
    {
        TileMapRenderer.RenderTiles(renderer, camera, surfaceData.Width, surfaceData.Height,
            (x, y) => PlanetSurfaceGenerator.GetTerrainColor(surfaceData.Tiles[x, y]),
            800f,
            (x, y, worldPos, hash) =>
            {
                var terrain = surfaceData.Tiles[x, y];
                var (r, g, b) = PlanetSurfaceGenerator.GetTerrainColor(terrain);

                if (terrain == TerrainType.Grass && (hash & 0x7) == 0)
                {
                    byte dr = (byte)Math.Clamp(r - 20, 0, 255);
                    byte dg = (byte)Math.Clamp(g + 30, 0, 255);
                    byte db = (byte)Math.Clamp(b - 10, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 8) & 0xF) - 8, ((hash >> 12) & 0xF) - 8),
                        6, 6, dr, dg, db);
                }
                else if (terrain == TerrainType.Rock && (hash & 0xF) == 0)
                {
                    byte dr = (byte)Math.Clamp(r + 20, 0, 255);
                    byte dg = (byte)Math.Clamp(g + 15, 0, 255);
                    byte db = (byte)Math.Clamp(b + 10, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 8) & 0xF) - 8, ((hash >> 12) & 0xF) - 8),
                        4, 4, dr, dg, db);
                }
                else if (terrain == TerrainType.Water && (hash & 0x3) == 0)
                {
                    byte wr = (byte)Math.Clamp(r + 30, 0, 255);
                    byte wg = (byte)Math.Clamp(g + 30, 0, 255);
                    byte wb = (byte)Math.Clamp(b + 40, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 4) & 0xF) - 8, ((hash >> 8) & 0x7) - 4),
                        8, 2, wr, wg, wb, 100);
                }
            });
    }

    /// <summary>Renders the interaction prompt at the bottom of the screen.</summary>
    public static void RenderInteractionPrompt(SpriteRenderer renderer,
        bool inVehicle, bool nearShip, bool nearVehicle, bool vehicleDeployed,
        SettlementData? nearSettlement)
    {
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        if (inVehicle && nearShip)
        {
            renderer.DrawTextScreen(w / 2 - 100, h - 60,
                "[E] BOARD STARSHIP", 100, 255, 100, 2f);
        }
        else if (inVehicle)
        {
            renderer.DrawTextScreen(w / 2 - 100, h - 60,
                "[E] DISMOUNT", 255, 200, 100, 2f);
        }
        else if (nearShip)
        {
            renderer.DrawTextScreen(w / 2 - 100, h - 60,
                "[E] BOARD STARSHIP", 100, 255, 100, 2f);
        }
        else if (nearVehicle && vehicleDeployed)
        {
            renderer.DrawTextScreen(w / 2 - 100, h - 60,
                "[E] MOUNT VEHICLE", 255, 200, 100, 2f);
        }
        else if (nearSettlement != null)
        {
            renderer.DrawTextScreen(w / 2 - 120, h - 60,
                $"[E] ENTER {nearSettlement.Name.ToUpper()}", 255, 255, 100, 2f);
        }
    }

    /// <summary>Renders the HUD: planet info, driving indicator.</summary>
    public static void RenderHud(SpriteRenderer renderer, PlanetData planet, bool inVehicle)
    {
        renderer.DrawTextScreen(10, 10, $"PLANET: {planet.Name.ToUpper()}", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"TYPE: {planet.Type}", 150, 150, 150, 1.5f);
        if (inVehicle)
        {
            renderer.DrawTextScreen(10, 55, "DRIVING VEHICLE", 255, 200, 100, 1.5f);
        }
    }

    /// <summary>Renders the minimap showing player, ship, and optional vehicle positions.</summary>
    public static void RenderMinimap(SpriteRenderer renderer, PlanetSurfaceData surfaceData,
        Vector2 playerPos, Vector2 shipPos, Vector2? vehiclePos)
    {
        float mmSize = 150;
        float mmX = GameConfig.WindowWidth - mmSize - 10;
        float mmY = 10;
        renderer.DrawRectScreen(mmX, mmY, mmSize, mmSize, 0, 0, 0, 200);

        float mmScaleX = mmSize / (surfaceData.Width * GameConfig.TileSize);
        float mmScaleY = mmSize / (surfaceData.Height * GameConfig.TileSize);

        // Player dot
        float pmx = mmX + playerPos.X * mmScaleX;
        float pmy = mmY + playerPos.Y * mmScaleY;
        renderer.DrawRectScreen(pmx - 2, pmy - 2, 4, 4, 100, 255, 100);

        // Ship dot
        float smx = mmX + shipPos.X * mmScaleX;
        float smy = mmY + shipPos.Y * mmScaleY;
        renderer.DrawRectScreen(smx - 2, smy - 2, 4, 4, 150, 150, 200);

        // Vehicle dot
        if (vehiclePos.HasValue)
        {
            float vmx = mmX + vehiclePos.Value.X * mmScaleX;
            float vmy = mmY + vehiclePos.Value.Y * mmScaleY;
            renderer.DrawRectScreen(vmx - 2, vmy - 2, 4, 4, 180, 140, 80);
        }
    }

}
