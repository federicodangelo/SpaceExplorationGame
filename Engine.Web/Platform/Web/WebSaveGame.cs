using System.Text.Json;
using Engine.Platform;

namespace Engine.Platform.Web;

/// <summary>
/// Web (localStorage) save game implementation.
/// Stores saves as localStorage entries with key prefix "savegame_".
/// Maintains up to 3 backups per player for corruption recovery.
/// </summary>
public sealed class WebSaveGame : ISaveGame
{
    private const int MaxBackups = 3;
    private const string Prefix = "savegame_";

    public void Save(string playerId, string json)
    {
        try
        {
            var mainKey = GetMainKey(playerId);

            // Rotate backups
            for (int i = MaxBackups; i >= 1; i--)
            {
                var srcKey = i == 1 ? mainKey : GetBackupKey(playerId, i - 1);
                var dstKey = GetBackupKey(playerId, i);
                var srcVal = SafeLoad(srcKey);
                if (srcVal != null)
                    JsSettings.Save(dstKey, srcVal);
            }

            JsSettings.Save(mainKey, json);
            EnsureIndexed(playerId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to save: {ex.Message}");
        }
    }

    public string? Load(string playerId)
    {
        var mainKey = GetMainKey(playerId);
        if (TryReadValid(mainKey) is { } mainJson)
            return mainJson;

        for (int i = 1; i <= MaxBackups; i++)
        {
            var backupKey = GetBackupKey(playerId, i);
            if (TryReadValid(backupKey) is { } backupJson)
            {
                Console.WriteLine($"[SaveGame] Main save corrupted, recovered from backup {i}");
                return backupJson;
            }
        }

        return null;
    }

    public void Delete(string playerId)
    {
        try
        {
            // localStorage doesn't have a "delete" via our interop, but we can save empty
            JsSettings.Save(GetMainKey(playerId), "");
            for (int i = 1; i <= MaxBackups; i++)
                JsSettings.Save(GetBackupKey(playerId, i), "");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to delete: {ex.Message}");
        }
    }

    public IReadOnlyList<SaveGameInfo> ListSaves()
    {
        // Web implementation: we store a save index in localStorage to track known player IDs
        var result = new List<SaveGameInfo>();
        var indexJson = SafeLoad(Prefix + "index");
        if (indexJson == null) return result;

        try
        {
            var playerIds = JsonSerializer.Deserialize<List<string>>(indexJson);
            if (playerIds == null) return result;

            foreach (var pid in playerIds)
            {
                var json = TryReadValid(GetMainKey(pid));
                if (json == null) continue;

                var info = TryParseInfo(json);
                if (info != null)
                    result.Add(info);
            }
        }
        catch { }

        result.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return result;
    }

    /// <summary>Update the save index to include this player ID.</summary>
    private void EnsureIndexed(string playerId)
    {
        try
        {
            var indexJson = SafeLoad(Prefix + "index");
            var playerIds = new List<string>();
            if (indexJson != null)
            {
                try { playerIds = JsonSerializer.Deserialize<List<string>>(indexJson) ?? new(); }
                catch { playerIds = new(); }
            }
            if (!playerIds.Contains(playerId))
            {
                playerIds.Add(playerId);
                JsSettings.Save(Prefix + "index", JsonSerializer.Serialize(playerIds));
            }
        }
        catch { }
    }

    private static string GetMainKey(string playerId) => $"{Prefix}{playerId}";
    private static string GetBackupKey(string playerId, int index) => $"{Prefix}{playerId}_bak{index}";

    private static string? SafeLoad(string key)
    {
        try { return JsSettings.Load(key); }
        catch { return null; }
    }

    private static string? TryReadValid(string key)
    {
        var json = SafeLoad(key);
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

    private static SaveGameInfo? TryParseInfo(string json)
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
