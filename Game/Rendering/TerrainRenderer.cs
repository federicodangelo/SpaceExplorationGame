using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.Rendering;

/// <summary>
/// Shared terrain-rendering utilities: deterministic per-tile color variation,
/// height-based shading, and terrain overview texture creation.
/// Used by <see cref="PlanetSurfaceRenderer"/>, planet map panels, and transition states.
/// </summary>
public static class TerrainRenderer
{
    // ── Color helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies deterministic per-tile brightness variation to a base color.
    /// The <paramref name="variationDivisor"/> controls the magnitude: higher values
    /// mean smaller variation (800 ≈ terrain tiles, 1200 ≈ interior tiles).
    /// </summary>
    public static Color3 GetColorVariation(Color3 baseColor, int x, int y, float variationDivisor)
    {
        int hash = (x * 374761393 + y * 668265263) ^ (x * y);
        float variation = ((hash & 0xFF) - 128) / variationDivisor;
        byte vr = (byte)Math.Clamp(baseColor.R + baseColor.R * variation, 0, 255);
        byte vg = (byte)Math.Clamp(baseColor.G + baseColor.G * variation, 0, 255);
        byte vb = (byte)Math.Clamp(baseColor.B + baseColor.B * variation, 0, 255);
        return new Color3(vr, vg, vb);
    }

    /// <summary>
    /// Canonical terrain tile color: applies a small deterministic per-tile brightness
    /// offset (±7 values) followed by height-based shading (±7.5% brightness at
    /// <paramref name="height"/> = 0 / 1 respectively, neutral at 0.5).
    /// </summary>
    /// <param name="baseColor">Base terrain color from <see cref="PlanetSurfaceGenerator.GetTerrainColor"/>.</param>
    /// <param name="x">Tile X coordinate (used as hash seed).</param>
    /// <param name="y">Tile Y coordinate (used as hash seed).</param>
    /// <param name="height">Normalised height value [0, 1] from the surface height-map.
    /// Pass 0.5 when no height-map is available (neutral shading).</param>
    public static Color3 GetTileColor(Color3 baseColor, int x, int y, float height)
    {
        int tileHash = (x * 374761393 + y * 668265263) ^ (x * 17 + y * 31);
        int variation = ((tileHash >> 4) & 0xF) - 8; // -8 to +7

        byte cr = (byte)Math.Clamp(baseColor.R + variation, 0, 255);
        byte cg = (byte)Math.Clamp(baseColor.G + variation, 0, 255);
        byte cb = (byte)Math.Clamp(baseColor.B + variation, 0, 255);

        // Height-based shading: darken low areas, brighten high
        float shade = (height - 0.5f) * 0.15f; // +/-7.5% brightness
        cr = (byte)Math.Clamp(cr + (int)(cr * shade), 0, 255);
        cg = (byte)Math.Clamp(cg + (int)(cg * shade), 0, 255);
        cb = (byte)Math.Clamp(cb + (int)(cb * shade), 0, 255);

        return new Color3(cr, cg, cb);
    }

    // ── Texture creation ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 1-pixel-per-tile terrain overview texture from <paramref name="surface"/>.
    /// Each tile is colored using <see cref="GetTileColor"/>.
    /// Settlement tile rectangles are overlaid with a distinct grey tone.
    /// The texture uses nearest-neighbor sampling so it stays crisp when scaled up.
    /// </summary>
    public static nint CreateTerrainTexture(ITextureManager textures, PlanetSurfaceData surface)
    {
        int w = surface.Width;
        int h = surface.Height;
        var heights = surface.HeightMap;
        var pixels = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var terrain = surface.Tiles[x, y];
                var baseColor = PlanetSurfaceGenerator.GetTerrainColor(terrain);
                var height = heights[x, y];
                var tileColor = GetTileColor(baseColor, x, y, height); // height = 0.5 (neutral, no height-map here)

                int idx = (y * w + x) * 4;
                pixels[idx + 0] = tileColor.R;
                pixels[idx + 1] = tileColor.G;
                pixels[idx + 2] = tileColor.B;
                pixels[idx + 3] = 255;
            }
        }

        // Settlement tile overlay
        foreach (var s in surface.Settlements)
        {
            for (int sx = s.TileRect.X; sx < s.TileRect.X + s.TileRect.Width && sx < w; sx++)
            {
                for (int sy = s.TileRect.Y; sy < s.TileRect.Y + s.TileRect.Height && sy < h; sy++)
                {
                    int idx = (sy * w + sx) * 4;
                    pixels[idx + 0] = 100;
                    pixels[idx + 1] = 100;
                    pixels[idx + 2] = 120;
                    pixels[idx + 3] = 255;
                }
            }
        }

        return textures.CreateTextureFromPixels(pixels, w, h, TextureScaleMode.Nearest);
    }
}
