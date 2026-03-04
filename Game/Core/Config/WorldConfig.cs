namespace SpaceExplorationGame.Core.Config;

public static class WorldConfig
{
    // Galaxy
    public const int GalaxyWidth = 2000;   // in tiles
    public const int GalaxyHeight = 2000;
    public const int MinStarSystems = 80;
    public const int MaxStarSystems = 80;

    // Solar System
    public const int SolarSystemWidth = 1000;  // in tiles
    public const int SolarSystemHeight = 1000;
    public const int MinPlanets = 2;
    public const int MaxPlanets = 10;

    // Planet Surface
    public const int PlanetSurfaceWidth = 256;  // in tiles
    public const int PlanetSurfaceHeight = 256;
}
