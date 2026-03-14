using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu.Base;
using SpaceExplorationGame.Core.Config;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>Menu actions for the main menu widget.</summary>
public enum MenuAction
{
    None = -1,
    ContinueGame,
    DangerLevel,
    LocationType,
    SubLocationType,
    RandomizeLocation,
    EditSeed,
    RandomSeed,
    PlayerName,
    Debug,
    JoinServer,
    StartGame,
    Quit,
}

/// <summary>
/// Overlay for the main menu. Uses cycling entries for danger/location,
/// action entries for seed/randomize, and a prominent START GAME button.
/// </summary>
public class MainMenuOverlay : MenuPanelOverlayBase<MenuAction>
{
    // Dynamic indices — recalculated when menu is rebuilt
    private int _locationIdx;
    private int _subLocationIdx;
    private int _dangerIdx;
    private int _randomizeIdx;
    private int _editSeedIdx;
    private int _randomSeedIdx;
    private int _playerNameIdx;
    private int _startGameIdx;

    // Shorthand properties for reading
    private int LocationIdx => _locationIdx;
    private int SubLocationIdx => _subLocationIdx;
    private int DangerIdx => _dangerIdx;
    private int PlayerNameIdx => _playerNameIdx;
    private int StartGameIdx => _startGameIdx;

    private static readonly string[] DangerLabels = ["ANY", "1 - SAFE", "2 - LOW", "3 - MEDIUM", "4 - HIGH", "5 - EXTREME"];
    private static readonly string[] LocationLabels = ["SOLAR SYSTEM", "SPACE STATION", "PLANET", "SETTLEMENT", "DERELICT SHIP", "DISTRESS BEACON", "DISTRESS AMBUSH"];
    private static readonly (string Label, StartOption Value)[][] SubLocationOptions =
    [
        [("-", StartOption.StarSystem)],
        [("ORBIT", StartOption.SpaceStation), ("MENU", StartOption.SpaceStationMenu), ("INSIDE", StartOption.SpaceStationInside)],
        [("ORBIT", StartOption.Planet), ("LANDED", StartOption.PlanetSurface), ("ON FOOT", StartOption.PlanetSurfaceOnFoot), ("ON VEHICLE", StartOption.PlanetSurfaceOnVehicle)],
        [("ABOVE", StartOption.Settlement), ("INSIDE", StartOption.SettlementInside), ("ON FOOT", StartOption.SettlementOnFoot), ("ON VEHICLE", StartOption.SettlementOnVehicle)],
        [("-", StartOption.DerelictShip)],
        [("-", StartOption.DistressBeacon)],
        [("-", StartOption.DistressAmbush)],
    ];

    private static MenuOption<MenuAction>[] BuildOptions(bool canQuit, bool debugEnabled)
    {
        // Note: Continue/Delete are not included here — they're added dynamically in RebuildMenu()
        var options = new List<MenuOption<MenuAction>>
        {
            new(MenuAction.LocationType, $"LOCATION: {LocationLabels[0]}", "Adjust starting location"),
            new(MenuAction.SubLocationType, $"SUB-LOCATION: {SubLocationOptions[0][0].Label}", "Adjust starting sub-location"),
            new(MenuAction.DangerLevel, $"DANGER: {DangerLabels[0]}", "Adjust danger level filter"),
            new(MenuAction.RandomizeLocation, "RANDOMIZE LOCATION", "Pick a new random starting spot matching the filters above"),
            new(MenuAction.EditSeed, "EDIT SEED", "Enter a specific galaxy seed"),
            new(MenuAction.RandomSeed, "NEW RANDOM SEED", "Generate a new random galaxy"),
            new(MenuAction.PlayerName, "PLAYER NAME: < PLAYER >", "Your display name in multiplayer"),
        };
        if (debugEnabled)
            options.Add(new(MenuAction.Debug, "DEBUG", "Open debug utilities"));
        options.Add(new(MenuAction.JoinServer, "JOIN SERVER", "Connect to a multiplayer server"));
        options.Add(new(MenuAction.StartGame, ">>> NEW GAME <<<", "Start a new game with the current settings"));
        if (canQuit)
            options.Add(new(MenuAction.Quit, "QUIT", "Exit the game"));
        return [.. options];
    }

    private MenuOption<MenuAction>[] BuildOptionsWithSave(bool canQuit, bool debugEnabled, Engine.Platform.SaveGameInfo saveInfo)
    {
        var options = new List<MenuOption<MenuAction>>
        {
            new(MenuAction.ContinueGame, ">>> CONTINUE <<<", $"Continue: {saveInfo.PlayerName} — {saveInfo.LocationDescription ?? "Unknown"}"),
            new(MenuAction.LocationType, $"LOCATION: {LocationLabels[0]}", "Adjust starting location"),
            new(MenuAction.SubLocationType, $"SUB-LOCATION: {SubLocationOptions[0][0].Label}", "Adjust starting sub-location"),
            new(MenuAction.DangerLevel, $"DANGER: {DangerLabels[0]}", "Adjust danger level filter"),
            new(MenuAction.RandomizeLocation, "RANDOMIZE LOCATION", "Pick a new random starting spot matching the filters above"),
            new(MenuAction.EditSeed, "EDIT SEED", "Enter a specific galaxy seed"),
            new(MenuAction.RandomSeed, "NEW RANDOM SEED", "Generate a new random galaxy"),
            new(MenuAction.PlayerName, "PLAYER NAME: < PLAYER >", "Your display name in multiplayer"),
        };
        if (debugEnabled)
            options.Add(new(MenuAction.Debug, "DEBUG", "Open debug utilities"));
        options.Add(new(MenuAction.JoinServer, "JOIN SERVER", "Connect to a multiplayer server"));
        options.Add(new(MenuAction.StartGame, ">>> NEW GAME <<<", "Start a new game with the current settings"));
        if (canQuit)
            options.Add(new(MenuAction.Quit, "QUIT", "Exit the game"));
        return [.. options];
    }

    private void RebuildMenu(Engine.Platform.SaveGameInfo? saveInfo)
    {
        _hasSaveGame = saveInfo != null;
        var options = saveInfo != null
            ? BuildOptionsWithSave(_canQuit, _debugEnabled, saveInfo)
            : BuildOptions(_canQuit, _debugEnabled);

        Menu = new MenuWidget<MenuAction>(options)
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

        // Recalculate indices
        int offset = saveInfo != null ? 1 : 0; // Continue
        _locationIdx = offset;
        _subLocationIdx = offset + 1;
        _dangerIdx = offset + 2;
        _randomizeIdx = offset + 3;
        _editSeedIdx = offset + 4;
        _randomSeedIdx = offset + 5;
        _playerNameIdx = offset + 6;
        int idx = offset + 7;
        if (_debugEnabled) idx++;
        idx++; // JoinServer
        _startGameIdx = idx;
    }

    private readonly TextInputOverlay _seedInputOverlay = new();
    private readonly TextInputOverlay _playerNameInputOverlay = new();
    private readonly TextInputOverlay _serverUrlInputOverlay = new();
    private readonly SaveGameListOverlay _saveGameListOverlay = new();
    private readonly MenuOptionsPersistence _menuOptions;
    private readonly bool _canQuit;
    private readonly bool _debugEnabled;

    // Current cycling state
    private int _dangerIndex;
    private int _locationIndex;
    private int _subLocationIndex;
    private string _playerName = "Player";

    // ── Public state for MainMenuState ──

    /// <summary>When set, the player confirmed START GAME.</summary>
    public bool StartRequested { get; set; }

    /// <summary>When set, the player chose a save to continue. Returns the PlayerId.</summary>
    public string? ContinuePlayerId => _saveGameListOverlay.SelectedPlayerId;

    /// <summary>When set, the player wants to delete a save. Returns the PlayerId.</summary>
    public string? DeleteSavePlayerId => _saveGameListOverlay.DeletePlayerId;

    /// <summary>Clear consumed save game actions after handling.</summary>
    public void ClearSaveGameActions()
    {
        _saveGameListOverlay.ClearActions();
    }

    private bool _hasSaveGame;

    /// <summary>Fired when the player wants to change the seed.</summary>
    public ulong? NewSeed { get; set; }

    /// <summary>Fired when the player wants to randomize the seed.</summary>
    public bool RandomizeSeed { get; set; }

    /// <summary>Fired when the location was randomized (re-roll).</summary>
    public bool RandomizeLocation { get; set; }

    /// <summary>Fired when the debug overlay should be opened.</summary>
    public bool DebugRequested { get; set; }

    /// <summary>Fired when the player wants to quit the game.</summary>
    public bool QuitRequested { get; set; }

    /// <summary>When non-null, the player wants to join a server at this URL.</summary>
    public string? JoinServerUrl { get; set; }

    /// <summary>Current player name for multiplayer.</summary>
    public string PlayerName => _playerName;

    /// <summary>Current danger filter: 0=ANY, 1-5=specific level.</summary>
    public int DangerFilter => _dangerIndex;

    /// <summary>Selected starting location type.</summary>
    public StartOption LocationType => CurrentSubLocations[_subLocationIndex].Value;

    /// <summary>True when danger or location cycling changed (consumed by MainMenuState).</summary>
    public bool FiltersChanged { get; set; }

    /// <summary>Top Y position of the panel, for external layout (e.g. title positioning).</summary>
    public float PanelTop => PanelY;

    /// <summary>Right edge X of the panel, for positioning adjacent panels.</summary>
    public float PanelRight => PanelX + PanelWidth;

    /// <summary>Vertical center Y of the panel, for positioning adjacent panels.</summary>
    public float PanelCenterY => PanelY + PanelHeight / 2f;

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

    public MainMenuOverlay(MenuOptionsPersistence menuOptions, bool canQuit = false)
    {
        _menuOptions = menuOptions;
        _canQuit = canQuit;
        _debugEnabled = WindowConfig.Debug;
        RebuildMenu(null); // Will be rebuilt in Open() with save info

        RegisterSubOverlay(_seedInputOverlay);
        RegisterSubOverlay(_playerNameInputOverlay);
        RegisterSubOverlay(_serverUrlInputOverlay);
        RegisterSubOverlay(_saveGameListOverlay);
    }

    // ── Open ──

    /// <summary>Call this to detect save game state and rebuild menu accordingly.</summary>
    public void DetectSaveGame(Game game)
    {
        var saveInfo = game.GetMostRecentSaveInfo();
        RebuildMenu(saveInfo);
        UpdateCyclingLabels();
        UpdatePlayerNameLabel();
        Menu.SelectedIndex = _hasSaveGame ? 0 : StartGameIdx;
    }

    public override void Open()
    {
        base.Open();
        var (savedDangerIndex, savedLocationIndex, savedSubLocationIndex) = _menuOptions.GetMainMenuSelections();
        _dangerIndex = Math.Clamp(savedDangerIndex, 0, DangerLabels.Length - 1);
        _locationIndex = Math.Clamp(savedLocationIndex, 0, LocationLabels.Length - 1);
        _subLocationIndex = Math.Clamp(savedSubLocationIndex, 0, CurrentSubLocations.Length - 1);
        StartRequested = false;
        _saveGameListOverlay.Close();
        NewSeed = null;
        RandomizeSeed = false;
        RandomizeLocation = false;
        DebugRequested = false;
        QuitRequested = false;
        JoinServerUrl = null;
        FiltersChanged = false;
        LocationPreview = null;
        StartingShipOverrideText = null;
        _playerName = _menuOptions.GetPlayerName();
        // Keep _dangerIndex and _locationIndex from previous session
        UpdateCyclingLabels();
        UpdatePlayerNameLabel();
        Menu.SelectedIndex = _hasSaveGame ? 0 : StartGameIdx;
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
            case MenuAction.PlayerName:
                _playerNameInputOverlay.Open("ENTER PLAYER NAME", _playerName, numericOnly: false, maxLength: 24);
                break;
            case MenuAction.Debug:
                DebugRequested = true;
                break;
            case MenuAction.JoinServer:
                _serverUrlInputOverlay.Open("ENTER SERVER URL", "ws://localhost:9050/", numericOnly: false, maxLength: 256);
                break;
            case MenuAction.ContinueGame:
            {
                var saves = game.SaveGame.ListSaves();
                if (saves.Count > 0)
                    _saveGameListOverlay.Open(saves);
                break;
            }
            case MenuAction.StartGame:
                StartRequested = true;
                break;
            case MenuAction.Quit:
                QuitRequested = true;
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
        _menuOptions.SetMainMenuSelections(_dangerIndex, _locationIndex, _subLocationIndex);
    }

    private void UpdatePlayerNameLabel()
    {
        Menu.SetOption(PlayerNameIdx, new MenuOption<MenuAction>(MenuAction.PlayerName,
            $"PLAYER NAME: < {_playerName} >",
            "Your display name in multiplayer"));
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

        // Check if player name was confirmed
        if (!_playerNameInputOverlay.IsOpen)
        {
            var confirmedName = _playerNameInputOverlay.TakeConfirmedValue();
            if (confirmedName != null)
            {
                _playerName = string.IsNullOrWhiteSpace(confirmedName) ? "Player" : confirmedName.Trim();
                _menuOptions.SetPlayerName(_playerName);
                game.PlayerName = _playerName;
                UpdatePlayerNameLabel();
            }
        }

        // Check if server URL was confirmed
        if (!_serverUrlInputOverlay.IsOpen)
        {
            var confirmedUrl = _serverUrlInputOverlay.TakeConfirmedValue();
            if (!string.IsNullOrWhiteSpace(confirmedUrl))
            {
                JoinServerUrl = confirmedUrl.Trim();
            }
        }

        // Don't process other input while any sub-overlay is open
        if (_seedInputOverlay.IsOpen || _playerNameInputOverlay.IsOpen || _serverUrlInputOverlay.IsOpen)
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
        // Separator after Continue/Delete (if present)
        if (_hasSaveGame)
        {
            float sepSaveY = MenuY + 1 * Menu.ItemHeight;
            renderer.DrawLineScreen(panelX + 15, sepSaveY, panelX + panelW - 15, sepSaveY, new Color3(60, 80, 140));
        }

        // Separator between config and actions
        float sep1Y = MenuY + (_dangerIdx + 3) * Menu.ItemHeight;
        renderer.DrawLineScreen(panelX + 15, sep1Y, panelX + panelW - 15, sep1Y, new Color3(60, 80, 140));

        // Separator before START GAME
        float sep2Y = MenuY + StartGameIdx * Menu.ItemHeight;
        renderer.DrawLineScreen(panelX + 15, sep2Y, panelX + panelW - 15, sep2Y, new Color3(60, 80, 140));

        // Compact info section below menu
        float infoY = MenuY + Menu.TotalHeight + 8;
        renderer.DrawLineScreen(panelX + 15, infoY - 4, panelX + panelW - 15, infoY - 4, new Color3(60, 80, 140));

        // Seed line
        renderer.DrawTextScreen(panelX + 15, infoY + 4, $"Seed: {CurrentSeed}", new Color3(120, 160, 200), 1.5f, panelW - 30f);

        float previewStartY = infoY + 24;
        if (!string.IsNullOrWhiteSpace(StartingShipOverrideText))
        {
            renderer.DrawTextScreen(panelX + 15, previewStartY, StartingShipOverrideText, new Color3(140, 190, 220), 1.5f, panelW - 30f);
            previewStartY += 18;
        }

        // Location preview (two lines)
        if (!string.IsNullOrEmpty(LocationPreview))
        {
            string[] lines = LocationPreview.Split('\n');
            float lineY = previewStartY;
            foreach (var line in lines)
            {
                renderer.DrawTextScreen(panelX + 15, lineY, line, new Color3(160, 180, 210), 1.5f, panelW - 30f);
                lineY += 18;
            }
        }
    }
}
