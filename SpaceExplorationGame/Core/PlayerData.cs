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

    /// <summary>Deduct fuel for an FTL jump. Returns false if not enough fuel.</summary>
    public bool TrySpendFuel(float amount)
    {
        if (ShipFuel < amount) return false;
        ShipFuel -= amount;
        return true;
    }

    /// <summary>Refuel up to max capacity.</summary>
    public void Refuel(float amount)
    {
        ShipFuel = Math.Min(ShipFuel + amount, ShipMaxFuel);
    }

    // Current location
    public int CurrentStarSystemIndex { get; set; } = -1;
    public int CurrentPlanetIndex { get; set; } = -1;

    // Return context: where to place the player when re-entering the solar system
    public enum ReturnContext { Default, FromStation, FromPlanet, FromMoon }
    public ReturnContext SolarSystemReturnContext { get; set; } = ReturnContext.Default;
    public int ReturnStationIndex { get; set; } = -1;
    public int ReturnPlanetIndex { get; set; } = -1;
    public int ReturnMoonPlanetIndex { get; set; } = -1;  // which planet the moon belongs to
    public int ReturnMoonIndex { get; set; } = -1;        // which moon within that planet

    // Credits
    public int Credits { get; set; } = 1000;
}
