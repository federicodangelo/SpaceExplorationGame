using Engine.Platform.Base;

namespace Engine.Platform.Sdl;

/// <summary>
/// File-based save game implementation for desktop (SDL).
/// Stores saves in a "saves" directory next to the executable.
/// Maintains up to <see cref="BaseSaveGame.MaxBackups"/> backup copies per player for corruption recovery.
/// </summary>
public sealed class SdlSaveGame : BaseSaveGame
{
    private const string SaveDir = "saves";
    private readonly string _savePath;

    public SdlSaveGame()
    {
        _savePath = Path.Combine(AppContext.BaseDirectory, SaveDir);
    }

    protected override string GetMainKey(string playerId) =>
        Path.Combine(_savePath, $"{SanitizeFileName(playerId)}.json");

    protected override string GetBackupKey(string playerId, int index) =>
        Path.Combine(_savePath, $"{SanitizeFileName(playerId)}.bak{index}");

    protected override string? ReadRaw(string key)
    {
        try
        {
            if (!File.Exists(key)) return null;
            return File.ReadAllText(key);
        }
        catch { return null; }
    }

    public override void Save(string playerId, string json)
    {
        try
        {
            Directory.CreateDirectory(_savePath);
            var mainFile = GetMainKey(playerId);

            // Rotate backups: .bak3 → delete, .bak2 → .bak3, .bak1 → .bak2, main → .bak1
            for (int i = MaxBackups; i >= 1; i--)
            {
                var src = i == 1 ? mainFile : GetBackupKey(playerId, i - 1);
                var dst = GetBackupKey(playerId, i);
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

    public override void Delete(string playerId)
    {
        try
        {
            var mainFile = GetMainKey(playerId);
            if (File.Exists(mainFile))
                File.Delete(mainFile);

            for (int i = 1; i <= MaxBackups; i++)
            {
                var backup = GetBackupKey(playerId, i);
                if (File.Exists(backup))
                    File.Delete(backup);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SaveGame] Failed to delete: {ex.Message}");
        }
    }

    public override IReadOnlyList<SaveGameInfo> ListSaves()
    {
        var result = new List<SaveGameInfo>();
        if (!Directory.Exists(_savePath))
            return result;

        foreach (var file in Directory.GetFiles(_savePath, "*.json"))
        {
            var json = ReadRaw(file);
            if (json == null) continue;
            var info = TryParseInfo(json);
            if (info != null)
                result.Add(info);
        }

        // Most recent first
        result.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return result;
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, input[i]) >= 0 ? '_' : input[i];
        return new string(sanitized);
    }
}
