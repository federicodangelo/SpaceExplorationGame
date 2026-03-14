using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Platform;

namespace Engine.Platform.Sdl;

/// <summary>
/// File-based save game implementation for desktop (SDL).
/// Stores saves in a "saves" directory next to the executable.
/// Maintains up to <see cref="MaxBackups"/> backup copies per player for corruption recovery.
/// </summary>
public sealed class SdlSaveGame : ISaveGame
{
    private const int MaxBackups = 3;
    private const string SaveDir = "saves";
    private readonly string _savePath;

    public SdlSaveGame()
    {
        _savePath = Path.Combine(AppContext.BaseDirectory, SaveDir);
    }

    public void Save(string playerId, string json)
    {
        try
        {
            Directory.CreateDirectory(_savePath);
            var mainFile = GetMainPath(playerId);

            // Rotate backups: .bak3 → delete, .bak2 → .bak3, .bak1 → .bak2, main → .bak1
            for (int i = MaxBackups; i >= 1; i--)
            {
                var src = i == 1 ? mainFile : GetBackupPath(playerId, i - 1);
                var dst = GetBackupPath(playerId, i);
                if (File.Exists(src))
                {
                    if (File.Exists(dst))
                        File.Delete(dst);
                    File.Move(src, dst);
                }
            }

            File.WriteAllText(mainFile, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to save: {ex.Message}");
        }
    }

    public string? Load(string playerId)
    {
        // Try main file first, then backups in order
        var mainFile = GetMainPath(playerId);
        if (TryReadValid(mainFile) is { } mainJson)
            return mainJson;

        for (int i = 1; i <= MaxBackups; i++)
        {
            var backupFile = GetBackupPath(playerId, i);
            if (TryReadValid(backupFile) is { } backupJson)
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
            var mainFile = GetMainPath(playerId);
            if (File.Exists(mainFile))
                File.Delete(mainFile);

            for (int i = 1; i <= MaxBackups; i++)
            {
                var backup = GetBackupPath(playerId, i);
                if (File.Exists(backup))
                    File.Delete(backup);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to delete: {ex.Message}");
        }
    }

    public IReadOnlyList<SaveGameInfo> ListSaves()
    {
        var result = new List<SaveGameInfo>();
        if (!Directory.Exists(_savePath))
            return result;

        foreach (var file in Directory.GetFiles(_savePath, "*.json"))
        {
            var info = TryReadInfo(file);
            if (info != null)
                result.Add(info);
        }

        // Most recent first
        result.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return result;
    }

    private string GetMainPath(string playerId) =>
        Path.Combine(_savePath, $"{SanitizeFileName(playerId)}.json");

    private string GetBackupPath(string playerId, int index) =>
        Path.Combine(_savePath, $"{SanitizeFileName(playerId)}.bak{index}");

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, input[i]) >= 0 ? '_' : input[i];
        return new string(sanitized);
    }

    private static string? TryReadValid(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            // Minimal validation: must parse as JSON and contain a playerId
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("playerId", out _))
                return json;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static SaveGameInfo? TryReadInfo(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var playerId = root.TryGetProperty("playerId", out var pidProp) ? pidProp.GetString() ?? "" : "";
            var playerName = root.TryGetProperty("playerName", out var nameProp) ? nameProp.GetString() ?? "Player" : "Player";
            var savedAt = root.TryGetProperty("savedAt", out var timeProp) && timeProp.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;
            var locationDesc = root.TryGetProperty("locationDescription", out var locProp) ? locProp.GetString() : null;

            if (string.IsNullOrEmpty(playerId)) return null;

            return new SaveGameInfo(playerId, playerName, savedAt, locationDesc);
        }
        catch
        {
            return null;
        }
    }
}
