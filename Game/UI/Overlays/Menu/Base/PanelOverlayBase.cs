using SpaceExplorationGame.Core;
using Engine.Platform;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays.Menu.Base;

/// <summary>
/// Base class for panel-based overlays: centered panel with title, border, background dimming,
/// status messages, controls hint, Escape to close, and click-outside-to-close.
/// Subclasses only provide configuration and content rendering — no input handling.
/// </summary>
public abstract class PanelOverlayBase : OverlayBase
{
    private string? _statusMessage;
    private float _statusTimer;
    private bool _statusIsPositive;
    private IInputManager? _currentInput;
    private int _windowW = GameConfig.DefaultWindowWidth;
    private int _windowH = GameConfig.DefaultWindowHeight;

    protected IInputManager? CurrentInput => _currentInput;

    // ── Configuration (override in subclasses) ──

    /// <summary>Title displayed at the top of the panel.</summary>
    protected abstract string Title { get; }

    /// <summary>Title text color.</summary>
    protected virtual Color3 TitleColor => new(200, 200, 255);

    /// <summary>Panel width in pixels.</summary>
    protected abstract float PanelWidth { get; }

    /// <summary>Panel height in pixels.</summary>
    protected abstract float PanelHeight { get; }

    /// <summary>Whether to show the player's credit balance in the title bar.</summary>
    protected virtual bool ShowCredits => false;

    /// <summary>Controls hint text at the bottom of the panel. Null to hide.</summary>
    protected virtual string? ControlsHint => null;

    /// <summary>Whether clicking outside the panel closes the overlay.</summary>
    protected virtual bool CloseOnClickOutside => true;

    /// <summary>Background dimming alpha (0-255).</summary>
    protected virtual byte DimAlpha => 180;

    // ── Panel geometry (computed, shared by rendering and input) ──

    /// <summary>Left edge of the panel on screen.</summary>
    protected float PanelX => _windowW / 2f - PanelWidth / 2f;

    /// <summary>Top edge of the panel on screen. Override for non-centered panels.</summary>
    protected virtual float PanelY => _windowH / 2f - PanelHeight / 2f;

    /// <summary>Bottom edge of the panel on screen.</summary>
    protected float PanelBottom => PanelY + PanelHeight;

    /// <summary>Y position where content starts (after title + separator).</summary>
    protected float ContentY => PanelY + 55;

    /// <summary>Available height for content (between title and controls hint).</summary>
    protected float ContentHeight => PanelHeight - 55 - 35;

    // ── Open / Close ──

    /// <summary>Open the overlay and reset status message state.</summary>
    public virtual void Open()
    {
        IsOpen = true;
        _statusMessage = null;
        _statusTimer = 0;
    }

    public override void Close()
    {
        IsOpen = false;
    }

    // ── Status message ──

    /// <summary>Show a status message for the given duration.</summary>
    protected void SetStatus(string msg, float duration = 3f)
    {
        _statusMessage = msg;
        _statusTimer = duration;
        _statusIsPositive = msg.StartsWith("EQUIPPED") || msg.StartsWith("PURCHASED") ||
                            msg.StartsWith("SOLD") || msg.StartsWith("TRACKING") ||
                            msg.StartsWith("MISSION") || msg.StartsWith("SWITCHED");
    }

    /// <summary>Current status message text, or null.</summary>
    protected string? StatusMessage => _statusMessage;

    // ── Input handling ──

    public sealed override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;
        _windowW = game.SpriteRenderer.WindowWidth;
        _windowH = game.SpriteRenderer.WindowHeight;

        // Sub-overlay input takes priority
        if (ProcessSubOverlayInput(game)) return true;

        var input = game.Input;

        // Escape to close (or back)
        if (input.IsActionPressed(InputAction.MenuBack))
        {
            OnEscapePressed();
            return true;
        }

        // Click outside panel to close
        if (CloseOnClickOutside && input.IsMouseReleased(MouseButton.Left) &&
            !IsPointInPanel(input.MouseX, input.MouseY))
        {
            Close();
            return true;
        }

        // Delegate to specialized input processing (override in ListPanelOverlay, MenuPanelOverlay, etc.)
        ProcessInput(game, input);

        return true;
    }

    /// <summary>
    /// Override point for specialized input processing.
    /// Default: Enter/E triggers OnConfirmAction (for simple action-based panels).
    /// ListPanelOverlay and MenuPanelOverlay override this entirely.
    /// </summary>
    protected virtual void ProcessInput(Game game, IInputManager input)
    {
        if (input.IsActionPressed(InputAction.MenuConfirm))
            OnConfirmAction(game);
    }

    /// <summary>Called when Escape is pressed. Default: close. Override for back navigation.</summary>
    protected virtual void OnEscapePressed() => Close();

    /// <summary>Called when Enter/E is pressed (simple action panels). Override for action logic.</summary>
    protected virtual void OnConfirmAction(Game game) { }

    /// <summary>Override to process sub-overlay input before own input. Return true if consumed.</summary>
    protected virtual bool ProcessSubOverlayInput(Game game) => false;

    // ── Update ──

    public sealed override void Update(Game game)
    {
        if (!IsOpen) return;

        // Status message timer
        if (_statusTimer > 0)
        {
            _statusTimer -= game.DeltaTime;
            if (_statusTimer <= 0) _statusMessage = null;
        }

        // Sub-overlay updates
        UpdateSubOverlays(game);

        // Additional update logic
        OnUpdate(game);
    }

    /// <summary>Override to update sub-overlays.</summary>
    protected virtual void UpdateSubOverlays(Game game) { }

    /// <summary>Override for additional update logic beyond status timer.</summary>
    protected virtual void OnUpdate(Game game) { }

    // ── Rendering ──

    public sealed override void Render(Game game)
    {
        if (!IsOpen) return;

        _currentInput = game.Input;
        _windowW = game.SpriteRenderer.WindowWidth;
        _windowH = game.SpriteRenderer.WindowHeight;

        var renderer = game.SpriteRenderer;

        // Dim background
        renderer.DrawRectScreen(0, 0, _windowW, _windowH,
            new Color4(0, 0, 0, DimAlpha));

        float px = PanelX, py = PanelY, pw = PanelWidth, ph = PanelHeight;

        // Panel frame with sci-fi styling
        DrawFrame(renderer, px, py, pw, ph, 245);

        // Title
        renderer.DrawTextScreen(px + 15, py + 10, Title, TitleColor, 2.5f);

        // Credits (if enabled)
        if (ShowCredits)
            renderer.DrawTextScreen(px + pw - 200, py + 10,
                $"CREDITS: {game.Player.Credits}", new Color3(255, 220, 80), 2f);

        // Separator
        renderer.DrawLineScreen(px + 15, py + 45, px + pw - 15, py + 45, new Color3(60, 80, 140));

        // Content (subclass renders here)
        RenderPanelContent(game, renderer, px, ContentY, pw, ContentHeight);

        // Status message
        if (_statusMessage != null)
        {
            renderer.DrawRectScreen(px + 15, py + ph - 55, pw - 30, 22, new Color4(0, 0, 0, 200));
            renderer.DrawTextScreen(px + 20, py + ph - 53, _statusMessage,
                new Color3(
                    _statusIsPositive ? (byte)100 : (byte)255,
                    _statusIsPositive ? (byte)255 : (byte)150,
                    _statusIsPositive ? (byte)100 : (byte)80), 1.5f);
        }

        // Controls hint
        if (ControlsHint != null)
            renderer.DrawTextScreen(px + 15, py + ph - 28, ControlsHint,
                new Color3(100, 100, 130), 1.3f);

        // Sub-overlays on top
        RenderSubOverlays(game);
    }

    /// <summary>Render the panel content. Called between the title/separator and the controls hint.</summary>
    protected abstract void RenderPanelContent(Game game, ISpriteRenderer renderer,
        float panelX, float contentY, float panelW, float contentH);

    /// <summary>Override to render sub-overlays on top of the main panel.</summary>
    protected virtual void RenderSubOverlays(Game game) { }

    // ── Utilities ──

    /// <summary>Test whether a screen point is inside the panel rectangle.</summary>
    protected bool IsPointInPanel(float mx, float my) =>
        mx >= PanelX && mx <= PanelX + PanelWidth &&
        my >= PanelY && my <= PanelY + PanelHeight;
}
