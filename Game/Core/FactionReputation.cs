using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Reputation standing levels, from most hostile to most friendly.
/// </summary>
public enum ReputationLevel
{
    Hostile,     // < -500
    Unfriendly,  // -500 to -100
    Neutral,     // -100 to  100
    Friendly,    //  100 to  500
    Allied,      // > 500
}

/// <summary>
/// Tracks the player's standing with each NPC faction.
/// Standing ranges from -1000 (hated) to +1000 (revered).
/// Kill events shift standings toward allied or hostile factions.
/// </summary>
public class FactionReputation
{
    public const int MinStanding = -1000;
    public const int MaxStanding = 1000;

    /// <summary>Default starting standings for a new game.</summary>
    private static readonly Dictionary<Faction, int> DefaultStandings = new()
    {
        { Faction.Pirate, -200 },
        { Faction.Trader, 100 },
        { Faction.Patrol, 100 },
    };

    /// <summary>
    /// Reputation change rules: when an entity of <c>KilledFaction</c> is destroyed,
    /// each entry describes the standing change applied to <c>AffectedFaction</c>.
    /// </summary>
    private static readonly (Faction KilledFaction, Faction AffectedFaction, int Delta)[] KillEffects =
    [
        // Killing pirates
        (Faction.Pirate, Faction.Pirate, -40),   // pirates dislike you more
        (Faction.Pirate, Faction.Trader, +20),    // traders appreciate protection
        (Faction.Pirate, Faction.Patrol, +15),    // patrols approve

        // Killing traders
        (Faction.Trader, Faction.Trader, -60),    // traders hate you
        (Faction.Trader, Faction.Patrol, -30),    // patrols disapprove of lawlessness
        (Faction.Trader, Faction.Pirate, +10),    // pirates respect ruthlessness

        // Killing patrols
        (Faction.Patrol, Faction.Patrol, -60),    // patrols consider you criminal
        (Faction.Patrol, Faction.Trader, -20),    // traders lose trust
        (Faction.Patrol, Faction.Pirate, +15),    // pirates appreciate anti-authority
    ];

    private readonly Dictionary<Faction, int> _standings = new();

    public FactionReputation()
    {
        Reset();
    }

    /// <summary>Reset standings to default values.</summary>
    public void Reset()
    {
        _standings.Clear();
        foreach (var (faction, value) in DefaultStandings)
            _standings[faction] = value;
    }

    /// <summary>Get the raw standing value for a faction.</summary>
    public int GetStanding(Faction faction)
    {
        return _standings.TryGetValue(faction, out int val) ? val : 0;
    }

    /// <summary>Get the reputation level for a faction.</summary>
    public ReputationLevel GetLevel(Faction faction)
    {
        int standing = GetStanding(faction);
        return standing switch
        {
            < -500 => ReputationLevel.Hostile,
            < -100 => ReputationLevel.Unfriendly,
            <= 100 => ReputationLevel.Neutral,
            <= 500 => ReputationLevel.Friendly,
            _ => ReputationLevel.Allied,
        };
    }

    /// <summary>Adjust standing with a faction, clamped to [MinStanding, MaxStanding].</summary>
    public void AdjustStanding(Faction faction, int delta)
    {
        if (faction == Faction.Player) return; // no self-reputation
        _standings.TryGetValue(faction, out int current);
        _standings[faction] = Math.Clamp(current + delta, MinStanding, MaxStanding);
    }

    /// <summary>
    /// Apply all reputation consequences of the player killing an entity of the given faction.
    /// </summary>
    public void OnPlayerKill(Faction killedFaction)
    {
        if (killedFaction == Faction.Player) return;

        foreach (var (killed, affected, delta) in KillEffects)
        {
            if (killed == killedFaction)
                AdjustStanding(affected, delta);
        }
    }

    /// <summary>
    /// Get all faction standings as a read-only snapshot (for save/display).
    /// Only includes NPC factions (Pirate, Trader, Patrol).
    /// </summary>
    public IReadOnlyDictionary<Faction, int> AllStandings => _standings;

    /// <summary>Load standings from a serialized dictionary (save game restore).</summary>
    public void LoadStandings(Dictionary<string, int> saved)
    {
        foreach (var (key, value) in saved)
        {
            if (Enum.TryParse<Faction>(key, out var faction) && faction != Faction.Player)
                _standings[faction] = Math.Clamp(value, MinStanding, MaxStanding);
        }
    }

    /// <summary>Export standings as string-keyed dictionary for serialization.</summary>
    public Dictionary<string, int> SaveStandings()
    {
        var result = new Dictionary<string, int>();
        foreach (var (faction, value) in _standings)
            result[faction.ToString()] = value;
        return result;
    }

    /// <summary>Get the display color for a reputation level.</summary>
    public static Color3 GetLevelColor(ReputationLevel level) => level switch
    {
        ReputationLevel.Hostile => new Color3(255, 60, 60),
        ReputationLevel.Unfriendly => new Color3(255, 160, 60),
        ReputationLevel.Neutral => new Color3(180, 180, 180),
        ReputationLevel.Friendly => new Color3(80, 200, 80),
        ReputationLevel.Allied => new Color3(80, 180, 255),
        _ => new Color3(180, 180, 180),
    };

    /// <summary>Get a faction display color (for HUD labels).</summary>
    public static Color3 GetFactionColor(Faction faction) => faction switch
    {
        Faction.Pirate => new Color3(255, 80, 80),
        Faction.Trader => new Color3(255, 220, 80),
        Faction.Patrol => new Color3(80, 200, 255),
        _ => new Color3(200, 200, 200),
    };
}
