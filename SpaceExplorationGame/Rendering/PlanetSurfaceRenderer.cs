using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Platform;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Renders planet surface visuals: terrain tiles with per-tile detail overlays.
/// </summary>
public static class PlanetSurfaceRenderer
{
    /// <summary>Renders the terrain tiles with per-tile detail overlays.</summary>
    public static void RenderTerrain(SpriteRenderer renderer, Camera camera, PlanetSurfaceData surfaceData)
    {
        renderer.RenderTiles(camera, surfaceData.Width, surfaceData.Height,
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
                        6, 6, new Color3(dr, dg, db));
                }
                else if (terrain == TerrainType.Rock && (hash & 0xF) == 0)
                {
                    byte dr = (byte)Math.Clamp(r + 20, 0, 255);
                    byte dg = (byte)Math.Clamp(g + 15, 0, 255);
                    byte db = (byte)Math.Clamp(b + 10, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 8) & 0xF) - 8, ((hash >> 12) & 0xF) - 8),
                        4, 4, new Color3(dr, dg, db));
                }
                else if (terrain == TerrainType.Water && (hash & 0x3) == 0)
                {
                    byte wr = (byte)Math.Clamp(r + 30, 0, 255);
                    byte wg = (byte)Math.Clamp(g + 30, 0, 255);
                    byte wb = (byte)Math.Clamp(b + 40, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 4) & 0xF) - 8, ((hash >> 8) & 0x7) - 4),
                        8, 2, new Color4(wr, wg, wb, 100));
                }
            });
    }
}
