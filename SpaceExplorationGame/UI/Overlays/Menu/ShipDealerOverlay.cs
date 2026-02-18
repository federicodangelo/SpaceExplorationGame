using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Overlay for buying and selling ship hulls at stations/settlements.
/// Shows all available ship types with stats, comparison to current ship, and buy/sell pricing.
/// </summary>
public class ShipDealerOverlay : ListPanelOverlay
{
    private readonly ShipType[] _ships = ShipTypeCatalog.AllTypes;

    protected override string Title => "SHIP DEALER";
    protected override Color3 TitleColor => new(255, 200, 80);
    protected override float PanelWidth => 900;
    protected override float PanelHeight => 550;
    protected override bool ShowCredits => true;
    protected override string? ControlsHint => "UP/DOWN: SELECT  ENTER: BUY SHIP  ESC: CLOSE";

    protected override int ItemCount => _ships.Length;
    protected override float ItemHeight => 70f;
    protected override float ListOffsetY => 30f; // after "AVAILABLE SHIPS" header
    protected override float ListWidth => 320f;  // left column width

    public override void Open()
    {
        base.Open();

        // Select current ship by default
        for (int i = 0; i < _ships.Length; i++)
        {
            if (_ships[i].Id == "scout")
                SelectedIndex = i;
        }
    }

    protected override void OnItemConfirmed(Game game, int index)
    {
        TryBuyShip(game);
    }

    protected override void RenderPanelContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        var player = game.Player;

        // Current ship info
        renderer.DrawTextScreen(panelX + 15, contentY - 3,
            $"CURRENT SHIP: {player.CurrentShipType.Name.ToUpper()}", new Color3(100, 220, 255), 1.8f);

        float tradeInValue = player.CurrentShipType.SellValue;
        renderer.DrawTextScreen(panelX + panelW - 250, contentY - 3,
            $"TRADE-IN VALUE: {tradeInValue} CR", new Color3(180, 220, 80), 1.5f);

        renderer.DrawLineScreen(panelX + 15, contentY + 23, panelX + panelW - 15, contentY + 23,
            new Color3(40, 40, 70));

        // Two-column layout
        float leftW = 320;
        float rightX = panelX + leftW + 10;
        float rightW = panelW - leftW - 25;
        float listStartY = contentY + ListOffsetY;

        // Column separator
        renderer.DrawLineScreen(panelX + leftW + 5, listStartY,
            panelX + leftW + 5, panelX + PanelHeight - 40, new Color3(40, 40, 70));

        // ── Left column: Ship list ──
        renderer.DrawTextScreen(panelX + 15, listStartY - 20, "AVAILABLE SHIPS",
            new Color3(180, 180, 220), 1.8f);

        float itemY = listStartY;
        for (int i = 0; i < _ships.Length; i++)
        {
            var ship = _ships[i];
            bool selected = i == SelectedIndex;
            bool isCurrent = ship.Id == player.CurrentShipType.Id;

            if (selected)
                renderer.DrawRectScreen(panelX + 5, itemY - 3, leftW - 5, ItemHeight,
                    new Color4(30, 40, 70, 180));

            byte nR = selected ? (byte)255 : (byte)160;
            byte nG = selected ? (byte)255 : (byte)160;
            byte nB = selected ? (byte)200 : (byte)180;
            string tag = isCurrent ? " [YOUR SHIP]" : "";
            string prefix = selected ? "> " : "  ";
            renderer.DrawTextScreen(panelX + 15, itemY, $"{prefix}{ship.Name.ToUpper()}{tag}",
                new Color3(nR, nG, nB), 1.8f);

            renderer.DrawTextScreen(panelX + 30, itemY + 22,
                $"SLOTS: {ship.AvailableSlots.Length}  HULL: {ship.BaseHull:F0}  FUEL: {ship.BaseFuel:F0}  CARGO: {ship.BaseCargo:F0}",
                new Color3(100, 140, 160), 1.3f);

            if (isCurrent)
            {
                renderer.DrawTextScreen(panelX + 30, itemY + 42, "CURRENT",
                    new Color3(100, 200, 255), 1.4f);
            }
            else if (ship.BuyCost <= 0)
            {
                renderer.DrawTextScreen(panelX + 30, itemY + 42, "FREE",
                    new Color3(100, 255, 100), 1.4f);
            }
            else
            {
                int netCost = ship.BuyCost - player.CurrentShipType.SellValue;
                bool canAfford = netCost <= player.Credits;
                string costText = $"COST: {ship.BuyCost} CR (NET: {netCost} CR)";
                renderer.DrawTextScreen(panelX + 30, itemY + 42, costText,
                    new Color3(
                        canAfford ? (byte)255 : (byte)255,
                        canAfford ? (byte)220 : (byte)80,
                        canAfford ? (byte)80 : (byte)80), 1.4f);
            }

            itemY += ItemHeight;
        }

        // ── Right column: Selected ship details ──
        var sel = _ships[SelectedIndex];
        bool selIsCurrent = sel.Id == player.CurrentShipType.Id;

        renderer.DrawTextScreen(rightX + 5, listStartY - 20, sel.Name.ToUpper(),
            new Color3(255, 220, 100), 2.2f);

        float detailY = listStartY + 15;

        renderer.DrawTextScreen(rightX + 5, detailY, sel.Description,
            new Color3(160, 160, 180), 1.5f);
        detailY += 25;

        renderer.DrawLineScreen(rightX + 5, detailY, rightX + rightW - 5, detailY,
            new Color3(40, 40, 70));
        detailY += 10;

        var cur = player.CurrentShipType;
        RenderStatRow(renderer, rightX + 5, ref detailY, "SLOTS", sel.AvailableSlots.Length, cur.AvailableSlots.Length, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "BASE HULL", sel.BaseHull, cur.BaseHull, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "BASE FUEL", sel.BaseFuel, cur.BaseFuel, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "BASE CARGO", sel.BaseCargo, cur.BaseCargo, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "WEIGHT", sel.Weight, cur.Weight, selIsCurrent, lowerIsBetter: true);
        RenderStatRow(renderer, rightX + 5, ref detailY, "SIZE", sel.SpriteSize, cur.SpriteSize, selIsCurrent);

        detailY += 5;
        renderer.DrawLineScreen(rightX + 5, detailY, rightX + rightW - 5, detailY,
            new Color3(40, 40, 70));
        detailY += 10;

        renderer.DrawTextScreen(rightX + 5, detailY, "AVAILABLE SLOTS:",
            new Color3(160, 180, 220), 1.5f);
        detailY += 22;
        foreach (var slot in sel.AvailableSlots)
        {
            string slotName = ShipPartCatalog.GetSlotName(slot);
            bool curHasSlot = Array.Exists(cur.AvailableSlots, s => s == slot);
            byte sR = selIsCurrent ? (byte)150 : curHasSlot ? (byte)150 : (byte)80;
            byte sG = selIsCurrent ? (byte)150 : curHasSlot ? (byte)150 : (byte)255;
            byte sB = selIsCurrent ? (byte)170 : curHasSlot ? (byte)170 : (byte)80;
            string marker = selIsCurrent ? "  " : curHasSlot ? "  " : "+ ";
            renderer.DrawTextScreen(rightX + 15, detailY, $"{marker}{slotName}",
                new Color3(sR, sG, sB), 1.4f);
            detailY += 18;
        }

        if (!selIsCurrent)
        {
            foreach (var slot in cur.AvailableSlots)
            {
                if (!Array.Exists(sel.AvailableSlots, s => s == slot))
                {
                    string slotName = ShipPartCatalog.GetSlotName(slot);
                    renderer.DrawTextScreen(rightX + 15, detailY, $"- {slotName}",
                        new Color3(255, 80, 80), 1.4f);
                    detailY += 18;
                }
            }
        }

        detailY += 10;
        if (selIsCurrent)
        {
            renderer.DrawTextScreen(rightX + 5, detailY, "THIS IS YOUR CURRENT SHIP",
                new Color3(100, 200, 255), 1.5f);
        }
        else
        {
            renderer.DrawTextScreen(rightX + 5, detailY, $"BUY PRICE: {sel.BuyCost} CR",
                new Color3(255, 220, 80), 1.5f);
            detailY += 20;
            renderer.DrawTextScreen(rightX + 5, detailY, $"TRADE-IN: -{cur.SellValue} CR",
                new Color3(80, 220, 80), 1.5f);
            detailY += 20;
            int net = sel.BuyCost - cur.SellValue;
            bool canAfford = net <= player.Credits;
            renderer.DrawTextScreen(rightX + 5, detailY, $"NET COST: {net} CR",
                new Color3(
                    canAfford ? (byte)100 : (byte)255,
                    canAfford ? (byte)255 : (byte)100,
                    canAfford ? (byte)100 : (byte)100), 1.8f);
        }
    }

    private void TryBuyShip(Game game)
    {
        var player = game.Player;
        var target = _ships[SelectedIndex];

        if (target.Id == player.CurrentShipType.Id)
        {
            SetStatus("ALREADY YOUR SHIP");
            return;
        }

        int netCost = target.BuyCost - player.CurrentShipType.SellValue;
        if (netCost > player.Credits)
        {
            SetStatus($"NEED {netCost} CR (HAVE {player.Credits})");
            return;
        }

        player.Credits -= netCost;
        player.SwitchShipType(target);
        SetStatus($"PURCHASED {target.Name.ToUpper()}! NET -{netCost} CR");
    }

    private static void RenderStatRow(SpriteRenderer renderer, float x, ref float y,
        string label, float newVal, float curVal, bool isCurrent, bool lowerIsBetter = false)
    {
        renderer.DrawTextScreen(x, y, $"{label}:", new Color3(140, 140, 160), 1.4f);
        renderer.DrawTextScreen(x + 140, y, $"{newVal:G4}", new Color3(200, 200, 220), 1.4f);

        if (!isCurrent)
        {
            float diff = newVal - curVal;
            if (Math.Abs(diff) > 0.001f)
            {
                bool good = lowerIsBetter ? diff < 0 : diff > 0;
                string diffText = $"({(diff > 0 ? "+" : "")}{diff:G3})";
                renderer.DrawTextScreen(x + 230, y, diffText,
                    new Color3(
                        good ? (byte)80 : (byte)255,
                        good ? (byte)255 : (byte)80,
                        (byte)80), 1.4f);
            }
        }

        y += 22;
    }
}
