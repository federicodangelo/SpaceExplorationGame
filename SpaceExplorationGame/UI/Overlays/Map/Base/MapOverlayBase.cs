using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays.Map.Base;

/// <summary>
/// Abstract base class for overlays that display a map panel with an info panel beside it.
/// Provides shared layout computation, dark background, frame/border rendering, clip-rect
/// management, and delegates content rendering to the active <see cref="MapPanelBase"/> panel.
/// </summary>
public abstract class MapOverlayBase : OverlayBase
{
    // ── Layout (virtual properties so subclasses can customise dimensions) ──

    /// <summary>Width of the map content area in pixels.</summary>
    protected virtual float MapContentWidth => 800f;

    /// <summary>Height of the map content area in pixels.</summary>
    protected virtual float MapContentHeight => 700f;

    /// <summary>Padding between the frame edge and the map content.</summary>
    protected virtual float MapContentPad => 12f;

    /// <summary>Height of the header strip at the top of the frame.</summary>
    protected virtual float HeaderHeight => 30f;

    /// <summary>Width of the info panel to the right of the map.</summary>
    protected virtual float InfoPanelWidthValue => 280f;

    /// <summary>Gap between the map frame and the info panel.</summary>
    protected virtual float InfoPanelGapValue => 20f;

    // ── Computed layout positions ──
    protected float FrameX, FrameY, FrameW, FrameH;
    protected float MapAreaX, MapAreaY;
    protected float IpX, IpY, IpH;

    // ─────────────────────────────────────────────────────────────
    //  LAYOUT
    // ─────────────────────────────────────────────────────────────

    /// <summary>Compute frame, map-area, and info-panel positions from virtual dimension properties.</summary>
    protected void ComputeLayout()
    {
        float mapW = MapContentWidth;
        float mapH = MapContentHeight;
        float pad = MapContentPad;
        float headerH = HeaderHeight;
        float ipW = InfoPanelWidthValue;
        float ipGap = InfoPanelGapValue;

        FrameW = mapW + pad * 2;
        FrameH = mapH + pad * 2 + headerH;
        float totalW = FrameW + ipGap + ipW;
        FrameX = (GameConfig.WindowWidth - totalW) / 2f;
        FrameY = (GameConfig.WindowHeight - FrameH) / 2f;
        MapAreaX = FrameX + pad;
        MapAreaY = FrameY + pad + headerH;
        IpX = FrameX + FrameW + ipGap;
        IpY = FrameY;
        IpH = FrameH;
    }

    /// <summary>Push layout positions into a panel so it knows where to render.</summary>
    protected void ApplyLayoutToPanel(MapPanelBase panel)
    {
        panel.SetLayout(MapAreaX, MapAreaY, MapContentWidth, MapContentHeight,
                        IpX, IpY, IpH, InfoPanelWidthValue);
    }

    // ─────────────────────────────────────────────────────────────
    //  ABSTRACT / VIRTUAL HOOKS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Return the panel that is currently active (receives input/update/render).</summary>
    protected abstract MapPanelBase GetActivePanel();

    /// <summary>Render the header area at the top of the frame.</summary>
    protected abstract void RenderHeader(SpriteRenderer renderer);

    /// <summary>Render additional HUD elements outside the frame (e.g., title bar, prompts).</summary>
    protected virtual void RenderHud(Game game, SpriteRenderer renderer) { }

    // ─────────────────────────────────────────────────────────────
    //  UPDATE
    // ─────────────────────────────────────────────────────────────

    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;
        GetActivePanel().Update(game, dt);
    }

    // ─────────────────────────────────────────────────────────────
    //  RENDER
    // ─────────────────────────────────────────────────────────────

    public override void Render(Game game)
    {
        if (!IsOpen) return;
        var renderer = game.SpriteRenderer;
        var panel = GetActivePanel();

        // Semi-transparent dark overlay
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, new Color4(0, 0, 0, 180));

        // Frame + header
        DrawFrame(renderer, FrameX, FrameY, FrameW, FrameH, 230);
        RenderHeader(renderer);

        // Inner map border
        renderer.DrawRectScreen(MapAreaX - 1, MapAreaY - 1, MapContentWidth + 2, MapContentHeight + 2, new Color4(50, 65, 110, 180));

        // Map content (clipped to map area)
        renderer.SetClipRect(MapAreaX, MapAreaY, MapContentWidth, MapContentHeight);
        panel.RenderContent(game, renderer);
        renderer.ClearClipRect();

        // Info panel
        DrawFrame(renderer, IpX, IpY, InfoPanelWidthValue, IpH, 220);
        panel.RenderInfoPanel(game, renderer);

        // Additional HUD (title bars, prompts, etc.)
        RenderHud(game, renderer);
    }
}
