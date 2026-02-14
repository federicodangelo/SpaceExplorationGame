using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.States;

/// <summary>
/// Vehicle customization overlay: browse equipped vehicle slots, swap/buy/sell parts.
/// Two-column layout identical to ShipCustomizationOverlay.
/// </summary>
public class VehicleCustomizationOverlay
{
    public bool IsOpen { get; private set; }

    private enum Column { Slots, Parts }
    private Column _activeColumn = Column.Slots;
    private int _selectedSlot;
    private int _selectedPart;
    private string? _statusMessage;
    private float _statusTimer;

    private static readonly VehicleSlotType[] SlotOrder =
    [
        VehicleSlotType.Engine,
        VehicleSlotType.Chassis,
        VehicleSlotType.Lights
    ];

    private VehiclePart[] _availableParts = [];

    public void Open()
    {
        IsOpen = true;
        _activeColumn = Column.Slots;
        _selectedSlot = 0;
        _selectedPart = 0;
        _statusMessage = null;
        _statusTimer = 0;
        RefreshAvailableParts();
    }

    public void Close() => IsOpen = false;

    public bool Update(Game game, InputManager input, float dt)
    {
        if (!IsOpen) return false;

        if (_statusTimer > 0)
        {
            _statusTimer -= dt;
            if (_statusTimer <= 0) _statusMessage = null;
        }

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            if (_activeColumn == Column.Parts) { _activeColumn = Column.Slots; return true; }
            Close();
            return true;
        }

        if (_activeColumn == Column.Slots)
        {
            if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            { _selectedSlot = (_selectedSlot - 1 + SlotOrder.Length) % SlotOrder.Length; RefreshAvailableParts(); }
            if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            { _selectedSlot = (_selectedSlot + 1) % SlotOrder.Length; RefreshAvailableParts(); }
            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E)
                || input.IsKeyPressed(SDL.Scancode.Right) || input.IsKeyPressed(SDL.Scancode.D))
            { _activeColumn = Column.Parts; _selectedPart = 0; }
        }
        else
        {
            if (input.IsKeyPressed(SDL.Scancode.Left) || input.IsKeyPressed(SDL.Scancode.A))
            { _activeColumn = Column.Slots; return true; }
            if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            { if (_availableParts.Length > 0) _selectedPart = (_selectedPart - 1 + _availableParts.Length) % _availableParts.Length; }
            if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            { if (_availableParts.Length > 0) _selectedPart = (_selectedPart + 1) % _availableParts.Length; }
            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
                TryEquipOrBuy(game);
            if (input.IsKeyPressed(SDL.Scancode.X) || input.IsKeyPressed(SDL.Scancode.Delete))
                TrySellPart(game);
        }

        return true;
    }

    private bool IsOwned(PlayerData player, VehiclePart part)
    {
        foreach (var eq in player.EquippedVehicleParts.Values)
            if (eq.Id == part.Id) return true;
        return player.OwnedVehicleParts.Any(p => p.Id == part.Id);
    }

    private bool IsInInventory(PlayerData player, VehiclePart part) =>
        player.OwnedVehicleParts.Any(p => p.Id == part.Id);

    private void TryEquipOrBuy(Game game)
    {
        if (_availableParts.Length == 0) return;
        var newPart = _availableParts[_selectedPart];
        var slot = SlotOrder[_selectedSlot];
        var player = game.Player;
        player.EquippedVehicleParts.TryGetValue(slot, out var current);

        if (current?.Id == newPart.Id) { SetStatus("ALREADY EQUIPPED"); return; }

        if (IsOwned(player, newPart))
        {
            var inv = player.OwnedVehicleParts.FirstOrDefault(p => p.Id == newPart.Id);
            if (inv != null) player.OwnedVehicleParts.Remove(inv);
            if (current != null) player.OwnedVehicleParts.Add(current);
            player.EquippedVehicleParts[slot] = newPart;
            SetStatus("EQUIPPED!");
        }
        else
        {
            if (newPart.BuyCost > player.Credits)
            { SetStatus($"NEED {newPart.BuyCost} CR (HAVE {player.Credits})"); return; }
            player.Credits -= newPart.BuyCost;
            if (current != null) player.OwnedVehicleParts.Add(current);
            player.EquippedVehicleParts[slot] = newPart;
            SetStatus($"PURCHASED & EQUIPPED! -{newPart.BuyCost} CR");
        }
    }

    private void TrySellPart(Game game)
    {
        if (_availableParts.Length == 0) return;
        var part = _availableParts[_selectedPart];
        var player = game.Player;

        if (!IsInInventory(player, part))
        {
            bool eq = player.EquippedVehicleParts.Values.Any(p => p.Id == part.Id);
            SetStatus(eq ? "UNEQUIP FIRST (SWAP TO ANOTHER PART)" : "YOU DON'T OWN THIS PART");
            return;
        }
        if (part.SellValue <= 0) { SetStatus("CANNOT SELL THIS PART"); return; }

        var inv = player.OwnedVehicleParts.First(p => p.Id == part.Id);
        player.OwnedVehicleParts.Remove(inv);
        player.Credits += part.SellValue;
        SetStatus($"SOLD {part.Name}! +{part.SellValue} CR");
    }

    private void SetStatus(string msg) { _statusMessage = msg; _statusTimer = 3f; }
    private void RefreshAvailableParts()
    {
        _availableParts = VehiclePartCatalog.GetPartsForSlot(SlotOrder[_selectedSlot]);
        _selectedPart = 0;
    }

    public void Render(Game game, SpriteRenderer renderer)
    {
        if (!IsOpen) return;

        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 180);

        float panelW = 900, panelH = 420;
        float panelX = w / 2f - panelW / 2f, panelY = h / 2f - panelH / 2f;

        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        renderer.DrawTextScreen(panelX + 15, panelY + 10, "VEHICLE CUSTOMIZATION", 255, 180, 80, 2.5f);
        renderer.DrawTextScreen(panelX + panelW - 200, panelY + 10, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);
        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, 60, 60, 100);

        float leftW = 300;
        float rightX = panelX + leftW + 10;
        float rightW = panelW - leftW - 25;
        float contentY = panelY + 55;

        renderer.DrawLineScreen(panelX + leftW + 5, contentY, panelX + leftW + 5, panelY + panelH - 40, 40, 40, 70);

        // Left column: slots
        renderer.DrawTextScreen(panelX + 15, contentY, "EQUIPMENT SLOTS", 180, 180, 220, 1.8f);
        float slotY = contentY + 30;

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            var slot = SlotOrder[i];
            bool selected = i == _selectedSlot;
            bool active = selected && _activeColumn == Column.Slots;
            game.Player.EquippedVehicleParts.TryGetValue(slot, out var equipped);

            float itemH = 55;
            if (selected)
                renderer.DrawRectScreen(panelX + 5, slotY - 3, leftW - 5, itemH, 30, 40, 70, active ? (byte)180 : (byte)100);

            string slotName = VehiclePartCatalog.GetSlotName(slot);
            byte nR = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nG = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nB = active ? (byte)200 : selected ? (byte)240 : (byte)180;
            renderer.DrawTextScreen(panelX + 15, slotY, $"{(active ? "> " : "  ")}{slotName}", nR, nG, nB, 1.8f);

            string partName = equipped?.Name ?? "(Empty)";
            byte pR = equipped?.Tier switch { 3 => 255, 2 => 100, _ => 130 };
            byte pG = equipped?.Tier switch { 3 => 180, 2 => 220, _ => 130 };
            byte pB = equipped?.Tier switch { 3 => 80, 2 => 255, _ => 150 };
            renderer.DrawTextScreen(panelX + 30, slotY + 22, partName, pR, pG, pB, 1.5f);
            slotY += itemH;
        }

        // Right column: available parts
        var currentSlot = SlotOrder[_selectedSlot];
        renderer.DrawTextScreen(rightX + 5, contentY, $"PARTS: {VehiclePartCatalog.GetSlotName(currentSlot)}", 180, 180, 220, 1.8f);
        game.Player.EquippedVehicleParts.TryGetValue(currentSlot, out var currentEquipped);
        var player = game.Player;

        float partY = contentY + 30;
        for (int i = 0; i < _availableParts.Length; i++)
        {
            var part = _availableParts[i];
            bool selected = i == _selectedPart && _activeColumn == Column.Parts;
            bool isEquipped = currentEquipped?.Id == part.Id;
            bool owned = IsOwned(player, part);
            bool inInventory = IsInInventory(player, part);

            float itemH = 65;
            if (selected) renderer.DrawRectScreen(rightX, partY - 3, rightW, itemH, 30, 40, 70, 180);

            byte tr = selected ? (byte)255 : (byte)180;
            byte tg = selected ? (byte)255 : (byte)180;
            byte tb = selected ? (byte)200 : (byte)200;
            string tag = isEquipped ? " [EQUIPPED]" : inInventory ? " [OWNED]" : "";
            renderer.DrawTextScreen(rightX + 5, partY, $"{(selected ? "> " : "  ")}{part.Name}{tag}", tr, tg, tb, 1.8f);
            renderer.DrawTextScreen(rightX + 20, partY + 22, part.Description, 130, 130, 150, 1.3f);

            if (isEquipped)
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, "IN USE", 100, 180, 255, 1.5f);
            else if (owned)
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, "FREE", 100, 255, 100, 1.5f);
            else if (part.BuyCost > 0)
            {
                bool canAfford = player.Credits >= part.BuyCost;
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, $"{part.BuyCost} CR",
                    canAfford ? (byte)255 : (byte)255, canAfford ? (byte)220 : (byte)80, canAfford ? (byte)80 : (byte)80, 1.5f);
            }

            if (selected && !isEquipped && currentEquipped != null)
                RenderStatComparison(renderer, rightX + 20, partY + 38, part, currentEquipped);

            partY += itemH;
            if (partY > panelY + panelH - 60) break;
        }

        // Status
        if (_statusMessage != null)
        {
            bool isGood = _statusMessage.StartsWith("EQUIPPED") || _statusMessage.StartsWith("PURCHASED") || _statusMessage.StartsWith("SOLD");
            renderer.DrawRectScreen(panelX + 15, panelY + panelH - 55, panelW - 30, 22, 0, 0, 0, 200);
            renderer.DrawTextScreen(panelX + 20, panelY + panelH - 53, _statusMessage,
                isGood ? (byte)100 : (byte)255, isGood ? (byte)255 : (byte)150, isGood ? (byte)100 : (byte)80, 1.5f);
        }

        renderer.DrawTextScreen(panelX + 15, panelY + panelH - 28,
            _activeColumn == Column.Slots
                ? "UP/DOWN: SELECT SLOT  ENTER/RIGHT: BROWSE PARTS  ESC: CLOSE"
                : "UP/DOWN: SELECT  ENTER: EQUIP/BUY  X: SELL  LEFT/ESC: BACK",
            100, 100, 130, 1.3f);
    }

    private void RenderStatComparison(SpriteRenderer renderer, float x, float y, VehiclePart newPart, VehiclePart oldPart)
    {
        var n = newPart.Stats; var o = oldPart.Stats;
        var diffs = new List<(string Label, float Diff)>();
        if (n.Acceleration - o.Acceleration != 0) diffs.Add(("ACC", n.Acceleration - o.Acceleration));
        if (n.MaxSpeed - o.MaxSpeed != 0) diffs.Add(("SPD", n.MaxSpeed - o.MaxSpeed));
        if (n.RotationSpeed - o.RotationSpeed != 0) diffs.Add(("ROT", n.RotationSpeed - o.RotationSpeed));
        if (n.Friction - o.Friction != 0) diffs.Add(("GRP", (n.Friction - o.Friction) * 1000));
        if (n.Visibility - o.Visibility != 0) diffs.Add(("VIS", n.Visibility - o.Visibility));

        float cx = x;
        foreach (var (label, diff) in diffs)
        {
            string text = $"{label}{(diff > 0 ? "+" : "")}{diff:F0} ";
            renderer.DrawTextScreen(cx, y, text, diff > 0 ? (byte)80 : (byte)255, diff > 0 ? (byte)255 : (byte)80, (byte)80, 1.2f);
            cx += text.Length * 7;
        }
    }
}
