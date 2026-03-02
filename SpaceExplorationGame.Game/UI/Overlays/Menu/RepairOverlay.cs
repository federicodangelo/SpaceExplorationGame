using SpaceExplorationGame.Core;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for the Repair Station interaction.
/// Used by both SpaceStationOverlay (docked menu) and InteriorState (walkable).
/// </summary>
public class RepairOverlay : PanelOverlayBase
{
    private const int RepairCostPerPoint = 2;

    protected override string Title => "REPAIR STATION";
    protected override Color3 TitleColor => new(100, 255, 100);
    protected override float PanelWidth => 500;
    protected override float PanelHeight => 400;
    protected override bool ShowCredits => true;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"[{input.GetActionHelpText(InputAction.MenuConfirm)}] REPAIR  {input.GetActionHelpText(InputAction.MenuBack)}: CLOSE";
        }
    }

    protected override void OnConfirmAction(Game game)
    {
        float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
        int cost = (int)(damage * RepairCostPerPoint);
        if (cost > 0 && game.Player.Credits >= cost)
        {
            game.Player.Credits -= cost;
            game.Player.ShipHealth = game.Player.ShipMaxHealth;
        }
    }

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        float damage = game.Player.ShipMaxHealth - game.Player.ShipHealth;
        int cost = (int)(damage * RepairCostPerPoint);

        renderer.DrawTextScreen(panelX + 20, contentY + 5,
            $"SHIP HULL: {game.Player.ShipHealth:F0} / {game.Player.ShipMaxHealth:F0}",
            new Color3(200, 200, 200), 2f);

        // Health bar
        float barX = panelX + 20;
        float barY = contentY + 35;
        float barW = panelW - 40;
        renderer.DrawRectScreen(barX, barY, barW, 20, new Color3(40, 40, 40));
        renderer.DrawRectScreen(barX, barY, barW * (game.Player.ShipHealth / game.Player.ShipMaxHealth), 20,
            new Color3(100, 255, 100));

        if (damage > 0)
        {
            renderer.DrawTextScreen(panelX + 20, contentY + 75, $"DAMAGE: {damage:F0} POINTS",
                new Color3(255, 150, 100), 2f);
            renderer.DrawTextScreen(panelX + 20, contentY + 105, $"REPAIR COST: {cost} CREDITS",
                new Color3(255, 220, 80), 2f);

            bool canAfford = game.Player.Credits >= cost;
            if (canAfford)
                renderer.DrawTextScreen(panelX + 20, contentY + 145,
                    $"[{game.Input.GetActionHelpText(InputAction.MenuConfirm)}] REPAIR ALL",
                    new Color3(100, 255, 100), 2f);
            else
                renderer.DrawTextScreen(panelX + 20, contentY + 145, "INSUFFICIENT CREDITS",
                    new Color3(255, 80, 80), 2f);
        }
        else
        {
            renderer.DrawTextScreen(panelX + 20, contentY + 75, "HULL INTEGRITY: 100%",
                new Color3(100, 255, 100), 2.5f);
            renderer.DrawTextScreen(panelX + 20, contentY + 110, "NO REPAIRS NEEDED",
                new Color3(150, 200, 150), 2f);
        }
    }
}
