using Engine.Platform;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Menu.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu;

/// <summary>Actions in the save game list overlay.</summary>
public enum SaveGameAction
{
    None = -1,
    Back,
    // Slots 0..9 — mapped dynamically from the saves list
    Slot0, Slot1, Slot2, Slot3, Slot4, Slot5, Slot6, Slot7, Slot8, Slot9,
}

/// <summary>
/// Sub-overlay that lists all available save games and lets the player pick one to load or delete.
/// </summary>
public class SaveGameListOverlay : MenuPanelOverlayBase<SaveGameAction>
{
    private IReadOnlyList<SaveGameInfo> _saves = [];

    /// <summary>When set, the player chose a save to load.</summary>
    public string? SelectedPlayerId { get; private set; }

    /// <summary>When set, the player chose a save to delete.</summary>
    public string? DeletePlayerId { get; private set; }

    /// <summary>Clear consumed actions after handling.</summary>
    public void ClearActions()
    {
        SelectedPlayerId = null;
        DeletePlayerId = null;
    }

    // Track which slot is being confirmed for deletion
    private int _confirmDeleteSlot = -1;

    protected override string Title => "SAVED GAMES";
    protected override Color3 TitleColor => new(180, 200, 255);
    protected override float PanelWidth => 800;
    protected override float BottomPadding => base.BottomPadding + 10;
    protected override string? ControlsHint => _confirmDeleteSlot >= 0
        ? "PRESS AGAIN TO CONFIRM DELETE  —  ANY OTHER KEY TO CANCEL"
        : $"{CurrentInput?.GetActionHelpText(InputAction.MenuConfirm).ToUpper() ?? "ENTER"}: LOAD  —  {CurrentInput?.GetActionHelpText(InputAction.MenuSecondaryAction).ToUpper() ?? "DEL"}: DELETE  —  {CurrentInput?.GetActionHelpText(InputAction.MenuBack).ToUpper() ?? "ESC"}: BACK";

    public void Open(IReadOnlyList<SaveGameInfo> saves)
    {
        _saves = saves;
        SelectedPlayerId = null;
        DeletePlayerId = null;
        _confirmDeleteSlot = -1;
        RebuildMenu();
        base.Open();
    }

    private void RebuildMenu()
    {
        var options = new List<MenuOption<SaveGameAction>>();

        for (int i = 0; i < _saves.Count && i < 10; i++)
        {
            var save = _saves[i];
            var action = (SaveGameAction)((int)SaveGameAction.Slot0 + i);
            string timeAgo = FormatTimeAgo(save.SavedAt);
            string label = $"{save.PlayerName} — {save.LocationDescription ?? "Unknown"}";
            string desc = $"Saved {timeAgo}";
            options.Add(new(action, label, desc));
        }

        options.Add(new(SaveGameAction.Back, "BACK", "Return to main menu"));

        Menu = new MenuWidget<SaveGameAction>([.. options])
        {
            CenterAlign = true,
            ItemHeight = 50f,
            SelectedScale = 2.2f,
            NormalScale = 1.8f,
            SelectedColor = new Color3(220, 240, 255),
            NormalColor = new Color3(140, 140, 160),
            HighlightBg = new Color3(40, 60, 120),
            HighlightAlpha = 180,
            DescriptionScale = 1.5f,
            DescriptionColor = new Color3(160, 160, 180)
        };

        Menu.SelectedIndex = 0;
    }

    protected override void OnEscapePressed() => Close();

    protected override void OnOptionSelected(Game game, SaveGameAction option)
    {
        if (option == SaveGameAction.Back)
        {
            Close();
            return;
        }

        int slotIndex = (int)option - (int)SaveGameAction.Slot0;
        if (slotIndex < 0 || slotIndex >= _saves.Count) return;

        // If we were confirming a delete on this slot, cancel it — this is a load confirm instead
        _confirmDeleteSlot = -1;
        SelectedPlayerId = _saves[slotIndex].PlayerId;
    }

    protected override void ProcessInput(Game game, IInputManager input)
    {
        // Handle delete via MenuSecondaryAction on a save slot
        if (input.IsActionPressed(InputAction.MenuSecondaryAction))
        {
            var selected = Menu.SelectedValue;
            int slotIndex = (int)selected - (int)SaveGameAction.Slot0;

            if (slotIndex >= 0 && slotIndex < _saves.Count)
            {
                if (_confirmDeleteSlot == slotIndex)
                {
                    // Second press — confirm delete
                    DeletePlayerId = _saves[slotIndex].PlayerId;
                    _confirmDeleteSlot = -1;
                    Close();
                    return;
                }
                else
                {
                    // First press — enter confirm mode
                    _confirmDeleteSlot = slotIndex;
                    return;
                }
            }
        }

        // Any navigation cancels delete confirmation
        if (input.IsActionPressed(InputAction.MenuUp) || input.IsActionPressed(InputAction.MenuDown))
            _confirmDeleteSlot = -1;

        base.ProcessInput(game, input);
    }

    protected override void RenderAdditionalContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        // Separator before BACK
        if (_saves.Count > 0)
        {
            float sepY = MenuY + _saves.Count * Menu.ItemHeight;
            renderer.DrawLineScreen(panelX + 15, sepY, panelX + panelW - 15, sepY, new Color3(60, 80, 140));
        }

        // Delete confirmation highlight
        if (_confirmDeleteSlot >= 0 && _confirmDeleteSlot < _saves.Count)
        {
            float itemY = MenuY + _confirmDeleteSlot * Menu.ItemHeight;
            renderer.DrawRectScreen(panelX + 5, itemY, panelW - 10, Menu.ItemHeight, new Color4(120, 30, 30, 100));
        }
    }

    private static string FormatTimeAgo(DateTime savedAt)
    {
        var elapsed = DateTime.UtcNow - savedAt;
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}d ago";
        return savedAt.ToString("yyyy-MM-dd");
    }
}
