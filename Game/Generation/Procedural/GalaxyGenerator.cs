using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Data describing a star system (used for both galaxy map and solar system generation).
/// </summary>
public class StarSystemData
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public Vector2 GalaxyPosition { get; init; }  // position on galaxy map in world pixels
    public StarClass StarClass { get; init; }
    public float StarRadius { get; init; }
    public Color3 StarColor { get; init; }
    public int PlanetCount { get; init; }
    public bool HasSpaceStation { get; init; }
    public int DangerLevel { get; init; }   // 1-5, determines enemy count/strength
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

        // Use Poisson disk sampling inside the galaxy disc for well-distributed star positions.
        // Choose minDist so that the disc fits roughly systemCount points:
        //   Packing estimate: N ≈ π·R² / (π/4·minDist²) = 4R²/minDist²  →  minDist = 2R/√N
        // Reduce by 0.5 to generate a surplus of candidates, then trim to systemCount.
        float minDist = 2f * galaxyRadius / MathF.Sqrt(systemCount) * 0.5f;
        float bx0 = centerX - galaxyRadius;
        float by0 = centerY - galaxyRadius;
        float bx1 = centerX + galaxyRadius;
        float by1 = centerY + galaxyRadius;

        var poissonPoints = PoissonDiskSampler.Sample(rng, bx0, by0, bx1, by1, minDist);

        // Keep only points that fall inside the galaxy disc (with a small empty-core exclusion).
        float innerRadius = galaxyRadius * 0.05f;
        float innerRadiusSq = innerRadius * innerRadius;
        float outerRadiusSq = galaxyRadius * galaxyRadius;
        var discPoints = new List<Vector2>(poissonPoints.Count);
        foreach (var p in poissonPoints)
        {
            float dx = p.X - centerX;
            float dy = p.Y - centerY;
            float distSq = dx * dx + dy * dy;
            if (distSq >= innerRadiusSq && distSq <= outerRadiusSq)
                discPoints.Add(p);
        }

        // Fisher-Yates shuffle so we pick a varied subset when we have more than needed.
        for (int s = discPoints.Count - 1; s > 0; s--)
        {
            int j = rng.NextInt(0, s + 1);
            (discPoints[s], discPoints[j]) = (discPoints[j], discPoints[s]);
        }

        // Pad with random disc positions if Poisson delivered fewer than requested
        // (should only happen in degenerate cases with unusual config values).
        while (discPoints.Count < systemCount)
        {
            float angle = rng.NextFloat(0, MathF.PI * 2f);
            float r = MathF.Sqrt(rng.NextFloat(innerRadius * innerRadius / outerRadiusSq, 1f)) * galaxyRadius;
            discPoints.Add(new Vector2(centerX + MathF.Cos(angle) * r, centerY + MathF.Sin(angle) * r));
        }

        // Generate names pool
        var usedNames = new HashSet<string>();

        for (int i = 0; i < systemCount; i++)
        {
            var pos = discPoints[i];
            float x = pos.X;
            float y = pos.Y;

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
                StarClass.O => rng.NextInt(3, 6),
                StarClass.B => rng.NextInt(3, 7),
                StarClass.A => rng.NextInt(4, 7),
                StarClass.F => rng.NextInt(4, 8),
                StarClass.G => rng.NextInt(6, 10),  // Sun-like stars have more planets
                StarClass.K => rng.NextInt(4, 8),
                StarClass.M => rng.NextInt(3, 6),
                _ => rng.NextInt(2, 6)
            };

            // Danger level: increases with distance from galaxy center (center = safer, edge = more dangerous)
            float dx = x - centerX;
            float dy = y - centerY;
            float normalizedDist = MathF.Sqrt(dx * dx + dy * dy) / galaxyRadius; // 0 = center, 1 = edge
            float baseDanger = Core.GameConfig.MinDangerLevel
                + normalizedDist * (Core.GameConfig.MaxDangerLevel - Core.GameConfig.MinDangerLevel);
            // Add a small random variation (±1) so nearby systems aren't identical
            int dangerLevel = Math.Clamp(
                (int)MathF.Round(baseDanger + rng.NextFloat(-1f, 1f)),
                Core.GameConfig.MinDangerLevel,
                Core.GameConfig.MaxDangerLevel);

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
