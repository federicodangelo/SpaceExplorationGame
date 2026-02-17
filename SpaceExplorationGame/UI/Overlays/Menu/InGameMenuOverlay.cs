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
        Missions,
        Controls,
        MainMenu
    }

    private static readonly MenuOption<InGameMenuOption>[] DefaultMenuOptions =
    [
        new(InGameMenuOption.Resume, "RESUME"),
        new(InGameMenuOption.Missions, "MISSIONS"),
        new(InGameMenuOption.Controls, "CONTROLS"),
        new(InGameMenuOption.MainMenu, "MAIN MENU")
    ];

    /// <summary>The current game state type, forwarded to the controls overlay.</summary>
    public GameStateType StateType
    {
        get => _controlsOverlay.StateType;
        set => _controlsOverlay.StateType = value;
    }

    private readonly MissionsListOverlay _missionsOverlay = new();
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
        RegisterSubOverlay(_controlsOverlay);
    }

    // ── Open / Toggle ──

    public void Open(Game game)
    {
        Menu.SelectedIndex = 0;
        UpdateMissionsOption(game);
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
            case InGameMenuOption.Missions:
                _missionsOverlay.Open(game);
                break;
            case InGameMenuOption.Controls:
                _controlsOverlay.Open();
                break;
            case InGameMenuOption.MainMenu:
                game.ChangeState(new MainMenuState());
                break;
        }
    }

    // ── Update ──

    protected override void OnUpdate(Game game, float dt)
    {
        UpdateMissionsOption(game);
    }

    // ── Helpers ──

    private void UpdateMissionsOption(Game game)
    {
        int missionCount = game.Player.ActiveMissions.Count;
        string label = missionCount > 0 ? $"MISSIONS ({missionCount})" : "MISSIONS";
        Menu.SetOption(1, new(InGameMenuOption.Missions, label));
    }
}
