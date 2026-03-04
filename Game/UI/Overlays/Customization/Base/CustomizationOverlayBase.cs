using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Customization.Base;

/// <summary>
/// Abstract base class for all customization overlays (ship, avatar, vehicle).
/// Provides the full two-column UI layout, input handling, equip/buy/sell logic.
/// Subclasses only supply configuration and type-specific data access.
/// Extends PanelOverlayBase for panel rendering, status messages, and Escape handling.
/// </summary>
public abstract class CustomizationOverlayBase : PanelOverlayBase
{
    private enum Column { Slots, Parts }
    private Column _activeColumn = Column.Slots;
    private int _selectedSlot;
    private int _selectedPart;

    private ICustomizablePart[] _availableParts = [];

    // ── Panel configuration (from PanelOverlayBase) ──

    protected override float PanelWidth => 900;
    protected override bool ShowCredits => true;

    protected override string? ControlsHint
    {
        get
        {
            var input = CurrentInput;
            if (input == null)
                return "";

            string upDown = $"{input.GetActionHelpText(InputAction.MenuUp)}/{input.GetActionHelpText(InputAction.MenuDown)}";
            string confirm = input.GetActionHelpText(InputAction.MenuConfirm);
            string right = input.GetActionHelpText(InputAction.MenuRight);
            string left = input.GetActionHelpText(InputAction.MenuLeft);
            string back = input.GetActionHelpText(InputAction.MenuBack);
            string secondary = input.GetActionHelpText(InputAction.MenuSecondaryAction);

            return _activeColumn == Column.Slots
                ? $"{upDown}: SELECT SLOT  {confirm}/{right}: BROWSE PARTS  {back}: CLOSE"
                : $"{upDown}: SELECT  {confirm}: EQUIP/BUY  {secondary}: SELL  {left}/{back}: BACK";
        }
    }

    // ── Abstract configuration (subclass provides) ──

    /// <summary>Number of equipment slots.</summary>
    protected abstract int SlotCount { get; }

    // ── Abstract data access ──

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
    /// Called after credits have been deducted (if buying).
    /// </summary>
    protected abstract void PerformEquip(PlayerData player, int slotIndex, ICustomizablePart newPart);

    /// <summary>Remove the part from the player's inventory (for selling).</summary>
    protected abstract void RemoveFromInventory(PlayerData player, ICustomizablePart part);

    /// <summary>Render stat comparison between new and current part.</summary>
    protected abstract void RenderStatComparison(ISpriteRenderer renderer, float x, float y,
        ICustomizablePart newPart, ICustomizablePart currentPart);

    // ── Open ──

    public override void Open()
    {
        base.Open();
        _activeColumn = Column.Slots;
        _selectedSlot = 0;
        _selectedPart = 0;
        RefreshAvailableParts();
    }

    // ── Escape handling (back navigation) ──

    protected override void OnEscapePressed()
    {
        if (_activeColumn == Column.Parts)
        {
            _activeColumn = Column.Slots;
            return;
        }
        Close();
    }

    // ── Input (two-column keyboard + mouse navigation) ──

    protected override void ProcessInput(Game game, IInputManager input)
    {
        if (_activeColumn == Column.Slots)
            ProcessSlotsInput(input);
        else
            ProcessPartsInput(game, input);
    }

    private void ProcessSlotsInput(IInputManager input)
    {
        // Keyboard navigation
        if (input.IsActionPressed(InputAction.MenuUp))
        {
            _selectedSlot = (_selectedSlot - 1 + SlotCount) % SlotCount;
            RefreshAvailableParts();
        }
        if (input.IsActionPressed(InputAction.MenuDown))
        {
            _selectedSlot = (_selectedSlot + 1) % SlotCount;
            RefreshAvailableParts();
        }
        if (input.IsActionPressed(InputAction.MenuConfirm)
            || input.IsActionPressed(InputAction.MenuRight))
        {
            _activeColumn = Column.Parts;
            _selectedPart = 0;
        }

        // Mouse: hover to select slot, click to enter parts
        float slotX = PanelX + 5;
        float slotW = 300 - 5;
        float slotStartY = ContentY + 30;
        float mx = input.MouseX, my = input.MouseY;

        for (int i = 0; i < SlotCount; i++)
        {
            float itemY = slotStartY + i * 55;
            if (mx >= slotX && mx <= slotX + slotW && my >= itemY && my < itemY + 55)
            {
                if (_selectedSlot != i)
                {
                    _selectedSlot = i;
                    RefreshAvailableParts();
                }
                if (input.IsMousePressed(MouseButton.Left))
                {
                    _activeColumn = Column.Parts;
                    _selectedPart = 0;
                }
                break;
            }
        }
    }

    private void ProcessPartsInput(Game game, IInputManager input)
    {
        // Back to slots
        if (input.IsActionPressed(InputAction.MenuLeft))
        {
            _activeColumn = Column.Slots;
            return;
        }

        // Keyboard navigation
        if (input.IsActionPressed(InputAction.MenuUp))
        {
            if (_availableParts.Length > 0)
                _selectedPart = (_selectedPart - 1 + _availableParts.Length) % _availableParts.Length;
        }
        if (input.IsActionPressed(InputAction.MenuDown))
        {
            if (_availableParts.Length > 0)
                _selectedPart = (_selectedPart + 1) % _availableParts.Length;
        }
        if (input.IsActionPressed(InputAction.MenuConfirm))
            TryEquipOrBuy(game);
        if (input.IsActionPressed(InputAction.MenuSecondaryAction))
            TrySellPart(game);

        // Mouse: hover to select part, click to equip/buy
        float rightX = PanelX + 300 + 10;
        float rightW = PanelWidth - 300 - 25;
        float partStartY = ContentY + 30;
        float mx = input.MouseX, my = input.MouseY;

        for (int i = 0; i < _availableParts.Length; i++)
        {
            float itemY = partStartY + i * 65;
            if (itemY + 65 > PanelY + PanelHeight - 60) break;

            if (mx >= rightX && mx <= rightX + rightW && my >= itemY && my < itemY + 65)
            {
                _selectedPart = i;
                if (input.IsMousePressed(MouseButton.Left))
                    TryEquipOrBuy(game);
                break;
            }
        }
    }

    // ── Content rendering ──

    protected override void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        float leftW = 300;
        float rightX = panelX + leftW + 10;
        float rightW = panelW - leftW - 25;
        var player = game.Player;

        // Column separator
        renderer.DrawLineScreen(panelX + leftW + 5, contentY,
            panelX + leftW + 5, panelX + PanelHeight - 40, new Color3(40, 40, 70));

        // ── Left column: Slot list ──
        renderer.DrawTextScreen(panelX + 15, contentY, "EQUIPMENT SLOTS",
            new Color3(180, 180, 220), 1.8f);
        float slotY = contentY + 30;

        for (int i = 0; i < SlotCount; i++)
        {
            bool selected = i == _selectedSlot;
            bool active = selected && _activeColumn == Column.Slots;
            var equipped = GetEquippedPart(player, i);

            float itemH = 55;
            if (selected)
            {
                byte bgA = active ? (byte)180 : (byte)100;
                renderer.DrawRectScreen(panelX + 5, slotY - 3, leftW - 5, itemH,
                    new Color4(30, 40, 70, bgA));
            }

            string slotName = GetSlotName(i);
            byte nR = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nG = active ? (byte)255 : selected ? (byte)220 : (byte)150;
            byte nB = active ? (byte)200 : selected ? (byte)240 : (byte)180;

            string prefix = active ? "> " : "  ";
            renderer.DrawTextScreen(panelX + 15, slotY, $"{prefix}{slotName}",
                new Color3(nR, nG, nB), 1.8f);

            string partName = equipped?.Name ?? "(Empty)";
            byte pR = equipped?.Tier switch { 3 => 255, 2 => 100, _ => 130 };
            byte pG = equipped?.Tier switch { 3 => 180, 2 => 220, _ => 130 };
            byte pB = equipped?.Tier switch { 3 => 80, 2 => 255, _ => 150 };
            renderer.DrawTextScreen(panelX + 30, slotY + 22, partName,
                new Color3(pR, pG, pB), 1.5f);

            slotY += itemH;
        }

        // ── Right column: Available parts ──
        renderer.DrawTextScreen(rightX + 5, contentY,
            $"PARTS: {GetSlotName(_selectedSlot)}", new Color3(180, 180, 220), 1.8f);

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
                renderer.DrawRectScreen(rightX, partY - 3, rightW, itemH,
                    new Color4(30, 40, 70, 180));

            byte tr = selected ? (byte)255 : (byte)180;
            byte tg = selected ? (byte)255 : (byte)180;
            byte tb = selected ? (byte)200 : (byte)200;
            string tag = isEquipped ? " [EQUIPPED]" : inInventory ? " [OWNED]" : "";
            renderer.DrawTextScreen(rightX + 5, partY,
                $"{(selected ? "> " : "  ")}{part.Name}{tag}", new Color3(tr, tg, tb), 1.8f);

            renderer.DrawTextScreen(rightX + 20, partY + 22, part.Description,
                new Color3(130, 130, 150), 1.3f);

            if (isEquipped)
            {
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, "IN USE",
                    new Color3(100, 180, 255), 1.5f);
            }
            else if (owned)
            {
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, "FREE",
                    new Color3(100, 255, 100), 1.5f);
            }
            else if (part.BuyCost > 0)
            {
                bool canAfford = player.Credits >= part.BuyCost;
                renderer.DrawTextScreen(rightX + rightW - 130, partY + 2, $"{part.BuyCost} CR",
                    new Color3(
                        canAfford ? (byte)255 : (byte)255,
                        canAfford ? (byte)220 : (byte)80,
                        canAfford ? (byte)80 : (byte)80), 1.5f);
            }

            if (selected && !isEquipped && currentEquipped != null)
                RenderStatComparison(renderer, rightX + 20, partY + 38, part, currentEquipped);

            partY += itemH;
            if (partY > PanelY + PanelHeight - 60) break;
        }
    }

    // ── Private helpers ──

    private void TryEquipOrBuy(Game game)
    {
        if (_availableParts.Length == 0) return;

        var newPart = _availableParts[_selectedPart];
        var player = game.Player;
        var currentEquipped = GetEquippedPart(player, _selectedSlot);

        if (currentEquipped?.Id == newPart.Id)
        {
            SetStatus("ALREADY EQUIPPED");
            return;
        }

        if (IsPartOwned(player, newPart))
        {
            PerformEquip(player, _selectedSlot, newPart);
            SetStatus("EQUIPPED!");
        }
        else
        {
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

    private void RefreshAvailableParts()
    {
        _availableParts = GetAvailablePartsForSlot(_selectedSlot);
        _selectedPart = 0;
    }

    /// <summary>Helper for subclass stat comparison rendering.</summary>
    protected static void RenderStatDiffs(ISpriteRenderer renderer, float x, float y,
        List<StatDiff> diffs)
    {
        float cx = x;
        foreach (var (label, diff) in diffs)
        {
            string text = $"{label}{(diff > 0 ? "+" : "")}{diff:F0} ";
            renderer.DrawTextScreen(cx, y, text,
                new Color3(
                    diff > 0 ? (byte)80 : (byte)255,
                    diff > 0 ? (byte)255 : (byte)80,
                    (byte)80), 1.2f);
            cx += text.Length * 7;
        }
    }
}
