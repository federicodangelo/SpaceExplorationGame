using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Terrain tile types for planet surface.
/// </summary>
public enum TerrainType
{
    Void,
    Rock,
    Sand,
    Grass,
    Water,
    Ice,
    Lava,
    Metal
}

/// <summary>
/// Data for a generated planet surface tilemap.
/// </summary>
public class PlanetSurfaceData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public TerrainType[,] Tiles { get; set; } = null!;
    public List<SettlementData> Settlements { get; set; } = [];
    public (int X, int Y) LandingZone { get; set; }
}

public class SettlementData
{
    public string Name { get; set; } = "";
    public int TileX { get; set; }
    public int TileY { get; set; }
    public int Width { get; set; } = 8;
    public int Height { get; set; } = 6;
}

/// <summary>
/// Generates planet surface tilemaps procedurally.
/// Uses a simple noise-like approach with the seeded RNG.
/// </summary>
public static class PlanetSurfaceGenerator
{
    public static PlanetSurfaceData Generate(SeededRandom rng, PlanetData planet)
    {
        int width = GameConfig.PlanetSurfaceWidth;
        int height = GameConfig.PlanetSurfaceHeight;
        var tiles = new TerrainType[width, height];

        // Generate heightmap using value noise
        var heightMap = GenerateNoiseMap(rng, width, height, 6);

        // Convert heightmap to terrain based on planet type
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                tiles[x, y] = HeightToTerrain(heightMap[x, y], planet.Type);
            }
        }

        var result = new PlanetSurfaceData
        {
            Width = width,
            Height = height,
            Tiles = tiles
        };

        // Place settlements
        if (planet.HasSettlement)
        {
            int settlementCount = rng.NextInt(1, 4);
            for (int i = 0; i < settlementCount; i++)
            {
                int sx, sy;
                int attempts = 0;
                do
                {
                    sx = rng.NextInt(20, width - 20);
                    sy = rng.NextInt(20, height - 20);
                    attempts++;
                } while (tiles[sx, sy] is TerrainType.Water or TerrainType.Lava or TerrainType.Void && attempts < 50);

                var settlement = new SettlementData
                {
                    Name = $"Outpost {(char)('A' + i)}{rng.NextInt(1, 100)}",
                    TileX = sx,
                    TileY = sy,
                    Width = rng.NextInt(6, 12),
                    Height = rng.NextInt(4, 8)
                };
                result.Settlements.Add(settlement);

                // Ensure the settlement area and a 2-tile border are walkable
                EnsureWalkableArea(tiles, width, height, settlement.TileX, settlement.TileY,
                    settlement.Width, settlement.Height, margin: 2, planet.Type);
            }
        }

        // Landing zone (flat area near center) — also ensure walkable
        result.LandingZone = (width / 2, height / 2);
        EnsureWalkableArea(tiles, width, height, width / 2 - 2, height / 2 - 2, 4, 4, margin: 2, planet.Type);

        return result;
    }

    /// <summary>
    /// Simple value noise using the seeded RNG. Not Perlin, but deterministic and adequate.
    /// </summary>
    private static float[,] GenerateNoiseMap(SeededRandom rng, int width, int height, int octaves)
    {
        var result = new float[width, height];

        // Generate base random grid at multiple scales and blend
        for (int oct = 0; oct < octaves; oct++)
        {
            int scale = 1 << (oct + 2); // 4, 8, 16, 32, 64, 128
            float amplitude = 1f / (1 << oct); // 1, 0.5, 0.25, ...

            int gridW = width / scale + 2;
            int gridH = height / scale + 2;

            var grid = new float[gridW, gridH];
            for (int gx = 0; gx < gridW; gx++)
                for (int gy = 0; gy < gridH; gy++)
                    grid[gx, gy] = rng.NextFloat();

            // Bilinear interpolation
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float fx = (float)x / scale;
                    float fy = (float)y / scale;
                    int ix = (int)fx;
                    int iy = (int)fy;
                    float dx = fx - ix;
                    float dy = fy - iy;

                    if (ix + 1 >= gridW || iy + 1 >= gridH) continue;

                    float v = grid[ix, iy] * (1 - dx) * (1 - dy)
                            + grid[ix + 1, iy] * dx * (1 - dy)
                            + grid[ix, iy + 1] * (1 - dx) * dy
                            + grid[ix + 1, iy + 1] * dx * dy;

                    result[x, y] += v * amplitude;
                }
            }
        }

        // Normalize to [0, 1]
        float min = float.MaxValue, max = float.MinValue;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (result[x, y] < min) min = result[x, y];
                if (result[x, y] > max) max = result[x, y];
            }
        }

        float range = max - min;
        if (range > 0)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    result[x, y] = (result[x, y] - min) / range;
        }

        return result;
    }

    private static TerrainType HeightToTerrain(float height, PlanetType type)
    {
        return type switch
        {
            PlanetType.Rocky => height switch
            {
                < 0.3f => TerrainType.Sand,
                < 0.7f => TerrainType.Rock,
                _ => TerrainType.Metal
            },
            PlanetType.Terrestrial => height switch
            {
                < 0.3f => TerrainType.Water,
                < 0.35f => TerrainType.Sand,
                < 0.6f => TerrainType.Grass,
                < 0.8f => TerrainType.Rock,
                _ => TerrainType.Ice
            },
            PlanetType.Desert => height switch
            {
                < 0.2f => TerrainType.Rock,
                < 0.8f => TerrainType.Sand,
                _ => TerrainType.Rock
            },
            PlanetType.Volcanic => height switch
            {
                < 0.3f => TerrainType.Lava,
                < 0.6f => TerrainType.Rock,
                _ => TerrainType.Metal
            },
            PlanetType.Ocean => height switch
            {
                < 0.7f => TerrainType.Water,
                < 0.8f => TerrainType.Sand,
                _ => TerrainType.Grass
            },
            PlanetType.Frozen => height switch
            {
                < 0.4f => TerrainType.Water,
                < 0.6f => TerrainType.Ice,
                _ => TerrainType.Rock
            },
            _ => TerrainType.Rock
        };
    }

    /// <summary>Get the color for a terrain tile type.</summary>
    public static (byte R, byte G, byte B) GetTerrainColor(TerrainType type)
    {
        return type switch
        {
            TerrainType.Rock => (128, 112, 96),
            TerrainType.Sand => (210, 190, 140),
            TerrainType.Grass => (60, 140, 60),
            TerrainType.Water => (40, 80, 180),
            TerrainType.Ice => (200, 220, 255),
            TerrainType.Lava => (220, 80, 20),
            TerrainType.Metal => (160, 160, 170),
            TerrainType.Void => (0, 0, 0),
            _ => (80, 80, 80)
        };
    }

    /// <summary>
    /// Replace any non-walkable tiles (Water, Lava, Void) within the given rectangle
    /// (plus a margin border) with the default walkable terrain for the planet type.
    /// The coordinates (topLeftX, topLeftY) represent the top-left corner of the area.
    /// </summary>
    private static void EnsureWalkableArea(TerrainType[,] tiles, int mapW, int mapH,
        int topLeftX, int topLeftY, int areaW, int areaH, int margin, PlanetType planetType)
    {
        var replacement = GetDefaultWalkableTerrain(planetType);
        int x0 = Math.Max(0, topLeftX - margin);
        int y0 = Math.Max(0, topLeftY - margin);
        int x1 = Math.Min(mapW - 1, topLeftX + areaW - 1 + margin);
        int y1 = Math.Min(mapH - 1, topLeftY + areaH - 1 + margin);

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (tiles[x, y] is TerrainType.Water or TerrainType.Lava or TerrainType.Void)
                {
                    tiles[x, y] = replacement;
                }
            }
        }
    }

    /// <summary>Returns the most natural walkable terrain for a given planet type.</summary>
    private static TerrainType GetDefaultWalkableTerrain(PlanetType type)
    {
        return type switch
        {
            PlanetType.Terrestrial => TerrainType.Grass,
            PlanetType.Desert => TerrainType.Sand,
            PlanetType.Rocky => TerrainType.Rock,
            PlanetType.Volcanic => TerrainType.Rock,
            PlanetType.Ocean => TerrainType.Sand,
            PlanetType.Frozen => TerrainType.Ice,
            _ => TerrainType.Rock
        };
    }
}
