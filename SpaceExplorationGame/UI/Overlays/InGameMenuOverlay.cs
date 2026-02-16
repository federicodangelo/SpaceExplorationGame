using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for the in-game menu. Provides Resume, Controls, and Main Menu options.
/// Can be reused by any game state that needs an escape menu.
/// Shows context-appropriate controls based on the current game state.
/// </summary>
public class InGameMenuOverlay : OverlayBase
{
    private enum InGameMenuOption
    {
        Resume,
        TrackMission,
        Controls,
        MainMenu
    }

    private static readonly MenuOption<InGameMenuOption>[] MenuOptions =
    [
        new(InGameMenuOption.Resume, "RESUME"),
        new(InGameMenuOption.TrackMission, "TRACK MISSION >>", Enabled: false, DisabledHint: "NO MISSIONS"),
        new(InGameMenuOption.Controls, "CONTROLS"),
        new(InGameMenuOption.MainMenu, "MAIN MENU")
    ];

    /// <summary>The current game state type, used to show context-appropriate controls.</summary>
    public GameStateType StateType { get; set; }

    private bool _showingControls;

    private readonly MenuWidget<InGameMenuOption> _menu = new(MenuOptions)
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

    public void Open(Game game)
    {
        _menu.SelectedIndex = 0;
        _showingControls = false;
        UpdateTrackMissionOption(game);
        IsOpen = true;
    }

    public void Toggle(Game game)
    {
        if (IsOpen) Close();
        else Open(game);
    }

    /// <summary>
    /// Process input for the in-game menu overlay. Returns true if the overlay consumed input
    /// (blocks underlying state controls). Handles state transitions internally.
    /// </summary>
    public override bool UpdateInput(Game game)
    {
        var input = game.Input;

        // Toggle on Escape
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Toggle(game);
            return true;
        }

        if (!IsOpen) return false;

        float menuCenterX = GameConfig.WindowWidth / 2f;
        float menuStartY = GameConfig.WindowHeight / 2f - 30;
        float menuW = 300f;

        if (_showingControls)
        {
            // Any key/click dismisses the controls view
            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E)
                || input.IsKeyPressed(SDL.Scancode.Space) || input.IsKeyPressed(SDL.Scancode.Escape))
            {
                _showingControls = false;
            }
            return true;
        }

        var confirmed = _menu.Update(input, menuCenterX - menuW / 2f, menuStartY, menuW);
        if (confirmed == InGameMenuOption.Resume)
            Close();
        else if (confirmed == InGameMenuOption.TrackMission)
        {
            game.Player.CycleTrackedMission();
            UpdateTrackMissionOption(game);
        }
        else if (confirmed == InGameMenuOption.Controls)
            _showingControls = true;
        else if (confirmed == InGameMenuOption.MainMenu)
            game.ChangeState(new MainMenuState());

        return true;
    }

    /// <summary>Render the in-game menu overlay.</summary>
    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;

        // Dim background
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, new Color4(0, 0, 0, 160));

        if (_showingControls)
        {
            RenderControlsPanel(renderer);
            return;
        }

        // Panel
        float panelW = 360;
        float panelH = 290;
        float panelX = GameConfig.WindowWidth / 2f - panelW / 2f;
        float panelY = GameConfig.WindowHeight / 2f - panelH / 2f - 20;
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, new Color4(10, 12, 30, 220));
        renderer.DrawRectScreen(panelX + 2, panelY + 2, panelW - 4, panelH - 4, new Color4(20, 24, 50, 200));

        // Title
        string title = "MENU";
        float ptScale = 3f;
        float ptW = renderer.MeasureText(title, ptScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - ptW / 2f, panelY + 14, title, new Color3(200, 210, 255), ptScale);

        // Options
        float menuStartY = GameConfig.WindowHeight / 2f - 50;
        float menuW = 300f;
        _menu.Render(renderer, GameConfig.WindowWidth / 2f - menuW / 2f, menuStartY, menuW);

        // Show currently tracked mission below menu when hovering Track Mission option
        if (_menu.IsSelected(InGameMenuOption.TrackMission))
        {
            var tracked = game.Player.GetTrackedMission();
            if (tracked != null)
            {
                string label = $"TRACKING: [{tracked.TypeLabel}] {tracked.Title}";
                float lw = renderer.MeasureText(label, 1.5f);
                renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - lw / 2f,
                    panelY + panelH - 30, label, tracked.TypeColor, 1.5f);
            }
        }
    }

    /// <summary>Renders the controls help panel with context-appropriate key bindings.</summary>
    private void RenderControlsPanel(SpriteRenderer renderer)
    {
        // Build controls list based on state
        string[] controls = StateType switch
        {
            GameStateType.SolarSystem =>
            [
                "W / UP ............. THRUST",
                "A / D .............. ROTATE",
                "S / DOWN ........... BRAKE",
                "SCROLL ............. ZOOM",
                "E .................. INTERACT",
                "M .................. GALAXY MAP",
                "SPACE .............. SHOOT",
                "ESC ................ MENU"
            ],
            GameStateType.PlanetSurface =>
            [
                "WASD / ARROWS ...... MOVE",
                "SCROLL ............. ZOOM",
                "E .................. INTERACT",
                "SPACE / LMB ........ SHOOT",
                "ESC ................ MENU"
            ],
            GameStateType.Interior =>
            [
                "WASD / ARROWS ...... MOVE",
                "SCROLL ............. ZOOM",
                "E .................. INTERACT",
                "ESC ................ MENU"
            ],
            _ => ["ESC ................ MENU"]
        };

        float lineH = 22f;
        float panelW = 380;
        float panelH = 60 + controls.Length * lineH + 30;
        float panelX = GameConfig.WindowWidth / 2f - panelW / 2f;
        float panelY = GameConfig.WindowHeight / 2f - panelH / 2f;

        // Background
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, new Color4(10, 12, 30, 220));
        renderer.DrawRectScreen(panelX + 2, panelY + 2, panelW - 4, panelH - 4, new Color4(20, 24, 50, 200));

        // Title
        string title = "CONTROLS";
        float ptScale = 3f;
        float ptW = renderer.MeasureText(title, ptScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - ptW / 2f, panelY + 14, title, new Color3(200, 210, 255), ptScale);

        // Control lines
        float cy = panelY + 50;
        foreach (var line in controls)
        {
            float lw = renderer.MeasureText(line, 1.5f);
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - lw / 2f, cy, line, new Color3(180, 180, 200), 1.5f);
            cy += lineH;
        }

        // Dismiss hint
        string hint = "PRESS ANY KEY TO CLOSE";
        float hw = renderer.MeasureText(hint, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - hw / 2f, cy + 8, hint, new Color3(120, 120, 140), 1.5f);
    }

    /// <summary>Update the Track Mission menu option based on the player's active missions.</summary>
    private void UpdateTrackMissionOption(Game game)
    {
        int missionCount = game.Player.ActiveMissions.Count;
        if (missionCount >= 2)
        {
            var tracked = game.Player.GetTrackedMission();
            string label = tracked != null
                ? $"TRACK: {tracked.TypeLabel} >>"
                : "TRACK MISSION >>";
            _menu.SetOption(1, new(InGameMenuOption.TrackMission, label));
        }
        else if (missionCount == 1)
        {
            _menu.SetOption(1, new(InGameMenuOption.TrackMission, "TRACK MISSION",
                Enabled: false, DisabledHint: "ONLY 1 MISSION"));
        }
        else
        {
            _menu.SetOption(1, new(InGameMenuOption.TrackMission, "TRACK MISSION",
                Enabled: false, DisabledHint: "NO MISSIONS"));
        }
    }
}
