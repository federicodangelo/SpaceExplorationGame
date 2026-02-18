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

                var (r, g, b) = color.Value;

                int hash = (x * 374761393 + y * 668265263) ^ (x * y);
                float variation = ((hash & 0xFF) - 128) / variationDivisor;
                byte vr = (byte)Math.Clamp(r + r * variation, 0, 255);
                byte vg = (byte)Math.Clamp(g + g * variation, 0, 255);
                byte vb = (byte)Math.Clamp(b + b * variation, 0, 255);

                var worldPos = new Vector2(
                    x * GameConfig.TileSize + GameConfig.TileSize / 2f,
                    y * GameConfig.TileSize + GameConfig.TileSize / 2f);

                renderer.DrawRect(camera, worldPos, GameConfig.TileSize, GameConfig.TileSize, new Color3(vr, vg, vb));

                renderDetail?.Invoke(x, y, worldPos, hash);
            }
        }
    }
}
