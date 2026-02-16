using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for the Health Station interaction – restores avatar health for credits.
/// </summary>
public class HealthStationOverlay : OverlayBase
{
    private const int HealCostPerPoint = 1;

    public void Open() => IsOpen = true;

    /// <summary>Process input for the health station overlay. Returns true if the overlay is active.</summary>
    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            float damage = game.Player.AvatarMaxHealth - game.Player.AvatarHealth;
            int cost = (int)(damage * HealCostPerPoint);
            if (cost > 0 && game.Player.Credits >= cost)
            {
                game.Player.Credits -= cost;
                game.Player.AvatarHealth = game.Player.AvatarMaxHealth;
            }
        }

        return true;
    }

    /// <summary>Render the health station overlay.</summary>
    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent background
        renderer.DrawRectScreen(0, 0, w, h, new Color4(0, 0, 0, 150));

        float panelW = 500;
        float panelH = 400;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        // Panel border
        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, new Color4(60, 60, 100, 200));
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, new Color4(15, 15, 35, 245));

        // Title
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "HEALTH STATION", new Color3(100, 200, 255), 2.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, new Color3(60, 60, 100));

        float damage = game.Player.AvatarMaxHealth - game.Player.AvatarHealth;
        int cost = (int)(damage * HealCostPerPoint);

        renderer.DrawTextScreen(panelX + 20, panelY + 60, $"HEALTH: {game.Player.AvatarHealth:F0} / {game.Player.AvatarMaxHealth:F0}", new Color3(200, 200, 200), 2f);

        // Health bar
        float barX = panelX + 20;
        float barY = panelY + 90;
        float barW = panelW - 40;
        renderer.DrawRectScreen(barX, barY, barW, 20, new Color3(40, 40, 40));
        renderer.DrawRectScreen(barX, barY, barW * (game.Player.AvatarHealth / game.Player.AvatarMaxHealth), 20, new Color3(100, 200, 255));

        if (damage > 0)
        {
            renderer.DrawTextScreen(panelX + 20, panelY + 130, $"INJURIES: {damage:F0} POINTS", new Color3(255, 150, 100), 2f);
            renderer.DrawTextScreen(panelX + 20, panelY + 160, $"TREATMENT COST: {cost} CREDITS", new Color3(255, 220, 80), 2f);

            bool canAfford = game.Player.Credits >= cost;
            if (canAfford)
                renderer.DrawTextScreen(panelX + 20, panelY + 200, "[ENTER] HEAL ALL", new Color3(100, 200, 255), 2f);
            else
                renderer.DrawTextScreen(panelX + 20, panelY + 200, "INSUFFICIENT CREDITS", new Color3(255, 80, 80), 2f);
        }
        else
        {
            renderer.DrawTextScreen(panelX + 20, panelY + 130, "HEALTH STATUS: 100%", new Color3(100, 200, 255), 2.5f);
            renderer.DrawTextScreen(panelX + 20, panelY + 165, "NO TREATMENT NEEDED", new Color3(150, 200, 200), 2f);
        }

        renderer.DrawTextScreen(panelX + 20, panelY + 250, $"CREDITS: {game.Player.Credits}", new Color3(255, 220, 80), 2f);

        // Close hint
        renderer.DrawTextScreen(panelX + 10, panelY + panelH - 25, "ESC: CLOSE", new Color3(100, 100, 130), 1.5f);
    }
}
