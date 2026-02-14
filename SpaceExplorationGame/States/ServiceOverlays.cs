using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI;

namespace SpaceExplorationGame.States;

/// <summary>
/// Shared overlay UI for Repair and Mission interactions.
/// Used by both SpaceStationState (menu) and InteriorState (walkable).
/// </summary>
public class ServiceOverlays
{
    public enum OverlayType { None, Repair, Mission }

    public OverlayType Active { get; private set; } = OverlayType.None;

    // Repair state
    private const int RepairCostPerPoint = 2;

    // Mission state
    private static readonly string[] MissionNames =
    [
        "CARGO DELIVERY - 200 CR",
        "SURVEY MISSION - 350 CR",
        "ESCORT DUTY - 500 CR"
    ];

    private readonly MenuWidget _missionMenu = new(MissionNames) { ItemHeight = 70f, NormalScale = 2f, SelectedScale = 2f };

    private static readonly string[] MissionDescriptions =
    [
        "Transport supplies to a nearby settlement.",
        "Map an uncharted planetary surface.",
        "Protect a freighter convoy through the sector."
    ];

    /// <summary>Open an overlay.</summary>
    public void Open(OverlayType type)
    {
        Active = type;
        _missionMenu.SelectedIndex = 0;
    }

    /// <summary>Close the current overlay.</summary>
    public void Close() => Active = OverlayType.None;

    /// <summary>Process input for the active overlay. Returns true if an overlay is active.</summary>
    public bool Update(Game game, InputManager input)
    {
        if (Active == OverlayType.None) return false;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        switch (Active)
        {
            case OverlayType.Repair:
                UpdateRepair(game, input);
                break;
            case OverlayType.Mission:
                UpdateMission(input);
                break;
        }

        return true;
    }

    /// <summary>Render the active overlay. Call only when Active != None.</summary>
    public void Render(Game game, SpriteRenderer renderer)
    {
        if (Active == OverlayType.None) return;

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

        switch (Active)
        {
            case OverlayType.Repair:
                RenderRepair(game, renderer, panelX, panelY, panelW, panelH);
                break;
            case OverlayType.Mission:
                RenderMission(renderer, panelX, panelY, panelW, panelH);
                break;
        }

        // Close hint
        renderer.DrawTextScreen(panelX + 10, panelY + panelH - 25, "ESC: CLOSE", 100, 100, 130, 1.5f);
    }

    #region Repair

    private void UpdateRepair(Game game, InputManager input)
    {
        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
            int cost = (int)(damage * RepairCostPerPoint);
            if (cost > 0 && game.Player.Credits >= cost)
            {
                game.Player.Credits -= cost;
                game.Player.ShipHealth = game.Player.ShipMaxHealth;
            }
        }
    }

    private void RenderRepair(Game game, SpriteRenderer renderer, float px, float py, float pw, float ph)
    {
        renderer.DrawTextScreen(px + 15, py + 10, "REPAIR STATION", 100, 255, 100, 2.5f);

        renderer.DrawLineScreen(px + 15, py + 45, px + pw - 15, py + 45, 60, 60, 100);

        float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
        int cost = (int)(damage * RepairCostPerPoint);

        renderer.DrawTextScreen(px + 20, py + 60, $"SHIP HULL: {game.Player.ShipHealth:F0} / {game.Player.ShipMaxHealth:F0}", 200, 200, 200, 2f);

        // Health bar
        float barX = px + 20;
        float barY = py + 90;
        float barW = pw - 40;
        renderer.DrawRectScreen(barX, barY, barW, 20, 40, 40, 40);
        renderer.DrawRectScreen(barX, barY, barW * (game.Player.ShipHealth / game.Player.ShipMaxHealth), 20, 100, 255, 100);

        if (damage > 0)
        {
            renderer.DrawTextScreen(px + 20, py + 130, $"DAMAGE: {damage:F0} POINTS", 255, 150, 100, 2f);
            renderer.DrawTextScreen(px + 20, py + 160, $"REPAIR COST: {cost} CREDITS", 255, 220, 80, 2f);

            bool canAfford = game.Player.Credits >= cost;
            if (canAfford)
                renderer.DrawTextScreen(px + 20, py + 200, "[ENTER] REPAIR ALL", 100, 255, 100, 2f);
            else
                renderer.DrawTextScreen(px + 20, py + 200, "INSUFFICIENT CREDITS", 255, 80, 80, 2f);
        }
        else
        {
            renderer.DrawTextScreen(px + 20, py + 130, "HULL INTEGRITY: 100%", 100, 255, 100, 2.5f);
            renderer.DrawTextScreen(px + 20, py + 165, "NO REPAIRS NEEDED", 150, 200, 150, 2f);
        }

        renderer.DrawTextScreen(px + 20, py + 250, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);
    }

    #endregion

    #region Mission

    private void UpdateMission(InputManager input)
    {
        _missionMenu.Update(input);
        // Missions are placeholders - just show them, no acceptance yet
    }

    private void RenderMission(SpriteRenderer renderer, float px, float py, float pw, float ph)
    {
        renderer.DrawTextScreen(px + 15, py + 10, "MISSION BOARD", 100, 180, 255, 2.5f);

        renderer.DrawLineScreen(px + 15, py + 45, px + pw - 15, py + 45, 60, 60, 100);

        for (int i = 0; i < MissionNames.Length; i++)
        {
            float optY = py + 60 + i * 70;
            bool selected = _missionMenu.IsSelected(i);

            if (selected)
                renderer.DrawRectScreen(px + 5, optY - 5, pw - 10, 60, 40, 40, 70);

            renderer.DrawTextScreen(px + 20, optY,
                selected ? $"> {MissionNames[i]}" : $"  {MissionNames[i]}",
                selected ? (byte)255 : (byte)180,
                selected ? (byte)255 : (byte)180,
                selected ? (byte)200 : (byte)200, 2f);

            renderer.DrawTextScreen(px + 30, optY + 25, MissionDescriptions[i], 130, 130, 150, 1.5f);
            renderer.DrawTextScreen(px + 30, optY + 43, "[COMING SOON]", 100, 100, 120, 1.2f);
        }
    }

    #endregion
}
