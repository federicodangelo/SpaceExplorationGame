using System.Numerics;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Rendering.Base;

/// <summary>
/// Shared utility for rendering tile-based maps with per-tile color variation.
/// Used by PlanetSurfaceState and InteriorState.
/// </summary>
public static class TileMapRenderer
{
    /// <summary>
    /// Renders visible tiles with deterministic per-tile brightness variation.
    /// </summary>
    /// <param name="renderer">Sprite renderer.</param>
    /// <param name="camera">Current camera.</param>
    /// <param name="mapWidth">Width of the tile map.</param>
    /// <param name="mapHeight">Height of the tile map.</param>
    /// <param name="getColor">Returns (R, G, B) for the tile at (x, y), or null to skip.</param>
    /// <param name="variationDivisor">Controls brightness variation strength (higher = subtler).</param>
    /// <param name="renderDetail">Optional per-tile detail callback: (x, y, worldPos, hash).</param>
    public static void RenderTiles(
        SpriteRenderer renderer, Camera camera,
        int mapWidth, int mapHeight,
        Func<int, int, Color3?> getColor,
        float variationDivisor = 800f,
        Action<int, int, Vector2, int>? renderDetail = null)
    {
        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startX = Math.Max(0, (int)(topLeft.X / GameConfig.TileSize) - 1);
        int startY = Math.Max(0, (int)(topLeft.Y / GameConfig.TileSize) - 1);
        int endX = Math.Min(mapWidth - 1, (int)(bottomRight.X / GameConfig.TileSize) + 1);
        int endY = Math.Min(mapHeight - 1, (int)(bottomRight.Y / GameConfig.TileSize) + 1);

        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                var color = getColor(x, y);
                if (color == null) continue;

                int hash = GetTileHash(x,y);
                var variationColor = GetColorVariation(color.Value, x, y, variationDivisor);

                var worldPos = new Vector2(
                    x * GameConfig.TileSize + GameConfig.TileSize / 2f,
                    y * GameConfig.TileSize + GameConfig.TileSize / 2f);

                renderer.DrawRect(camera, worldPos, GameConfig.TileSize, GameConfig.TileSize, variationColor);

                renderDetail?.Invoke(x, y, worldPos, hash);
            }
        }
    }

    public static int GetTileHash(int x, int y)
    {
        return (x * 374761393 + y * 668265263) ^ (x * y);
    }

    public static Color3 GetColorVariation(Color3 baseColor, int x, int y, float variationDivisor)
    {
        int hash = GetTileHash(x,y);
        float variation = ((hash & 0xFF) - 128) / variationDivisor;
        byte vr = (byte)Math.Clamp(baseColor.R + baseColor.R * variation, 0, 255);
        byte vg = (byte)Math.Clamp(baseColor.G + baseColor.G * variation, 0, 255);
        byte vb = (byte)Math.Clamp(baseColor.B + baseColor.B * variation, 0, 255);
        return new Color3(vr, vg, vb);
    }
}
