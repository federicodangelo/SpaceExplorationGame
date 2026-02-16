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

        int planetCount = starSystem.PlanetCount;
        float baseOrbitRadius = 300f; // starting orbit distance from center
        float orbitSpacing = 200f;

        for (int i = 0; i < planetCount; i++)
        {
            float orbitRadius = baseOrbitRadius + i * orbitSpacing + rng.NextFloat(-40, 40);
            float orbitSpeed = 0.015f / (1f + i * 0.5f); // outer planets orbit slower

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
            bool hasRings = planetType is PlanetType.GasGiant or PlanetType.IceGiant && rng.NextBool(0.4f);

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
                InnerRadius = beltRadius - 60,
                OuterRadius = beltRadius + 60,
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
                    stationOrbitRadius = planets[parentPlanet].Radius + rng.NextFloat(50, 100);
                    stationOrbitSpeed = 0.04f;
                }
                else
                {
                    stationOrbitRadius = baseOrbitRadius + rng.NextFloat(0, planetCount * orbitSpacing);
                    stationOrbitSpeed = 0.01f;
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

        return new SolarSystemContent(planets, asteroidBelts, stations);
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
        return type switch
        {
            PlanetType.Rocky => new(new Color3((byte)rng.NextInt(130, 180), (byte)rng.NextInt(110, 150), (byte)rng.NextInt(90, 130)), rng.NextFloat(16, 28)),
            PlanetType.Terrestrial => new(new Color3((byte)rng.NextInt(40, 100), (byte)rng.NextInt(100, 200), (byte)rng.NextInt(50, 150)), rng.NextFloat(24, 36)),
            PlanetType.Desert => new(new Color3((byte)rng.NextInt(180, 230), (byte)rng.NextInt(140, 180), (byte)rng.NextInt(60, 100)), rng.NextFloat(20, 32)),
            PlanetType.GasGiant => new(new Color3((byte)rng.NextInt(180, 240), (byte)rng.NextInt(150, 200), (byte)rng.NextInt(80, 140)), rng.NextFloat(50, 80)),
            PlanetType.IceGiant => new(new Color3((byte)rng.NextInt(80, 140), (byte)rng.NextInt(140, 200), (byte)rng.NextInt(200, 255)), rng.NextFloat(40, 64)),
            PlanetType.Volcanic => new(new Color3((byte)rng.NextInt(200, 255), (byte)rng.NextInt(60, 100), (byte)rng.NextInt(20, 60)), rng.NextFloat(16, 28)),
            PlanetType.Ocean => new(new Color3((byte)rng.NextInt(20, 60), (byte)rng.NextInt(80, 140), (byte)rng.NextInt(180, 240)), rng.NextFloat(24, 36)),
            PlanetType.Frozen => new(new Color3((byte)rng.NextInt(180, 220), (byte)rng.NextInt(200, 240), (byte)rng.NextInt(230, 255)), rng.NextFloat(20, 32)),
            _ => new(new Color3(128, 128, 128), 24)
        };
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
                OrbitRadius = parentRadius + 40 + i * 30 + rng.NextFloat(-10, 10),
                OrbitSpeed = 0.075f + rng.NextFloat(-0.015f, 0.015f),
                StartAngle = rng.NextFloat(0, MathF.PI * 2),
                Radius = rng.NextFloat(6, 14),
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
