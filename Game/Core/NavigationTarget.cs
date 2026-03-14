namespace SpaceExplorationGame.Core;

/// <summary>Type of navigation target the player can set.</summary>
public enum NavigationTargetType
{
    None,
    Star,
    Planet,
    Moon,
    SpaceStation,
    DerelictShip,
    DistressBeacon,
    SurfaceTarget
}

/// <summary>
/// Holds the player's current navigation target state (orbital or surface).
/// Set via map panels, consumed by states and HUD renderers.
/// </summary>
public class NavigationTarget
{
    /// <summary>Type of the current navigation target.</summary>
    public NavigationTargetType Type { get; set; } = NavigationTargetType.None;

    /// <summary>Index of the target planet (if targeting a planet or moon).</summary>
    public int PlanetIndex { get; set; } = -1;

    /// <summary>Index of the target moon within its parent planet (if targeting a moon).</summary>
    public int MoonIndex { get; set; } = -1;

    /// <summary>Index of the target space station (if targeting a space station).</summary>
    public int SpaceStationIndex { get; set; } = -1;

    /// <summary>Index of the target derelict ship (if targeting a derelict).</summary>
    public int DerelictShipIndex { get; set; } = -1;

    /// <summary>Index of the target distress beacon (if targeting a distress beacon).</summary>
    public int DistressBeaconIndex { get; set; } = -1;

    /// <summary>Name of the navigation target (for display).</summary>
    public string Name { get; set; } = "";

    /// <summary>Color of the navigation target marker.</summary>
    public Color3 Color { get; set; } = new(255, 200, 100);

    /// <summary>World X position of the surface navigation target.</summary>
    public float WorldX { get; set; }

    /// <summary>World Y position of the surface navigation target.</summary>
    public float WorldY { get; set; }

    /// <summary>Whether the player has an active navigation target.</summary>
    public bool HasTarget => Type != NavigationTargetType.None;

    // ── Set methods ──

    /// <summary>Set the star as the nav target.</summary>
    public void SetStar(string name, Color3 color)
    {
        Type = NavigationTargetType.Star;
        PlanetIndex = -1;
        MoonIndex = -1;
        SpaceStationIndex = -1;
        DerelictShipIndex = -1;
        DistressBeaconIndex = -1;
        Name = name;
        Color = color;
    }

    /// <summary>Set a planet as the nav target.</summary>
    public void SetPlanet(int planetIndex, string name, Color3 color)
    {
        Type = NavigationTargetType.Planet;
        PlanetIndex = planetIndex;
        MoonIndex = -1;
        SpaceStationIndex = -1;
        DerelictShipIndex = -1;
        DistressBeaconIndex = -1;
        Name = name;
        Color = color;
    }

    /// <summary>Set a moon as the nav target.</summary>
    public void SetMoon(int planetIndex, int moonIndex, string name, Color3 color)
    {
        Type = NavigationTargetType.Moon;
        PlanetIndex = planetIndex;
        MoonIndex = moonIndex;
        SpaceStationIndex = -1;
        DerelictShipIndex = -1;
        DistressBeaconIndex = -1;
        Name = name;
        Color = color;
    }

    /// <summary>Set a space station as the nav target.</summary>
    public void SetStation(int spaceStationIndex, string name, Color3 color)
    {
        Type = NavigationTargetType.SpaceStation;
        PlanetIndex = -1;
        MoonIndex = -1;
        SpaceStationIndex = spaceStationIndex;
        DerelictShipIndex = -1;
        DistressBeaconIndex = -1;
        Name = name;
        Color = color;
    }

    /// <summary>Set a derelict ship as the nav target.</summary>
    public void SetDerelict(int derelictShipIndex, string name, Color3 color)
    {
        Type = NavigationTargetType.DerelictShip;
        PlanetIndex = -1;
        MoonIndex = -1;
        SpaceStationIndex = -1;
        DerelictShipIndex = derelictShipIndex;
        DistressBeaconIndex = -1;
        Name = name;
        Color = color;
    }

    /// <summary>Set a distress beacon as the nav target.</summary>
    public void SetDistressBeacon(int distressBeaconIndex, string name, Color3 color)
    {
        Type = NavigationTargetType.DistressBeacon;
        PlanetIndex = -1;
        MoonIndex = -1;
        SpaceStationIndex = -1;
        DerelictShipIndex = -1;
        DistressBeaconIndex = distressBeaconIndex;
        Name = name;
        Color = color;
    }

    /// <summary>Set a surface-level target (settlement, ship, etc.) as the nav target.</summary>
    public void SetSurface(string name, Color3 color, float worldX, float worldY)
    {
        Type = NavigationTargetType.SurfaceTarget;
        PlanetIndex = -1;
        MoonIndex = -1;
        SpaceStationIndex = -1;
        DerelictShipIndex = -1;
        DistressBeaconIndex = -1;
        Name = name;
        Color = color;
        WorldX = worldX;
        WorldY = worldY;
    }

    /// <summary>Clear the navigation target.</summary>
    public void Clear()
    {
        Type = NavigationTargetType.None;
        PlanetIndex = -1;
        MoonIndex = -1;
        SpaceStationIndex = -1;
        DerelictShipIndex = -1;
        DistressBeaconIndex = -1;
        Name = "";
        Color = new Color3(255, 200, 100);
        WorldX = 0;
        WorldY = 0;
    }
}
