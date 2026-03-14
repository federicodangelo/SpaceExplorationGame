namespace SpaceExplorationGame.Core;

using System.Numerics;
using SpaceExplorationGame.Core.Config;

/// <summary>
/// Distinguishes the locally-controlled player from remote (networked) players.
/// </summary>
public enum PlayerType
{
    /// <summary>The player running on this machine — receives input, drives the camera, owns HUD state.</summary>
    Local,
    /// <summary>A remote player connected via network. Never drives local proximity / HUD logic.</summary>
    Remote,
}

/// <summary>
/// Persistent player data that survives across state changes.
/// Delegates mission tracking to <see cref="MissionTracker"/>
/// and navigation targeting to <see cref="NavigationTarget"/>.
/// </summary>
public class PlayerData
{
    /// <summary>Whether this is the local or a remote player.</summary>
    public PlayerType Type { get; private set; } = PlayerType.Local;
    public byte RemotePlayerId { get; private set; } = 255; // assigned by server for remote players, 255 means unassigned

    /// <summary>Unique persistent player ID (GUID). Assigned once on first play, persisted across saves.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Mission tracking (accept, abandon, turn-in, objective notifications).</summary>
    public MissionTracker Missions { get; } = new();

    /// <summary>Navigation target state (orbital and surface waypoints).</summary>
    public NavigationTarget Navigation { get; } = new();

    /// <summary>Lifetime statistics (combat, economy, exploration). Persisted across saves.</summary>
    public PlayerStats Stats { get; } = new();

    // Current ship
    public ShipType CurrentShipType { get; private set; } = ShipTypeCatalog.StarterShip;
    public float ShipHealth { get; set; } = ShipTypeCatalog.StarterShip.BaseHull;
    public float ShipMaxHealth { get; private set; } = ShipTypeCatalog.StarterShip.BaseHull;
    public float ShipFuel { get; set; } = ShipTypeCatalog.StarterShip.BaseFuel;
    public float ShipMaxFuel { get; private set; } = ShipTypeCatalog.StarterShip.BaseFuel;

    /// <summary>Current ship world position (updated each frame by the active state).</summary>
    public Vector2 ShipWorldPosition { get; set; }

    // Ship equipment
    public Dictionary<ShipSlotType, ShipPart> EquippedParts { get; set; } = ShipPartCatalog.GetStarterLoadout(ShipTypeCatalog.StarterShip);

    /// <summary>Parts the player owns but are not currently equipped (inventory).</summary>
    public List<ShipPart> OwnedParts { get; set; } = new();

    public static PlayerData CreateLocal() => new() { Type = PlayerType.Local };
    public static PlayerData CreateRemote(byte remotePlayerId) => new()
    {
        Type = PlayerType.Remote,
        RemotePlayerId = remotePlayerId
    };

    /// <summary>Reset all player data to initial values (new game).</summary>
    public void Reset()
    {
        // New identity for new games
        Id = Guid.NewGuid().ToString();

        // Ship
        CurrentShipType = ShipTypeCatalog.StarterShip;
        ShipHealth = ShipTypeCatalog.StarterShip.BaseHull;
        ShipMaxHealth = ShipTypeCatalog.StarterShip.BaseHull;
        ShipFuel = ShipTypeCatalog.StarterShip.BaseFuel;
        ShipMaxFuel = ShipTypeCatalog.StarterShip.BaseFuel;
        EquippedParts = ShipPartCatalog.GetStarterLoadout(ShipTypeCatalog.StarterShip);
        OwnedParts.Clear();

        // Location
        CurrentStarSystemIndex = -1;
        SolarSystemReturnContext = ReturnContext.Default;
        ReturnSpaceStationIndex = -1;
        ReturnPlanetIndex = -1;
        ReturnMoonPlanetIndex = -1;
        ReturnMoonIndex = -1;
        ReturnSettlementIndex = -1;
        ClearSavedSurfacePositions();

        // Economy
        Credits = 10000;
        Cargo.Clear();

        // Vehicle
        HasVehicle = true;
        InVehicle = false;

        // Avatar
        AvatarHealth = 100f;
        AvatarMaxHealth = 100f;
        EquippedAvatarParts = AvatarPartCatalog.GetStarterLoadout();
        OwnedAvatarParts.Clear();

        // Vehicle equipment
        EquippedVehicleParts = VehiclePartCatalog.GetStarterLoadout();
        OwnedVehicleParts.Clear();

        // Missions
        Missions.Reset();

        // Navigation target
        Navigation.Clear();

        // Stats
        Stats.Reset();
    }

    /// <summary>Recalculate derived stats from equipped parts and ship type. Call after changing parts or ship.</summary>
    public void RecalculateShipStats()
    {
        var stats = GetCombinedShipStats();

        // Hull = ship base hull + part bonuses
        ShipMaxHealth = CurrentShipType.BaseHull + stats.MaxHull;
        // Fuel = ship base fuel + part bonuses
        ShipMaxFuel = CurrentShipType.BaseFuel + stats.MaxFuel;

        // Clamp current values to new maximums
        ShipHealth = Math.Min(ShipHealth, ShipMaxHealth);
        ShipFuel = Math.Min(ShipFuel, ShipMaxFuel);
    }

    /// <summary>Sum up stats from all equipped parts. Acceleration/MaxSpeed are reduced by ship weight.</summary>
    public ShipPartStats GetCombinedShipStats()
    {
        return ShipStatsHelper.GetCombinedStats(CurrentShipType, EquippedParts.Values);
    }

    /// <summary>Deduct fuel for an FTL jump. Returns false if not enough fuel.</summary>
    public bool TrySpendFuel(float amount)
    {
        // Apply fuel efficiency from parts
        float efficiency = 1f - GetCombinedShipStats().FuelEfficiency;
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

    // Return context: where to place the player when re-entering the solar system
    public enum ReturnContext { Default, FromSpaceStation, FromPlanet, FromMoon }
    public ReturnContext SolarSystemReturnContext { get; set; } = ReturnContext.Default;
    public int ReturnSpaceStationIndex { get; set; } = -1;
    public int ReturnPlanetIndex { get; set; } = -1;
    public int ReturnMoonPlanetIndex { get; set; } = -1;  // which planet the moon belongs to
    public int ReturnMoonIndex { get; set; } = -1;        // which moon within that planet
    public int ReturnSettlementIndex { get; set; } = -1;  // which settlement on the planet/moon (for interior save)

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

    public void ClearReturnContext()
    {
        SolarSystemReturnContext = ReturnContext.Default;
        ReturnSpaceStationIndex = -1;
        ReturnPlanetIndex = -1;
        ReturnMoonPlanetIndex = -1;
        ReturnMoonIndex = -1;
        ReturnSettlementIndex = -1;
    }

    // Credits
    public int Credits { get; set; } = 10000;

    /// <summary>Add credits and record them as earned in lifetime stats.</summary>
    public void EarnCredits(int amount)
    {
        Credits += amount;
        Stats.TotalCreditsEarned += amount;
    }

    /// <summary>Deduct credits and record them as spent in lifetime stats.</summary>
    public void SpendCredits(int amount)
    {
        Credits -= amount;
        Stats.TotalCreditsSpent += amount;
    }

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
    public int MaxCargo => (int)(CurrentShipType.BaseCargo + GetCombinedShipStats().CargoCapacity);

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
        EarnCredits(total);
        Cargo.Clear();
        return total;
    }

    /// <summary>Sell a specific resource and return credits earned.</summary>
    public int SellCargo(ResourceType resource)
    {
        if (!Cargo.TryGetValue(resource, out int amount) || amount <= 0) return 0;
        int earned = amount * ResourceCatalog.Get(resource).ValuePerUnit;
        EarnCredits(earned);
        Cargo.Remove(resource);
        return earned;
    }

    /// <summary>Discard one unit of a specific cargo resource. Returns true if discarded.</summary>
    public bool TryDiscardOneCargo(ResourceType resource)
    {
        if (!Cargo.TryGetValue(resource, out int amount) || amount <= 0) return false;

        amount--;
        if (amount <= 0)
            Cargo.Remove(resource);
        else
            Cargo[resource] = amount;

        return true;
    }

    /// <summary>Discard all cargo and return the total discarded units.</summary>
    public int DiscardAllCargo()
    {
        int total = CargoUsed;
        Cargo.Clear();
        return total;
    }

    // Vehicle
    public bool HasVehicle { get; set; } = true;   // player starts with a vehicle
    public bool InVehicle { get; set; } = false;    // currently driving?

    // Avatar health (persistent across surface visits)
    public float AvatarHealth { get; set; } = AvatarConfig.AvatarBaseMaxHealth;
    public float AvatarMaxHealth { get; private set; } = AvatarConfig.AvatarBaseMaxHealth;
    public float AvatarWalkSpeed { get; private set; } = AvatarConfig.AvatarBaseWalkSpeed;

    /// <summary>Recalculate avatar max health from base + suit armor stat.</summary>
    public void RecalculateAvatarStats()
    {
        var stats = GetCombinedAvatarStats();
        AvatarMaxHealth = AvatarConfig.AvatarBaseMaxHealth + stats.Armor;
        AvatarHealth = Math.Min(AvatarHealth, AvatarMaxHealth);
        AvatarWalkSpeed = AvatarConfig.AvatarBaseWalkSpeed + stats.WalkSpeed;
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
