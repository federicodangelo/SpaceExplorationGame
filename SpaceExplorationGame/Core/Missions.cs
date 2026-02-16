namespace SpaceExplorationGame.Core;

/// <summary>
/// Types of missions the player can undertake.
/// </summary>
public enum MissionType
{
    /// <summary>Travel to and dock at a station in a target star system.</summary>
    Delivery,

    /// <summary>Mine a specific amount of a resource type.</summary>
    Mining,

    /// <summary>Destroy a number of pirate ships.</summary>
    BountyHunt,

    /// <summary>Land on a specific planet in a target star system.</summary>
    Exploration,

    /// <summary>Travel to a specific star system.</summary>
    Patrol
}

/// <summary>
/// Current status of a mission.
/// </summary>
public enum MissionStatus
{
    /// <summary>Available on a mission board, not yet accepted.</summary>
    Available,

    /// <summary>Accepted by the player, objectives in progress.</summary>
    Active,

    /// <summary>All objectives met, ready to turn in at any mission board.</summary>
    Completed
}

/// <summary>
/// A mission with objectives, progress tracking, and rewards.
/// Missions are generated deterministically per station/settlement using seeds.
/// </summary>
public class Mission
{
    /// <summary>Unique mission ID (deterministic from board seed + mission index).</summary>
    public int Id { get; init; }

    /// <summary>Short title displayed in the mission board.</summary>
    public string Title { get; init; } = "";

    /// <summary>Longer description of the mission objective.</summary>
    public string Description { get; init; } = "";

    /// <summary>Type of mission (determines completion logic).</summary>
    public MissionType Type { get; init; }

    /// <summary>Current status.</summary>
    public MissionStatus Status { get; set; } = MissionStatus.Available;

    // ── Target information ──

    /// <summary>Star system index the mission targets (-1 if any system).</summary>
    public int TargetSystemIndex { get; init; } = -1;

    /// <summary>Name of the target star system.</summary>
    public string TargetSystemName { get; init; } = "";

    /// <summary>Planet index within the target system (-1 if N/A).</summary>
    public int TargetPlanetIndex { get; init; } = -1;

    /// <summary>Name of the target planet (null if N/A).</summary>
    public string? TargetPlanetName { get; init; }

    // ── Progress ──

    /// <summary>Resource type for mining missions.</summary>
    public ResourceType TargetResource { get; init; }

    /// <summary>Amount required (kills for bounty, units for mining).</summary>
    public int RequiredAmount { get; init; }

    /// <summary>Current progress toward RequiredAmount.</summary>
    public int CurrentAmount { get; set; }

    // ── Rewards ──

    /// <summary>Credits awarded on turn-in.</summary>
    public int CreditReward { get; init; }

    // ── Origin ──

    /// <summary>System index where this mission was picked up.</summary>
    public int OriginSystemIndex { get; init; }

    /// <summary>Name of the system where this mission was picked up.</summary>
    public string OriginSystemName { get; init; } = "";

    // ── Helpers ──

    /// <summary>Formatted progress string for display.</summary>
    public string ProgressText => Type switch
    {
        MissionType.Mining => $"{CurrentAmount}/{RequiredAmount} {ResourceCatalog.Get(TargetResource).Name.ToUpper()}",
        MissionType.BountyHunt => $"{CurrentAmount}/{RequiredAmount} PIRATES",
        MissionType.Delivery => Status == MissionStatus.Completed ? "DELIVERED" : $"GO TO {TargetSystemName.ToUpper()}",
        MissionType.Exploration => Status == MissionStatus.Completed ? "EXPLORED" : $"LAND ON {TargetPlanetName?.ToUpper() ?? "?"}",
        MissionType.Patrol => Status == MissionStatus.Completed ? "VISITED" : $"GO TO {TargetSystemName.ToUpper()}",
        _ => ""
    };

    /// <summary>Short type label for display.</summary>
    public string TypeLabel => Type switch
    {
        MissionType.Delivery => "DELIVERY",
        MissionType.Mining => "MINING",
        MissionType.BountyHunt => "BOUNTY",
        MissionType.Exploration => "EXPLORE",
        MissionType.Patrol => "PATROL",
        _ => "MISSION"
    };

    /// <summary>Color for the mission type label.</summary>
    public Color3 TypeColor => Type switch
    {
        MissionType.Delivery => new Color3(100, 200, 255),
        MissionType.Mining => new Color3(255, 200, 80),
        MissionType.BountyHunt => new Color3(255, 100, 100),
        MissionType.Exploration => new Color3(100, 255, 150),
        MissionType.Patrol => new Color3(180, 150, 255),
        _ => new Color3(200, 200, 200)
    };
}
