using SpaceExplorationGame.Core;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>
/// Base class for panel overlays with a navigable list of items.
/// Handles Up/Down/W/S navigation, Enter/E confirm, X/Delete secondary action,
/// Left/Right tab navigation, and mouse hover/click on items.
/// Subclasses provide item data, rendering, and action callbacks — no input handling.
/// </summary>
public abstract class ListPanelOverlay : PanelOverlayBase
{
    private int _selectedIndex;

    // ── List configuration ──

    /// <summary>Total number of items in the current list.</summary>
    protected abstract int ItemCount { get; }

    /// <summary>Height of each list item in pixels.</summary>
    protected virtual float ItemHeight => 65f;

    /// <summary>Y offset from ContentY where the list starts (for headers above the list).</summary>
    protected virtual float ListOffsetY => 0f;

    /// <summary>X offset from PanelX for the clickable list area.</summary>
    protected virtual float ListOffsetX => 0f;

    /// <summary>Width override for the clickable list area. 0 = full panel width.</summary>
    protected virtual float ListWidth => 0f;

    // ── Selection state ──

    /// <summary>Currently selected item index.</summary>
    protected int SelectedIndex
    {
        get => _selectedIndex;
        set => _selectedIndex = ItemCount > 0 ? Math.Clamp(value, 0, ItemCount - 1) : 0;
    }

    // ── Open ──

    public override void Open()
    {
        base.Open();
        _selectedIndex = 0;
    }

    // ── Input (all handled here, child classes provide callbacks only) ──

    protected override void ProcessInput(Game game, InputManager input)
    {
        // Tab / column navigation (always available, even with empty lists)
        if (input.IsActionPressed(InputAction.MenuLeft))
            OnNavigateLeft(game);

        if (input.IsActionPressed(InputAction.MenuRight))
            OnNavigateRight(game);

        int count = ItemCount;
        if (count <= 0) return;

        // Up/Down navigation
        if (input.IsActionPressed(InputAction.MenuUp))
            _selectedIndex = (_selectedIndex - 1 + count) % count;

        if (input.IsActionPressed(InputAction.MenuDown))
            _selectedIndex = (_selectedIndex + 1) % count;

        // Confirm selected item
        if (input.IsActionPressed(InputAction.MenuConfirm))
            OnItemConfirmed(game, _selectedIndex);

        // Secondary action (sell, abandon, etc.)
        if (input.IsActionPressed(InputAction.MenuSecondaryAction))
            OnItemSecondary(game, _selectedIndex);

        // Mouse hover + click on list items
        HandleListMouseInput(game, input);
    }

    private void HandleListMouseInput(Game game, InputManager input)
    {
        int count = ItemCount;
        if (count <= 0) return;

        float mx = input.MouseX;
        float my = input.MouseY;

        float listX = PanelX + ListOffsetX;
        float listW = ListWidth > 0 ? ListWidth : PanelWidth;
        float listY = ContentY + ListOffsetY;

        for (int i = 0; i < count; i++)
        {
            float itemY = listY + i * ItemHeight;
            float itemBottom = itemY + ItemHeight;

            // Don't check items that overflow the panel
            if (itemBottom > PanelY + PanelHeight - 35) break;

            if (mx >= listX && mx <= listX + listW &&
                my >= itemY && my < itemBottom)
            {
                _selectedIndex = i;

                if (input.IsMousePressed(1))
                    OnItemConfirmed(game, i);

                break;
            }
        }
    }

    // ── Callbacks (override in leaf classes) ──

    /// <summary>Called when the user confirms the selected item (Enter/E or mouse click).</summary>
    protected virtual void OnItemConfirmed(Game game, int index) { }

    /// <summary>Called when the user performs secondary action on an item (X/Delete).</summary>
    protected virtual void OnItemSecondary(Game game, int index) { }

    /// <summary>Called when the user navigates left (for tab switching). Default: no-op.</summary>
    protected virtual void OnNavigateLeft(Game game) { }

    /// <summary>Called when the user navigates right (for tab switching). Default: no-op.</summary>
    protected virtual void OnNavigateRight(Game game) { }

    /// <summary>Clamp selection after item count changes (e.g. after selling/removing).</summary>
    protected void ClampSelection()
    {
        int count = ItemCount;
        if (_selectedIndex >= count)
            _selectedIndex = Math.Max(0, count - 1);
    }
}
