namespace SpaceExplorationGame.Core;

/// <summary>
/// Central game configuration. All tunable constants live here.
/// </summary>
public static class GameConfig
{
    // Window
    public const int WindowWidth = 1920;
    public const int WindowHeight = 1080;
    public const string WindowTitle = "Space Exploration Game";

    // Tiles
    public const int TileSize = 32;

    // Timing
    public const float TargetFps = 60f;
    public const float FixedTimeStep = 1f / TargetFps;
    public const int MaxFrameSkip = 5;

    // Camera
    public const float CameraZoomMin = 0.25f;
    public const float CameraZoomMax = 4.0f;
    public const float CameraZoomSpeed = 0.1f;

    // Galaxy
    public const int GalaxyWidth = 200;   // in tiles
    public const int GalaxyHeight = 200;
    public const int MinStarSystems = 40;
    public const int MaxStarSystems = 80;

    // Solar System
    public const int SolarSystemWidth = 400;  // in tiles
    public const int SolarSystemHeight = 400;
    public const int MinPlanets = 2;
    public const int MaxPlanets = 10;

    // Planet Surface
    public const int PlanetSurfaceWidth = 256;  // in tiles
    public const int PlanetSurfaceHeight = 256;

    // Player Ship
    public const float ShipAcceleration = 200f;     // pixels/sec^2
    public const float ShipMaxSpeed = 400f;          // pixels/sec
    public const float ShipFriction = 0.98f;
    public const float ShipRotationSpeed = 180f;     // degrees/sec

    // FTL Travel
    public const float FuelPerDistanceUnit = 0.02f;  // fuel cost per world-pixel of distance
    public const float FtlMaxRange = 2500f;          // max FTL jump range in world-pixels
    public const float StationRefuelAmount = 50f;    // fuel restored when docking at a station
}
