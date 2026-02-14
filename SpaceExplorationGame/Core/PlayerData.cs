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

    // Ship equipment
    public Dictionary<ShipSlotType, ShipPart> EquippedParts { get; set; } = ShipPartCatalog.GetStarterLoadout();

    /// <summary>Parts the player owns but are not currently equipped (inventory).</summary>
    public List<ShipPart> OwnedParts { get; set; } = new();

    /// <summary>Recalculate derived stats from equipped parts. Call after changing parts.</summary>
    public void RecalculateShipStats()
    {
        var stats = GetCombinedStats();

        float oldMaxHealth = ShipMaxHealth;
        float oldMaxFuel = ShipMaxFuel;

        ShipMaxHealth = stats.MaxHull > 0 ? stats.MaxHull : 100f;
        ShipMaxFuel = stats.MaxFuel > 0 ? stats.MaxFuel : 100f;

        // Clamp current values to new maximums
        ShipHealth = Math.Min(ShipHealth, ShipMaxHealth);
        ShipFuel = Math.Min(ShipFuel, ShipMaxFuel);
    }

    /// <summary>Sum up stats from all equipped parts.</summary>
    public ShipPartStats GetCombinedStats()
    {
        float accel = 0, maxSpd = 0, rot = 0, hull = 0, fuel = 0, ftl = 0;
        float shield = 0, dmg = 0, fuelEff = 0;

        foreach (var part in EquippedParts.Values)
        {
            var s = part.Stats;
            accel += s.Acceleration;
            maxSpd += s.MaxSpeed;
            rot += s.RotationSpeed;
            hull += s.MaxHull;
            fuel += s.MaxFuel;
            ftl += s.FtlRange;
            shield += s.ShieldStrength;
            dmg += s.WeaponDamage;
            fuelEff += s.FuelEfficiency;
        }

        return new ShipPartStats(accel, maxSpd, rot, hull, fuel, ftl, shield, dmg, fuelEff);
    }

    /// <summary>Deduct fuel for an FTL jump. Returns false if not enough fuel.</summary>
    public bool TrySpendFuel(float amount)
    {
        // Apply fuel efficiency from parts
        float efficiency = 1f - GetCombinedStats().FuelEfficiency;
        float actual = amount * Math.Max(0.1f, efficiency);
        if (ShipFuel < actual) return false;
        ShipFuel -= actual;
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

    // Vehicle
    public bool HasVehicle { get; set; } = true;   // player starts with a vehicle
    public bool InVehicle { get; set; } = false;    // currently driving?
}
