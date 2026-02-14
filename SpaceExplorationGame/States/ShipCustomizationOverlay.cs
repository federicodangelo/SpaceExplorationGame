using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.States;

/// <summary>
/// Ship customization overlay: browse equipped slots, swap/buy/sell parts.
/// Two-column layout: left column lists slots, right column shows available parts for the selected slot.
/// Owned parts can be equipped for free. Unowned parts must be purchased. Owned parts can be sold manually.
/// </summary>
public class ShipCustomizationOverlay
{
    public bool IsOpen { get; private set; }

    // Navigation state
    private enum Column { Slots, Parts }
    private Column _activeColumn = Column.Slots;
    private int _selectedSlot;
    private int _selectedPart;
    private string? _statusMessage;
    private float _statusTimer;

    private static readonly ShipSlotType[] SlotOrder =
    [
        ShipSlotType.Engine,
        ShipSlotType.Armor,
        ShipSlotType.Shield,
        ShipSlotType.FtlDrive,
        ShipSlotType.Weapon1,
        ShipSlotType.Weapon2,
        ShipSlotType.Utility
    ];

    // Cached parts list for the currently selected slot
    private ShipPart[] _availableParts = [];

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

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>Returns true if overlay consumed input this frame.</summary>
    public bool Update(Game game, InputManager input, float dt)
    {
        if (!IsOpen) return false;

        // Tick status message timer
        if (_statusTimer > 0)
        {
            _statusTimer -= dt;
            if (_statusTimer <= 0) _statusMessage = null;
        }

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            if (_activeColumn == Column.Parts)
            {
                _activeColumn = Column.Slots;
                return true;
            }
            Close();
            return true;
        }

        if (_activeColumn == Column.Slots)
        {
            if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            {
                _selectedSlot = (_selectedSlot - 1 + SlotOrder.Length) % SlotOrder.Length;
                RefreshAvailableParts();
            }
            if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            {
                _selectedSlot = (_selectedSlot + 1) % SlotOrder.Length;
                RefreshAvailableParts();
            }
            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E)
                || input.IsKeyPressed(SDL.Scancode.Right) || input.IsKeyPressed(SDL.Scancode.D))
            {
                _activeColumn = Column.Parts;
                _selectedPart = 0;
            }
        }
        else // Column.Parts
        {
            if (input.IsKeyPressed(SDL.Scancode.Left) || input.IsKeyPressed(SDL.Scancode.A))
            {
                _activeColumn = Column.Slots;
                return true;
            }
            if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            {
                if (_availableParts.Length > 0)
                    _selectedPart = (_selectedPart - 1 + _availableParts.Length) % _availableParts.Length;
            }
            if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            {
                if (_availableParts.Length > 0)
                    _selectedPart = (_selectedPart + 1) % _availableParts.Length;
            }
            if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
            {
                TryEquipOrBuy(game);
            }
            if (input.IsKeyPressed(SDL.Scancode.X) || input.IsKeyPressed(SDL.Scancode.Delete))
            {
                TrySellPart(game);
            }
        }

        return true;
    }

    /// <summary>Check whether the player owns a part (either equipped or in inventory).</summary>
    private bool IsOwned(PlayerData player, ShipPart part)
    {
        // Equipped in any slot counts as owned
        foreach (var equipped in player.EquippedParts.Values)
            if (equipped.Id == part.Id) return true;
        // In inventory
        return player.OwnedParts.Any(p => p.Id == part.Id);
    }

    /// <summary>Check whether the part is in the player's inventory (owned but not currently equipped).</summary>
    private bool IsInInventory(PlayerData player, ShipPart part)
    {
        return player.OwnedParts.Any(p => p.Id == part.Id);
    }

    private void TryEquipOrBuy(Game game)
    {
        if (_availableParts.Length == 0) return;

        var newPart = _availableParts[_selectedPart];
        var slot = SlotOrder[_selectedSlot];
        var player = game.Player;
        player.EquippedParts.TryGetValue(slot, out var currentEquipped);

        // Already equipped in this slot?
        if (currentEquipped?.Id == newPart.Id)
        {
            SetStatus("ALREADY EQUIPPED");
            return;
        }

        if (IsOwned(player, newPart))
        {
            // Player owns this part — equip for free
            // Remove from inventory if it's there
            var invItem = player.OwnedParts.FirstOrDefault(p => p.Id == newPart.Id);
            if (invItem != null)
                player.OwnedParts.Remove(invItem);

            // Un-equip current part → goes to inventory (if it's a real part)
            if (currentEquipped != null && currentEquipped.Tier > 0)
                player.OwnedParts.Add(currentEquipped);

            player.EquippedParts[slot] = newPart;
            player.RecalculateShipStats();
            SetStatus("EQUIPPED!");
        }
        else
        {
            // Must buy the part first
            if (newPart.BuyCost > player.Credits)
            {
                SetStatus($"NEED {newPart.BuyCost} CR (HAVE {player.Credits})");
                return;
            }

            player.Credits -= newPart.BuyCost;

            // Un-equip current part → goes to inventory (if it's a real part)
            if (currentEquipped != null && currentEquipped.Tier > 0)
                player.OwnedParts.Add(currentEquipped);

            player.EquippedParts[slot] = newPart;
            player.RecalculateShipStats();
            SetStatus($"PURCHASED & EQUIPPED! -{newPart.BuyCost} CR");
        }
    }

    private void TrySellPart(Game game)
    {
        if (_availableParts.Length == 0) return;

        var part = _availableParts[_selectedPart];
        var player = game.Player;

        // Can only sell parts that are in inventory (owned, not equipped)
        if (!IsInInventory(player, part))
        {
            // Check if it's equipped
            bool equipped = player.EquippedParts.Values.Any(p => p.Id == part.Id);
            if (equipped)
                SetStatus("UNEQUIP FIRST (SWAP TO ANOTHER PART)");
            else
                SetStatus("YOU DON'T OWN THIS PART");
            return;
        }

        if (part.SellValue <= 0)
        {
            SetStatus("CANNOT SELL THIS PART");
            return;
        }

        var invItem = player.OwnedParts.First(p => p.Id == part.Id);
        player.OwnedParts.Remove(invItem);
        player.Credits += part.SellValue;
        SetStatus($"SOLD {part.Name}! +{part.SellValue} CR");
    }

    private void SetStatus(string msg)
    {
        _statusMessage = msg;
        _statusTimer = 3f;
    }

    private void RefreshAvailableParts()
    {
        var slot = SlotOrder[_selectedSlot];
        _availableParts = ShipPartCatalog.GetPartsForSlot(slot);
        _selectedPart = 0;
    }

    public void Render(Game game, SpriteRenderer renderer)
    {
        if (!IsOpen) return;

        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Dim background
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 180);

        // Main panel
        float panelW = 900;
        float panelH = 620;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        // Title
        renderer.DrawTextScreen(panelX + 15, panelY + 10, "SHIP CUSTOMIZATION", 100, 220, 255, 2.5f);
        renderer.DrawTextScreen(panelX + panelW - 200, panelY + 10, $"CREDITS: {game.Player.Credits}", 255, 220, 80, 2f);
        renderer.DrawLineScreen(panelX + 15, panelY + 45, panelX + panelW - 15, panelY + 45, 60, 60, 100);

        // Two-column layout
        float leftW = 300;
        float rightX = panelX + leftW + 10;
        float rightW = panelW - leftW - 25;
        float contentY = panelY + 55;

        // Column separator
        renderer.DrawLineScreen(panelX + leftW + 5, contentY, panelX + leftW + 5, panelY + panelH - 40, 40, 40, 70);

        // ── Left column: Slot list ──
        renderer.DrawTextScreen(panelX + 15, contentY, "EQUIPMENT SLOTS", 180, 180, 220, 1.8f);
        float slotY = contentY + 30;

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            var slot = SlotOrder[i];
            bool selected = i == _selectedSlot;
            bool active = selected && _activeColumn == Column.Slots;
            game.Player.EquippedParts.TryGetValue(slot, out var equipped);

            float itemH = 55;
            if (selected)
            {
                byte bgA = active ? (byte)180 : (byte)100;
                renderer.DrawRectScreen(panelX + 5, slotY - 3, leftW - 5, itemH, 30, 40, 70, bgA);
            }

            string slotName = ShipPartCatalog.GetSlotName(slot);
            byte nR = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nG = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nB = active ? (byte)200 : selected ? (byte)240 : (byte)180;

            string prefix = active ? "> " : "  ";
            renderer.DrawTextScreen(panelX + 15, slotY, $"{prefix}{slotName}", nR, nG, nB, 1.8f);

            // Show equipped part name
            string partName = equipped?.Name ?? "(Empty)";
            byte pR = equipped?.Tier switch { 3 => 255, 2 => 100, _ => 130 };
            byte pG = equipped?.Tier switch { 3 => 180, 2 => 220, _ => 130 };
            byte pB = equipped?.Tier switch { 3 => 80, 2 => 255, _ => 150 };
            renderer.DrawTextScreen(panelX + 30, slotY + 22, partName, pR, pG, pB, 1.5f);

            slotY += itemH;
        }

        // ── Right column: Available parts ──
        var currentSlot = SlotOrder[_selectedSlot];
        renderer.DrawTextScreen(rightX + 5, contentY,
            $"PARTS: {ShipPartCatalog.GetSlotName(currentSlot)}", 180, 180, 220, 1.8f);

        game.Player.EquippedParts.TryGetValue(currentSlot, out var currentEquipped);
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
            if (selected)
                renderer.DrawRectScreen(rightX, partY - 3, rightW, itemH, 30, 40, 70, 180);

            // Part name
            byte tr = selected ? (byte)255 : (byte)180;
            byte tg = selected ? (byte)255 : (byte)180;
            byte tb = selected ? (byte)200 : (byte)200;
            string prefix = selected ? "> " : "  ";
            string tag = isEquipped ? " [EQUIPPED]" : inInventory ? " [OWNED]" : "";
            renderer.DrawTextScreen(rightX + 5, partY, $"{prefix}{part.Name}{tag}", tr, tg, tb, 1.8f);

            // Description
            renderer.DrawTextScreen(rightX + 20, partY + 22, part.Description, 130, 130, 150, 1.3f);

            // Cost / status indicator
            if (isEquipped)
            {
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, "IN USE", 100, 180, 255, 1.5f);
            }
            else if (owned)
            {
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, "FREE", 100, 255, 100, 1.5f);
            }
            else if (part.BuyCost > 0)
            {
                bool canAfford = player.Credits >= part.BuyCost;
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, $"{part.BuyCost} CR",
                    canAfford ? (byte)255 : (byte)255,
                    canAfford ? (byte)220 : (byte)80,
                    canAfford ? (byte)80 : (byte)80, 1.5f);
            }

            // Stat comparison (brief)
            if (selected && !isEquipped && currentEquipped != null)
            {
                RenderStatComparison(renderer, rightX + 20, partY + 38, part, currentEquipped);
            }

            partY += itemH;
            if (partY > panelY + panelH - 60) break; // clip overflow
        }

        // Status message
        if (_statusMessage != null)
        {
            bool isGood = _statusMessage.StartsWith("EQUIPPED") || _statusMessage.StartsWith("PURCHASED")
                          || _statusMessage.StartsWith("SOLD");
            renderer.DrawRectScreen(panelX + 15, panelY + panelH - 55, panelW - 30, 22, 0, 0, 0, 200);
            renderer.DrawTextScreen(panelX + 20, panelY + panelH - 53,
                _statusMessage,
                isGood ? (byte)100 : (byte)255,
                isGood ? (byte)255 : (byte)150,
                isGood ? (byte)100 : (byte)80, 1.5f);
        }

        // Controls
        string controlsText = _activeColumn == Column.Slots
            ? "UP/DOWN: SELECT SLOT  ENTER/RIGHT: BROWSE PARTS  ESC: CLOSE"
            : "UP/DOWN: SELECT  ENTER: EQUIP/BUY  X: SELL  LEFT/ESC: BACK";
        renderer.DrawTextScreen(panelX + 15, panelY + panelH - 28, controlsText, 100, 100, 130, 1.3f);
    }

    private void RenderStatComparison(SpriteRenderer renderer, float x, float y,
        ShipPart newPart, ShipPart oldPart)
    {
        var n = newPart.Stats;
        var o = oldPart.Stats;

        var diffs = new List<(string Label, float Diff)>();
        if (n.Acceleration - o.Acceleration != 0) diffs.Add(("ACC", n.Acceleration - o.Acceleration));
        if (n.MaxSpeed - o.MaxSpeed != 0) diffs.Add(("SPD", n.MaxSpeed - o.MaxSpeed));
        if (n.RotationSpeed - o.RotationSpeed != 0) diffs.Add(("ROT", n.RotationSpeed - o.RotationSpeed));
        if (n.MaxHull - o.MaxHull != 0) diffs.Add(("HULL", n.MaxHull - o.MaxHull));
        if (n.MaxFuel - o.MaxFuel != 0) diffs.Add(("FUEL", n.MaxFuel - o.MaxFuel));
        if (n.FtlRange - o.FtlRange != 0) diffs.Add(("FTL", n.FtlRange - o.FtlRange));
        if (n.ShieldStrength - o.ShieldStrength != 0) diffs.Add(("SHD", n.ShieldStrength - o.ShieldStrength));
        if (n.WeaponDamage - o.WeaponDamage != 0) diffs.Add(("DMG", n.WeaponDamage - o.WeaponDamage));
        if (n.FuelEfficiency - o.FuelEfficiency != 0) diffs.Add(("EFF", (n.FuelEfficiency - o.FuelEfficiency) * 100));

        float cx = x;
        foreach (var (label, diff) in diffs)
        {
            string sign = diff > 0 ? "+" : "";
            byte dr = diff > 0 ? (byte)80 : (byte)255;
            byte dg = diff > 0 ? (byte)255 : (byte)80;
            byte db = diff > 0 ? (byte)80 : (byte)80;
            string text = $"{label}{sign}{diff:F0} ";
            renderer.DrawTextScreen(cx, y, text, dr, dg, db, 1.2f);
            cx += text.Length * 7; // approximate character width at 1.2 scale
        }
    }
}
