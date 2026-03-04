using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Data describing a planet within a solar system.
/// </summary>
public class PlanetData
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public PlanetType Type { get; init; }
    public float OrbitRadius { get; init; }       // distance from star in world pixels
    public float OrbitSpeed { get; init; }         // radians per second
    public float StartAngle { get; init; }         // starting orbital angle
    public float Radius { get; init; }             // visual radius
    public Color3 Color { get; init; }
    public bool HasSolidSurface { get; init; }
    public int MoonCount { get; init; }
    public bool HasRings { get; init; }
    public List<MoonData> Moons { get; init; } = [];
    public bool HasSettlement { get; init; }
}

public class MoonData
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public float OrbitRadius { get; init; }
    public float OrbitSpeed { get; init; }
    public float StartAngle { get; init; }
    public float Radius { get; init; }
    public Color3 Color { get; init; }
    public PlanetType Type { get; init; } = PlanetType.Rocky;

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
    public float InnerRadius { get; init; }
    public float OuterRadius { get; init; }
    public int AsteroidCount { get; init; }
}

public class SpaceStationData
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public int OrbitParentPlanetIndex { get; init; } = -1; // -1 = orbits star
    public float OrbitRadius { get; init; }
    public float OrbitSpeed { get; init; }
    public float StartAngle { get; init; }
}

public class NpcShipSpawnData
{
    public Vector2 Position { get; init; }
    public float Rotation { get; init; }
    public Faction Faction { get; init; }
    public NpcShipStats Stats { get; init; }
    public ShipWeaponSpec[] Weapons { get; init; } = [];
    public int DangerLevel { get; init; }
    public int LootCredits { get; init; }
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

    private const float BaseOrbitRadius = 3000f; // starting orbit distance from center
    private const float OrbitSpacing = 2000f;


    /// <summary>
    /// Generates only the planet data for a star system, without stations, NPC spawns,
    /// or asteroid belts. Useful for mission queries that only need planet metadata.
    /// </summary>
    public static List<PlanetData> GeneratePlanetsOnly(
        SeededRandom rng, StarSystemData starSystem)
    {
        var planets = new List<PlanetData>();
        int planetCount = starSystem.PlanetCount;

        for (int i = 0; i < planetCount; i++)
        {
            float orbitRadius = BaseOrbitRadius + i * OrbitSpacing + rng.NextFloat(-400, 400);
            float orbitSpeed = 0.0015f / (1f + i * 0.5f);

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

        return planets;
    }

    public static SolarSystemContent Generate(
        SeededRandom rng, StarSystemData starSystem)
    {
        var planets = GeneratePlanetsOnly(rng, starSystem);
        var asteroidBelts = new List<AsteroidBeltData>();
        var stations = new List<SpaceStationData>();
        int planetCount = starSystem.PlanetCount;

        // Asteroid belt (50% chance, usually between inner and outer planets)
        if (rng.NextBool(0.5f) && planetCount >= 4)
        {
            int beltPosition = planetCount / 3;
            float beltRadius = BaseOrbitRadius + beltPosition * OrbitSpacing;
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
                    var parentPlanetData = planets[parentPlanet];
                    // Place station beyond the outermost moon orbit so orbits don't overlap
                    float minOrbit = parentPlanetData.Radius + 500f;
                    if (parentPlanetData.Moons.Count > 0)
                    {
                        float outermostMoon = parentPlanetData.Moons.Max(m => m.OrbitRadius + m.Radius);
                        minOrbit = MathF.Max(minOrbit, outermostMoon + 300f);
                    }
                    stationOrbitRadius = minOrbit + rng.NextFloat(0, 500f);
                    stationOrbitSpeed = 0.004f;
                }
                else
                {
                    stationOrbitRadius = BaseOrbitRadius + rng.NextFloat(0, planetCount * OrbitSpacing);
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

        var npcSpawnConfig = GenerateNpcSpawnConfig(rng, starSystem, planets);

        float centerX = WorldConfig.SolarSystemWidth * WindowConfig.TileSize / 2f;
        float centerY = WorldConfig.SolarSystemHeight * WindowConfig.TileSize / 2f;
        float starDisplayRadius = starSystem.StarRadius * 2f;
        Vector2 startingPosition = new(centerX - (starDisplayRadius + 100f), centerY);

        return new SolarSystemContent(planets, asteroidBelts, stations, npcSpawnConfig, startingPosition);
    }

    private static NpcSpawnConfig GenerateNpcSpawnConfig(
        SeededRandom rng,
        StarSystemData starSystem,
        List<PlanetData> planets)
    {
        var enemyRng = new SeededRandom(rng.DeriveChildSeed(5000));
        int dangerLevel = starSystem.DangerLevel;

        float maxOrbit = 0f;
        foreach (var planet in planets)
            maxOrbit = MathF.Max(maxOrbit, planet.OrbitRadius);

        // Initial spawn radius: anywhere across the system's orbit zone
        float initialMinSpawnRadius = 250f;
        float initialMaxSpawnRadius = MathF.Max(maxOrbit + 4000f, 8000f);

        // Warp-in radius: close to the star
        float warpInMinRadius = starSystem.StarRadius * 2f;
        float warpInMaxRadius = warpInMinRadius + 4000f;

        int pirateCount = NpcConfig.MinEnemiesPerSystem + (int)((NpcConfig.MaxEnemiesPerSystem - NpcConfig.MinEnemiesPerSystem) * (dangerLevel - 1f) / 4f);
        int traderCount = enemyRng.NextInt(NpcConfig.MinTradersPerSystem, NpcConfig.MaxTradersPerSystem + 1);
        int patrolCount = enemyRng.NextInt(NpcConfig.MinPatrolsPerSystem, NpcConfig.MaxPatrolsPerSystem + 1);
        int qualityTier = NpcShipLoadoutHelper.GetNpcQualityTier(dangerLevel);

        return new NpcSpawnConfig(
            TargetPirates: pirateCount,
            TargetTraders: traderCount,
            TargetPatrols: patrolCount,
            DangerLevel: dangerLevel,
            QualityTier: qualityTier,
            InitialMinSpawnRadius: initialMinSpawnRadius,
            InitialMaxSpawnRadius: initialMaxSpawnRadius,
            WarpInMinRadius: warpInMinRadius,
            WarpInMaxRadius: warpInMaxRadius);
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
        if (count == 0) return moons;

        // Moons must stay within half the planet orbit spacing from the planet center
        // so they never overlap with moons of a neighbouring planet.
        const float maxMoonReach = OrbitSpacing / 2f; // 1000 units from planet centre
        const float innerGap = 150f;                  // minimum clearance from planet surface

        // Radial range available for all moon orbit centres
        float available = maxMoonReach - parentRadius - innerGap;
        available = MathF.Max(available, count * 80f); // guarantee at least 80 units per slot

        float slotSize = available / count;

        // Moon radius must fit inside its slot with a small margin on each side
        float maxMoonRadius = Math.Clamp(slotSize * 0.38f, 20f, Math.Min(140f, parentRadius * 0.5f));
        float minMoonRadius = Math.Clamp(maxMoonRadius * 0.5f, 20f, maxMoonRadius);

        // Evenly distribute start-angles with a random base rotation
        float angleStep = MathF.PI * 2f / count;
        float baseAngle = rng.NextFloat(0, MathF.PI * 2f);

        for (int i = 0; i < count; i++)
        {
            var moonType = rng.NextFloat() switch
            {
                < 0.4f => PlanetType.Rocky,
                < 0.6f => PlanetType.Frozen,
                < 0.75f => PlanetType.Volcanic,
                < 0.9f => PlanetType.Desert,
                _ => PlanetType.Ocean
            };

            // Centre of slot i, with a small random jitter (max ±10 % of slot)
            float slotCentre = parentRadius + innerGap + (i + 0.5f) * slotSize;
            float jitter = slotSize * 0.1f;
            float moonOrbitRadius = slotCentre + rng.NextFloat(-jitter, jitter);
            float moonRadius = rng.NextFloat(minMoonRadius, maxMoonRadius);

            moons.Add(new MoonData
            {
                Index = i,
                Name = $"{parentName} {(char)('a' + i)}",
                OrbitRadius = moonOrbitRadius,
                OrbitSpeed = 0.0075f + rng.NextFloat(-0.0015f, 0.0015f),
                StartAngle = baseAngle + angleStep * i,
                Radius = moonRadius,
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
