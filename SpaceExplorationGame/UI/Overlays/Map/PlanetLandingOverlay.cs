using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>
/// Orbital landing site selection overlay: delegates terrain map rendering, cursor logic,
/// and landing to <see cref="PlanetLandingPanel"/>. This container handles frame/header/HUD
/// and inherits shared layout from <see cref="MapOverlayBase"/>.
/// </summary>
public class PlanetLandingOverlay : MapOverlayBase
{
    private readonly PlanetLandingPanel _panel;

    public Action<Game, LandingSelectionRequest>? OnLandingConfirmed
    {
        get => _panel.OnLandingConfirmed;
        set => _panel.OnLandingConfirmed = value;
    }

    // ── Layout overrides (700×700 map, 260px info panel) ──
    protected override float MapContentWidth => 700f;
    protected override float MapContentHeight => 700f;
    protected override float InfoPanelWidthValue => 260f;

    public PlanetLandingOverlay(TextureManager textures)
    {
        _panel = new PlanetLandingPanel(textures);
        _panel.OnRequestClose = game => Close();
    }

    protected override MapPanelBase GetActivePanel() => _panel;

    // ─────────────────────────────────────────────────────────────
    //  OPEN / CLOSE / CLEANUP
    // ─────────────────────────────────────────────────────────────

    public void Open(StarSystemData starSystem, PlanetData planet, Game game,
        bool isMoon = false, int moonPlanetIndex = -1, int moonIndex = -1)
    {
        IsOpen = true;

        ComputeLayout();
        ApplyLayoutToPanel(_panel);

        _panel.OpenWithPlanet(game, starSystem, planet, isMoon, moonPlanetIndex, moonIndex);
        _panel.SetupCamera(game);
    }

    /// <summary>Destroy the cached terrain texture. Call when leaving the solar system.</summary>
    public void Cleanup() => _panel.Cleanup();

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        // Escape cancels landing
        if (game.Input.IsActionPressed(InputAction.MenuBack))
        {
            Cleanup();
            Close();
            return true;
        }

        return _panel.UpdateInput(game);
    }

    // ─────────────────────────────────────────────────────────────
    //  HEADER & HUD
    // ─────────────────────────────────────────────────────────────

    protected override void RenderHeader(SpriteRenderer renderer)
    {
        // Simple title header strip
        string title = $"SURFACE SCAN - {_panel.PlanetName.ToUpper()}";
        renderer.DrawRectScreen(FrameX, FrameY, FrameW, HeaderHeight, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(FrameX, FrameY + HeaderHeight - 1, FrameW, 1, new Color4(60, 80, 140, 200));
        float labelW = renderer.MeasureText(title, 1.8f);
        renderer.DrawTextScreen(FrameX + FrameW / 2f - labelW / 2f, FrameY + 6, title, new Color3(140, 170, 220), 1.8f);
    }

    protected override void RenderHud(Game game, SpriteRenderer renderer)
    {
        const float hudMargin = 5f;

        // Title bar (centered above frame)
        string title = $"ORBITAL VIEW - {_panel.PlanetName.ToUpper()}";
        float titleW = renderer.MeasureText(title, 2.5f);
        float titleBgW = titleW + 30;
        DrawFrame(renderer, GameConfig.WindowWidth / 2f - titleBgW / 2f, hudMargin + 3, titleBgW, 32, 200);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - titleW / 2f, hudMargin + 9, title, new Color3(180, 200, 255), 2.5f);

        // Landing prompt (bottom of screen)
        if (_panel.HasCursor)
        {
            bool canLand = _panel.CanLandAtCursor;
            string confirmText = game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper();
            bool usingGamepad = game.Input.ActiveInputMethod == InputMethod.Gamepad;
            string prompt = canLand
                ? usingGamepad
                    ? $"[{confirmText}] CONFIRM LANDING"
                    : $"[DBLCLICK/{confirmText}/{game.Input.GetMouseButtonHelpText(SDL.ButtonLeft).ToUpper()}] CONFIRM LANDING"
                : "CANNOT LAND ON " + _panel.CursorTerrainName;
            byte pr = canLand ? (byte)100 : (byte)255;
            byte pg = canLand ? (byte)255 : (byte)80;
            byte pb = canLand ? (byte)100 : (byte)80;
            float promptW = renderer.MeasureText(prompt, 2f);
            DrawFrame(renderer, GameConfig.WindowWidth / 2f - promptW / 2f - 6,
                GameConfig.WindowHeight - 50 - hudMargin, promptW + 12, 28, 200);
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - promptW / 2f,
                GameConfig.WindowHeight - 45 - hudMargin, prompt, new Color3(pr, pg, pb), 2f);
        }
    }
}
