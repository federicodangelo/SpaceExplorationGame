using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for the in-game pause menu. Provides Resume and Main Menu options.
/// Can be reused by any game state that needs a pause/escape menu.
/// </summary>
public class PauseMenuOverlay
{
    public bool IsOpen { get; private set; }

    private enum PauseMenuOption
    {
        Resume,
        MainMenu
    }

    private static readonly MenuOption<PauseMenuOption>[] PauseMenuOptions =
    [
        new(PauseMenuOption.Resume, "RESUME"),
        new(PauseMenuOption.MainMenu, "MAIN MENU")
    ];

    private readonly MenuWidget<PauseMenuOption> _pauseMenu = new(PauseMenuOptions)
    {
        CenterAlign = true,
        ItemHeight = 45f,
        SelectedScale = 2.5f,
        NormalScale = 2f,
        SelectedColor = (220, 240, 255),
        NormalColor = (140, 140, 160),
        HighlightBg = (50, 70, 140),
        HighlightAlpha = 180,
    };

    public void Open()
    {
        _pauseMenu.SelectedIndex = 0;
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>
    /// Process input for the pause menu overlay. Returns true if the overlay consumed input
    /// (blocks underlying state controls). Handles state transitions internally.
    /// </summary>
    public bool Update(Game game, InputManager input)
    {
        // Toggle on Escape
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Toggle();
            return true;
        }

        if (!IsOpen) return false;

        float menuCenterX = GameConfig.WindowWidth / 2f;
        float menuStartY = GameConfig.WindowHeight / 2f - 30;
        float menuW = 300f;

        var confirmed = _pauseMenu.Update(input, menuCenterX - menuW / 2f, menuStartY, menuW);
        if (confirmed == PauseMenuOption.Resume)
            Close();
        else if (confirmed == PauseMenuOption.MainMenu)
            game.ChangeState(new MainMenuState());

        return true;
    }

    /// <summary>Render the pause menu overlay.</summary>
    public void Render(SpriteRenderer renderer)
    {
        if (!IsOpen) return;

        // Dim background
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, 0, 0, 0, 160);

        // Panel
        float panelW = 360;
        float panelH = 160;
        float panelX = GameConfig.WindowWidth / 2f - panelW / 2f;
        float panelY = GameConfig.WindowHeight / 2f - panelH / 2f - 20;
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 10, 12, 30, 220);
        renderer.DrawRectScreen(panelX + 2, panelY + 2, panelW - 4, panelH - 4, 20, 24, 50, 200);

        // Title
        string pauseTitle = "PAUSED";
        float ptScale = 3f;
        float ptW = renderer.MeasureText(pauseTitle, ptScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - ptW / 2f, panelY + 14, pauseTitle, 200, 210, 255, ptScale);

        // Options
        float menuStartY = GameConfig.WindowHeight / 2f - 30;
        float menuW = 300f;
        _pauseMenu.Render(renderer, GameConfig.WindowWidth / 2f - menuW / 2f, menuStartY, menuW);
    }
}
