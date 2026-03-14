using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Serializable snapshot of all player data needed to restore a game session.
/// Designed for JSON serialization with AOT source generators.
/// </summary>
public class SaveGameData
{
    // ── Identity ──
    public string PlayerId { get; set; } = "";
    public string PlayerName { get; set; } = "Player";
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    // ── Galaxy ──
    public ulong GalaxySeed { get; set; }

    // ── Location ──
    public int CurrentStarSystemIndex { get; set; } = -1;
    public string ReturnContext { get; set; } = "Default"; // PlayerData.ReturnContext enum name
    public int ReturnSpaceStationIndex { get; set; } = -1;
    public int ReturnPlanetIndex { get; set; } = -1;
    public int ReturnMoonPlanetIndex { get; set; } = -1;
    public int ReturnMoonIndex { get; set; } = -1;
    public int ReturnSettlementIndex { get; set; } = -1;

    /// <summary>GameStateType name at the time of save ("SolarSystem", "PlanetSurface", "Interior").</summary>
    public string SavedStateType { get; set; } = "SolarSystem";

    // ── World positions ──
    /// <summary>Ship position in the solar system when saved.</summary>
    public float ShipWorldX { get; set; }
    public float ShipWorldY { get; set; }

    // ── Planet surface positions ──
    public bool HasSavedSurfacePositions { get; set; }
    public float SurfaceShipX { get; set; }
    public float SurfaceShipY { get; set; }
    public float SurfaceVehicleX { get; set; }
    public float SurfaceVehicleY { get; set; }
    public bool SurfaceVehicleDeployed { get; set; }
    public float SurfacePlayerX { get; set; }
    public float SurfacePlayerY { get; set; }
    public bool SurfacePlayerInVehicle { get; set; }

    /// <summary>Description of current location for display in save list.</summary>
    public string LocationDescription { get; set; } = "";

    // ── Ship ──
    public string ShipTypeId { get; set; } = "scout";
    public float ShipHealth { get; set; }
    public float ShipFuel { get; set; }
    public Dictionary<string, string> EquippedShipParts { get; set; } = new(); // slot enum name → part id
    public List<string> OwnedShipParts { get; set; } = []; // part ids

    // ── Economy ──
    public int Credits { get; set; }
    public Dictionary<string, int> Cargo { get; set; } = new(); // resource enum name → amount

    // ── Vehicle ──
    public bool HasVehicle { get; set; }
    public Dictionary<string, string> EquippedVehicleParts { get; set; } = new();
    public List<string> OwnedVehicleParts { get; set; } = [];

    // ── Avatar ──
    public float AvatarHealth { get; set; }
    public Dictionary<string, string> EquippedAvatarParts { get; set; } = new();
    public List<string> OwnedAvatarParts { get; set; } = [];

    // ── Missions ──
    public List<SavedMission> ActiveMissions { get; set; } = [];
    public List<int> ClaimedMissionIds { get; set; } = [];
    public int CompletedMissions { get; set; }

    // ── Stats ──
    public int StatTotalKills { get; set; }
    public int StatDeaths { get; set; }
    public float StatTotalDamageDealt { get; set; }
    public float StatTotalDamageReceived { get; set; }
    public Dictionary<string, int> StatKillsByFaction { get; set; } = new();
    public int StatTotalCreditsEarned { get; set; }
    public int StatTotalCreditsSpent { get; set; }
    public int StatTotalResourcesMined { get; set; }
    public Dictionary<string, int> StatResourcesMinedByType { get; set; } = new();
    public int StatPartsFound { get; set; }
    public List<int> StatSystemsVisited { get; set; } = [];
    public int StatPlanetsLanded { get; set; }
    public int StatSpaceStationsVisited { get; set; }
    public double StatPlayTimeSeconds { get; set; }

    // ── Reputation ──
    public Dictionary<string, int> ReputationStandings { get; set; } = new();

    /// <summary>Create a SaveGameData snapshot from live PlayerData.</summary>
    public static SaveGameData FromPlayerData(PlayerData player, string playerName, ulong galaxySeed, string locationDescription)
    {
        var data = new SaveGameData
        {
            PlayerId = player.Id,
            PlayerName = playerName,
            SavedAt = DateTime.UtcNow,
            GalaxySeed = galaxySeed,
            LocationDescription = locationDescription,

            // Location
            CurrentStarSystemIndex = player.CurrentStarSystemIndex,
            ReturnContext = player.SolarSystemReturnContext.ToString(),
            ReturnSpaceStationIndex = player.ReturnSpaceStationIndex,
            ReturnPlanetIndex = player.ReturnPlanetIndex,
            ReturnMoonPlanetIndex = player.ReturnMoonPlanetIndex,
            ReturnMoonIndex = player.ReturnMoonIndex,
            ReturnSettlementIndex = player.ReturnSettlementIndex,

            // World positions
            ShipWorldX = player.ShipWorldPosition.X,
            ShipWorldY = player.ShipWorldPosition.Y,

            // Surface positions
            HasSavedSurfacePositions = player.HasSavedSurfacePositions,
            SurfaceShipX = player.SavedShipX,
            SurfaceShipY = player.SavedShipY,
            SurfaceVehicleX = player.SavedVehicleX,
            SurfaceVehicleY = player.SavedVehicleY,
            SurfaceVehicleDeployed = player.SavedVehicleDeployed,
            SurfacePlayerX = player.SavedPlayerX,
            SurfacePlayerY = player.SavedPlayerY,
            SurfacePlayerInVehicle = player.SavedPlayerInVehicle,

            // Ship
            ShipTypeId = player.CurrentShipType.Id,
            ShipHealth = player.ShipHealth,
            ShipFuel = player.ShipFuel,

            // Economy
            Credits = player.Credits,

            // Vehicle
            HasVehicle = player.HasVehicle,

            // Avatar
            AvatarHealth = player.AvatarHealth,

            // Missions
            CompletedMissions = player.Missions.Completed,
        };

        // Ship parts
        foreach (var (slot, part) in player.EquippedParts)
            data.EquippedShipParts[slot.ToString()] = part.Id;
        foreach (var part in player.OwnedParts)
            data.OwnedShipParts.Add(part.Id);

        // Cargo
        foreach (var (resource, amount) in player.Cargo)
            data.Cargo[resource.ToString()] = amount;

        // Vehicle parts
        foreach (var (slot, part) in player.EquippedVehicleParts)
            data.EquippedVehicleParts[slot.ToString()] = part.Id;
        foreach (var part in player.OwnedVehicleParts)
            data.OwnedVehicleParts.Add(part.Id);

        // Avatar parts
        foreach (var (slot, part) in player.EquippedAvatarParts)
            data.EquippedAvatarParts[slot.ToString()] = part.Id;
        foreach (var part in player.OwnedAvatarParts)
            data.OwnedAvatarParts.Add(part.Id);

        // Missions
        foreach (var m in player.Missions.Active)
            data.ActiveMissions.Add(SavedMission.FromMission(m));
        data.ClaimedMissionIds = [.. player.Missions.ClaimedIds];

        // Stats
        var stats = player.Stats;
        data.StatTotalKills = stats.TotalKills;
        data.StatDeaths = stats.Deaths;
        data.StatTotalDamageDealt = stats.TotalDamageDealt;
        data.StatTotalDamageReceived = stats.TotalDamageReceived;
        data.StatKillsByFaction = new Dictionary<string, int>(stats.KillsByFaction);
        data.StatTotalCreditsEarned = stats.TotalCreditsEarned;
        data.StatTotalCreditsSpent = stats.TotalCreditsSpent;
        data.StatTotalResourcesMined = stats.TotalResourcesMined;
        data.StatResourcesMinedByType = new Dictionary<string, int>(stats.ResourcesMinedByType);
        data.StatPartsFound = stats.PartsFound;
        data.StatSystemsVisited = [.. stats.SystemsVisited];
        data.StatPlanetsLanded = stats.PlanetsLanded;
        data.StatSpaceStationsVisited = stats.SpaceStationsVisited;
        data.StatPlayTimeSeconds = stats.PlayTimeSeconds;

        // Reputation
        data.ReputationStandings = player.Reputation.SaveStandings();

        return data;
    }

    /// <summary>Restore live PlayerData from this save snapshot.</summary>
    public void ApplyToPlayerData(PlayerData player)
    {
        player.Id = PlayerId;

        // Location
        player.CurrentStarSystemIndex = CurrentStarSystemIndex;
        player.SolarSystemReturnContext = Enum.TryParse<PlayerData.ReturnContext>(ReturnContext, out var rc) ? rc : PlayerData.ReturnContext.Default;
        player.ReturnSpaceStationIndex = ReturnSpaceStationIndex;
        player.ReturnPlanetIndex = ReturnPlanetIndex;
        player.ReturnMoonPlanetIndex = ReturnMoonPlanetIndex;
        player.ReturnMoonIndex = ReturnMoonIndex;
        player.ReturnSettlementIndex = ReturnSettlementIndex;

        // World positions
        player.ShipWorldPosition = new System.Numerics.Vector2(ShipWorldX, ShipWorldY);

        // Surface positions
        if (HasSavedSurfacePositions)
            player.SaveSurfacePositions(SurfaceShipX, SurfaceShipY, SurfaceVehicleX, SurfaceVehicleY,
                SurfaceVehicleDeployed, SurfacePlayerX, SurfacePlayerY, SurfacePlayerInVehicle);
        else
            player.ClearSavedSurfacePositions();

        // Ship
        var shipType = ShipTypeCatalog.GetById(ShipTypeId) ?? ShipTypeCatalog.StarterShip;
        player.SwitchShipType(shipType);

        // Ship parts
        var equippedShip = new Dictionary<ShipSlotType, ShipPart>();
        foreach (var (slotStr, partId) in EquippedShipParts)
        {
            if (Enum.TryParse<ShipSlotType>(slotStr, out var slot))
            {
                var part = ShipPartCatalog.GetById(partId);
                if (part != null)
                    equippedShip[slot] = part;
            }
        }
        // Fill any missing slots with starter parts
        var starterLoadout = ShipPartCatalog.GetStarterLoadout(shipType);
        foreach (var slot in shipType.AvailableSlots)
        {
            if (!equippedShip.ContainsKey(slot))
                equippedShip[slot] = starterLoadout[slot];
        }
        player.EquippedParts = equippedShip;

        player.OwnedParts = OwnedShipParts
            .Select(id => ShipPartCatalog.GetById(id))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        player.RecalculateShipStats();
        player.ShipHealth = Math.Min(ShipHealth, player.ShipMaxHealth);
        player.ShipFuel = Math.Min(ShipFuel, player.ShipMaxFuel);

        // Economy
        player.Credits = Credits;
        player.Cargo.Clear();
        foreach (var (resourceStr, amount) in Cargo)
        {
            if (Enum.TryParse<ResourceType>(resourceStr, out var resource))
                player.Cargo[resource] = amount;
        }

        // Vehicle
        player.HasVehicle = HasVehicle;

        // Vehicle parts
        var equippedVehicle = new Dictionary<VehicleSlotType, VehiclePart>();
        foreach (var (slotStr, partId) in EquippedVehicleParts)
        {
            if (Enum.TryParse<VehicleSlotType>(slotStr, out var slot))
            {
                var part = VehiclePartCatalog.GetById(partId);
                if (part != null)
                    equippedVehicle[slot] = part;
            }
        }
        var starterVehicle = VehiclePartCatalog.GetStarterLoadout();
        foreach (var slotVal in Enum.GetValues<VehicleSlotType>())
        {
            if (!equippedVehicle.ContainsKey(slotVal) && starterVehicle.ContainsKey(slotVal))
                equippedVehicle[slotVal] = starterVehicle[slotVal];
        }
        player.EquippedVehicleParts = equippedVehicle;

        player.OwnedVehicleParts = OwnedVehicleParts
            .Select(id => VehiclePartCatalog.GetById(id))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        // Avatar parts
        var equippedAvatar = new Dictionary<AvatarSlotType, AvatarPart>();
        foreach (var (slotStr, partId) in EquippedAvatarParts)
        {
            if (Enum.TryParse<AvatarSlotType>(slotStr, out var slot))
            {
                var part = AvatarPartCatalog.GetById(partId);
                if (part != null)
                    equippedAvatar[slot] = part;
            }
        }
        var starterAvatar = AvatarPartCatalog.GetStarterLoadout();
        foreach (var slotVal in Enum.GetValues<AvatarSlotType>())
        {
            if (!equippedAvatar.ContainsKey(slotVal) && starterAvatar.ContainsKey(slotVal))
                equippedAvatar[slotVal] = starterAvatar[slotVal];
        }
        player.EquippedAvatarParts = equippedAvatar;

        player.OwnedAvatarParts = OwnedAvatarParts
            .Select(id => AvatarPartCatalog.GetById(id))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        player.AvatarHealth = AvatarHealth;
        player.RecalculateAvatarStats();
        player.AvatarHealth = Math.Min(AvatarHealth, player.AvatarMaxHealth);

        // Missions
        player.Missions.Reset();
        player.Missions.Completed = CompletedMissions;
        player.Missions.ClaimedIds = [.. ClaimedMissionIds];
        foreach (var sm in ActiveMissions)
        {
            var mission = sm.ToMission();
            if (mission != null)
                player.Missions.Active.Add(mission);
        }

        // Navigation — clear on load (targets are world-position dependent)
        player.Navigation.Clear();
        player.InVehicle = false;

        // Stats
        var stats = player.Stats;
        stats.TotalKills = StatTotalKills;
        stats.Deaths = StatDeaths;
        stats.TotalDamageDealt = StatTotalDamageDealt;
        stats.TotalDamageReceived = StatTotalDamageReceived;
        stats.KillsByFaction = new Dictionary<string, int>(StatKillsByFaction);
        stats.TotalCreditsEarned = StatTotalCreditsEarned;
        stats.TotalCreditsSpent = StatTotalCreditsSpent;
        stats.TotalResourcesMined = StatTotalResourcesMined;
        stats.ResourcesMinedByType = new Dictionary<string, int>(StatResourcesMinedByType);
        stats.PartsFound = StatPartsFound;
        stats.SystemsVisited = new HashSet<int>(StatSystemsVisited);
        stats.PlanetsLanded = StatPlanetsLanded;
        stats.SpaceStationsVisited = StatSpaceStationsVisited;
        stats.PlayTimeSeconds = StatPlayTimeSeconds;

        // Reputation
        player.Reputation.LoadStandings(ReputationStandings);
    }
}

/// <summary>
/// Serializable mission snapshot.
/// </summary>
public class SavedMission
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = ""; // MissionType enum name
    public string Status { get; set; } = ""; // MissionStatus enum name

    // Target location
    public string TargetType { get; set; } = "";
    public int TargetSystemIndex { get; set; } = -1;
    public string TargetSystemName { get; set; } = "";
    public int TargetPlanetIndex { get; set; } = -1;
    public string? TargetPlanetName { get; set; }

    // Turn-in location
    public string TurnInType { get; set; } = "";
    public int TurnInSystemIndex { get; set; } = -1;
    public string TurnInSystemName { get; set; } = "";

    // Origin location
    public string OriginType { get; set; } = "";
    public int OriginSystemIndex { get; set; } = -1;
    public string OriginSystemName { get; set; } = "";

    // Progress
    public string TargetResource { get; set; } = "";
    public int RequiredAmount { get; set; }
    public int CurrentAmount { get; set; }
    public int CreditReward { get; set; }

    public static SavedMission FromMission(Mission m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        Description = m.Description,
        Type = m.Type.ToString(),
        Status = m.Status.ToString(),

        TargetType = m.Target.Type.ToString(),
        TargetSystemIndex = m.Target.SystemIndex,
        TargetSystemName = m.Target.SystemName,
        TargetPlanetIndex = m.Target.PlanetIndex,
        TargetPlanetName = m.Target.PlanetName,

        TurnInType = m.TurnIn.Type.ToString(),
        TurnInSystemIndex = m.TurnIn.SystemIndex,
        TurnInSystemName = m.TurnIn.SystemName,

        OriginType = m.Origin.Type.ToString(),
        OriginSystemIndex = m.Origin.SystemIndex,
        OriginSystemName = m.Origin.SystemName,

        TargetResource = m.TargetResource.ToString(),
        RequiredAmount = m.RequiredAmount,
        CurrentAmount = m.CurrentAmount,
        CreditReward = m.CreditReward,
    };

    public Mission? ToMission()
    {
        if (!Enum.TryParse<MissionType>(Type, out var missionType)) return null;
        if (!Enum.TryParse<MissionStatus>(Status, out var missionStatus)) return null;
        Enum.TryParse<ResourceType>(TargetResource, out var resource);
        Enum.TryParse<GalaxyLocationType>(TargetType, out var targetLocType);
        Enum.TryParse<GalaxyLocationType>(TurnInType, out var turnInLocType);
        Enum.TryParse<GalaxyLocationType>(OriginType, out var originLocType);

        return new Mission
        {
            Id = Id,
            Title = Title,
            Description = Description,
            Type = missionType,
            Status = missionStatus,
            Target = new GalaxyLocation
            {
                Type = targetLocType,
                SystemIndex = TargetSystemIndex,
                SystemName = TargetSystemName,
                PlanetIndex = TargetPlanetIndex,
                PlanetName = TargetPlanetName,
            },
            TurnIn = new GalaxyLocation
            {
                Type = turnInLocType,
                SystemIndex = TurnInSystemIndex,
                SystemName = TurnInSystemName,
            },
            Origin = new GalaxyLocation
            {
                Type = originLocType,
                SystemIndex = OriginSystemIndex,
                SystemName = OriginSystemName,
            },
            TargetResource = resource,
            RequiredAmount = RequiredAmount,
            CurrentAmount = CurrentAmount,
            CreditReward = CreditReward,
        };
    }
}

/// <summary>
/// AOT-compatible JSON serialization context for save game data.
/// </summary>
[JsonSerializable(typeof(SaveGameData))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class SaveGameJsonContext : JsonSerializerContext;
