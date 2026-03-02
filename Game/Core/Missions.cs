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
    Patrol,

    /// <summary>Visit a settlement on a specific planet.</summary>
    SettlementDelivery
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

    /// <summary>All objectives met, ready to turn in at the designated station.</summary>
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

    // ── Locations ──

    /// <summary>Where the mission objective must be completed.</summary>
    public GalaxyLocation Target { get; init; }

    /// <summary>Where the mission must be turned in after completion.</summary>
    public GalaxyLocation TurnIn { get; init; }

    /// <summary>Where the mission was originally picked up.</summary>
    public GalaxyLocation Origin { get; init; }

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

    // ── Helpers ──

    /// <summary>Formatted progress string for display.</summary>
    public string ProgressText
    {
        get
        {
            if (Status == MissionStatus.Completed)
                return $"TURN IN AT {TurnIn.SystemName.ToUpper()}";

            return Type switch
            {
                MissionType.Mining => $"{CurrentAmount}/{RequiredAmount} {ResourceCatalog.Get(TargetResource).Name.ToUpper()}",
                MissionType.BountyHunt => $"{CurrentAmount}/{RequiredAmount} PIRATES",
                MissionType.Delivery => $"GO TO {Target.SystemName.ToUpper()}",
                MissionType.Exploration => $"LAND ON {Target.PlanetName?.ToUpper() ?? "?"}",
                MissionType.Patrol => $"GO TO {Target.SystemName.ToUpper()}",
                MissionType.SettlementDelivery => $"VISIT SETTLEMENT ON {Target.PlanetName?.ToUpper() ?? "?"}",
                _ => ""
            };
        }
    }

    /// <summary>Short type label for display.</summary>
    public string TypeLabel => Type switch
    {
        MissionType.Delivery => "DELIVERY",
        MissionType.Mining => "MINING",
        MissionType.BountyHunt => "BOUNTY",
        MissionType.Exploration => "EXPLORE",
        MissionType.Patrol => "PATROL",
        MissionType.SettlementDelivery => "SETTLEMENT",
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
        MissionType.SettlementDelivery => new Color3(255, 180, 120),
        _ => new Color3(200, 200, 200)
    };
}
