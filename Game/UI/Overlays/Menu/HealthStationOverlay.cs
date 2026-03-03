using SpaceExplorationGame.Core;
using Engine.Platform;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for the Health Station interaction – restores avatar health for credits.
/// </summary>
public class HealthStationOverlay : PanelOverlayBase
{
    private const int HealCostPerPoint = 1;

    protected override string Title => "HEALTH STATION";
    protected override Color3 TitleColor => new(100, 200, 255);
    protected override float PanelWidth => 500;
    protected override float PanelHeight => 400;
    protected override bool ShowCredits => true;
    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null) return "";

            return $"[{input.GetActionHelpText(InputAction.MenuConfirm)}] HEAL  {input.GetActionHelpText(InputAction.MenuBack)}: CLOSE";
        }
    }

    protected override void OnConfirmAction(Game game)
    {
        float damage = game.Player.AvatarMaxHealth - game.Player.AvatarHealth;
        int cost = (int)(damage * HealCostPerPoint);
        if (cost > 0 && game.Player.Credits >= cost)
        {
            game.Player.Credits -= cost;
            game.Player.AvatarHealth = game.Player.AvatarMaxHealth;
        }
    }

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        float damage = game.Player.AvatarMaxHealth - game.Player.AvatarHealth;
        int cost = (int)(damage * HealCostPerPoint);

        renderer.DrawTextScreen(panelX + 20, contentY + 5,
            $"HEALTH: {game.Player.AvatarHealth:F0} / {game.Player.AvatarMaxHealth:F0}",
            new Color3(200, 200, 200), 2f, panelW - 40f);

        // Health bar
        float barX = panelX + 20;
        float barY = contentY + 35;
        float barW = panelW - 40;
        renderer.DrawRectScreen(barX, barY, barW, 20, new Color3(40, 40, 40));
        renderer.DrawRectScreen(barX, barY, barW * (game.Player.AvatarHealth / game.Player.AvatarMaxHealth), 20,
            new Color3(100, 200, 255));

        if (damage > 0)
        {
            renderer.DrawTextScreen(panelX + 20, contentY + 75, $"INJURIES: {damage:F0} POINTS",
                new Color3(255, 150, 100), 2f, panelW - 40f);
            renderer.DrawTextScreen(panelX + 20, contentY + 105, $"TREATMENT COST: {cost} CREDITS",
                new Color3(255, 220, 80), 2f, panelW - 40f);

            bool canAfford = game.Player.Credits >= cost;
            if (canAfford)
                renderer.DrawTextScreen(panelX + 20, contentY + 145,
                    $"[{game.Input.GetActionHelpText(InputAction.MenuConfirm)}] HEAL ALL",
                    new Color3(100, 200, 255), 2f, panelW - 40f);
            else
                renderer.DrawTextScreen(panelX + 20, contentY + 145, "INSUFFICIENT CREDITS",
                    new Color3(255, 80, 80), 2f, panelW - 40f);
        }
        else
        {
            renderer.DrawTextScreen(panelX + 20, contentY + 75, "HEALTH STATUS: 100%",
                new Color3(100, 200, 255), 2.5f, panelW - 40f);
            renderer.DrawTextScreen(panelX + 20, contentY + 110, "NO TREATMENT NEEDED",
                new Color3(150, 200, 200), 2f, panelW - 40f);
        }
    }
}
