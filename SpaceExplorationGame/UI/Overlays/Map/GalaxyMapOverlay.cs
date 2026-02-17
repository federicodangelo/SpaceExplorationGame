using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>The two view modes the overlay can show.</summary>
public enum MapViewMode { SolarSystem, Galaxy }

/// <summary>
/// Full-screen overlay container that switches between a solar system map and galaxy star chart.
/// Opened with M key from SolarSystemState. Tab/M toggles between modes.
/// Delegates all mode-specific logic to <see cref="SolarSystemMapPanel"/> and <see cref="GalaxyMapPanel"/>.
/// </summary>
public class GalaxyMapOverlay : MapOverlayBase
{
    // ── Panels ──
    private readonly SolarSystemMapPanel _solarPanel = new();
    private readonly GalaxyMapPanel _galaxyPanel = new();
    private MapPanelBase _activePanel;

    // ── Current mode ──
    private MapViewMode _viewMode = MapViewMode.SolarSystem;

    public GalaxyMapOverlay()
    {
        _activePanel = _solarPanel;

        // Wire close callbacks
        _solarPanel.OnRequestClose = game => Close(game);
        _galaxyPanel.OnRequestClose = game => Close(game);
    }

    protected override MapPanelBase GetActivePanel() => _activePanel;

    // ─────────────────────────────────────────────────────────────
    //  OPEN / CLOSE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Open the overlay in any mode. Default = SolarSystem.</summary>
    public void Open(Game game, MapViewMode initialMode = MapViewMode.SolarSystem)
    {
        IsOpen = true;
        _viewMode = initialMode;

        ComputeLayout();
        ApplyLayoutToPanel(_solarPanel);
        ApplyLayoutToPanel(_galaxyPanel);

        // Initialize both panels with data
        _solarPanel.Open(game);
        _galaxyPanel.Open(game);

        // Activate the requested panel
        _activePanel = initialMode == MapViewMode.Galaxy ? _galaxyPanel : _solarPanel;
        _activePanel.SetupCamera(game);
    }

    /// <summary>Close the overlay and both panels.</summary>
    public void Close(Game game)
    {
        _solarPanel.Close(game);
        _galaxyPanel.Close(game);
        base.Close();
    }

    // ─────────────────────────────────────────────────────────────
    //  MODE SWITCHING
    // ─────────────────────────────────────────────────────────────

    private void SwitchMode(Game game)
    {
        _viewMode = _viewMode == MapViewMode.SolarSystem ? MapViewMode.Galaxy : MapViewMode.SolarSystem;
        _activePanel = _viewMode == MapViewMode.Galaxy ? _galaxyPanel : _solarPanel;
        _activePanel.SetupCamera(game);
    }

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;
        var input = game.Input;

        // Escape closes
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close(game);
            return true;
        }

        // M or Tab toggles between modes
        if (input.IsKeyPressed(SDL.Scancode.M) || input.IsKeyPressed(SDL.Scancode.Tab))
        {
            SwitchMode(game);
            return true;
        }

        // Check if mouse clicked on tab buttons
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
        if (input.IsMouseReleased(1))
        {
            float tabSolarX = FrameX;
            float tabGalaxyX = FrameX + FrameW / 2f;
            float tabY = FrameY;
            if (currentMouse.Y >= tabY && currentMouse.Y <= tabY + HeaderHeight)
            {
                if (currentMouse.X >= tabSolarX && currentMouse.X < tabGalaxyX && _viewMode != MapViewMode.SolarSystem)
                {
                    _viewMode = MapViewMode.SolarSystem;
                    _activePanel = _solarPanel;
                    _activePanel.SetupCamera(game);
                    return true;
                }
                else if (currentMouse.X >= tabGalaxyX && currentMouse.X < tabSolarX + FrameW && _viewMode != MapViewMode.Galaxy)
                {
                    _viewMode = MapViewMode.Galaxy;
                    _activePanel = _galaxyPanel;
                    _activePanel.SetupCamera(game);
                    return true;
                }
            }
        }

        return _activePanel.UpdateInput(game);
    }

    // ─────────────────────────────────────────────────────────────
    //  HEADER (tab bar)
    // ─────────────────────────────────────────────────────────────

    protected override void RenderHeader(SpriteRenderer renderer)
    {
        float halfW = FrameW / 2f;

        // Tab background
        renderer.DrawRectScreen(FrameX, FrameY, FrameW, HeaderHeight, new Color4(20, 25, 50, 240));
        renderer.DrawRectScreen(FrameX, FrameY + HeaderHeight - 1, FrameW, 1, new Color4(60, 80, 140, 200));

        // Solar System tab
        bool solarActive = _viewMode == MapViewMode.SolarSystem;
        var solarBg = solarActive ? new Color4(40, 55, 100, 240) : new Color4(20, 25, 50, 200);
        var solarText = solarActive ? new Color3(200, 220, 255) : new Color3(100, 110, 140);
        renderer.DrawRectScreen(FrameX, FrameY, halfW, HeaderHeight - 1, solarBg);
        string solarLabel = "SOLAR SYSTEM [M]";
        float solarLabelW = renderer.MeasureText(solarLabel, 1.6f);
        renderer.DrawTextScreen(FrameX + halfW / 2f - solarLabelW / 2f, FrameY + 6, solarLabel, solarText, 1.6f);

        // Galaxy tab
        bool galaxyActive = _viewMode == MapViewMode.Galaxy;
        var galaxyBg = galaxyActive ? new Color4(40, 55, 100, 240) : new Color4(20, 25, 50, 200);
        var galaxyText = galaxyActive ? new Color3(200, 220, 255) : new Color3(100, 110, 140);
        renderer.DrawRectScreen(FrameX + halfW, FrameY, halfW, HeaderHeight - 1, galaxyBg);
        string galaxyLabel = "STAR CHART [M]";
        float galaxyLabelW = renderer.MeasureText(galaxyLabel, 1.6f);
        renderer.DrawTextScreen(FrameX + halfW + halfW / 2f - galaxyLabelW / 2f, FrameY + 6, galaxyLabel, galaxyText, 1.6f);

        // Divider between tabs
        renderer.DrawRectScreen(FrameX + halfW - 1, FrameY + 4, 1, HeaderHeight - 8, new Color4(60, 80, 140, 150));
    }
}
