namespace Engine.Platform;

/// <summary>
/// Metadata about a save game slot (shown in load/continue UI).
/// </summary>
public record SaveGameInfo(
    string PlayerId,
    string PlayerName,
    DateTime SavedAt,
    string? LocationDescription
);

/// <summary>
/// Platform-provided save game persistence.
/// Single active save slot with multiple backups for corruption recovery.
/// </summary>
public interface ISaveGame
{
    /// <summary>Save game data as a JSON string. Creates backups of previous saves.</summary>
    void Save(string playerId, string json);

    /// <summary>Load the most recent valid save for a player. Returns null if none found.</summary>
    string? Load(string playerId);

    /// <summary>Delete a save game and all its backups.</summary>
    void Delete(string playerId);

    /// <summary>List all available save games (one per player ID) with metadata.</summary>
    IReadOnlyList<SaveGameInfo> ListSaves();
}
