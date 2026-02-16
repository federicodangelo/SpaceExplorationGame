using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays.Customization;

/// <summary>
/// Abstract base class for all customization overlays (ship, avatar, vehicle).
/// Provides the full two-column UI layout, input handling, equip/buy/sell logic.
/// Subclasses only supply configuration and type-specific data access.
/// </summary>
public abstract class CustomizationOverlayBase : OverlayBase
{
    private enum Column { Slots, Parts }
    private Column _activeColumn = Column.Slots;
    private int _selectedSlot;
    private int _selectedPart;
    private string? _statusMessage;
    private float _statusTimer;

    private ICustomizablePart[] _availableParts = [];

    // ── Abstract configuration ──

    /// <summary>Title displayed at the top of the overlay panel.</summary>
    protected abstract string Title { get; }

    /// <summary>Title text color.</summary>
    protected abstract Color3 TitleColor { get; }

    /// <summary>Panel height in pixels (e.g. 620 for ship with 7 slots, 420 for 3 slots).</summary>
    protected abstract float PanelHeight { get; }

    /// <summary>Number of equipment slots.</summary>
    protected abstract int SlotCount { get; }

    // ── Abstract data access (subclass provides) ──

    /// <summary>Get the display name for a slot by index.</summary>
    protected abstract string GetSlotName(int slotIndex);

    /// <summary>Get the currently equipped part for the given slot index, or null.</summary>
    protected abstract ICustomizablePart? GetEquippedPart(PlayerData player, int slotIndex);

    /// <summary>Get all catalog parts available for the given slot index.</summary>
    protected abstract ICustomizablePart[] GetAvailablePartsForSlot(int slotIndex);

    /// <summary>Whether the player owns the part (equipped or in inventory).</summary>
    protected abstract bool IsPartOwned(PlayerData player, ICustomizablePart part);

    /// <summary>Whether the part is in the player's inventory (owned but not equipped).</summary>
    protected abstract bool IsPartInInventory(PlayerData player, ICustomizablePart part);

    /// <summary>
    /// Perform the equip operation: move newPart into the slot, handle inventory for current part.
    /// Called after credits have been deducted (if buying). Should also remove newPart from inventory if owned.
    /// </summary>
    protected abstract void PerformEquip(PlayerData player, int slotIndex, ICustomizablePart newPart);

    /// <summary>Remove the part from the player's inventory (for selling).</summary>
    protected abstract void RemoveFromInventory(PlayerData player, ICustomizablePart part);

    /// <summary>Render stat comparison between new and current part.</summary>
    protected abstract void RenderStatComparison(SpriteRenderer renderer, float x, float y,
        ICustomizablePart newPart, ICustomizablePart currentPart);

    // ── Public API ──

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

    /// <summary>Returns true if overlay consumed input this frame.</summary>
    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            if (_activeColumn == Column.Parts) { _activeColumn = Column.Slots; return true; }
            Close();
            return true;
        }

        if (_activeColumn == Column.Slots)
        {
            if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            {
                _selectedSlot = (_selectedSlot - 1 + SlotCount) % SlotCount;
                RefreshAvailableParts();
            }
            if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            {
                _selectedSlot = (_selectedSlot + 1) % SlotCount;
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
                TryEquipOrBuy(game);
            if (input.IsKeyPressed(SDL.Scancode.X) || input.IsKeyPressed(SDL.Scancode.Delete))
                TrySellPart(game);
        }

        return true;
    }

    /// <summary>Fixed timestep update for status message timer.</summary>
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

        // Dim background
        renderer.DrawRectScreen(0, 0, w, h, 0, 0, 0, 180);

        // Main panel
        float panelW = 900;
        float panelH = PanelHeight;
        float panelX = w / 2f - panelW / 2f;
        float panelY = h / 2f - panelH / 2f;

        renderer.DrawRectScreen(panelX - 2, panelY - 2, panelW + 4, panelH + 4, 60, 60, 100, 200);
        renderer.DrawRectScreen(panelX, panelY, panelW, panelH, 15, 15, 35, 245);

        // Title
        var tc = TitleColor;
        renderer.DrawTextScreen(panelX + 15, panelY + 10, Title, tc.R, tc.G, tc.B, 2.5f);
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
        var player = game.Player;

        for (int i = 0; i < SlotCount; i++)
        {
            bool selected = i == _selectedSlot;
            bool active = selected && _activeColumn == Column.Slots;
            var equipped = GetEquippedPart(player, i);

            float itemH = 55;
            if (selected)
            {
                byte bgA = active ? (byte)180 : (byte)100;
                renderer.DrawRectScreen(panelX + 5, slotY - 3, leftW - 5, itemH, 30, 40, 70, bgA);
            }

            string slotName = GetSlotName(i);
            byte nR = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nG = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nB = active ? (byte)200 : selected ? (byte)240 : (byte)180;

            string prefix = active ? "> " : "  ";
            renderer.DrawTextScreen(panelX + 15, slotY, $"{prefix}{slotName}", nR, nG, nB, 1.8f);

            // Show equipped part name with tier-based color
            string partName = equipped?.Name ?? "(Empty)";
            byte pR = equipped?.Tier switch { 3 => 255, 2 => 100, _ => 130 };
            byte pG = equipped?.Tier switch { 3 => 180, 2 => 220, _ => 130 };
            byte pB = equipped?.Tier switch { 3 => 80, 2 => 255, _ => 150 };
            renderer.DrawTextScreen(panelX + 30, slotY + 22, partName, pR, pG, pB, 1.5f);

            slotY += itemH;
        }

        // ── Right column: Available parts ──
        renderer.DrawTextScreen(rightX + 5, contentY,
            $"PARTS: {GetSlotName(_selectedSlot)}", 180, 180, 220, 1.8f);

        var currentEquipped = GetEquippedPart(player, _selectedSlot);

        float partY = contentY + 30;
        for (int i = 0; i < _availableParts.Length; i++)
        {
            var part = _availableParts[i];
            bool selected = i == _selectedPart && _activeColumn == Column.Parts;
            bool isEquipped = currentEquipped?.Id == part.Id;
            bool owned = IsPartOwned(player, part);
            bool inInventory = IsPartInInventory(player, part);

            float itemH = 65;
            if (selected)
                renderer.DrawRectScreen(rightX, partY - 3, rightW, itemH, 30, 40, 70, 180);

            // Part name
            byte tr = selected ? (byte)255 : (byte)180;
            byte tg = selected ? (byte)255 : (byte)180;
            byte tb = selected ? (byte)200 : (byte)200;
            string tag = isEquipped ? " [EQUIPPED]" : inInventory ? " [OWNED]" : "";
            renderer.DrawTextScreen(rightX + 5, partY,
                $"{(selected ? "> " : "  ")}{part.Name}{tag}", tr, tg, tb, 1.8f);

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

            // Stat comparison
            if (selected && !isEquipped && currentEquipped != null)
                RenderStatComparison(renderer, rightX + 20, partY + 38, part, currentEquipped);

            partY += itemH;
            if (partY > panelY + panelH - 60) break;
        }

        // Status message
        if (_statusMessage != null)
        {
            bool isGood = _statusMessage.StartsWith("EQUIPPED") || _statusMessage.StartsWith("PURCHASED")
                          || _statusMessage.StartsWith("SOLD");
            renderer.DrawRectScreen(panelX + 15, panelY + panelH - 55, panelW - 30, 22, 0, 0, 0, 200);
            renderer.DrawTextScreen(panelX + 20, panelY + panelH - 53, _statusMessage,
                isGood ? (byte)100 : (byte)255,
                isGood ? (byte)255 : (byte)150,
                isGood ? (byte)100 : (byte)80, 1.5f);
        }

        // Controls
        renderer.DrawTextScreen(panelX + 15, panelY + panelH - 28,
            _activeColumn == Column.Slots
                ? "UP/DOWN: SELECT SLOT  ENTER/RIGHT: BROWSE PARTS  ESC: CLOSE"
                : "UP/DOWN: SELECT  ENTER: EQUIP/BUY  X: SELL  LEFT/ESC: BACK",
            100, 100, 130, 1.3f);
    }

    // ── Private helpers ──

    private void TryEquipOrBuy(Game game)
    {
        if (_availableParts.Length == 0) return;

        var newPart = _availableParts[_selectedPart];
        var player = game.Player;
        var currentEquipped = GetEquippedPart(player, _selectedSlot);

        // Already equipped in this slot?
        if (currentEquipped?.Id == newPart.Id)
        {
            SetStatus("ALREADY EQUIPPED");
            return;
        }

        if (IsPartOwned(player, newPart))
        {
            // Player owns this part — equip for free
            PerformEquip(player, _selectedSlot, newPart);
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
            PerformEquip(player, _selectedSlot, newPart);
            SetStatus($"PURCHASED & EQUIPPED! -{newPart.BuyCost} CR");
        }
    }

    private void TrySellPart(Game game)
    {
        if (_availableParts.Length == 0) return;

        var part = _availableParts[_selectedPart];
        var player = game.Player;

        if (!IsPartInInventory(player, part))
        {
            bool equipped = GetEquippedPart(player, _selectedSlot)?.Id == part.Id
                || Enumerable.Range(0, SlotCount).Any(i => GetEquippedPart(player, i)?.Id == part.Id);
            SetStatus(equipped ? "UNEQUIP FIRST (SWAP TO ANOTHER PART)" : "YOU DON'T OWN THIS PART");
            return;
        }

        if (part.SellValue <= 0)
        {
            SetStatus("CANNOT SELL THIS PART");
            return;
        }

        RemoveFromInventory(player, part);
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
        _availableParts = GetAvailablePartsForSlot(_selectedSlot);
        _selectedPart = 0;
    }

    /// <summary>Helper for subclass stat comparison rendering.</summary>
    protected static void RenderStatDiffs(SpriteRenderer renderer, float x, float y,
        List<StatDiff> diffs)
    {
        float cx = x;
        foreach (var (label, diff) in diffs)
        {
            string text = $"{label}{(diff > 0 ? "+" : "")}{diff:F0} ";
            renderer.DrawTextScreen(cx, y, text,
                diff > 0 ? (byte)80 : (byte)255,
                diff > 0 ? (byte)255 : (byte)80,
                (byte)80, 1.2f);
            cx += text.Length * 7;
        }
    }
}
