using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering.Base;

namespace SpaceExplorationGame.UI.Overlays.Map.Base;

/// <summary>
/// Abstract base class for map panels displayed inside a <see cref="MapOverlayBase"/>.
/// Provides shared camera, panning, zooming, WASD movement, and utility rendering helpers.
/// </summary>
public abstract class MapPanelBase
{
    protected readonly Camera Camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight, 0.01f, 10f);
    protected bool IsPanning;
    protected Vector2 LastMouseScreen;
    protected const float DoubleClickTime = 0.4f;

    /// <summary>Called when the panel wants the container overlay to close.</summary>
    public Action<Game>? OnRequestClose { get; set; }

    // ── Layout (set by container via SetLayout) ──
    protected float MapX, MapY, MapW, MapH;
    protected float IpX, IpY, IpH;
    protected float InfoPanelW;

    /// <summary>Set layout positions computed by the container overlay.</summary>
    public void SetLayout(float mapX, float mapY, float mapW, float mapH,
                          float ipX, float ipY, float ipH, float infoPanelW)
    {
        MapX = mapX; MapY = mapY; MapW = mapW; MapH = mapH;
        IpX = ipX; IpY = ipY; IpH = ipH; InfoPanelW = infoPanelW;

        Camera.ViewportWidth = (int)mapW;
        Camera.ViewportHeight = (int)mapH;
        Camera.ViewportOffsetX = mapX;
        Camera.ViewportOffsetY = mapY;
    }

    /// <summary>Called when the panel becomes the active view.</summary>
    public abstract void Open(Game game);

    /// <summary>Called when the overlay is closed; clear data.</summary>
    public abstract void Close(Game game);

    /// <summary>Configure camera zoom limits and initial position for this panel.</summary>
    public abstract void SetupCamera(Game game);

    /// <summary>Handle panel-specific input (zoom, pan, hover, click). Returns true if input was consumed.</summary>
    public abstract bool UpdateInput(Game game, float dt);

    /// <summary>Render the map content inside the clipped map area.</summary>
    public abstract void RenderContent(Game game, SpriteRenderer renderer);

    /// <summary>Render the info panel beside the map.</summary>
    public abstract void RenderInfoPanel(Game game, SpriteRenderer renderer);

    /// <summary>WASD / arrow key camera movement (shared for all panels).</summary>
    public virtual void Update(Game game, float dt)
    {
        var input = game.Input;
        float camSpeed = 500f / Camera.Zoom;
        Vector2 moveDir = input.GetActionAxisDirection(InputActionAxis.Movement);
        Camera.Position += moveDir * camSpeed * dt;
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED INPUT HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Apply mouse wheel zoom (zoom-to-cursor) and drag panning.</summary>
    protected void HandleZoomAndPan(InputManager input, Vector2 currentMouse)
    {
        // Zoom to cursor
        if (input.MouseWheelY != 0)
        {
            var worldBeforeZoom = Camera.ScreenToWorld(currentMouse);
            Camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            Camera.ClampZoom();
            var worldAfterZoom = Camera.ScreenToWorld(currentMouse);
            Camera.Position += worldBeforeZoom - worldAfterZoom;
        }

        // Drag pan
        if (input.IsMousePressed(1)) { LastMouseScreen = currentMouse; IsPanning = false; }
        if (input.IsMouseDown(1))
        {
            Vector2 delta = currentMouse - LastMouseScreen;
            if (delta.LengthSquared() > 4f)
            {
                IsPanning = true;
                Camera.Position -= delta / Camera.Zoom;
                LastMouseScreen = currentMouse;
            }
        }
    }

    /// <summary>Check if mouse position is inside the map area.</summary>
    protected bool IsMouseInMap(Vector2 mouse) =>
        mouse.X >= MapX && mouse.X < MapX + MapW &&
        mouse.Y >= MapY && mouse.Y < MapY + MapH;

    protected Vector2 GetMapScreenCenter() => new(MapX + MapW * 0.5f, MapY + MapH * 0.5f);

    protected void HandleGamepadTriggerZoom(InputManager input, float dt)
    {
        if (input.ActiveInputMethod != InputMethod.Gamepad)
            return;

        bool zoomOut = input.IsActionDown(InputAction.MapZoomOut);
        bool zoomIn = input.IsActionDown(InputAction.MapZoomIn);
        if (!zoomIn && !zoomOut)
            return;

        Vector2 zoomCenter = GetMapScreenCenter();
        var worldBeforeZoom = Camera.ScreenToWorld(zoomCenter);

        const float triggerZoomPerSecond = 1.8f;
        float zoomDelta = 0f;
        if (zoomIn) zoomDelta += triggerZoomPerSecond * dt;
        if (zoomOut) zoomDelta -= triggerZoomPerSecond * dt;

        Camera.Zoom *= 1f + zoomDelta;
        Camera.ClampZoom();

        var worldAfterZoom = Camera.ScreenToWorld(zoomCenter);
        Camera.Position += worldBeforeZoom - worldAfterZoom;
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED RENDER HELPERS
    // ─────────────────────────────────────────────────────────────

    /// <summary>Draw animated targeting brackets around an object.</summary>
    protected static void DrawTargetBrackets(SpriteRenderer renderer, Camera camera,
        Vector2 worldPos, float radius, Game game)
    {
        float pulse = (float)(0.7 + 0.3 * Math.Sin(game.GlobalTime * 3.0));
        float r = radius + 2f * pulse;
        var color = new Color4(255, 200, 50, (byte)(150 + (int)(pulse * 105)));
        renderer.DrawCircle(camera, worldPos, r, color);
        renderer.DrawCircle(camera, worldPos, r + 1.5f, new Color4(255, 200, 50, (byte)(60 + (int)(pulse * 60))));
    }

    /// <summary>Draw a mission diamond icon above an object.</summary>
    protected static void DrawMissionDiamond(SpriteRenderer renderer, Camera camera,
        Vector2 objectPos, float objectRadius, Color3 color, byte alpha, string? label = null)
    {
        var iconPos = objectPos + new Vector2(0, -(objectRadius + 16));
        float diamondSize = 6f;
        var screenIcon = camera.WorldToScreen(iconPos);
        if (screenIcon.X >= -20 && screenIcon.X < GameConfig.WindowWidth + 20 &&
            screenIcon.Y >= -20 && screenIcon.Y < GameConfig.WindowHeight + 20)
        {
            float ds = diamondSize * Math.Max(1f, camera.Zoom * 0.5f);
            var c = new Color4(color.R, color.G, color.B, alpha);
            // Filled diamond
            renderer.DrawLineScreen(screenIcon.X, screenIcon.Y - ds, screenIcon.X + ds, screenIcon.Y, c);
            renderer.DrawLineScreen(screenIcon.X + ds, screenIcon.Y, screenIcon.X, screenIcon.Y + ds, c);
            renderer.DrawLineScreen(screenIcon.X, screenIcon.Y + ds, screenIcon.X - ds, screenIcon.Y, c);
            renderer.DrawLineScreen(screenIcon.X - ds, screenIcon.Y, screenIcon.X, screenIcon.Y - ds, c);
            // "!" inside diamond
            renderer.DrawTextScreen(screenIcon.X - 3, screenIcon.Y - ds + 2, "!",
                new Color3(color.R, color.G, color.B), 1.5f);
            // Label above diamond
            if (label != null)
            {
                float labelW = renderer.MeasureText(label, 1.2f);
                renderer.DrawTextScreen(screenIcon.X - labelW / 2f, screenIcon.Y - ds - 14, label,
                    new Color3(color.R, color.G, color.B), 1.2f);
            }
        }
    }

    /// <summary>Render an info panel header strip with centered title.</summary>
    protected void RenderInfoPanelHeader(SpriteRenderer renderer, string title)
    {
        renderer.DrawRectScreen(IpX, IpY, InfoPanelW, 30, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(IpX, IpY + 29, InfoPanelW, 1, new Color4(60, 80, 140, 200));
        float headerW = renderer.MeasureText(title, 1.8f);
        renderer.DrawTextScreen(IpX + InfoPanelW / 2f - headerW / 2f, IpY + 6, title, new Color3(140, 170, 220), 1.8f);
    }

    protected void RenderCenterSelectionReticle(SpriteRenderer renderer, Color4 color)
    {
        Vector2 c = GetMapScreenCenter();
        const float outer = 9f;
        const float inner = 3f;

        renderer.DrawLineScreen(c.X - outer, c.Y, c.X - inner, c.Y, color);
        renderer.DrawLineScreen(c.X + inner, c.Y, c.X + outer, c.Y, color);
        renderer.DrawLineScreen(c.X, c.Y - outer, c.X, c.Y - inner, color);
        renderer.DrawLineScreen(c.X, c.Y + inner, c.X, c.Y + outer, color);
    }
}
