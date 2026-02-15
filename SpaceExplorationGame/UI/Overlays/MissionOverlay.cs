using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for the Mission Board interaction.
/// Used by both SpaceStationOverlay (docked menu) and InteriorState (walkable).
/// </summary>
public class MissionOverlay : OverlayBase
{
    public enum MissionType
    {
        CargoDelivery,
        SurveyMission,
        EscortDuty
    }

    private static readonly MenuOption<MissionType>[] MissionOptions =
    [
        new(MissionType.CargoDelivery, "CARGO DELIVERY - 200 CR", "Transport supplies to a nearby settlement."),
        new(MissionType.SurveyMission, "SURVEY MISSION - 350 CR", "Map an uncharted planetary surface."),
        new(MissionType.EscortDuty, "ESCORT DUTY - 500 CR", "Protect a freighter convoy through the sector.")
    ];

    private readonly MenuWidget<MissionType> _missionMenu = new(MissionOptions) { ItemHeight = 70f, NormalScale = 2f, SelectedScale = 2f };

    public void Open()
    {
        IsOpen = true;
        _missionMenu.SelectedIndex = 0;
    }

    /// <summary>Process input for the mission overlay. Returns true if the overlay is active.</summary>
    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        _missionMenu.Update(input);
        // Missions are placeholders - just show them, no acceptance yet

        return true;
    }

    /// <summary>Render the mission overlay.</summary>
    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent background
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 150);

        float panelW = 500;
        float panelH = 400;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        // Panel border
        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        // Title
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "MISSION BOARD", 100, 180, 255, 2.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, 60, 60, 100);

        var options = _missionMenu.Options;
        for (int i = 0; i < options.Count; i++)
        {
            float optY = panelY + 60 + i * 70;
            bool selected = _missionMenu.IsSelected(options[i].Value);

            if (selected)
                renderer.DrawRectScreen(panelX + 5, optY - 5, panelW - 10, 60, 40, 40, 70);

            renderer.DrawTextScreen(panelX + 20, optY,
                selected ? $"> {options[i].Label}" : $"  {options[i].Label}",
                selected ? (byte)255 : (byte)180,
                selected ? (byte)255 : (byte)180,
                selected ? (byte)200 : (byte)200, 2f);

            renderer.DrawTextScreen(panelX + 30, optY + 25, options[i].Description ?? "", 130, 130, 150, 1.5f);
            renderer.DrawTextScreen(panelX + 30, optY + 43, "[COMING SOON]", 100, 100, 120, 1.2f);
        }

        // Close hint
        renderer.DrawTextScreen(panelX + 10, panelY + panelH - 25, "ESC: CLOSE", 100, 100, 130, 1.5f);
    }
}
