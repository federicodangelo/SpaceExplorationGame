namespace SpaceExplorationGame.Core;

using System.Numerics;

/// <summary>
/// Navigation target state: set/clear orbital and surface targets.
/// </summary>
public partial class PlayerData
{
    // ── Navigation Target ──

    /// <summary>Type of the current navigation target.</summary>
    public NavigationTargetType NavTargetType { get; set; } = NavigationTargetType.None;

    /// <summary>Index of the target planet (if targeting a planet or moon).</summary>
    public int NavTargetPlanetIndex { get; set; } = -1;

    /// <summary>Index of the target moon within its parent planet (if targeting a moon).</summary>
    public int NavTargetMoonIndex { get; set; } = -1;

    /// <summary>Index of the target station (if targeting a station).</summary>
    public int NavTargetStationIndex { get; set; } = -1;

    /// <summary>Name of the navigation target (for display).</summary>
    public string NavTargetName { get; set; } = "";

    /// <summary>Color of the navigation target marker.</summary>
    public Color3 NavTargetColor { get; set; } = new(255, 200, 100);

    /// <summary>World X position of the surface navigation target.</summary>
    public float NavTargetWorldX { get; set; }

    /// <summary>World Y position of the surface navigation target.</summary>
    public float NavTargetWorldY { get; set; }

    /// <summary>Whether the player has an active navigation target.</summary>
    public bool HasNavigationTarget => NavTargetType != NavigationTargetType.None;

    /// <summary>Set the star as the nav target.</summary>
    public void SetNavTargetStar(string name, Color3 color)
    {
        NavTargetType = NavigationTargetType.Star;
        NavTargetPlanetIndex = -1;
        NavTargetMoonIndex = -1;
        NavTargetStationIndex = -1;
        NavTargetName = name;
        NavTargetColor = color;
    }

    /// <summary>Set a planet as the nav target.</summary>
    public void SetNavTargetPlanet(int planetIndex, string name, Color3 color)
    {
        NavTargetType = NavigationTargetType.Planet;
        NavTargetPlanetIndex = planetIndex;
        NavTargetMoonIndex = -1;
        NavTargetStationIndex = -1;
        NavTargetName = name;
        NavTargetColor = color;
    }

    /// <summary>Set a moon as the nav target.</summary>
    public void SetNavTargetMoon(int planetIndex, int moonIndex, string name, Color3 color)
    {
        NavTargetType = NavigationTargetType.Moon;
        NavTargetPlanetIndex = planetIndex;
        NavTargetMoonIndex = moonIndex;
        NavTargetStationIndex = -1;
        NavTargetName = name;
        NavTargetColor = color;
    }

    /// <summary>Set a station as the nav target.</summary>
    public void SetNavTargetStation(int stationIndex, string name, Color3 color)
    {
        NavTargetType = NavigationTargetType.Station;
        NavTargetPlanetIndex = -1;
        NavTargetMoonIndex = -1;
        NavTargetStationIndex = stationIndex;
        NavTargetName = name;
        NavTargetColor = color;
    }

    /// <summary>Set a surface-level target (settlement, ship, etc.) as the nav target.</summary>
    public void SetNavTargetSurface(string name, Color3 color, float worldX, float worldY)
    {
        NavTargetType = NavigationTargetType.SurfaceTarget;
        NavTargetPlanetIndex = -1;
        NavTargetMoonIndex = -1;
        NavTargetStationIndex = -1;
        NavTargetName = name;
        NavTargetColor = color;
        NavTargetWorldX = worldX;
        NavTargetWorldY = worldY;
    }

    /// <summary>Clear the navigation target.</summary>
    public void ClearNavigationTarget()
    {
        NavTargetType = NavigationTargetType.None;
        NavTargetPlanetIndex = -1;
        NavTargetMoonIndex = -1;
        NavTargetStationIndex = -1;
        NavTargetName = "";
        NavTargetColor = new Color3(255, 200, 100);
        NavTargetWorldX = 0;
        NavTargetWorldY = 0;
    }
}

/// <summary>Type of navigation target the player can set.</summary>
public enum NavigationTargetType
{
    None,
    Star,
    Planet,
    Moon,
    Station,
    SurfaceTarget
}
