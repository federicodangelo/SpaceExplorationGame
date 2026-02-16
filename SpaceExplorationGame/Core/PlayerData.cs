namespace SpaceExplorationGame.Core;

/// <summary>
/// Persistent player data that survives across state changes.
/// </summary>
public class PlayerData
{
    // Current ship
    public ShipType CurrentShipType { get; set; } = ShipTypeCatalog.StarterShip;
    public float ShipHealth { get; set; } = ShipTypeCatalog.StarterShip.BaseHull;
    public float ShipMaxHealth { get; set; } = ShipTypeCatalog.StarterShip.BaseHull;
    public float ShipFuel { get; set; } = ShipTypeCatalog.StarterShip.BaseFuel;
    public float ShipMaxFuel { get; set; } = ShipTypeCatalog.StarterShip.BaseFuel;

    // Ship equipment
    public Dictionary<ShipSlotType, ShipPart> EquippedParts { get; set; } = ShipPartCatalog.GetStarterLoadout(ShipTypeCatalog.StarterShip);

    /// <summary>Parts the player owns but are not currently equipped (inventory).</summary>
    public List<ShipPart> OwnedParts { get; set; } = new();

    /// <summary>Recalculate derived stats from equipped parts and ship type. Call after changing parts or ship.</summary>
    public void RecalculateShipStats()
    {
        var stats = GetCombinedStats();

        // Hull = ship base hull + part bonuses
        ShipMaxHealth = CurrentShipType.BaseHull + stats.MaxHull;
        // Fuel = ship base fuel + part bonuses
        ShipMaxFuel = CurrentShipType.BaseFuel + stats.MaxFuel;

        // Clamp current values to new maximums
        ShipHealth = Math.Min(ShipHealth, ShipMaxHealth);
        ShipFuel = Math.Min(ShipFuel, ShipMaxFuel);
    }

    /// <summary>Sum up stats from all equipped parts. Acceleration/MaxSpeed are reduced by ship weight.</summary>
    public ShipPartStats GetCombinedStats()
    {
        float accel = 0, maxSpd = 0, rot = 0, hull = 0, fuel = 0, ftl = 0;
        float shield = 0, dmg = 0, fuelEff = 0, cargo = 0;

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
            cargo += s.CargoCapacity;
        }

        // Apply ship weight: heavier ships are slower
        float weight = CurrentShipType.Weight;
        accel /= weight;
        maxSpd /= weight;

        return new ShipPartStats(accel, maxSpd, rot, hull, fuel, ftl, shield, dmg, fuelEff, cargo);
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

    /// <summary>Switch to a new ship type. Moves incompatible parts to inventory and fills empty slots with defaults.</summary>
    public void SwitchShipType(ShipType newType)
    {
        var oldType = CurrentShipType;
        CurrentShipType = newType;

        var newSlots = new HashSet<ShipSlotType>(newType.AvailableSlots);
        var newEquipped = new Dictionary<ShipSlotType, ShipPart>();

        // Keep parts that fit the new ship's slots
        foreach (var (slot, part) in EquippedParts)
        {
            if (newSlots.Contains(slot))
            {
                newEquipped[slot] = part;
            }
            else
            {
                // Part doesn't fit new ship → move to inventory (skip tier-0 empties)
                if (part.Tier > 0)
                    OwnedParts.Add(part);
            }
        }

        // Fill empty slots with starter parts
        var starterLoadout = ShipPartCatalog.GetStarterLoadout(newType);
        foreach (var slot in newType.AvailableSlots)
        {
            if (!newEquipped.ContainsKey(slot))
                newEquipped[slot] = starterLoadout[slot];
        }

        EquippedParts = newEquipped;

        // Recalculate and restore health/fuel proportionally
        float healthPct = ShipMaxHealth > 0 ? ShipHealth / ShipMaxHealth : 1f;
        float fuelPct = ShipMaxFuel > 0 ? ShipFuel / ShipMaxFuel : 1f;

        RecalculateShipStats();

        ShipHealth = ShipMaxHealth * healthPct;
        ShipFuel = ShipMaxFuel * fuelPct;
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

    // Planet surface position memory (preserved across settlement visits)
    /// <summary>Whether there are saved surface positions to restore (e.g. after exiting a settlement).</summary>
    public bool HasSavedSurfacePositions { get; set; }
    public float SavedShipX { get; set; }
    public float SavedShipY { get; set; }
    public float SavedVehicleX { get; set; }
    public float SavedVehicleY { get; set; }
    public bool SavedVehicleDeployed { get; set; }
    public float SavedPlayerX { get; set; }
    public float SavedPlayerY { get; set; }
    public bool SavedPlayerInVehicle { get; set; }

    /// <summary>Save surface entity positions before entering a settlement.</summary>
    public void SaveSurfacePositions(float shipX, float shipY, float vehicleX, float vehicleY,
        bool vehicleDeployed, float playerX, float playerY, bool playerInVehicle)
    {
        HasSavedSurfacePositions = true;
        SavedShipX = shipX;
        SavedShipY = shipY;
        SavedVehicleX = vehicleX;
        SavedVehicleY = vehicleY;
        SavedVehicleDeployed = vehicleDeployed;
        SavedPlayerX = playerX;
        SavedPlayerY = playerY;
        SavedPlayerInVehicle = playerInVehicle;
    }

    /// <summary>Clear saved surface positions (e.g. when leaving the planet).</summary>
    public void ClearSavedSurfacePositions()
    {
        HasSavedSurfacePositions = false;
    }

    // Credits
    public int Credits { get; set; } = 10000;

    // Cargo hold
    public Dictionary<ResourceType, int> Cargo { get; set; } = new();

    /// <summary>Total units currently in the cargo hold.</summary>
    public int CargoUsed
    {
        get
        {
            int total = 0;
            foreach (var amount in Cargo.Values) total += amount;
            return total;
        }
    }

    /// <summary>Max cargo capacity = ship base + part bonuses.</summary>
    public int MaxCargo => (int)(CurrentShipType.BaseCargo + GetCombinedStats().CargoCapacity);

    /// <summary>Remaining cargo space.</summary>
    public int CargoFree => MaxCargo - CargoUsed;

    /// <summary>Add resources to cargo. Returns actual amount added (clamped to available space).</summary>
    public int AddCargo(ResourceType resource, int amount)
    {
        int space = CargoFree;
        int toAdd = Math.Min(amount, space);
        if (toAdd <= 0) return 0;

        Cargo.TryGetValue(resource, out int current);
        Cargo[resource] = current + toAdd;
        return toAdd;
    }

    /// <summary>Sell all cargo and return credits earned.</summary>
    public int SellAllCargo()
    {
        int total = 0;
        foreach (var (resource, amount) in Cargo)
        {
            total += amount * ResourceCatalog.Get(resource).ValuePerUnit;
        }
        Credits += total;
        Cargo.Clear();
        return total;
    }

    /// <summary>Sell a specific resource and return credits earned.</summary>
    public int SellCargo(ResourceType resource)
    {
        if (!Cargo.TryGetValue(resource, out int amount) || amount <= 0) return 0;
        int earned = amount * ResourceCatalog.Get(resource).ValuePerUnit;
        Credits += earned;
        Cargo.Remove(resource);
        return earned;
    }

    // Vehicle
    public bool HasVehicle { get; set; } = true;   // player starts with a vehicle
    public bool InVehicle { get; set; } = false;    // currently driving?

    // Avatar health (persistent across surface visits)
    public float AvatarHealth { get; set; } = 100f;
    public float AvatarMaxHealth { get; set; } = 100f;

    /// <summary>Recalculate avatar max health from base + suit armor stat.</summary>
    public void RecalculateAvatarStats()
    {
        var stats = GetCombinedAvatarStats();
        AvatarMaxHealth = 100f + stats.Armor;
        AvatarHealth = Math.Min(AvatarHealth, AvatarMaxHealth);
    }

    // Avatar equipment
    public Dictionary<AvatarSlotType, AvatarPart> EquippedAvatarParts { get; set; } = AvatarPartCatalog.GetStarterLoadout();
    public List<AvatarPart> OwnedAvatarParts { get; set; } = new();

    /// <summary>Sum up stats from all equipped avatar parts.</summary>
    public AvatarPartStats GetCombinedAvatarStats()
    {
        float walkSpeed = 0f, oxygen = 0f, terrain = 0f, weaponDmg = 0f, armor = 0f;
        foreach (var part in EquippedAvatarParts.Values)
        {
            var s = part.Stats;
            walkSpeed += s.WalkSpeed;
            oxygen += s.OxygenCapacity;
            terrain += s.TerrainPenalty;
            weaponDmg += s.WeaponDamage;
            armor += s.Armor;
        }
        return new AvatarPartStats(walkSpeed, oxygen, terrain, weaponDmg, armor);
    }

    // Vehicle equipment
    public Dictionary<VehicleSlotType, VehiclePart> EquippedVehicleParts { get; set; } = VehiclePartCatalog.GetStarterLoadout();
    public List<VehiclePart> OwnedVehicleParts { get; set; } = new();

    /// <summary>Sum up stats from all equipped vehicle parts.</summary>
    public VehiclePartStats GetCombinedVehicleStats()
    {
        float accel = 0f, maxSpd = 0f, rot = 0f, friction = 0f, vis = 0f;
        foreach (var part in EquippedVehicleParts.Values)
        {
            var s = part.Stats;
            accel += s.Acceleration;
            maxSpd += s.MaxSpeed;
            rot += s.RotationSpeed;
            friction += s.Friction;
            vis += s.Visibility;
        }
        return new VehiclePartStats(accel, maxSpd, rot, friction, vis);
    }
}
