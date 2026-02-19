using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>Menu actions for the main menu widget.</summary>
public enum MenuAction
{
    None = -1,
    DangerLevel,
    LocationType,
    RandomizeLocation,
    EditSeed,
    RandomSeed,
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
    private const int RandomizeIdx = 2;
    private const int EditSeedIdx = 3;
    private const int RandomSeedIdx = 4;
    private const int StartGameIdx = 5;

    private static readonly string[] DangerLabels = ["ANY", "1 - SAFE", "2 - LOW", "3 - MEDIUM", "4 - HIGH", "5 - EXTREME"];
    private static readonly string[] LocationLabels = ["STAR SYSTEM", "SPACE STATION", "INSIDE SPACE STATION", "PLANET SURFACE", "SETTLEMENT", "INSIDE SETTLEMENT"];
    private static readonly StartOption[] LocationValues = [StartOption.StarSystem, StartOption.SpaceStation, StartOption.SpaceStationInside, StartOption.PlanetSurface, StartOption.Settlement, StartOption.SettlementInside];

    private static MenuOption<MenuAction>[] BuildOptions() =>
    [
        new(MenuAction.DangerLevel, $"DANGER: {DangerLabels[0]}", "Press ENTER or LEFT/RIGHT to change danger level filter"),
        new(MenuAction.LocationType, $"START AT: {LocationLabels[0]}", "Press ENTER or LEFT/RIGHT to change starting location type"),
        new(MenuAction.RandomizeLocation, "RANDOMIZE LOCATION", "Pick a new random starting spot matching the filters above"),
        new(MenuAction.EditSeed, "EDIT SEED", "Enter a specific galaxy seed"),
        new(MenuAction.RandomSeed, "NEW RANDOM SEED", "Generate a new random galaxy"),
        new(MenuAction.StartGame, ">>> START GAME <<<", "Launch the game with the current settings"),
    ];

    private readonly TextInputOverlay _seedInputOverlay = new();

    // Persist filter selections across menu recreations
    private static int s_savedDangerIndex;
    private static int s_savedLocationIndex;

    // Current cycling state
    private int _dangerIndex = s_savedDangerIndex;
    private int _locationIndex = s_savedLocationIndex;

    // ── Public state for MainMenuState ──

    /// <summary>When set, the player confirmed START GAME.</summary>
    public bool StartRequested { get; set; }

    /// <summary>Fired when the player wants to change the seed.</summary>
    public ulong? NewSeed { get; set; }

    /// <summary>Fired when the player wants to randomize the seed.</summary>
    public bool RandomizeSeed { get; set; }

    /// <summary>Fired when the location was randomized (re-roll).</summary>
    public bool RandomizeLocation { get; set; }

    /// <summary>Current danger filter: 0=ANY, 1-5=specific level.</summary>
    public int DangerFilter => _dangerIndex;

    /// <summary>Selected starting location type.</summary>
    public StartOption LocationType => LocationValues[_locationIndex];

    /// <summary>True when danger or location cycling changed (consumed by MainMenuState).</summary>
    public bool FiltersChanged { get; set; }

    /// <summary>Top Y position of the panel, for external layout (e.g. title positioning).</summary>
    public float PanelTop => PanelY;

    /// <summary>Current seed to display.</summary>
    public ulong CurrentSeed { get; set; }

    /// <summary>Location preview text (set by MainMenuState).</summary>
    public string? LocationPreview { get; set; }

    // ── Panel configuration ──

    protected override string Title => "CHOOSE YOUR ADVENTURE";
    protected override Color3 TitleColor => new(180, 200, 255);
    protected override float PanelWidth => 640;
    protected override float BottomPadding => base.BottomPadding + 75;
    protected override bool CloseOnClickOutside => false;
    protected override byte DimAlpha => 0; // MainMenuState draws its own background
    protected override string? ControlsHint => "UP/DOWN: NAVIGATE  ENTER/LEFT/RIGHT: CHANGE  ENTER: CONFIRM";

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
        StartRequested = false;
        NewSeed = null;
        RandomizeSeed = false;
        RandomizeLocation = false;
        FiltersChanged = false;
        LocationPreview = null;
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
            case MenuAction.RandomizeLocation:
                RandomizeLocation = true;
                break;
            case MenuAction.EditSeed:
                _seedInputOverlay.Open("ENTER GALAXY SEED", CurrentSeed.ToString(), numericOnly: true, maxLength: 20);
                break;
            case MenuAction.RandomSeed:
                RandomizeSeed = true;
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
        s_savedDangerIndex = _dangerIndex;
        UpdateCyclingLabels();
        FiltersChanged = true;
    }

    private void CycleLocation(int direction)
    {
        _locationIndex = (_locationIndex + direction + LocationLabels.Length) % LocationLabels.Length;
        s_savedLocationIndex = _locationIndex;
        UpdateCyclingLabels();
        FiltersChanged = true;
    }

    private void UpdateCyclingLabels()
    {
        Menu.SetOption(DangerIdx, new MenuOption<MenuAction>(MenuAction.DangerLevel,
            $"DANGER: < {DangerLabels[_dangerIndex]} >",
            "Press ENTER or LEFT/RIGHT to change danger level filter"));
        Menu.SetOption(LocationIdx, new MenuOption<MenuAction>(MenuAction.LocationType,
            $"START AT: < {LocationLabels[_locationIndex]} >",
            "Press ENTER or LEFT/RIGHT to change starting location type"));
    }

    // ── Custom input processing ──

    protected override void ProcessInput(Game game, InputManager input)
    {
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
            if (input.IsKeyPressed(SDL3.SDL.Scancode.Left))
            { CycleDanger(-1); game.Audio.PlaySfx(Audio.SfxType.MenuSelect); return; }
            if (input.IsKeyPressed(SDL3.SDL.Scancode.Right))
            { CycleDanger(1); game.Audio.PlaySfx(Audio.SfxType.MenuSelect); return; }
        }
        else if (selected == MenuAction.LocationType)
        {
            if (input.IsKeyPressed(SDL3.SDL.Scancode.Left))
            { CycleLocation(-1); game.Audio.PlaySfx(Audio.SfxType.MenuSelect); return; }
            if (input.IsKeyPressed(SDL3.SDL.Scancode.Right))
            { CycleLocation(1); game.Audio.PlaySfx(Audio.SfxType.MenuSelect); return; }
        }

        // Default menu input processing
        base.ProcessInput(game, input);
    }

    // ── Custom content rendering ──

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer, float panelX, float contentY, float panelW, float contentH)
    {
        // Render the menu
        Menu.Render(renderer, MenuX, MenuY, MenuWidth, PanelBottom);

        // Separator between config and actions
        float sep1Y = MenuY + 2 * Menu.ItemHeight;
        renderer.DrawLineScreen(panelX + 15, sep1Y, panelX + panelW - 15, sep1Y, new Color3(60, 80, 140));

        // Separator before START GAME
        float sep2Y = MenuY + (Menu.ItemCount - 1) * Menu.ItemHeight;
        renderer.DrawLineScreen(panelX + 15, sep2Y, panelX + panelW - 15, sep2Y, new Color3(60, 80, 140));

        // Compact info section below menu
        float infoY = MenuY + Menu.TotalHeight + 8;
        renderer.DrawLineScreen(panelX + 15, infoY - 4, panelX + panelW - 15, infoY - 4, new Color3(60, 80, 140));

        // Seed line
        renderer.DrawTextScreen(panelX + 15, infoY, $"Seed: {CurrentSeed}", new Color3(120, 160, 200), 1.5f);

        // Location preview (two lines)
        if (!string.IsNullOrEmpty(LocationPreview))
        {
            string[] lines = LocationPreview.Split('\n');
            float lineY = infoY + 20;
            foreach (var line in lines)
            {
                renderer.DrawTextScreen(panelX + 15, lineY, line, new Color3(160, 180, 210), 1.5f);
                lineY += 18;
            }
        }
    }
}
