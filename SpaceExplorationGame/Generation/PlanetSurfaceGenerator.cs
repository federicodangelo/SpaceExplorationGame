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
    public TilePos LandingZone { get; set; }

    /// <summary>Spawn positions for hostile fauna (world-space coordinates).</summary>
    public List<CreatureSpawn> FaunaSpawns { get; set; } = [];
    /// <summary>Spawn positions for hostile bandits (world-space coordinates).</summary>
    public List<CreatureSpawn> BanditSpawns { get; set; } = [];
    /// <summary>Spawn positions for mineable rocks (world-space coordinates + resource info).</summary>
    public List<RockSpawn> RockSpawns { get; set; } = [];
}

/// <summary>A building within a settlement layout.</summary>
public class SettlementBuilding
{
    public float X { get; set; } // world-space top-left
    public float Y { get; set; }
    public float W { get; set; } // world-space size
    public float H { get; set; }
    public Color3 Color { get; set; }
    public bool HasAntenna { get; set; }
    public bool HasChimney { get; set; }
    public int WindowRows { get; set; } // 0 = no windows
    public int WindowCols { get; set; }
}

/// <summary>Pre-computed visual layout of a settlement.</summary>
public class SettlementLayout
{
    public List<SettlementBuilding> Buildings { get; set; } = [];
    public List<FloatRect> Streets { get; set; } = [];
    public List<FloatPos> Lights { get; set; } = [];
    public FloatRect Perimeter { get; set; }
}

public class SettlementData
{
    public string Name { get; set; } = "";
    public TileRect TileRect { get; set; } = new(0, 0, 8, 6);
    public SettlementLayout Layout { get; set; } = null!;
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
                    TileRect = new TileRect(sx, sy, rng.NextInt(6, 12), rng.NextInt(4, 8))
                };
                result.Settlements.Add(settlement);

                // Generate building layout
                settlement.Layout = GenerateSettlementLayout(rng, settlement);

                // Ensure the settlement area and a 2-tile border are walkable
                EnsureWalkableArea(tiles, width, height, settlement.TileRect.X, settlement.TileRect.Y,
                    settlement.TileRect.Width, settlement.TileRect.Height, margin: 2, planet.Type);
            }
        }

        // Landing zone (flat area near center) — also ensure walkable
        result.LandingZone = new TilePos(width / 2, height / 2);
        EnsureWalkableArea(tiles, width, height, width / 2 - 2, height / 2 - 2, 4, 4, margin: 2, planet.Type);

        // Generate enemy spawn points on walkable terrain, away from landing zone and settlements
        GenerateEnemySpawns(rng, result, planet);

        // Generate mineable rock spawn points on walkable terrain
        GenerateRockSpawns(rng, result, planet);

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
    public static Color3 GetTerrainColor(TerrainType type)
    {
        return type switch
        {
            TerrainType.Rock => new(128, 112, 96),
            TerrainType.Sand => new(210, 190, 140),
            TerrainType.Grass => new(60, 140, 60),
            TerrainType.Water => new(40, 80, 180),
            TerrainType.Ice => new(200, 220, 255),
            TerrainType.Lava => new(220, 80, 20),
            TerrainType.Metal => new(160, 160, 170),
            TerrainType.Void => new(0, 0, 0),
            _ => new(80, 80, 80)
        };
    }

    /// <summary>
    /// Generate spawn positions for fauna and bandits on walkable terrain,
    /// away from the landing zone and settlements.
    /// </summary>
    private static void GenerateEnemySpawns(SeededRandom rng, PlanetSurfaceData data, PlanetData planet)
    {
        float ts = GameConfig.TileSize;
        float lzX = data.LandingZone.X * ts;
        float lzY = data.LandingZone.Y * ts;
        float safeRadius = 8 * ts; // minimum distance from landing zone

        int faunaCount = rng.NextInt(GameConfig.MinFaunaPerPlanet, GameConfig.MaxFaunaPerPlanet + 1);
        int banditCount = rng.NextInt(GameConfig.MinBanditsPerPlanet, GameConfig.MaxBanditsPerPlanet + 1);

        // No fauna on ocean worlds (hostile marine life not implemented)
        if (planet.Type == PlanetType.Ocean)
            faunaCount = Math.Max(0, faunaCount - 3);

        // Spawn fauna
        for (int i = 0; i < faunaCount; i++)
        {
            if (TryFindSpawnPosition(rng, data, lzX, lzY, safeRadius, out float sx, out float sy))
            {
                data.FaunaSpawns.Add(new CreatureSpawn(sx, sy, rng.NextFloat() * MathF.PI * 2f));
            }
        }

        // Spawn bandits (only on planets with settlements)
        if (data.Settlements.Count > 0)
        {
            for (int i = 0; i < banditCount; i++)
            {
                if (TryFindSpawnPosition(rng, data, lzX, lzY, safeRadius, out float sx, out float sy))
                {
                    data.BanditSpawns.Add(new CreatureSpawn(sx, sy, rng.NextFloat() * MathF.PI * 2f));
                }
            }
        }
    }

    /// <summary>
    /// Generate spawn positions for mineable rocks on walkable terrain,
    /// away from the landing zone and settlements.
    /// </summary>
    private static void GenerateRockSpawns(SeededRandom rng, PlanetSurfaceData data, PlanetData planet)
    {
        float ts = GameConfig.TileSize;
        float lzX = data.LandingZone.X * ts;
        float lzY = data.LandingZone.Y * ts;
        float safeRadius = 4 * ts; // rocks can be closer to LZ than enemies

        int rockCount = rng.NextInt(GameConfig.MinRocksPerPlanet, GameConfig.MaxRocksPerPlanet + 1);

        // Choose available resources based on planet type
        ResourceType[] availableResources = GetPlanetResources(planet.Type);

        for (int i = 0; i < rockCount; i++)
        {
            if (TryFindSpawnPosition(rng, data, lzX, lzY, safeRadius, out float rx, out float ry))
            {
                var resource = availableResources[rng.NextInt(0, availableResources.Length)];
                int amount = rng.NextInt(GameConfig.SurfaceRockMinResource, GameConfig.SurfaceRockMaxResource + 1);
                float size = GameConfig.SurfaceRockMinSize + rng.NextFloat() * (GameConfig.SurfaceRockMaxSize - GameConfig.SurfaceRockMinSize);
                float hp = GameConfig.SurfaceRockMinHp + rng.NextFloat() * (GameConfig.SurfaceRockMaxHp - GameConfig.SurfaceRockMinHp);
                data.RockSpawns.Add(new RockSpawn(rx, ry, resource, amount, size, hp));
            }
        }
    }

    /// <summary>Get the resource types available on a planet based on its type.</summary>
    private static ResourceType[] GetPlanetResources(PlanetType type)
    {
        return type switch
        {
            PlanetType.Rocky => [ResourceType.Iron, ResourceType.Nickel, ResourceType.Gold, ResourceType.Platinum],
            PlanetType.Volcanic => [ResourceType.Iron, ResourceType.Nickel, ResourceType.Gold, ResourceType.Crystal],
            PlanetType.Desert => [ResourceType.Iron, ResourceType.Nickel, ResourceType.Gold],
            PlanetType.Frozen => [ResourceType.Ice, ResourceType.Iron, ResourceType.Crystal],
            PlanetType.Terrestrial => [ResourceType.Iron, ResourceType.Nickel, ResourceType.Crystal],
            PlanetType.Ocean => [ResourceType.Ice, ResourceType.Crystal],
            _ => [ResourceType.Iron, ResourceType.Nickel]
        };
    }

    /// <summary>Find a walkable spawn position away from landing zone and settlements.</summary>
    private static bool TryFindSpawnPosition(SeededRandom rng, PlanetSurfaceData data,
        float lzX, float lzY, float safeRadius, out float worldX, out float worldY)
    {
        float ts = GameConfig.TileSize;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int tx = rng.NextInt(5, data.Width - 5);
            int ty = rng.NextInt(5, data.Height - 5);

            // Must be walkable
            if (data.Tiles[tx, ty] is TerrainType.Water or TerrainType.Lava or TerrainType.Void)
                continue;

            worldX = tx * ts + ts / 2f;
            worldY = ty * ts + ts / 2f;

            // Must be away from landing zone
            float distToLz = MathF.Sqrt((worldX - lzX) * (worldX - lzX) + (worldY - lzY) * (worldY - lzY));
            if (distToLz < safeRadius)
                continue;

            // Must be away from settlements
            bool tooCloseToSettlement = false;
            foreach (var s in data.Settlements)
            {
                float sx = (s.TileRect.X + s.TileRect.Width / 2f) * ts;
                float sy = (s.TileRect.Y + s.TileRect.Height / 2f) * ts;
                if (MathF.Sqrt((worldX - sx) * (worldX - sx) + (worldY - sy) * (worldY - sy)) < 4 * ts)
                {
                    tooCloseToSettlement = true;
                    break;
                }
            }
            if (tooCloseToSettlement) continue;

            return true;
        }

        worldX = 0;
        worldY = 0;
        return false;
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

    /// <summary>Generates the visual layout (buildings, streets, lights) for a settlement.</summary>
    private static SettlementLayout GenerateSettlementLayout(SeededRandom rng, SettlementData settlement)
    {
        var layout = new SettlementLayout();
        float ts = GameConfig.TileSize;
        float baseX = settlement.TileRect.X * ts;
        float baseY = settlement.TileRect.Y * ts;
        float totalW = settlement.TileRect.Width * ts;
        float totalH = settlement.TileRect.Height * ts;

        // Perimeter
        layout.Perimeter = new FloatRect(baseX, baseY, totalW, totalH);

        // Street grid: one horizontal and one vertical street through the settlement
        float streetWidth = ts * 0.6f;
        float streetCenterX = baseX + totalW * (0.35f + rng.NextFloat() * 0.3f);
        float streetCenterY = baseY + totalH * (0.35f + rng.NextFloat() * 0.3f);

        // Vertical street
        layout.Streets.Add(new FloatRect(streetCenterX - streetWidth / 2, baseY, streetWidth, totalH));
        // Horizontal street
        layout.Streets.Add(new FloatRect(baseX, streetCenterY - streetWidth / 2, totalW, streetWidth));

        // Define building zones (quadrants around the street intersection)
        var zones = new FloatRect[]
        {
            new(baseX, baseY, streetCenterX - streetWidth / 2 - baseX, streetCenterY - streetWidth / 2 - baseY),
            new(streetCenterX + streetWidth / 2, baseY, baseX + totalW - streetCenterX - streetWidth / 2, streetCenterY - streetWidth / 2 - baseY),
            new(baseX, streetCenterY + streetWidth / 2, streetCenterX - streetWidth / 2 - baseX, baseY + totalH - streetCenterY - streetWidth / 2),
            new(streetCenterX + streetWidth / 2, streetCenterY + streetWidth / 2, baseX + totalW - streetCenterX - streetWidth / 2, baseY + totalH - streetCenterY - streetWidth / 2),
        };

        // Building color palettes (muted sci-fi tones)
        var palettes = new Color3[]
        {
            new(110, 115, 130), // blue-gray
            new(130, 110, 100), // warm gray
            new(100, 120, 110), // teal-gray
            new(125, 120, 135), // purple-gray
            new(140, 130, 110), // tan
            new(95,  105, 120), // steel blue
            new(120, 110, 95),  // brown-gray
        };

        // Place buildings in each zone
        foreach (var zone in zones)
        {
            if (zone.W < ts * 1.2f || zone.H < ts * 1.2f) continue; // zone too small

            float margin = ts * 0.25f;
            float cursorX = zone.X + margin;
            float cursorY = zone.Y + margin;

            // Fill zone row by row with varied buildings
            while (cursorY + ts < zone.Y + zone.H - margin)
            {
                cursorX = zone.X + margin;
                float rowH = ts * (0.8f + rng.NextFloat() * 0.6f);

                while (cursorX + ts < zone.X + zone.W - margin)
                {
                    float bw = ts * (0.8f + rng.NextFloat() * 1.2f);
                    float bh = rowH;

                    // Clamp to zone bounds
                    if (cursorX + bw > zone.X + zone.W - margin)
                        bw = zone.X + zone.W - margin - cursorX;
                    if (cursorY + bh > zone.Y + zone.H - margin)
                        bh = zone.Y + zone.H - margin - cursorY;

                    if (bw < ts * 0.5f || bh < ts * 0.5f)
                    {
                        cursorX += bw + margin * 0.5f;
                        continue;
                    }

                    var pal = palettes[rng.NextInt(0, palettes.Length)];
                    var building = new SettlementBuilding
                    {
                        X = cursorX,
                        Y = cursorY,
                        W = bw,
                        H = bh,
                        Color = pal,
                        HasAntenna = rng.NextFloat() < 0.2f,
                        HasChimney = rng.NextFloat() < 0.15f,
                        WindowRows = bh > ts * 0.6f ? rng.NextInt(1, 3) : 0,
                        WindowCols = bw > ts * 0.6f ? rng.NextInt(2, 5) : 0,
                    };
                    layout.Buildings.Add(building);

                    cursorX += bw + margin * 0.5f;
                }

                cursorY += rowH + margin * 0.5f;
            }
        }

        // Lights along streets
        float lightSpacing = ts * 1.5f;
        for (float ly = baseY + lightSpacing; ly < baseY + totalH - lightSpacing; ly += lightSpacing)
        {
            layout.Lights.Add(new FloatPos(streetCenterX - streetWidth / 2 - 4, ly));
            layout.Lights.Add(new FloatPos(streetCenterX + streetWidth / 2 + 4, ly));
        }
        for (float lx = baseX + lightSpacing; lx < baseX + totalW - lightSpacing; lx += lightSpacing)
        {
            layout.Lights.Add(new FloatPos(lx, streetCenterY - streetWidth / 2 - 4));
            layout.Lights.Add(new FloatPos(lx, streetCenterY + streetWidth / 2 + 4));
        }

        return layout;
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
