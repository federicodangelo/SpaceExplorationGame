using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Data describing a planet within a solar system.
/// </summary>
public class PlanetData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public PlanetType Type { get; set; }
    public float OrbitRadius { get; set; }       // distance from star in world pixels
    public float OrbitSpeed { get; set; }         // radians per second
    public float StartAngle { get; set; }         // starting orbital angle
    public float Radius { get; set; }             // visual radius
    public Color3 Color { get; set; }
    public bool HasSolidSurface { get; set; }
    public int MoonCount { get; set; }
    public bool HasRings { get; set; }
    public List<MoonData> Moons { get; set; } = [];
    public bool HasSettlement { get; set; }
}

public class MoonData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public float OrbitRadius { get; set; }
    public float OrbitSpeed { get; set; }
    public float StartAngle { get; set; }
    public float Radius { get; set; }
    public Color3 Color { get; set; }
    public PlanetType Type { get; set; } = PlanetType.Rocky;

    /// <summary>Convert moon data to PlanetData for surface generation.</summary>
    public PlanetData ToPlanetData(int parentPlanetIndex)
    {
        return new PlanetData
        {
            Index = parentPlanetIndex * 100 + Index, // unique index for seed derivation
            Name = Name,
            Type = Type,
            OrbitRadius = OrbitRadius,
            OrbitSpeed = OrbitSpeed,
            StartAngle = StartAngle,
            Radius = Radius,
            Color = Color,
            HasSolidSurface = true,
            MoonCount = 0,
            HasRings = false,
            Moons = [],
            HasSettlement = false
        };
    }
}

public class AsteroidBeltData
{
    public float InnerRadius { get; set; }
    public float OuterRadius { get; set; }
    public int AsteroidCount { get; set; }
}

public class SpaceStationData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int OrbitParentPlanetIndex { get; set; } = -1; // -1 = orbits star
    public float OrbitRadius { get; set; }
    public float OrbitSpeed { get; set; }
    public float StartAngle { get; set; }
}

public class NpcShipSpawnData
{
    public Vector2 Position { get; set; }
    public float Rotation { get; set; }
    public Faction Faction { get; set; }
    public NpcShipStats Stats { get; set; }
    public ShipWeaponSpec[] Weapons { get; set; } = [];
    public int DangerLevel { get; set; }
    public int LootCredits { get; set; }
}


public enum PlanetType
{
    Rocky,          // Mercury-like
    Terrestrial,    // Earth-like
    Desert,         // Mars-like
    GasGiant,       // Jupiter-like
    IceGiant,       // Neptune-like
    Volcanic,       // Io-like
    Ocean,          // Water world
    Frozen          // Europa/Pluto-like
}

/// <summary>
/// Generates a complete solar system given a star system's seed.
/// </summary>
public static class SolarSystemGenerator
{
    private static readonly string[] PlanetNameParts =
    [
        "a", "e", "i", "o", "u", "ar", "el", "in", "or", "us",
        "th", "ra", "on", "is", "an", "er", "al", "en", "as", "os",
        "kr", "pl", "tr", "gr", "br", "dr", "fr", "pr", "st", "ch"
    ];

    public static SolarSystemContent Generate(
        SeededRandom rng, StarSystemData starSystem)
    {
        var planets = new List<PlanetData>();
        var asteroidBelts = new List<AsteroidBeltData>();
        var stations = new List<SpaceStationData>();
        var npcShipSpawns = new List<NpcShipSpawnData>();

        int planetCount = starSystem.PlanetCount;
        float baseOrbitRadius = 3000f; // starting orbit distance from center
        float orbitSpacing = 2000f;

        for (int i = 0; i < planetCount; i++)
        {
            float orbitRadius = baseOrbitRadius + i * orbitSpacing + rng.NextFloat(-400, 400);
            float orbitSpeed = 0.0015f / (1f + i * 0.5f); // outer planets orbit slower

            var planetType = GeneratePlanetType(rng, i, planetCount);
            var (color, radius) = GetPlanetProperties(planetType, rng);

            string name = GeneratePlanetName(rng);
            bool hasSolidSurface = planetType is not (PlanetType.GasGiant or PlanetType.IceGiant);

            int moonCount = planetType switch
            {
                PlanetType.GasGiant => rng.NextInt(2, 8),
                PlanetType.IceGiant => rng.NextInt(1, 5),
                _ => rng.NextInt(0, 3)
            };

            var moons = GenerateMoons(rng, moonCount, radius, name);
            bool hasRings = (planetType is PlanetType.GasGiant or PlanetType.IceGiant) && rng.NextBool(0.4f);

            planets.Add(new PlanetData
            {
                Index = i,
                Name = name,
                Type = planetType,
                OrbitRadius = orbitRadius,
                OrbitSpeed = orbitSpeed,
                StartAngle = rng.NextFloat(0, MathF.PI * 2),
                Radius = radius,
                Color = color,
                HasSolidSurface = hasSolidSurface,
                MoonCount = moonCount,
                HasRings = hasRings,
                Moons = moons,
                HasSettlement = hasSolidSurface && rng.NextBool(0.3f)
            });
        }

        // Asteroid belt (50% chance, usually between inner and outer planets)
        if (rng.NextBool(0.5f) && planetCount >= 4)
        {
            int beltPosition = planetCount / 3;
            float beltRadius = baseOrbitRadius + beltPosition * orbitSpacing;
            asteroidBelts.Add(new AsteroidBeltData
            {
                InnerRadius = beltRadius - 600,
                OuterRadius = beltRadius + 600,
                AsteroidCount = rng.NextInt(30, 80)
            });
        }

        // Space stations (always at least 1 for gameplay; more if the system flag is set)
        {
            int stationCount = starSystem.HasSpaceStation ? rng.NextInt(2, 5) : 1;
            for (int i = 0; i < stationCount; i++)
            {
                int parentPlanet = rng.NextInt(-1, planetCount); // -1 = orbit star
                float stationOrbitRadius;
                float stationOrbitSpeed;

                if (parentPlanet >= 0)
                {
                    stationOrbitRadius = planets[parentPlanet].Radius + rng.NextFloat(500, 1000);
                    stationOrbitSpeed = 0.004f;
                }
                else
                {
                    stationOrbitRadius = baseOrbitRadius + rng.NextFloat(0, planetCount * orbitSpacing);
                    stationOrbitSpeed = 0.001f;
                }

                stations.Add(new SpaceStationData
                {
                    Index = i,
                    Name = $"Station {GeneratePlanetName(rng)}",
                    OrbitParentPlanetIndex = parentPlanet,
                    OrbitRadius = stationOrbitRadius,
                    OrbitSpeed = stationOrbitSpeed,
                    StartAngle = rng.NextFloat(0, MathF.PI * 2)
                });
            }
        }

        GenerateNpcShipSpawns(rng, starSystem, planets, npcShipSpawns);

        return new SolarSystemContent(planets, asteroidBelts, stations, npcShipSpawns);
    }

    private static void GenerateNpcShipSpawns(
        SeededRandom rng,
        StarSystemData starSystem,
        List<PlanetData> planets,
        List<NpcShipSpawnData> npcShipSpawns)
    {
        float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 center = new(centerX, centerY);

        var enemyRng = new SeededRandom(rng.DeriveChildSeed(5000));
        int dangerLevel = starSystem.DangerLevel;

        float maxOrbit = 0f;
        foreach (var planet in planets)
            maxOrbit = MathF.Max(maxOrbit, planet.OrbitRadius);
        float spawnRadius = MathF.Max(maxOrbit + 4000f, 8000f);

        int pirateCount = GameConfig.MinEnemiesPerSystem + (int)((GameConfig.MaxEnemiesPerSystem - GameConfig.MinEnemiesPerSystem) * (dangerLevel - 1f) / 4f);
        int traderCount = enemyRng.NextInt(GameConfig.MinTradersPerSystem, GameConfig.MaxTradersPerSystem + 1);
        int patrolCount = enemyRng.NextInt(GameConfig.MinPatrolsPerSystem, GameConfig.MaxPatrolsPerSystem + 1);
        int qualityTier = NpcShipLoadoutHelper.GetNpcQualityTier(dangerLevel);

        for (int i = 0; i < pirateCount; i++)
        {
            var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(Faction.Pirate, dangerLevel, enemyRng);
            var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, Faction.Pirate, qualityTier, enemyRng);
            var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
            var weapons = CombatHelper.BuildWeaponSpecs(loadout);
            int lootCredits = NpcShipLoadoutHelper.ComputeNpcLootCredits(shipType, loadout);

            npcShipSpawns.Add(new NpcShipSpawnData
            {
                Position = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 250f),
                Rotation = enemyRng.NextFloat(0, 360),
                Faction = Faction.Pirate,
                Stats = stats,
                Weapons = weapons,
                DangerLevel = dangerLevel,
                LootCredits = lootCredits
            });
        }

        for (int i = 0; i < traderCount; i++)
        {
            var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(Faction.Trader, dangerLevel, enemyRng);
            var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, Faction.Trader, qualityTier, enemyRng);
            var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
            var weapons = CombatHelper.BuildWeaponSpecs(loadout);

            npcShipSpawns.Add(new NpcShipSpawnData
            {
                Position = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 300f),
                Rotation = enemyRng.NextFloat(0, 360),
                Faction = Faction.Trader,
                Stats = stats,
                Weapons = weapons,
                DangerLevel = dangerLevel,
                LootCredits = 0
            });
        }

        for (int i = 0; i < patrolCount; i++)
        {
            var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(Faction.Patrol, dangerLevel, enemyRng);
            var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, Faction.Patrol, qualityTier, enemyRng);
            var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
            var weapons = CombatHelper.BuildWeaponSpecs(loadout);

            npcShipSpawns.Add(new NpcShipSpawnData
            {
                Position = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 300f),
                Rotation = enemyRng.NextFloat(0, 360),
                Faction = Faction.Patrol,
                Stats = stats,
                Weapons = weapons,
                DangerLevel = dangerLevel,
                LootCredits = 0
            });
        }
    }

    private static Vector2 SpawnPositionInOrbitZone(SeededRandom rng, Vector2 center, float maxRadius, float minRadius)
    {
        float angle = rng.NextFloat(0, MathF.PI * 2f);
        float dist = rng.NextFloat(minRadius, maxRadius);
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
    }

    private static PlanetType GeneratePlanetType(SeededRandom rng, int orbitIndex, int totalPlanets)
    {
        // Inner planets tend to be rocky, outer tend to be gas/ice giants
        float innerFactor = 1f - (float)orbitIndex / totalPlanets;

        if (innerFactor > 0.7f)
        {
            // Inner orbits
            float roll = rng.NextFloat();
            return roll switch
            {
                < 0.3f => PlanetType.Rocky,
                < 0.55f => PlanetType.Terrestrial,
                < 0.7f => PlanetType.Desert,
                < 0.85f => PlanetType.Volcanic,
                _ => PlanetType.Ocean
            };
        }
        else if (innerFactor > 0.3f)
        {
            // Middle orbits
            float roll = rng.NextFloat();
            return roll switch
            {
                < 0.25f => PlanetType.Terrestrial,
                < 0.45f => PlanetType.GasGiant,
                < 0.6f => PlanetType.Desert,
                < 0.75f => PlanetType.Ocean,
                < 0.9f => PlanetType.IceGiant,
                _ => PlanetType.Frozen
            };
        }
        else
        {
            // Outer orbits
            float roll = rng.NextFloat();
            return roll switch
            {
                < 0.35f => PlanetType.GasGiant,
                < 0.55f => PlanetType.IceGiant,
                < 0.75f => PlanetType.Frozen,
                _ => PlanetType.Rocky
            };
        }
    }

    private static ColoredRadius GetPlanetProperties(PlanetType type, SeededRandom rng)
    {
        const float MinStarRadius = 200f;
        const float MaxPlanetRadius = 400f;

        ColoredRadius planet = type switch
        {
            PlanetType.Rocky => new(new Color3((byte)rng.NextInt(130, 180), (byte)rng.NextInt(110, 150), (byte)rng.NextInt(90, 130)), rng.NextFloat(160, 280)),
            PlanetType.Terrestrial => new(new Color3((byte)rng.NextInt(40, 100), (byte)rng.NextInt(100, 200), (byte)rng.NextInt(50, 150)), rng.NextFloat(240, 360)),
            PlanetType.Desert => new(new Color3((byte)rng.NextInt(180, 230), (byte)rng.NextInt(140, 180), (byte)rng.NextInt(60, 100)), rng.NextFloat(200, 320)),
            PlanetType.GasGiant => new(new Color3((byte)rng.NextInt(180, 240), (byte)rng.NextInt(150, 200), (byte)rng.NextInt(80, 140)), rng.NextFloat(360, 400)),
            PlanetType.IceGiant => new(new Color3((byte)rng.NextInt(80, 140), (byte)rng.NextInt(140, 200), (byte)rng.NextInt(200, 255)), rng.NextFloat(360, 400)),
            PlanetType.Volcanic => new(new Color3((byte)rng.NextInt(200, 255), (byte)rng.NextInt(60, 100), (byte)rng.NextInt(20, 60)), rng.NextFloat(160, 280)),
            PlanetType.Ocean => new(new Color3((byte)rng.NextInt(20, 60), (byte)rng.NextInt(80, 140), (byte)rng.NextInt(180, 240)), rng.NextFloat(240, 360)),
            PlanetType.Frozen => new(new Color3((byte)rng.NextInt(180, 220), (byte)rng.NextInt(200, 240), (byte)rng.NextInt(230, 255)), rng.NextFloat(200, 320)),
            _ => new(new Color3(128, 128, 128), 240)
        };
        // Clamp planet radius
        return new ColoredRadius(planet.Color, planet.Radius * (MinStarRadius / MaxPlanetRadius) * 0.9f); // planets should be smaller than stars, scaled by max radius
    }

    private static List<MoonData> GenerateMoons(SeededRandom rng, int count, float parentRadius, string parentName)
    {
        var moons = new List<MoonData>(count);
        for (int i = 0; i < count; i++)
        {
            // Assign moon type based on parent planet context
            var moonType = rng.NextFloat() switch
            {
                < 0.4f => PlanetType.Rocky,
                < 0.6f => PlanetType.Frozen,
                < 0.75f => PlanetType.Volcanic,
                < 0.9f => PlanetType.Desert,
                _ => PlanetType.Ocean
            };

            moons.Add(new MoonData
            {
                Index = i,
                Name = $"{parentName} {(char)('a' + i)}",
                OrbitRadius = parentRadius + 400 + i * 300 + rng.NextFloat(-100, 100),
                OrbitSpeed = 0.0075f + rng.NextFloat(-0.0015f, 0.0015f),
                StartAngle = rng.NextFloat(0, MathF.PI * 2),
                Radius = rng.NextFloat(60, 140),
                Color = new Color3((byte)rng.NextInt(150, 220), (byte)rng.NextInt(150, 220), (byte)rng.NextInt(150, 220)),
                Type = moonType
            });
        }
        return moons;
    }

    private static string GeneratePlanetName(SeededRandom rng)
    {
        int syllables = rng.NextInt(2, 4);
        var name = "";
        for (int i = 0; i < syllables; i++)
        {
            name += rng.Pick(PlanetNameParts);
        }
        // Capitalize first letter
        return char.ToUpper(name[0]) + name[1..];
    }
}
