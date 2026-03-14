using System.Text.Json;
using Engine.Platform.Base;

namespace Engine.Platform.Web;

/// <summary>
/// Web (localStorage) save game implementation.
/// Stores saves as localStorage entries with key prefix "savegame_".
/// Maintains up to <see cref="BaseSaveGame.MaxBackups"/> backups per player for corruption recovery.
/// </summary>
public sealed class WebSaveGame : BaseSaveGame
{
    private const string Prefix = "savegame_";

    protected override string GetMainKey(string playerId) => $"{Prefix}{playerId}";
    protected override string GetBackupKey(string playerId, int index) => $"{Prefix}{playerId}_bak{index}";

    protected override string? ReadRaw(string key)
    {
        try { return JsSettings.Load(key); }
        catch { return null; }
    }

    public override void Save(string playerId, string json)
    {
        try
        {
            var mainKey = GetMainKey(playerId);

            // Rotate backups
            for (int i = MaxBackups; i >= 1; i--)
            {
                var srcKey = i == 1 ? mainKey : GetBackupKey(playerId, i - 1);
                var dstKey = GetBackupKey(playerId, i);
                var srcVal = ReadRaw(srcKey);
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

    public override void Delete(string playerId)
    {
        try
        {
            JsSettings.Remove(GetMainKey(playerId));
            for (int i = 1; i <= MaxBackups; i++)
                JsSettings.Remove(GetBackupKey(playerId, i));

            RemoveFromIndex(playerId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to delete: {ex.Message}");
        }
    }

    public override IReadOnlyList<SaveGameInfo> ListSaves()
    {
        // Web implementation: we store a save index in localStorage to track known player IDs
        var result = new List<SaveGameInfo>();
        var indexJson = ReadRaw(Prefix + "index");
        if (indexJson == null) return result;

        try
        {
            var playerIds = JsonSerializer.Deserialize(indexJson, SaveGameIndexJsonContext.Default.ListString);
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

    private void EnsureIndexed(string playerId)
    {
        try
        {
            var indexJson = ReadRaw(Prefix + "index");
            var playerIds = new List<string>();
            if (indexJson != null)
            {
                try { playerIds = JsonSerializer.Deserialize(indexJson, SaveGameIndexJsonContext.Default.ListString) ?? new(); }
                catch { playerIds = new(); }
            }
            if (!playerIds.Contains(playerId))
            {
                playerIds.Add(playerId);
                JsSettings.Save(Prefix + "index", JsonSerializer.Serialize(playerIds, SaveGameIndexJsonContext.Default.ListString));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to update save index for player {playerId}: {ex.Message}");
        }
    }

    private void RemoveFromIndex(string playerId)
    {
        try
        {
            var indexJson = ReadRaw(Prefix + "index");
            if (indexJson == null) return;
            List<string> playerIds;
            try { playerIds = JsonSerializer.Deserialize(indexJson, SaveGameIndexJsonContext.Default.ListString) ?? new(); }
            catch { return; }
            if (playerIds.Remove(playerId))
                JsSettings.Save(Prefix + "index", JsonSerializer.Serialize(playerIds, SaveGameIndexJsonContext.Default.ListString));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to remove player {playerId} from save index: {ex.Message}");
        }
    }
}
