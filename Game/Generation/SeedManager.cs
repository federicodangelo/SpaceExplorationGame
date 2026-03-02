namespace SpaceExplorationGame.Generation;

/// <summary>
/// Manages the seed hierarchy for deterministic procedural generation.
/// Galaxy seed → Star system seeds → Planet seeds → etc.
/// </summary>
public class SeedManager
{
    public ulong GalaxySeed { get; }

    public SeedManager(ulong galaxySeed)
    {
        GalaxySeed = galaxySeed;
    }

    /// <summary>Get the RNG for galaxy-level generation.</summary>
    public SeededRandom GetGalaxyRandom() => new(GalaxySeed);

    /// <summary>Get the RNG for a specific star system.</summary>
    public SeededRandom GetStarSystemRandom(int systemIndex)
    {
        var galaxyRng = new SeededRandom(GalaxySeed);
        return new SeededRandom(galaxyRng.DeriveChildSeed(systemIndex));
    }

    /// <summary>Get the RNG for a specific planet within a star system.</summary>
    public SeededRandom GetPlanetRandom(int systemIndex, int planetIndex)
    {
        var systemRng = GetStarSystemRandom(systemIndex);
        return new SeededRandom(systemRng.DeriveChildSeed(planetIndex));
    }

    /// <summary>Get the RNG for a planet's surface.</summary>
    public SeededRandom GetPlanetSurfaceRandom(int systemIndex, int planetIndex)
    {
        var planetRng = GetPlanetRandom(systemIndex, planetIndex);
        return new SeededRandom(planetRng.DeriveChildSeed(1000));
    }
}
