namespace SpaceExplorationGame.Core;

/// <summary>
/// The kind of location a <see cref="GalaxyLocation"/> points to.
/// </summary>
public enum GalaxyLocationType
{
    /// <summary>No specific location (mine anywhere, kill anywhere).</summary>
    None,

    /// <summary>A star system (patrol, delivery, turn-in, origin).</summary>
    System,

    /// <summary>A specific planet within a star system (exploration).</summary>
    Planet,

    /// <summary>A settlement on a specific planet (settlement delivery).</summary>
    Settlement
}

/// <summary>
/// Lightweight value type that describes a location in the galaxy.
/// Used by <see cref="Mission"/> for target, turn-in, and origin locations.
/// </summary>
public readonly record struct GalaxyLocation
{
    /// <summary>What kind of location this target represents.</summary>
    public GalaxyLocationType Type { get; init; }

    /// <summary>Star system index (-1 when <see cref="Type"/> is <see cref="GalaxyLocationType.None"/>).</summary>
    public int SystemIndex { get; init; }

    /// <summary>Display name of the star system.</summary>
    public string SystemName { get; init; }

    /// <summary>Planet index within the system (-1 when not a planet target).</summary>
    public int PlanetIndex { get; init; }

    /// <summary>Display name of the planet (null when not a planet target).</summary>
    public string? PlanetName { get; init; }

    // ── Convenience properties ──

    /// <summary>True when this target has no location.</summary>
    public bool IsNone => Type == GalaxyLocationType.None;

    /// <summary>True when this target points to a specific star system (or planet within one).</summary>
    public bool HasSystem => Type != GalaxyLocationType.None;

    /// <summary>True when this target points to a specific planet.</summary>
    public bool HasPlanet => Type == GalaxyLocationType.Planet || Type == GalaxyLocationType.Settlement;

    /// <summary>True when this target points to a settlement on a planet.</summary>
    public bool HasSettlement => Type == GalaxyLocationType.Settlement;

    /// <summary>Check whether this target's system matches <paramref name="systemIndex"/>.</summary>
    public bool IsSystem(int systemIndex) => HasSystem && SystemIndex == systemIndex;

    /// <summary>Check whether this target matches a specific planet in a specific system.</summary>
    public bool IsPlanet(int systemIndex, int planetIndex) =>
        HasPlanet && SystemIndex == systemIndex && PlanetIndex == planetIndex;

    // ── Factory methods ──

    /// <summary>A target that represents no specific location.</summary>
    public static readonly GalaxyLocation None = new()
    {
        Type = GalaxyLocationType.None,
        SystemIndex = -1,
        SystemName = "",
        PlanetIndex = -1
    };

    /// <summary>Create a target pointing at a star system.</summary>
    public static GalaxyLocation ForSystem(int systemIndex, string systemName) => new()
    {
        Type = GalaxyLocationType.System,
        SystemIndex = systemIndex,
        SystemName = systemName,
        PlanetIndex = -1
    };

    /// <summary>Create a target pointing at a planet within a star system.</summary>
    public static GalaxyLocation ForPlanet(int systemIndex, string systemName,
        int planetIndex, string planetName) => new()
        {
            Type = GalaxyLocationType.Planet,
            SystemIndex = systemIndex,
            SystemName = systemName,
            PlanetIndex = planetIndex,
            PlanetName = planetName
        };

    /// <summary>Create a target pointing at a settlement on a planet.</summary>
    public static GalaxyLocation ForSettlement(int systemIndex, string systemName,
        int planetIndex, string planetName) => new()
        {
            Type = GalaxyLocationType.Settlement,
            SystemIndex = systemIndex,
            SystemName = systemName,
            PlanetIndex = planetIndex,
            PlanetName = planetName
        };
}
