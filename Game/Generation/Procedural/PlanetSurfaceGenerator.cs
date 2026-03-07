using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;

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
    Metal,
    Settlement
}

/// <summary>
/// Data for a generated planet surface tilemap.
/// </summary>
public class PlanetSurfaceData
{
    public int Width { get; init; }
    public int Height { get; init; }
    public TerrainType[,] Tiles { get; init; } = null!;
    /// <summary>Noise-based height value per tile (0-1), used for shading.</summary>
    public float[,] HeightMap { get; init; } = null!;
    public List<SettlementData> Settlements { get; init; } = [];
    public TilePos LandingZone { get; set; }

    /// <summary>Spawn positions for mineable rocks (world-space coordinates + resource info).</summary>
    public List<RockSpawn> RockSpawns { get; init; } = [];

    /// <summary>Runtime NPC spawn configuration (enemies, cargo, patrols). All spawning is handled dynamically.</summary>
    public NpcAvatarSpawnConfig NpcAvatarSpawnConfig { get; set; }
}

/// <summary>A building within a settlement layout.</summary>
public class SettlementBuilding
{
    public float X { get; init; } // world-space top-left
    public float Y { get; init; }
    public float W { get; init; } // world-space size
    public float H { get; init; }
    public Color3 Color { get; init; }
    public bool HasAntenna { get; init; }
    public bool HasChimney { get; init; }
    public int WindowRows { get; init; } // 0 = no windows
    public int WindowCols { get; init; }
}

/// <summary>Pre-computed visual layout of a settlement.</summary>
public class SettlementLayout
{
    public List<SettlementBuilding> Buildings { get; init; } = [];
    public List<Rect> Streets { get; init; } = [];
    public List<Vector2> Lights { get; init; } = [];
    public Rect Perimeter { get; set; }
}

public class SettlementData
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public TileRect TileRect { get; init; } = new(0, 0, 8, 6);
    public SettlementLayout Layout { get; set; } = null!;
}

/// <summary>
/// Generates planet surface tilemaps procedurally.
/// Uses a simple noise-like approach with the seeded RNG.
/// </summary>
public static class PlanetSurfaceGenerator
{
    public static PlanetSurfaceData Generate(SeededRandom rng, PlanetData planet, int dangerLevel = 1)
    {
        int width = WorldConfig.PlanetSurfaceWidth;
        int height = WorldConfig.PlanetSurfaceHeight;
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

        // Carve the surface into a circular planet footprint.
        ApplyCircularBoundary(tiles, width, height);

        var result = new PlanetSurfaceData
        {
            Width = width,
            Height = height,
            Tiles = tiles,
            HeightMap = heightMap
        };

        // Place settlements
        if (planet.HasSettlement)
        {
            int settlementCount = rng.NextInt(1, 4);
            for (int i = 0; i < settlementCount; i++)
            {
                int sx, sy, sw, sh;
                int attempts = 0;
                do
                {
                    sw = rng.NextInt(6, 12);
                    sh = rng.NextInt(4, 8);
                    sx = rng.NextInt(20, Math.Max(21, width - 20 - sw));
                    sy = rng.NextInt(20, Math.Max(21, height - 20 - sh));
                    attempts++;
                } while ((SurfaceTerrainRules.IsBlockedForTraversal(tiles[sx, sy])
                    || !IsRectInsidePlanetBoundary(sx, sy, sw, sh, width, height, margin: 2))
                    && attempts < 50);

                var settlement = new SettlementData
                {
                    Index = i,
                    Name = $"Outpost {(char)('A' + i)}{rng.NextInt(1, 100)}",
                    TileRect = new TileRect(sx, sy, sw, sh)
                };
                result.Settlements.Add(settlement);

                // Generate building layout
                settlement.Layout = GenerateSettlementLayout(rng, settlement);

                // Ensure the settlement area and a 2-tile border are walkable
                EnsureWalkableArea(tiles, width, height, settlement.TileRect.X, settlement.TileRect.Y,
                    settlement.TileRect.Width, settlement.TileRect.Height, margin: 2, planet.Type);

                // Stamp settlement tiles so they are intrinsically non-traversable
                for (int tx = settlement.TileRect.X; tx < settlement.TileRect.X + settlement.TileRect.Width; tx++)
                    for (int ty = settlement.TileRect.Y; ty < settlement.TileRect.Y + settlement.TileRect.Height; ty++)
                        tiles[tx, ty] = TerrainType.Settlement;
            }
        }

        // Landing zone (flat area near center) — also ensure walkable
        result.LandingZone = new TilePos(width / 2, height / 2);
        EnsureWalkableArea(tiles, width, height, width / 2 - 2, height / 2 - 2, 4, 4, margin: 2, planet.Type);

        // Safety pass in case any operation modified edge tiles.
        ApplyCircularBoundary(tiles, width, height);

        // Generate surface NPC spawn configuration
        result.NpcAvatarSpawnConfig = GenerateNpcAvatarSpawnConfig(rng, result, dangerLevel);

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
            TerrainType.Settlement => new(100, 100, 120),
            TerrainType.Void => new(0, 0, 0),
            _ => new(80, 80, 80)
        };
    }

    /// <summary>
    /// Generate the runtime NPC spawn configuration based on planet danger level and presence of settlements.
    /// </summary>
    private static NpcAvatarSpawnConfig GenerateNpcAvatarSpawnConfig(SeededRandom rng, PlanetSurfaceData data, int dangerLevel)
    {
        bool hasSettlement = data.Settlements.Count > 0;

        // Enemies scale with danger level
        int enemies = rng.NextInt(NpcConfig.SurfaceNpcMinEnemies, NpcConfig.SurfaceNpcMaxEnemies + 1);
        // Cargo and patrols only on settlement planets
        int cargo = hasSettlement ? rng.NextInt(NpcConfig.SurfaceNpcMinCargo, NpcConfig.SurfaceNpcMaxCargo + 1) : 0;
        int patrols = hasSettlement ? rng.NextInt(NpcConfig.SurfaceNpcMinPatrols, NpcConfig.SurfaceNpcMaxPatrols + 1) : 0;

        return new NpcAvatarSpawnConfig(
            TargetEnemies: enemies,
            TargetCargo: cargo,
            TargetPatrols: patrols,
            DangerLevel: dangerLevel);
    }

    /// <summary>
    /// Generate spawn positions for mineable rocks on walkable terrain,
    /// away from the landing zone and settlements.
    /// </summary>
    private static void GenerateRockSpawns(SeededRandom rng, PlanetSurfaceData data, PlanetData planet)
    {
        float ts = WindowConfig.TileSize;
        float lzX = data.LandingZone.X * ts;
        float lzY = data.LandingZone.Y * ts;
        float safeRadius = 4 * ts; // rocks can be closer to LZ than enemies

        int rockCount = rng.NextInt(CombatConfig.MinRocksPerPlanet, CombatConfig.MaxRocksPerPlanet + 1);

        // Choose available resources based on planet type
        ResourceType[] availableResources = GetPlanetResources(planet.Type);

        for (int i = 0; i < rockCount; i++)
        {
            if (TryFindSpawnPosition(rng, data, lzX, lzY, safeRadius, out float rx, out float ry))
            {
                var resource = availableResources[rng.NextInt(0, availableResources.Length)];
                int amount = rng.NextInt(CombatConfig.SurfaceRockMinResource, CombatConfig.SurfaceRockMaxResource + 1);
                float size = CombatConfig.SurfaceRockMinSize + rng.NextFloat() * (CombatConfig.SurfaceRockMaxSize - CombatConfig.SurfaceRockMinSize);
                float hp = CombatConfig.SurfaceRockMinHp + rng.NextFloat() * (CombatConfig.SurfaceRockMaxHp - CombatConfig.SurfaceRockMinHp);
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
        float ts = WindowConfig.TileSize;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int tx = rng.NextInt(5, data.Width - 5);
            int ty = rng.NextInt(5, data.Height - 5);

            // Must be walkable
            if (SurfaceTerrainRules.IsBlockedForTraversal(data.Tiles[tx, ty]))
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

    /// <summary>Mark tiles outside the planet disc as <see cref="TerrainType.Void"/>.</summary>
    private static void ApplyCircularBoundary(TerrainType[,] tiles, int width, int height)
    {
        float centerX = (width - 1) * 0.5f;
        float centerY = (height - 1) * 0.5f;
        float radius = MathF.Min(width, height) * 0.5f - 2f;
        float radiusSq = radius * radius;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float dx = x - centerX;
                float dy = y - centerY;
                if (dx * dx + dy * dy > radiusSq)
                    tiles[x, y] = TerrainType.Void;
            }
        }
    }

    /// <summary>Checks whether a settlement rectangle (plus margin) remains inside the planet disc.</summary>
    private static bool IsRectInsidePlanetBoundary(int x, int y, int w, int h, int mapW, int mapH, int margin)
    {
        float centerX = (mapW - 1) * 0.5f;
        float centerY = (mapH - 1) * 0.5f;
        float radius = MathF.Min(mapW, mapH) * 0.5f - 2f;
        float radiusSq = radius * radius;

        int left = x - margin;
        int right = x + w - 1 + margin;
        int top = y - margin;
        int bottom = y + h - 1 + margin;

        return IsPointInsideCircle(left, top, centerX, centerY, radiusSq)
            && IsPointInsideCircle(right, top, centerX, centerY, radiusSq)
            && IsPointInsideCircle(left, bottom, centerX, centerY, radiusSq)
            && IsPointInsideCircle(right, bottom, centerX, centerY, radiusSq);
    }

    private static bool IsPointInsideCircle(int px, int py, float centerX, float centerY, float radiusSq)
    {
        float dx = px - centerX;
        float dy = py - centerY;
        return dx * dx + dy * dy <= radiusSq;
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
                if (SurfaceTerrainRules.IsReplaceableForWalkableArea(tiles[x, y]))
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
        float ts = WindowConfig.TileSize;
        float baseX = settlement.TileRect.X * ts;
        float baseY = settlement.TileRect.Y * ts;
        float totalW = settlement.TileRect.Width * ts;
        float totalH = settlement.TileRect.Height * ts;

        // Perimeter
        layout.Perimeter = new Rect(baseX, baseY, totalW, totalH);

        // Street grid: one horizontal and one vertical street through the settlement
        float streetWidth = ts * 0.6f;
        float streetCenterX = baseX + totalW * (0.35f + rng.NextFloat() * 0.3f);
        float streetCenterY = baseY + totalH * (0.35f + rng.NextFloat() * 0.3f);

        // Vertical street
        layout.Streets.Add(new Rect(streetCenterX - streetWidth / 2, baseY, streetWidth, totalH));
        // Horizontal street
        layout.Streets.Add(new Rect(baseX, streetCenterY - streetWidth / 2, totalW, streetWidth));

        // Define building zones (quadrants around the street intersection)
        var zones = new Rect[]
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
            layout.Lights.Add(new Vector2(streetCenterX - streetWidth / 2 - 4, ly));
            layout.Lights.Add(new Vector2(streetCenterX + streetWidth / 2 + 4, ly));
        }
        for (float lx = baseX + lightSpacing; lx < baseX + totalW - lightSpacing; lx += lightSpacing)
        {
            layout.Lights.Add(new Vector2(lx, streetCenterY - streetWidth / 2 - 4));
            layout.Lights.Add(new Vector2(lx, streetCenterY + streetWidth / 2 + 4));
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
