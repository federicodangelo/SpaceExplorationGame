namespace SpaceExplorationGame.Core;

/// <summary>
/// Persistent player data that survives across state changes.
/// </summary>
public class PlayerData
{
    // Current ship template
    public string ShipType { get; set; } = "starter";
    public float ShipHealth { get; set; } = 100f;
    public float ShipMaxHealth { get; set; } = 100f;
    public float ShipFuel { get; set; } = 100f;
    public float ShipMaxFuel { get; set; } = 100f;

    // Current location
    public int CurrentStarSystemIndex { get; set; } = -1;
    public int CurrentPlanetIndex { get; set; } = -1;

    // Credits
    public int Credits { get; set; } = 1000;
}
