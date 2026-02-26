using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceExplorationGame.Core;

public sealed class PersistedMenuOptions
{
    public int DangerIndex { get; set; }
    public int LocationIndex { get; set; }
    public int DebugStarTypeIndex { get; set; }
    public int DebugShipTypeIndex { get; set; }
    public int DebugSelectedIndex { get; set; }
}

public static class MenuOptionsPersistence
{
    private static readonly object Sync = new();
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "menu-options.json");
    private static PersistedMenuOptions? _cached;

    public static (int dangerIndex, int locationIndex) GetMainMenuSelections()
    {
        var settings = EnsureLoaded();
        return (settings.DangerIndex, settings.LocationIndex);
    }

    public static void SetMainMenuSelections(int dangerIndex, int locationIndex)
    {
        lock (Sync)
        {
            var settings = EnsureLoaded();
            settings.DangerIndex = dangerIndex;
            settings.LocationIndex = locationIndex;
            SaveInternal(settings);
        }
    }

    public static (int starTypeIndex, int shipTypeIndex, int selectedIndex) GetDebugSelections()
    {
        var settings = EnsureLoaded();
        return (settings.DebugStarTypeIndex, settings.DebugShipTypeIndex, settings.DebugSelectedIndex);
    }

    public static void SetDebugSelections(int starTypeIndex, int shipTypeIndex, int selectedIndex)
    {
        lock (Sync)
        {
            var settings = EnsureLoaded();
            settings.DebugStarTypeIndex = starTypeIndex;
            settings.DebugShipTypeIndex = shipTypeIndex;
            settings.DebugSelectedIndex = selectedIndex;
            SaveInternal(settings);
        }
    }

    private static PersistedMenuOptions EnsureLoaded()
    {
        lock (Sync)
        {
            _cached ??= LoadInternal();
            return _cached;
        }
    }

    private static PersistedMenuOptions LoadInternal()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new PersistedMenuOptions();
            }

            string json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, MenuOptionsJsonContext.Default.PersistedMenuOptions) as PersistedMenuOptions
                ?? new PersistedMenuOptions();
        }
        catch
        {
            return new PersistedMenuOptions();
        }
    }

    private static void SaveInternal(PersistedMenuOptions settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, MenuOptionsJsonContext.Default.PersistedMenuOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
        }
    }
}

[JsonSerializable(typeof(PersistedMenuOptions))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class MenuOptionsJsonContext : JsonSerializerContext
{
}
