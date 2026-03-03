using System.Text.Json;
using System.Text.Json.Serialization;
using Engine.Platform.Base;

namespace Engine.Platform.Sdl;

/// <summary>
/// File-based settings implementation.
/// Stores all key/value pairs in a single JSON file next to the executable.
/// </summary>
public sealed class SdlSettings : BaseSettings
{
    private static readonly object Sync = new();
    private readonly string _filePath;
    private Dictionary<string, string>? _cache;

    public SdlSettings(string fileName = "settings.json")
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, fileName);
    }

    public override void Save(string key, string value)
    {
        lock (Sync)
        {
            var data = EnsureLoaded();
            data[key] = value;
            SaveInternal(data);
        }
    }

    public override string? Load(string key)
    {
        lock (Sync)
        {
            var data = EnsureLoaded();
            return data.GetValueOrDefault(key);
        }
    }

    private Dictionary<string, string> EnsureLoaded()
    {
        _cache ??= LoadInternal();
        return _cache;
    }

    private Dictionary<string, string> LoadInternal()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.DictionaryStringString)
                ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveInternal(Dictionary<string, string> data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, SettingsJsonContext.Default.DictionaryStringString);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;
