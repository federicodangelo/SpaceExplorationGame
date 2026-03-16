using SpaceExplorationGame.Core;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for the in-game menu. Provides Resume, Missions, Controls, and Main Menu options.
/// Can be reused by any game state that needs an escape menu.
/// Note: States are responsible for toggling this overlay via Escape (Open/Close).
/// </summary>
public class InGameMenuOverlay : MenuPanelOverlayBase<InGameMenuOverlay.InGameMenuOption>
{
    public enum MapType
    {
        SolarSystem,
        Galaxy,
        Planet
    }

    public enum InGameMenuOption
    {
        Resume,
        SolarSystemMap,
        GalaxyMap,
        PlanetMap,
        Missions,
        Cargo,
        Stats,
        Controls,
        SaveGame,
        MainMenu
    }

    private static readonly MenuOption<InGameMenuOption>[] DefaultMenuOptions =
    [
        new(InGameMenuOption.Resume, "RESUME"),
        new(InGameMenuOption.SolarSystemMap, "SOLAR SYSTEM MAP"),
        new(InGameMenuOption.GalaxyMap, "GALAXY MAP"),
        new(InGameMenuOption.PlanetMap, "PLANET MAP"),
        new(InGameMenuOption.Missions, "MISSIONS"),
        new(InGameMenuOption.Cargo, "CARGO"),
        new(InGameMenuOption.Stats, "STATS"),
        new(InGameMenuOption.Controls, "CONTROLS"),
        new(InGameMenuOption.SaveGame, "SAVE GAME"),
        new(InGameMenuOption.MainMenu, "MAIN MENU")
    ];

    /// <summary>The current game state type, forwarded to the controls overlay.</summary>
    public GameStateType StateType
    {
        get => _controlsOverlay.StateType;
        set => _controlsOverlay.StateType = value;
    }

    /// <summary>Callback invoked when the player selects the Map option. Set by each game state.</summary>
    public Action<MapType>? OnMapRequested { get; set; }

    private readonly MissionsListOverlay _missionsOverlay = new();
    private readonly CargoListOverlay _cargoOverlay = new();
    private readonly StatsOverlay _statsOverlay = new();
    private readonly ControlsOverlay _controlsOverlay = new();

    // ── Panel configuration ──

    protected override string Title => "MENU";
    protected override Color3 TitleColor => new(200, 210, 255);
    protected override float PanelWidth => 360;
    protected override bool CloseOnClickOutside => true;

    // ── Constructor ──

    public InGameMenuOverlay(MapType[] availableMaps)
    {
        var menuOptions = new List<MenuOption<InGameMenuOption>>(DefaultMenuOptions);
        if (!availableMaps.Contains(MapType.SolarSystem)) menuOptions.RemoveAll(o => o.Value == InGameMenuOption.SolarSystemMap);
        if (!availableMaps.Contains(MapType.Galaxy)) menuOptions.RemoveAll(o => o.Value == InGameMenuOption.GalaxyMap);
        if (!availableMaps.Contains(MapType.Planet)) menuOptions.RemoveAll(o => o.Value == InGameMenuOption.PlanetMap);

        Menu = new MenuWidget<InGameMenuOption>(menuOptions.ToArray())
        {
            CenterAlign = true,
            ItemHeight = 45f,
            SelectedScale = 2.5f,
            NormalScale = 2f,
            SelectedColor = new Color3(220, 240, 255),
            NormalColor = new Color3(140, 140, 160),
            HighlightBg = new Color3(50, 70, 140),
            HighlightAlpha = 180,
        };

        RegisterSubOverlay(_missionsOverlay);
        RegisterSubOverlay(_cargoOverlay);
        RegisterSubOverlay(_statsOverlay);
        RegisterSubOverlay(_controlsOverlay);
    }

    // ── Open / Toggle ──

    public void Open(Game game)
    {
        Menu.SelectedIndex = 0;
        UpdateMissionsOption(game);
        UpdateCargoOption(game);
        base.Open();
    }

    public void Toggle(Game game)
    {
        if (IsOpen) Close();
        else Open(game);
    }

    // ── Menu actions ──

    protected override void OnOptionSelected(Game game, InGameMenuOption option)
    {
        switch (option)
        {
            case InGameMenuOption.Resume:
                Close();
                break;
            case InGameMenuOption.SolarSystemMap:
                if (OnMapRequested != null)
                {
                    Close();
                    OnMapRequested(MapType.SolarSystem);
                }
                break;
            case InGameMenuOption.GalaxyMap:
                if (OnMapRequested != null)
                {
                    Close();
                    OnMapRequested(MapType.Galaxy);
                }
                break;
            case InGameMenuOption.PlanetMap:
                if (OnMapRequested != null)
                {
                    Close();
                    OnMapRequested(MapType.Planet);
                }
                break;
            case InGameMenuOption.Missions:
                _missionsOverlay.Open(game);
                break;
            case InGameMenuOption.Cargo:
                _cargoOverlay.Open();
                break;
            case InGameMenuOption.Stats:
                _statsOverlay.Open();
                break;
            case InGameMenuOption.Controls:
                _controlsOverlay.Open(game);
                break;
            case InGameMenuOption.SaveGame:
                game.SaveCurrentGame();
                SetStatus("GAME SAVED");
                break;
            case InGameMenuOption.MainMenu:
                game.ChangeState(new MainMenuState());
                break;
        }
    }

    // ── Update ──

    protected override void OnUpdate(Game game)
    {
        UpdateMissionsOption(game);
        UpdateCargoOption(game);
    }

    // ── Helpers ──

    private void UpdateMissionsOption(Game game)
    {
        string label = $"MISSIONS ({game.Player.Missions.Active.Count}/{MissionTracker.MaxActive})";
        Menu.ReplaceOption(new(InGameMenuOption.Missions, label));
    }

    private void UpdateCargoOption(Game game)
    {
        string label = $"CARGO ({game.Player.CargoUsed}/{game.Player.MaxCargo})";
        Menu.ReplaceOption(new(InGameMenuOption.Cargo, label));
    }
}
