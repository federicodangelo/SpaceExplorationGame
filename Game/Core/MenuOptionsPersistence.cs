namespace SpaceExplorationGame.Core;

/// <summary>
/// Persists menu option selections (danger, location, debug) across sessions
/// using the platform <see cref="Engine.Platform.ISettings"/> service.
/// </summary>
public sealed class MenuOptionsPersistence
{
    private const string KeyDangerIndex = "menu.dangerIndex";
    private const string KeyLocationIndex = "menu.locationIndex";
    private const string KeySubLocationIndex = "menu.subLocationIndex";
    private const string KeyDebugStarTypeIndex = "menu.debugStarTypeIndex";
    private const string KeyDebugShipTypeIndex = "menu.debugShipTypeIndex";
    private const string KeyDebugSelectedIndex = "menu.debugSelectedIndex";

    private readonly Engine.Platform.ISettings _settings;

    public MenuOptionsPersistence(Engine.Platform.ISettings settings)
    {
        _settings = settings;
    }

    public (int dangerIndex, int locationIndex, int subLocationIndex) GetMainMenuSelections()
    {
        return (
            LoadInt(KeyDangerIndex),
            LoadInt(KeyLocationIndex),
            LoadInt(KeySubLocationIndex));
    }

    public void SetMainMenuSelections(int dangerIndex, int locationIndex, int subLocationIndex)
    {
        SaveInt(KeyDangerIndex, dangerIndex);
        SaveInt(KeyLocationIndex, locationIndex);
        SaveInt(KeySubLocationIndex, subLocationIndex);
    }

    public (int starTypeIndex, int shipTypeIndex, int selectedIndex) GetDebugSelections()
    {
        return (
            LoadInt(KeyDebugStarTypeIndex),
            LoadInt(KeyDebugShipTypeIndex),
            LoadInt(KeyDebugSelectedIndex));
    }

    public void SetDebugSelections(int starTypeIndex, int shipTypeIndex, int selectedIndex)
    {
        SaveInt(KeyDebugStarTypeIndex, starTypeIndex);
        SaveInt(KeyDebugShipTypeIndex, shipTypeIndex);
        SaveInt(KeyDebugSelectedIndex, selectedIndex);
    }

    private int LoadInt(string key)
    {
        var value = _settings.Load(key);
        return value != null && int.TryParse(value, out int result) ? result : 0;
    }

    private void SaveInt(string key, int value)
    {
        _settings.Save(key, value.ToString());
    }
}
