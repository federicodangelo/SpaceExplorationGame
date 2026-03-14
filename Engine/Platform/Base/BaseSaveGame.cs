using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engine.Platform.Base;

/// <summary>AOT-compatible JSON context for save-game index (list of player IDs).</summary>
[JsonSerializable(typeof(List<string>))]
public partial class SaveGameIndexJsonContext : JsonSerializerContext;

/// <summary>
/// Shared base for save-game implementations.
/// Provides backup-aware <see cref="Load"/>, JSON validation, and info parsing.
/// Subclasses supply storage read/write primitives via <see cref="ReadRaw"/>,
/// <see cref="GetMainKey"/>, and <see cref="GetBackupKey"/>.
/// </summary>
public abstract class BaseSaveGame : ISaveGame
{
    protected const int MaxBackups = 3;

    /// <summary>Returns the storage key/path for the main save of <paramref name="playerId"/>.</summary>
    protected abstract string GetMainKey(string playerId);

    /// <summary>Returns the storage key/path for backup number <paramref name="index"/> (1-based).</summary>
    protected abstract string GetBackupKey(string playerId, int index);

    /// <summary>Read raw content from storage. Returns null on any error or if the entry doesn't exist.</summary>
    protected abstract string? ReadRaw(string key);

    public abstract void Save(string playerId, string json);
    public abstract void Delete(string playerId);
    public abstract IReadOnlyList<SaveGameInfo> ListSaves();

    /// <inheritdoc/>
    public string? Load(string playerId)
    {
        if (TryReadValid(GetMainKey(playerId)) is { } mainJson)
            return mainJson;

        for (int i = 1; i <= MaxBackups; i++)
        {
            if (TryReadValid(GetBackupKey(playerId, i)) is { } backupJson)
            {
                Console.WriteLine($"[SaveGame] Main save corrupted, recovered from backup {i}");
                return backupJson;
            }
        }

        return null;
    }

    /// <summary>Read from storage and validate it is non-empty JSON containing a "playerId" field.</summary>
    protected string? TryReadValid(string key)
    {
        var json = ReadRaw(key);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("playerId", out _))
                return json;
            return null;
        }
        catch { return null; }
    }

    /// <summary>Parse a <see cref="SaveGameInfo"/> from a JSON string. Returns null on any failure.</summary>
    protected static SaveGameInfo? TryParseInfo(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var playerId = root.TryGetProperty("playerId", out var pidProp) ? pidProp.GetString() ?? "" : "";
            var playerName = root.TryGetProperty("playerName", out var nameProp) ? nameProp.GetString() ?? "Player" : "Player";
            var savedAt = root.TryGetProperty("savedAt", out var timeProp) && timeProp.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;
            var locationDesc = root.TryGetProperty("locationDescription", out var locProp) ? locProp.GetString() : null;
            if (string.IsNullOrEmpty(playerId)) return null;
            return new SaveGameInfo(playerId, playerName, savedAt, locationDesc);
        }
        catch { return null; }
    }
}
