using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>
/// Planet surface map overlay: shows terrain overview with selectable settlements and ship marker.
/// Opened with M key from PlanetSurfaceState. Allows selecting targets and setting navigation targets.
/// </summary>
public class PlanetSurfaceMapOverlay : MapOverlayBase
{
    private readonly PlanetSurfaceMapPanel _panel;

    // ── Layout overrides (700×700 map, 260px info panel) ──
    protected override float MapContentWidth => 700f;
    protected override float MapContentHeight => 700f;
    protected override float InfoPanelWidthValue => 260f;

    public PlanetSurfaceMapOverlay(TextureManager textures)
    {
        _panel = new PlanetSurfaceMapPanel(textures);
        _panel.OnRequestClose = game => Close();
    }

    protected override MapPanelBase GetActivePanel() => _panel;

    // ─────────────────────────────────────────────────────────────
    //  OPEN / CLOSE / CLEANUP
    // ─────────────────────────────────────────────────────────────

    public void Open(Game game, StarSystemData starSystem, PlanetData planet,
        PlanetSurfaceData surfaceData, Vector2 shipWorldPos, Vector2 playerWorldPos,
        Vector2? vehicleWorldPos)
    {
        IsOpen = true;

        ComputeLayout();
        ApplyLayoutToPanel(_panel);

        _panel.OpenWithData(game, starSystem, planet, surfaceData,
            shipWorldPos, playerWorldPos, vehicleWorldPos);
        _panel.SetupCamera(game);
    }

    /// <summary>Destroy the cached terrain texture.</summary>
    public void Cleanup() => _panel.Cleanup();

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        // Back or Map closes
        if (game.Input.IsActionPressed(InputAction.MenuBack)
            || game.Input.IsActionPressed(InputAction.ToggleMap))
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
        string title = $"SURFACE MAP - {_panel.PlanetName.ToUpper()}";
        renderer.DrawRectScreen(FrameX, FrameY, FrameW, HeaderHeight, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(FrameX, FrameY + HeaderHeight - 1, FrameW, 1, new Color4(60, 80, 140, 200));
        float labelW = renderer.MeasureText(title, 1.8f);
        renderer.DrawTextScreen(FrameX + FrameW / 2f - labelW / 2f, FrameY + 6, title, new Color3(140, 170, 220), 1.8f);
    }

    protected override void RenderHud(Game game, SpriteRenderer renderer)
    {
        const float hudMargin = 5f;

        // Title bar (centered above frame)
        string title = $"SURFACE MAP - {_panel.PlanetName.ToUpper()}";
        float titleW = renderer.MeasureText(title, 2.5f);
        float titleBgW = titleW + 30;
        DrawFrame(renderer, GameConfig.WindowWidth / 2f - titleBgW / 2f, hudMargin + 3, titleBgW, 32, 200);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - titleW / 2f, hudMargin + 9, title, new Color3(180, 200, 255), 2.5f);
    }
}
