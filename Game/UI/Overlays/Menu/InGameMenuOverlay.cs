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
    public enum InGameMenuOption
    {
        Resume,
        Map,
        Missions,
        Cargo,
        Controls,
        SaveGame,
        MainMenu
    }

    private static readonly MenuOption<InGameMenuOption>[] DefaultMenuOptions =
    [
        new(InGameMenuOption.Resume, "RESUME"),
        new(InGameMenuOption.Map, "MAP"),
        new(InGameMenuOption.Missions, "MISSIONS"),
        new(InGameMenuOption.Cargo, "CARGO"),
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
    public Action<Game>? OnMapRequested { get; set; }

    private readonly MissionsListOverlay _missionsOverlay = new();
    private readonly CargoListOverlay _cargoOverlay = new();
    private readonly ControlsOverlay _controlsOverlay = new();

    // ── Panel configuration ──

    protected override string Title => "MENU";
    protected override Color3 TitleColor => new(200, 210, 255);
    protected override float PanelWidth => 360;
    protected override bool CloseOnClickOutside => true;

    // ── Constructor ──

    public InGameMenuOverlay()
    {
        Menu = new MenuWidget<InGameMenuOption>(DefaultMenuOptions)
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
        RegisterSubOverlay(_controlsOverlay);
    }

    // ── Open / Toggle ──

    public void Open(Game game)
    {
        Menu.SelectedIndex = 0;
        UpdateMapOption();
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
            case InGameMenuOption.Map:
                if (OnMapRequested != null)
                {
                    Close();
                    OnMapRequested(game);
                }
                break;
            case InGameMenuOption.Missions:
                _missionsOverlay.Open(game);
                break;
            case InGameMenuOption.Cargo:
                _cargoOverlay.Open();
                break;
            case InGameMenuOption.Controls:
                _controlsOverlay.Open();
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
        UpdateMapOption();
        UpdateMissionsOption(game);
        UpdateCargoOption(game);
    }

    // ── Helpers ──

    private void UpdateMapOption()
    {
        bool hasMap = OnMapRequested != null;
        Menu.SetOption(1, new(InGameMenuOption.Map, "MAP", Enabled: hasMap, DisabledHint: hasMap ? null : "Not available"));
    }

    private void UpdateMissionsOption(Game game)
    {
        int missionCount = game.Player.Missions.Active.Count;
        string label = missionCount > 0 ? $"MISSIONS ({missionCount})" : "MISSIONS";
        Menu.SetOption(2, new(InGameMenuOption.Missions, label));
    }

    private void UpdateCargoOption(Game game)
    {
        int cargoUsed = game.Player.CargoUsed;
        string label = cargoUsed > 0
            ? $"CARGO ({cargoUsed}/{game.Player.MaxCargo})"
            : "CARGO";
        Menu.SetOption(3, new(InGameMenuOption.Cargo, label));
    }
}
