using System.Numerics;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Platform;

/// <summary>
/// Abstraction for tile-based map rendering with per-tile color variation.
/// </summary>
public interface ITileMapRenderer
{
    void RenderTiles(ISpriteRenderer renderer, Camera camera,
        int mapWidth, int mapHeight, float tileSize,
        Func<int, int, Color3?> getColor,
        float variationDivisor = 800f,
        Action<int, int, Vector2, int>? renderDetail = null);

    /// <summary>
    /// Applies deterministic per-tile brightness variation to a base color.
    /// </summary>
    static Color3 GetColorVariation(Color3 baseColor, int x, int y, float variationDivisor)
    {
        int hash = (x * 374761393 + y * 668265263) ^ (x * y);
        float variation = ((hash & 0xFF) - 128) / variationDivisor;
        byte vr = (byte)Math.Clamp(baseColor.R + baseColor.R * variation, 0, 255);
        byte vg = (byte)Math.Clamp(baseColor.G + baseColor.G * variation, 0, 255);
        byte vb = (byte)Math.Clamp(baseColor.B + baseColor.B * variation, 0, 255);
        return new Color3(vr, vg, vb);
    }
}
