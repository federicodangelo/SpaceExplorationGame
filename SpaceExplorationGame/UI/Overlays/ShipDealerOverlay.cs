using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SDL3;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Overlay for buying and selling ship hulls at stations/settlements.
/// Shows all available ship types with stats, comparison to current ship, and buy/sell pricing.
/// </summary>
public class ShipDealerOverlay : OverlayBase
{
    private int _selectedIndex;
    private string? _statusMessage;
    private float _statusTimer;

    private readonly ShipType[] _ships = ShipTypeCatalog.AllTypes;

    public void Open()
    {
        IsOpen = true;
        _selectedIndex = 0;
        _statusMessage = null;
        _statusTimer = 0;

        // Select current ship by default
        for (int i = 0; i < _ships.Length; i++)
        {
            if (_ships[i].Id == "scout") // fallback to first
                _selectedIndex = i;
        }
    }

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close();
            return true;
        }

        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selectedIndex = (_selectedIndex - 1 + _ships.Length) % _ships.Length;

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selectedIndex = (_selectedIndex + 1) % _ships.Length;

        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
            TryBuyShip(game);

        return true;
    }

    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;

        // Tick status message timer
        if (_statusTimer > 0)
        {
            _statusTimer -= dt;
            if (_statusTimer <= 0) _statusMessage = null;
        }
    }

    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;
        var player = game.Player;

        // Dim background
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 180);

        // Main panel
        float panelW = 900;
        float panelH = 550;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        // Title
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "SHIP DEALER", 255, 200, 80, 2.5f);
        renderer.DrawTextScreen(panelX + panelW - 200, panelY + 10, $"CREDITS: {player.Credits}", 255, 220, 80, 2f);
        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, 60, 60, 100);

        // Current ship info
        renderer.DrawTextScreen(panelX + 15, panelY + 52,
            $"CURRENT SHIP: {player.CurrentShipType.Name.ToUpper()}", 100, 220, 255, 1.8f);

        float tradeInValue = player.CurrentShipType.SellValue;
        renderer.DrawTextScreen(panelX + panelW - 250, panelY + 52,
            $"TRADE-IN VALUE: {tradeInValue} CR", 180, 220, 80, 1.5f);

        renderer.DrawLineScreen(panelX + 15, panelY + 78, panelX + panelW - 15, panelY + 78, 40, 40, 70);

        // Two-column layout
        float leftW = 320;
        float rightX = panelX + leftW + 10;
        float rightW = panelW - leftW - 25;
        float contentY = panelY + 85;

        // Column separator
        renderer.DrawLineScreen(panelX + leftW + 5, contentY,
            panelX + leftW + 5, panelY + panelH - 40, 40, 40, 70);

        // ── Left column: Ship list ──
        renderer.DrawTextScreen(panelX + 15, contentY, "AVAILABLE SHIPS", 180, 180, 220, 1.8f);
        float itemY = contentY + 30;

        for (int i = 0; i < _ships.Length; i++)
        {
            var ship = _ships[i];
            bool selected = i == _selectedIndex;
            bool isCurrent = ship.Id == player.CurrentShipType.Id;

            float itemH = 70;
            if (selected)
                renderer.DrawRectScreen(panelX + 5, itemY - 3, leftW - 5, itemH, 30, 40, 70, 180);

            // Ship name
            byte nR = selected ? (byte)255 : (byte)160;
            byte nG = selected ? (byte)255 : (byte)160;
            byte nB = selected ? (byte)200 : (byte)180;
            string tag = isCurrent ? " [YOUR SHIP]" : "";
            string prefix = selected ? "> " : "  ";
            renderer.DrawTextScreen(panelX + 15, itemY, $"{prefix}{ship.Name.ToUpper()}{tag}", nR, nG, nB, 1.8f);

            // Quick stats line
            renderer.DrawTextScreen(panelX + 30, itemY + 22,
                $"SLOTS: {ship.AvailableSlots.Length}  HULL: {ship.BaseHull:F0}  FUEL: {ship.BaseFuel:F0}",
                100, 140, 160, 1.3f);

            // Price
            if (isCurrent)
            {
                renderer.DrawTextScreen(panelX + 30, itemY + 42, "CURRENT", 100, 200, 255, 1.4f);
            }
            else if (ship.BuyCost <= 0)
            {
                renderer.DrawTextScreen(panelX + 30, itemY + 42, "FREE", 100, 255, 100, 1.4f);
            }
            else
            {
                int netCost = ship.BuyCost - player.CurrentShipType.SellValue;
                bool canAfford = netCost <= player.Credits;
                string costText = $"COST: {ship.BuyCost} CR (NET: {netCost} CR)";
                renderer.DrawTextScreen(panelX + 30, itemY + 42, costText,
                    canAfford ? (byte)255 : (byte)255,
                    canAfford ? (byte)220 : (byte)80,
                    canAfford ? (byte)80 : (byte)80, 1.4f);
            }

            itemY += itemH;
        }

        // ── Right column: Selected ship details ──
        var sel = _ships[_selectedIndex];
        bool selIsCurrent = sel.Id == player.CurrentShipType.Id;

        renderer.DrawTextScreen(rightX + 5, contentY, sel.Name.ToUpper(), 255, 220, 100, 2.2f);

        float detailY = contentY + 35;

        // Description
        renderer.DrawTextScreen(rightX + 5, detailY, sel.Description, 160, 160, 180, 1.5f);
        detailY += 25;

        renderer.DrawLineScreen(rightX + 5, detailY, rightX + rightW - 5, detailY, 40, 40, 70);
        detailY += 10;

        // Stats table
        var cur = player.CurrentShipType;
        RenderStatRow(renderer, rightX + 5, ref detailY, "SLOTS", sel.AvailableSlots.Length, cur.AvailableSlots.Length, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "BASE HULL", sel.BaseHull, cur.BaseHull, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "BASE FUEL", sel.BaseFuel, cur.BaseFuel, selIsCurrent);
        RenderStatRow(renderer, rightX + 5, ref detailY, "WEIGHT", sel.Weight, cur.Weight, selIsCurrent, lowerIsBetter: true);
        RenderStatRow(renderer, rightX + 5, ref detailY, "SIZE", sel.SpriteSize, cur.SpriteSize, selIsCurrent);

        detailY += 5;
        renderer.DrawLineScreen(rightX + 5, detailY, rightX + rightW - 5, detailY, 40, 40, 70);
        detailY += 10;

        // Slot breakdown
        renderer.DrawTextScreen(rightX + 5, detailY, "AVAILABLE SLOTS:", 160, 180, 220, 1.5f);
        detailY += 22;
        foreach (var slot in sel.AvailableSlots)
        {
            string slotName = ShipPartCatalog.GetSlotName(slot);
            bool curHasSlot = Array.Exists(cur.AvailableSlots, s => s == slot);
            byte sR = selIsCurrent ? (byte)150 : curHasSlot ? (byte)150 : (byte)80;
            byte sG = selIsCurrent ? (byte)150 : curHasSlot ? (byte)150 : (byte)255;
            byte sB = selIsCurrent ? (byte)170 : curHasSlot ? (byte)170 : (byte)80;
            string marker = selIsCurrent ? "  " : curHasSlot ? "  " : "+ ";
            renderer.DrawTextScreen(rightX + 15, detailY, $"{marker}{slotName}", sR, sG, sB, 1.4f);
            detailY += 18;
        }

        // Show slots that would be lost
        if (!selIsCurrent)
        {
            foreach (var slot in cur.AvailableSlots)
            {
                if (!Array.Exists(sel.AvailableSlots, s => s == slot))
                {
                    string slotName = ShipPartCatalog.GetSlotName(slot);
                    renderer.DrawTextScreen(rightX + 15, detailY, $"- {slotName}", 255, 80, 80, 1.4f);
                    detailY += 18;
                }
            }
        }

        // Price summary
        detailY += 10;
        if (selIsCurrent)
        {
            renderer.DrawTextScreen(rightX + 5, detailY, "THIS IS YOUR CURRENT SHIP", 100, 200, 255, 1.5f);
        }
        else
        {
            renderer.DrawTextScreen(rightX + 5, detailY, $"BUY PRICE: {sel.BuyCost} CR", 255, 220, 80, 1.5f);
            detailY += 20;
            renderer.DrawTextScreen(rightX + 5, detailY, $"TRADE-IN: -{cur.SellValue} CR", 80, 220, 80, 1.5f);
            detailY += 20;
            int net = sel.BuyCost - cur.SellValue;
            bool canAfford = net <= player.Credits;
            renderer.DrawTextScreen(rightX + 5, detailY, $"NET COST: {net} CR",
                canAfford ? (byte)100 : (byte)255,
                canAfford ? (byte)255 : (byte)100,
                canAfford ? (byte)100 : (byte)100, 1.8f);
        }

        // Status message
        if (_statusMessage != null)
        {
            bool isGood = _statusMessage.StartsWith("PURCHASED") || _statusMessage.StartsWith("SWITCHED");
            renderer.DrawRectScreen(panelX + 15, panelY + panelH - 55, panelW - 30, 22, 0, 0, 0, 200);
            renderer.DrawTextScreen(panelX + 20, panelY + panelH - 53, _statusMessage,
                isGood ? (byte)100 : (byte)255,
                isGood ? (byte)255 : (byte)150,
                isGood ? (byte)100 : (byte)80, 1.5f);
        }

        // Controls
        renderer.DrawTextScreen(panelX + 15, panelY + panelH - 28,
            "UP/DOWN: SELECT  ENTER: BUY SHIP  ESC: CLOSE",
            100, 100, 130, 1.3f);
    }

    private void TryBuyShip(Game game)
    {
        var player = game.Player;
        var target = _ships[_selectedIndex];

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

        // Process the trade
        player.Credits -= netCost;
        player.SwitchShipType(target);
        SetStatus($"PURCHASED {target.Name.ToUpper()}! NET -{netCost} CR");
    }

    private void SetStatus(string msg)
    {
        _statusMessage = msg;
        _statusTimer = 3f;
    }

    private static void RenderStatRow(SpriteRenderer renderer, float x, ref float y,
        string label, float newVal, float curVal, bool isCurrent, bool lowerIsBetter = false)
    {
        renderer.DrawTextScreen(x, y, $"{label}:", 140, 140, 160, 1.4f);
        renderer.DrawTextScreen(x + 140, y, $"{newVal:G4}", 200, 200, 220, 1.4f);

        if (!isCurrent)
        {
            float diff = newVal - curVal;
            if (Math.Abs(diff) > 0.001f)
            {
                bool good = lowerIsBetter ? diff < 0 : diff > 0;
                string diffText = $"({(diff > 0 ? "+" : "")}{diff:G3})";
                renderer.DrawTextScreen(x + 230, y, diffText,
                    good ? (byte)80 : (byte)255,
                    good ? (byte)255 : (byte)80,
                    (byte)80, 1.4f);
            }
        }

        y += 22;
    }
}
