namespace SpaceExplorationGame.Core.Config;

/// <summary>
/// Configuration constants for world events: anomalies, derelict ships, and distress signals.
/// </summary>
public static class WorldEventConfig
{
    // ── Anomalies ──────────────────────────────────────────────
    /// <summary>Chance per system to have an anomaly (0-1).</summary>
    public const float AnomalyChance = 0.15f;

    /// <summary>Resource yield multiplier for ResourceSurge anomaly.</summary>
    public const float ResourceSurgeMultiplier = 2.0f;

    /// <summary>Danger level bonus for GravityStorm anomaly.</summary>
    public const int GravityStormDangerBonus = 2;

    // ── Derelict Ships ─────────────────────────────────────────
    /// <summary>Max derelicts per system (0 to MaxDerelictsPerSystem based on danger).</summary>
    public const int MaxDerelictsPerSystem = 3;

    /// <summary>Chance per slot that a derelict spawns (0-1).</summary>
    public const float DerelictSpawnChance = 0.5f;

    /// <summary>Base credit reward range for salvaging a derelict.</summary>
    public const int DerelictMinCredits = 50;
    public const int DerelictMaxCredits = 500;

    /// <summary>Chance to find a rare/legendary part in a derelict (0-1).</summary>
    public const float DerelictRarePartChance = 0.10f;

    /// <summary>Collision/interaction radius for derelict ships.</summary>
    public const float DerelictRadius = 40f;

    // ── Distress Signals ───────────────────────────────────────
    /// <summary>Chance per system visit to spawn a distress signal (0-1).</summary>
    public const float DistressSignalChance = 0.25f;

    /// <summary>Chance the distress signal is a pirate ambush (0-1).</summary>
    public const float DistressAmbushChance = 0.40f;

    /// <summary>Credit reward for helping a trader in distress.</summary>
    public const int DistressHelpCreditsMin = 100;
    public const int DistressHelpCreditsMax = 400;

    /// <summary>Reputation reward for helping a trader.</summary>
    public const int DistressHelpReputation = 15;

    /// <summary>Number of pirates to spawn in an ambush (danger-scaled).</summary>
    public const int AmbushBasePirates = 2;

    /// <summary>Interaction radius for distress beacons.</summary>
    public const float DistressBeaconRadius = 50f;
}
