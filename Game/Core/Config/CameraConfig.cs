namespace SpaceExplorationGame.Core.Config;

public static class CameraConfig
{
    public const float CameraZoomFactor = 0.15f;  // multiplicative zoom per scroll step

    // Zoom limits per context
    public const float SolarSystemZoomMin = 0.5f;
    public const float SolarSystemZoomMax = 1.0f;
    public const float SolarSystemZoomDefault = 1.0f;

    public const float InteriorZoomMin = 1.0f;
    public const float InteriorZoomMax = 1.5f;
    public const float InteriorZoomDefault = 1.5f;

    public const float PlanetSurfaceZoomMin = 1.0f;
    public const float PlanetSurfaceZoomMax = 1.5f;
    public const float PlanetSurfaceZoomDefault = 1.5f;

    public const float GalaxyMapZoomMin = 0.005f;
    public const float GalaxyMapZoomMax = 6.0f;
    public const float GalaxyMapZoomDefault = 0.1f;
}
