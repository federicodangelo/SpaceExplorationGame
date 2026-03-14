using SpaceExplorationGame.ECS.Components;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Persistent lifetime statistics for a player. Tracked across sessions via save/load.
/// </summary>
public class PlayerStats
{
    // ── Combat ──
    public int TotalKills { get; set; }
    public int Deaths { get; set; }
    public float TotalDamageDealt { get; set; }
    public float TotalDamageReceived { get; set; }
    public Dictionary<string, int> KillsByFaction { get; set; } = new(); // Faction name → count

    // ── Economy ──
    public int TotalCreditsEarned { get; set; }
    public int TotalCreditsSpent { get; set; }
    public int TotalResourcesMined { get; set; }
    public Dictionary<string, int> ResourcesMinedByType { get; set; } = new(); // ResourceType name → count
    public int PartsFound { get; set; }

    // ── Exploration ──
    public HashSet<int> SystemsVisited { get; set; } = new();
    public int PlanetsLanded { get; set; }
    public int SpaceStationsVisited { get; set; }

    // ── Session timing ──
    public double PlayTimeSeconds { get; set; }

    // ── Helpers ──

    public void RecordKill(Faction faction)
    {
        TotalKills++;
        var key = faction.ToString();
        KillsByFaction.TryGetValue(key, out int count);
        KillsByFaction[key] = count + 1;
    }

    public void RecordResourceMined(ResourceType resource, int amount)
    {
        TotalResourcesMined += amount;
        var key = resource.ToString();
        ResourcesMinedByType.TryGetValue(key, out int count);
        ResourcesMinedByType[key] = count + amount;
    }

    public void Reset()
    {
        TotalKills = 0;
        Deaths = 0;
        TotalDamageDealt = 0;
        TotalDamageReceived = 0;
        KillsByFaction.Clear();
        TotalCreditsEarned = 0;
        TotalCreditsSpent = 0;
        TotalResourcesMined = 0;
        ResourcesMinedByType.Clear();
        PartsFound = 0;
        SystemsVisited.Clear();
        PlanetsLanded = 0;
        SpaceStationsVisited = 0;
        PlayTimeSeconds = 0;
    }
}
