using SpaceExplorationGame.Core;
using SpaceExplorationGame.Platform;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu.Base;

/// <summary>
/// Base class for panel overlays driven by a <see cref="MenuWidget{T}"/>.
/// Handles menu keyboard/mouse input, sub-overlay delegation, and rendering.
/// Subclasses provide menu options, content rendering, and action callbacks — no input handling.
/// </summary>
public abstract class MenuPanelOverlayBase<T> : PanelOverlayBase where T : struct, Enum
{
    private readonly List<OverlayBase> _subOverlays = [];

    // ── Menu ──

    /// <summary>The menu widget. Subclasses should set this in constructor or Open().</summary>
    protected MenuWidget<T> Menu { get; set; } = null!;

    // ── Default panel height (auto-calculated from menu) ──

    /// <summary>Panel height auto-calculated from title (55px) + menu items + bottom padding.
    /// Override in subclasses that render additional content beyond the menu.</summary>
    protected override float PanelHeight => 55 + Menu.TotalHeight + BottomPadding;

    /// <summary>
    /// Bottom padding below the menu, for spacing or additional content (e.g. controls hint).
    /// </summary>
    protected virtual float BottomPadding => ControlsHint != null ? 35 : 0;

    // ── Menu layout (subclass can override for positioning) ──

    /// <summary>Left edge of the menu area in screen coordinates.</summary>
    protected virtual float MenuX => PanelX + 10;

    /// <summary>Top of the menu area in screen coordinates.</summary>
    protected virtual float MenuY => ContentY;

    /// <summary>Width of the menu area (for mouse hit testing and highlight rects).</summary>
    protected virtual float MenuWidth => PanelWidth - 20;

    // ── Sub-overlay management ──

    /// <summary>Register a sub-overlay for automatic input/update/render delegation.</summary>
    protected void RegisterSubOverlay(OverlayBase overlay) => _subOverlays.Add(overlay);

    /// <summary>All registered sub-overlays.</summary>
    protected IReadOnlyList<OverlayBase> SubOverlays => _subOverlays;

    protected override bool ProcessSubOverlayInput(Game game)
    {
        foreach (var sub in _subOverlays)
        {
            if (sub.UpdateInput(game))
                return true;
        }
        return false;
    }

    protected override void UpdateSubOverlays(Game game)
    {
        foreach (var sub in _subOverlays)
            sub.Update(game);
    }

    protected override void RenderSubOverlays(Game game)
    {
        foreach (var sub in _subOverlays)
            sub.Render(game);
    }

    // ── Input ──

    protected override void ProcessInput(Game game, InputManager input)
    {
        var confirmed = Menu.Update(input, MenuX, MenuY, MenuWidth);
        if (confirmed.HasValue)
            OnOptionSelected(game, confirmed.Value);
    }

    /// <summary>Called when a menu option is confirmed (Enter/E or mouse click).</summary>
    protected abstract void OnOptionSelected(Game game, T option);

    // ── Default content rendering ──

    /// <summary>Renders the menu widget. Override for additional content beyond the menu.</summary>
    protected sealed override void RenderPanelContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    {
        Menu.Render(renderer, MenuX, MenuY, MenuWidth, PanelBottom);

        RenderAdditionalContent(game, renderer, panelX, contentY, panelW, contentH);
    }

    // ── Custom rendering (for unique layouts) ──
    protected virtual void RenderAdditionalContent(Game game, SpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH)
    { }
}
