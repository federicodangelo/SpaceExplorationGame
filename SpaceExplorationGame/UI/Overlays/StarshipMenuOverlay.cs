using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

public enum StarshipMenuOption
{
    FlyToSpace,
    DisembarkOnFoot,
    DisembarkOnVehicle
}

/// <summary>
/// Overlay displayed when the player is inside the starship on a planet surface.
/// Provides options to fly to space, or disembark on foot or in a vehicle.
/// </summary>
public class StarshipMenuOverlay : OverlayBase
{
    private MenuWidget<StarshipMenuOption> _menu = null!;

    /// <summary>The last confirmed menu choice, or null if none.</summary>
    public StarshipMenuOption? LastChoice { get; private set; }

    /// <summary>Whether the player has a vehicle available for the vehicle option.</summary>
    public bool HasVehicle { get; set; } = true;

    /// <summary>Whether the vehicle is already deployed on the planet surface.</summary>
    public bool VehicleDeployed { get; set; }

    public void Open()
    {
        // Build menu options with correct enabled state for the vehicle option
        bool vehicleEnabled = HasVehicle && !VehicleDeployed;
        string? vehicleHint = !HasVehicle ? "(NO VEHICLE)" : VehicleDeployed ? "(ALREADY DEPLOYED)" : null;

        MenuOption<StarshipMenuOption>[] options =
        [
            new(StarshipMenuOption.FlyToSpace, "FLY TO SPACE", "Leave the planet and return to orbit"),
            new(StarshipMenuOption.DisembarkOnFoot, "DISEMBARK (ON FOOT)", "Exit the starship on foot"),
            new(StarshipMenuOption.DisembarkOnVehicle, "DISEMBARK (ON VEHICLE)", "Exit the starship in your vehicle",
                Enabled: vehicleEnabled, DisabledHint: vehicleHint)
        ];

        _menu = new MenuWidget<StarshipMenuOption>(options)
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.5f,
            NormalScale = 2f,
            SelectedColor = (100, 255, 200),
            NormalColor = (160, 160, 180),
            HighlightBg = (40, 60, 120),
            HighlightAlpha = 200,
        };

        LastChoice = null;
        IsOpen = true;
    }

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        // Escape closes the overlay (go back in case we re-boarded)
        // But since this is the initial state on landing, Escape does nothing when first opened
        // Actually, Escape should do nothing here — the player must pick an option
        // Let's allow Escape to toggle in-game menu instead
        // No — let the parent state handle Escape for in-game menu
        // We just consume all input while open

        float menuCenterX = GameConfig.WindowWidth / 2f;
        float menuW = 400f;
        float panelH = 320;
        float panelY = GameConfig.WindowHeight / 2f - panelH / 2f - 20;
        float menuStartY = panelY + 60;

        var confirmed = _menu.Update(input, menuCenterX - menuW / 2f, menuStartY, menuW);
        if (confirmed.HasValue)
        {
            LastChoice = confirmed.Value;
            Close();
        }

        return true; // always consume input when open
    }

    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;

        // Dim background
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, 0, 0, 0, 160);

        // Panel
        float panelW = 500;
        float panelH = 320;
        float panelX = GameConfig.WindowWidth / 2f - panelW / 2f;
        float panelY = GameConfig.WindowHeight / 2f - panelH / 2f - 20;
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 10, 12, 30, 220);
        renderer.DrawRectScreen(panelX + 2, panelY + 2, panelW - 4, panelH - 4, 20, 24, 50, 200);

        // Title
        string title = "STARSHIP";
        float titleScale = 3f;
        float titleW = renderer.MeasureText(title, titleScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - titleW / 2f, panelY + 15, title, 100, 200, 255, titleScale);

        // Menu
        float menuCenterX = GameConfig.WindowWidth / 2f;
        float menuW = 400f;
        float menuStartY = panelY + 60;
        _menu.Render(renderer, menuCenterX - menuW / 2f, menuStartY, menuW);
    }
}
