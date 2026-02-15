using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for the Repair Station interaction.
/// Used by both SpaceStationOverlay (docked menu) and InteriorState (walkable).
/// </summary>
public class RepairOverlay
{
    public bool IsOpen { get; private set; }

    private const int RepairCostPerPoint = 2;

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    /// <summary>Process input for the repair overlay. Returns true if the overlay is active.</summary>
    public bool Update(Game game, InputManager input)
    {
        if (!IsOpen) return false;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

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

        return true;
    }

    /// <summary>Render the repair overlay.</summary>
    public void Render(Game game, SpriteRenderer renderer)
    {
        if (!IsOpen) return;

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
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "REPAIR STATION", 100, 255, 100, 2.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, 60, 60, 100);

        float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
        int cost = (int)(damage * RepairCostPerPoint);

        renderer.DrawTextScreen(panelX + 20, panelY + 60, $"SHIP HULL: {game.Player.ShipHealth:F0} / {game.Player.ShipMaxHealth:F0}", 200, 200, 200, 2f);

        // Health bar
        float barX = panelX + 20;
        float barY = panelY + 90;
        float barW = panelW - 40;
        renderer.DrawRectScreen(barX, barY, barW, 20, 40, 40, 40);
        renderer.DrawRectScreen(barX, barY, barW * (game.Player.ShipHealth / game.Player.ShipMaxHealth), 20, 100, 255, 100);

        if (damage > 0)
        {
            renderer.DrawTextScreen(panelX + 20, panelY + 130, $"DAMAGE: {damage:F0} POINTS", 255, 150, 100, 2f);
            renderer.DrawTextScreen(panelX + 20, panelY + 160, $"REPAIR COST: {cost} CREDITS", 255, 220, 80, 2f);

            bool canAfford = game.Player.Credits >= cost;
            if (canAfford)
                renderer.DrawTextScreen(panelX + 20, panelY + 200, "[ENTER] REPAIR ALL", 100, 255, 100, 2f);
            else
                renderer.DrawTextScreen(panelX + 20, panelY + 200, "INSUFFICIENT CREDITS", 255, 80, 80, 2f);
        }
        else
        {
            renderer.DrawTextScreen(panelX + 20, panelY + 130, "HULL INTEGRITY: 100%", 100, 255, 100, 2.5f);
            renderer.DrawTextScreen(panelX + 20, panelY + 165, "NO REPAIRS NEEDED", 150, 200, 150, 2f);
        }

        renderer.DrawTextScreen(panelX + 20, panelY + 250, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);

        // Close hint
        renderer.DrawTextScreen(panelX + 10, panelY + panelH - 25, "ESC: CLOSE", 100, 100, 130, 1.5f);
    }
}
