using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Core;
using Engine.Platform;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>Menu actions for the main menu widget.</summary>
public enum MenuAction
{
    None = -1,
    DangerLevel,
    LocationType,
    SubLocationType,
    RandomizeLocation,
    EditSeed,
    RandomSeed,
    Debug,
    StartGame,
}

/// <summary>
/// Overlay for the main menu. Uses cycling entries for danger/location,
/// action entries for seed/randomize, and a prominent START GAME button.
/// </summary>
public class MainMenuOverlay : MenuPanelOverlayBase<MenuAction>
{
    // Indices into the Options array for dynamic label updates
    private const int DangerIdx = 0;
    private const int LocationIdx = 1;
    private const int SubLocationIdx = 2;
    private const int RandomizeIdx = 3;
    private const int EditSeedIdx = 4;
    private const int RandomSeedIdx = 5;
    private const int DebugIdx = 6;
    private const int StartGameIdx = 7;

    private static readonly string[] DangerLabels = ["ANY", "1 - SAFE", "2 - LOW", "3 - MEDIUM", "4 - HIGH", "5 - EXTREME"];
    private static readonly string[] LocationLabels = ["SOLAR SYSTEM", "SPACE STATION", "PLANET", "SETTLEMENT"];
    private static readonly (string Label, StartOption Value)[][] SubLocationOptions =
    [
        [("-", StartOption.StarSystem)],
        [("ORBIT", StartOption.SpaceStation), ("DOCKED", StartOption.SpaceStationDocked), ("INSIDE", StartOption.SpaceStationInside)],
        [("ORBIT", StartOption.Planet), ("LANDED", StartOption.PlanetSurface), ("ON FOOT", StartOption.PlanetSurfaceOnFoot), ("ON VEHICLE", StartOption.PlanetSurfaceOnVehicle)],
        [("ABOVE", StartOption.Settlement), ("INSIDE", StartOption.SettlementInside), ("ON FOOT", StartOption.SettlementOnFoot), ("ON VEHICLE", StartOption.SettlementOnVehicle)],
    ];

    private static MenuOption<MenuAction>[] BuildOptions() =>
    [
        new(MenuAction.DangerLevel, $"DANGER: {DangerLabels[0]}", "Adjust danger level filter"),
        new(MenuAction.LocationType, $"LOCATION: {LocationLabels[0]}", "Adjust starting location"),
        new(MenuAction.SubLocationType, $"SUB-LOCATION: {SubLocationOptions[0][0].Label}", "Adjust starting sub-location"),
        new(MenuAction.RandomizeLocation, "RANDOMIZE LOCATION", "Pick a new random starting spot matching the filters above"),
        new(MenuAction.EditSeed, "EDIT SEED", "Enter a specific galaxy seed"),
        new(MenuAction.RandomSeed, "NEW RANDOM SEED", "Generate a new random galaxy"),
        new(MenuAction.Debug, "DEBUG", "Open debug utilities"),
        new(MenuAction.StartGame, ">>> START GAME <<<", "Launch the game with the current settings"),
    ];

    private readonly TextInputOverlay _seedInputOverlay = new();

    // Current cycling state
    private int _dangerIndex;
    private int _locationIndex;
    private int _subLocationIndex;

    // ── Public state for MainMenuState ──

    /// <summary>When set, the player confirmed START GAME.</summary>
    public bool StartRequested { get; set; }

    /// <summary>Fired when the player wants to change the seed.</summary>
    public ulong? NewSeed { get; set; }

    /// <summary>Fired when the player wants to randomize the seed.</summary>
    public bool RandomizeSeed { get; set; }

    /// <summary>Fired when the location was randomized (re-roll).</summary>
    public bool RandomizeLocation { get; set; }

    /// <summary>Fired when the debug overlay should be opened.</summary>
    public bool DebugRequested { get; set; }

    /// <summary>Current danger filter: 0=ANY, 1-5=specific level.</summary>
    public int DangerFilter => _dangerIndex;

    /// <summary>Selected starting location type.</summary>
    public StartOption LocationType => CurrentSubLocations[_subLocationIndex].Value;

    /// <summary>True when danger or location cycling changed (consumed by MainMenuState).</summary>
    public bool FiltersChanged { get; set; }

    /// <summary>Top Y position of the panel, for external layout (e.g. title positioning).</summary>
    public float PanelTop => PanelY;

    /// <summary>Current seed to display.</summary>
    public ulong CurrentSeed { get; set; }

    /// <summary>Location preview text (set by MainMenuState).</summary>
    public string? LocationPreview { get; set; }

    /// <summary>Optional starting ship override text (set by MainMenuState).</summary>
    public string? StartingShipOverrideText { get; set; }

    // ── Panel configuration ──

    protected override string Title => "CHOOSE YOUR ADVENTURE";
    protected override Color3 TitleColor => new(180, 200, 255);
    protected override float PanelWidth => 640;
    protected override float BottomPadding => base.BottomPadding + 75;
    protected override bool CloseOnClickOutside => false;
    protected override byte DimAlpha => 0; // MainMenuState draws its own background
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}: NAVIGATE  " +
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}/{input.GetActionHelpText(InputAction.MenuLeft)}/{input.GetActionHelpText(InputAction.MenuRight)}: CHANGE  " +
                   $"{input.GetActionHelpText(InputAction.MenuConfirm)}: CONFIRM";
        }
    }

    // ── Constructor ──

    public MainMenuOverlay()
    {
        Menu = new MenuWidget<MenuAction>(BuildOptions())
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.5f,
            NormalScale = 2f,
            SelectedColor = new Color3(220, 240, 255),
            NormalColor = new Color3(140, 140, 160),
            HighlightBg = new Color3(40, 60, 120),
            HighlightAlpha = 180,
            DescriptionScale = 1.5f,
            DescriptionColor = new Color3(160, 160, 180)
        };

        RegisterSubOverlay(_seedInputOverlay);
    }

    // ── Open ──

    public override void Open()
    {
        base.Open();
        var (savedDangerIndex, savedLocationIndex, savedSubLocationIndex) = MenuOptionsPersistence.GetMainMenuSelections();
        _dangerIndex = Math.Clamp(savedDangerIndex, 0, DangerLabels.Length - 1);
        _locationIndex = Math.Clamp(savedLocationIndex, 0, LocationLabels.Length - 1);
        _subLocationIndex = Math.Clamp(savedSubLocationIndex, 0, CurrentSubLocations.Length - 1);
        StartRequested = false;
        NewSeed = null;
        RandomizeSeed = false;
        RandomizeLocation = false;
        DebugRequested = false;
        FiltersChanged = false;
        LocationPreview = null;
        StartingShipOverrideText = null;
        // Keep _dangerIndex and _locationIndex from previous session
        UpdateCyclingLabels();
    }

    // ── Escape does nothing on main menu ──

    protected override void OnEscapePressed() { }

    // ── Selection ──

    protected override void OnOptionSelected(Game game, MenuAction option)
    {
        switch (option)
        {
            case MenuAction.DangerLevel:
                CycleDanger(1);
                break;
            case MenuAction.LocationType:
                CycleLocation(1);
                break;
            case MenuAction.SubLocationType:
                CycleSubLocation(1);
                break;
            case MenuAction.RandomizeLocation:
                RandomizeLocation = true;
                break;
            case MenuAction.EditSeed:
                _seedInputOverlay.Open("ENTER GALAXY SEED", CurrentSeed.ToString(), numericOnly: true, maxLength: 20);
                break;
            case MenuAction.RandomSeed:
                RandomizeSeed = true;
                break;
            case MenuAction.Debug:
                DebugRequested = true;
                break;
            case MenuAction.StartGame:
                StartRequested = true;
                break;
        }
    }

    // ── Cycling helpers ──

    private void CycleDanger(int direction)
    {
        _dangerIndex = (_dangerIndex + direction + DangerLabels.Length) % DangerLabels.Length;
        SaveSelections();
        UpdateCyclingLabels();
        FiltersChanged = true;
    }

    private void CycleLocation(int direction)
    {
        _locationIndex = (_locationIndex + direction + LocationLabels.Length) % LocationLabels.Length;
        _subLocationIndex = 0;
        SaveSelections();
        UpdateCyclingLabels();
        FiltersChanged = true;
    }

    private void CycleSubLocation(int direction)
    {
        var subLocations = CurrentSubLocations;
        _subLocationIndex = (_subLocationIndex + direction + subLocations.Length) % subLocations.Length;
        SaveSelections();
        UpdateCyclingLabels();
        FiltersChanged = true;
    }

    private (string Label, StartOption Value)[] CurrentSubLocations => SubLocationOptions[_locationIndex];

    private void SaveSelections()
    {
        MenuOptionsPersistence.SetMainMenuSelections(_dangerIndex, _locationIndex, _subLocationIndex);
    }

    private void UpdateCyclingLabels()
    {
        string confirm = CurrentInput?.GetActionHelpText(InputAction.MenuConfirm).ToUpper() ?? "CONFIRM";
        string left = CurrentInput?.GetActionHelpText(InputAction.MenuLeft).ToUpper() ?? "LEFT";
        string right = CurrentInput?.GetActionHelpText(InputAction.MenuRight).ToUpper() ?? "RIGHT";

        Menu.SetOption(DangerIdx, new MenuOption<MenuAction>(MenuAction.DangerLevel,
            $"DANGER: < {DangerLabels[_dangerIndex]} >",
            $"Press {confirm} or {left}/{right} to change danger level filter"));
        Menu.SetOption(LocationIdx, new MenuOption<MenuAction>(MenuAction.LocationType,
            $"LOCATION: < {LocationLabels[_locationIndex]} >",
            $"Press {confirm} or {left}/{right} to change starting location"));
        Menu.SetOption(SubLocationIdx, new MenuOption<MenuAction>(MenuAction.SubLocationType,
            $"SUB-LOCATION: < {CurrentSubLocations[_subLocationIndex].Label} >",
            $"Press {confirm} or {left}/{right} to change starting sub-location"));
    }

    // ── Custom input processing ──

    protected override void ProcessInput(Game game, IInputManager input)
    {
        UpdateCyclingLabels();

        // Check if seed input was confirmed
        if (!_seedInputOverlay.IsOpen)
        {
            var confirmedSeed = _seedInputOverlay.TakeConfirmedValue();
            if (confirmedSeed != null && ulong.TryParse(confirmedSeed, out ulong newSeed))
            {
                NewSeed = newSeed;
            }
        }

        // Don't process other input while seed input is open
        if (_seedInputOverlay.IsOpen)
            return;

        // Left/Right to cycle the currently selected option
        var selected = Menu.SelectedValue;
        if (selected == MenuAction.DangerLevel)
        {
            if (input.IsActionPressed(InputAction.MenuLeft))
            { CycleDanger(-1); game.Audio.PlaySfx(AudioSfx.MenuSelect); return; }
            if (input.IsActionPressed(InputAction.MenuRight))
            { CycleDanger(1); game.Audio.PlaySfx(AudioSfx.MenuSelect); return; }
        }
        else if (selected == MenuAction.LocationType)
        {
            if (input.IsActionPressed(InputAction.MenuLeft))
            { CycleLocation(-1); game.Audio.PlaySfx(AudioSfx.MenuSelect); return; }
            if (input.IsActionPressed(InputAction.MenuRight))
            { CycleLocation(1); game.Audio.PlaySfx(AudioSfx.MenuSelect); return; }
        }
        else if (selected == MenuAction.SubLocationType)
        {
            if (input.IsActionPressed(InputAction.MenuLeft))
            { CycleSubLocation(-1); game.Audio.PlaySfx(AudioSfx.MenuSelect); return; }
            if (input.IsActionPressed(InputAction.MenuRight))
            { CycleSubLocation(1); game.Audio.PlaySfx(AudioSfx.MenuSelect); return; }
        }

        // Default menu input processing
        base.ProcessInput(game, input);
    }

    // ── Custom content rendering ──

    protected override void RenderAdditionalContent(Game game, ISpriteRenderer renderer, float panelX, float contentY, float panelW, float contentH)
    {
        // Separator between config and actions
        float sep1Y = MenuY + 3 * Menu.ItemHeight;
        renderer.DrawLineScreen(panelX + 15, sep1Y, panelX + panelW - 15, sep1Y, new Color3(60, 80, 140));

        // Separator before START GAME
        float sep2Y = MenuY + (Menu.ItemCount - 1) * Menu.ItemHeight;
        renderer.DrawLineScreen(panelX + 15, sep2Y, panelX + panelW - 15, sep2Y, new Color3(60, 80, 140));

        // Compact info section below menu
        float infoY = MenuY + Menu.TotalHeight + 8;
        renderer.DrawLineScreen(panelX + 15, infoY - 4, panelX + panelW - 15, infoY - 4, new Color3(60, 80, 140));

        // Seed line
        renderer.DrawTextScreen(panelX + 15, infoY + 4, $"Seed: {CurrentSeed}", new Color3(120, 160, 200), 1.5f);

        float previewStartY = infoY + 24;
        if (!string.IsNullOrWhiteSpace(StartingShipOverrideText))
        {
            renderer.DrawTextScreen(panelX + 15, previewStartY, StartingShipOverrideText, new Color3(140, 190, 220), 1.5f);
            previewStartY += 18;
        }

        // Location preview (two lines)
        if (!string.IsNullOrEmpty(LocationPreview))
        {
            string[] lines = LocationPreview.Split('\n');
            float lineY = previewStartY;
            foreach (var line in lines)
            {
                renderer.DrawTextScreen(panelX + 15, lineY, line, new Color3(160, 180, 210), 1.5f);
                lineY += 18;
            }
        }
    }
}
