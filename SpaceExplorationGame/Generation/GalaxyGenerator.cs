using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Data describing a star system (used for both galaxy map and solar system generation).
/// </summary>
public class StarSystemData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public Vector2 GalaxyPosition { get; set; }  // position on galaxy map in world pixels
    public StarClass StarClass { get; set; }
    public float StarRadius { get; set; }
    public Color3 StarColor { get; set; }
    public int PlanetCount { get; set; }
    public bool HasSpaceStation { get; set; }
    public int DangerLevel { get; set; }   // 1-5, determines enemy count/strength
}

/// <summary>
/// Generates the galaxy - a collection of star systems with positions and properties.
/// </summary>
public static class GalaxyGenerator
{
    private static readonly string[] StarNamePrefixes =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta",
        "Iota", "Kappa", "Lambda", "Sigma", "Omega", "Nova", "Proxima", "Rigel",
        "Vega", "Altair", "Deneb", "Sirius", "Polaris", "Antares", "Betelgeuse",
        "Capella", "Aldebaran", "Arcturus", "Canopus", "Achernar", "Fomalhaut"
    ];

    private static readonly string[] StarNameSuffixes =
    [
        "Prime", "Major", "Minor", "Centauri", "Eridani", "Cygni", "Draconis",
        "Orionis", "Lyrae", "Ursae", "Cassiopeiae", "Andromedae", "Persei",
        "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"
    ];

    public static List<StarSystemData> Generate(SeededRandom rng)
    {
        int systemCount = rng.NextInt(Core.GameConfig.MinStarSystems, Core.GameConfig.MaxStarSystems + 1);
        var systems = new List<StarSystemData>(systemCount);

        float mapWidth = Core.GameConfig.GalaxyWidth * Core.GameConfig.TileSize;
        float mapHeight = Core.GameConfig.GalaxyHeight * Core.GameConfig.TileSize;
        float centerX = mapWidth / 2f;
        float centerY = mapHeight / 2f;
        float galaxyRadius = MathF.Min(mapWidth, mapHeight) * 0.4f;

        // Generate names pool
        var usedNames = new HashSet<string>();

        for (int i = 0; i < systemCount; i++)
        {
            // Distribute stars in a spiral/disc pattern
            float angle = rng.NextFloat(0, MathF.PI * 2f);
            float dist = rng.NextFloat(0.05f, 1f);
            dist = MathF.Sqrt(dist); // sqrt for uniform disc distribution
            float x = centerX + MathF.Cos(angle) * dist * galaxyRadius;
            float y = centerY + MathF.Sin(angle) * dist * galaxyRadius;

            // Generate unique name (with retry limit to avoid infinite loop)
            string name;
            int nameAttempts = 0;
            const int MaxNameAttempts = 50;
            do
            {
                name = rng.Pick(StarNamePrefixes) + " " + rng.Pick(StarNameSuffixes);
                nameAttempts++;
            } while (usedNames.Contains(name) && nameAttempts < MaxNameAttempts);

            // Fallback: append system index to guarantee uniqueness
            if (usedNames.Contains(name))
                name = $"{rng.Pick(StarNamePrefixes)} {rng.Pick(StarNameSuffixes)}-{i}";

            usedNames.Add(name);

            // Determine star class (weighted distribution)
            var starClass = GenerateStarClass(rng);
            var (color, radius) = GetStarProperties(starClass, rng);

            // Planet count (correlates somewhat with star class)
            int planetCount = starClass switch
            {
                StarClass.O => rng.NextInt(1, 4),
                StarClass.B => rng.NextInt(1, 5),
                StarClass.A => rng.NextInt(2, 6),
                StarClass.F => rng.NextInt(2, 8),
                StarClass.G => rng.NextInt(3, 10),  // Sun-like stars have more planets
                StarClass.K => rng.NextInt(2, 8),
                StarClass.M => rng.NextInt(1, 6),
                _ => rng.NextInt(2, 6)
            };

            // Danger level: seeded per-system (1-5)
            int dangerLevel = rng.NextInt(Core.GameConfig.MinDangerLevel, Core.GameConfig.MaxDangerLevel + 1);

            systems.Add(new StarSystemData
            {
                Index = i,
                Name = name,
                GalaxyPosition = new Vector2(x, y),
                StarClass = starClass,
                StarRadius = radius,
                StarColor = color,
                PlanetCount = planetCount,
                HasSpaceStation = rng.NextBool(0.75f),  // 75% chance of having a station
                DangerLevel = dangerLevel
            });
        }

        return systems;
    }

    private static StarClass GenerateStarClass(SeededRandom rng)
    {
        // Realistic distribution: M stars most common, O least
        float roll = rng.NextFloat();
        return roll switch
        {
            < 0.003f => StarClass.O,
            < 0.013f => StarClass.B,
            < 0.073f => StarClass.A,
            < 0.133f => StarClass.F,
            < 0.213f => StarClass.G,
            < 0.343f => StarClass.K,
            _ => StarClass.M
        };
    }

    private static ColoredRadius GetStarProperties(StarClass starClass, SeededRandom rng)
    {
        const float StarSizeScale = 2.0f; // Scale factor to make stars larger for better visuals

        ColoredRadius star = starClass switch
        {
            StarClass.O => new(new Color3(100, 140, 255), rng.NextFloat(200, 300)),   // Blue
            StarClass.B => new(new Color3(150, 180, 255), rng.NextFloat(160, 240)),   // Blue-white
            StarClass.A => new(new Color3(220, 220, 255), rng.NextFloat(140, 200)),   // White
            StarClass.F => new(new Color3(255, 255, 220), rng.NextFloat(120, 180)),   // Yellow-white
            StarClass.G => new(new Color3(255, 255, 100), rng.NextFloat(100, 160)),   // Yellow
            StarClass.K => new(new Color3(255, 180, 80), rng.NextFloat(100, 140)),    // Orange
            StarClass.M => new(new Color3(255, 100, 60), rng.NextFloat(100, 120)),     // Red
            _ => new(new Color3(255, 255, 255), 120)
        };

        return new ColoredRadius(star.Color, star.Radius * StarSizeScale);
    }
}
