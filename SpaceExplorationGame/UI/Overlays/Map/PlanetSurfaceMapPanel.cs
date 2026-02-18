using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>Type of object hovered/selected in the planet surface map.</summary>
public enum SurfaceMapObjectType { None, Settlement, Ship, Location }

/// <summary>Identifies a clickable object in the planet surface map.</summary>
public readonly record struct SurfaceMapSelection(
    SurfaceMapObjectType Type, int SettlementIndex = -1, int TileX = -1, int TileY = -1);

/// <summary>
/// Map panel showing a planet surface terrain overview with interactive settlements and ship marker.
/// Supports hover, click-to-select, double-click-to-target, and navigation target management.
/// Opened from PlanetSurfaceState with the M key.
/// </summary>
public class PlanetSurfaceMapPanel : PlanetMapPanelBase
{
    // Ship world position (in tile coordinates for this panel)
    private Vector2 _shipTilePos;

    // Player world position (in tile coordinates for this panel)
    private Vector2 _playerTilePos;

    // Vehicle position (in tile coordinates), null if not deployed
    private Vector2? _vehicleTilePos;

    // Selection
    private SurfaceMapSelection _hoveredObject = new(SurfaceMapObjectType.None);
    private SurfaceMapSelection _selectedObject = new(SurfaceMapObjectType.None);
    private SurfaceMapSelection _lastClickObject = new(SurfaceMapObjectType.None);
    private float _lastClickTime;

    // -----------------------------------------------------------------
    //  LIFECYCLE
    // -----------------------------------------------------------------

    /// <summary>Open the panel with surface data and positions.</summary>
    public void OpenWithData(Game game, StarSystemData starSystem, PlanetData planet,
        PlanetSurfaceData surfaceData, Vector2 shipWorldPos, Vector2 playerWorldPos,
        Vector2? vehicleWorldPos)
    {
        _starSystem = starSystem;
        _planet = planet;
        _surfaceData = surfaceData;

        // Convert world positions (pixels) to tile positions
        float tileSize = GameConfig.TileSize;
        _shipTilePos = shipWorldPos / tileSize;
        _playerTilePos = playerWorldPos / tileSize;
        _vehicleTilePos = vehicleWorldPos.HasValue ? vehicleWorldPos.Value / tileSize : null;

        _hoveredObject = new(SurfaceMapObjectType.None);
        _selectedObject = new(SurfaceMapObjectType.None);
        _lastClickObject = new(SurfaceMapObjectType.None);
        _lastClickTime = 0f;

        // Create terrain overview texture
        _terrainTexture = CreateTerrainTexture(game);
    }

    /// <summary>Center on the player position instead of terrain center.</summary>
    protected override Vector2 GetInitialCameraPosition() => _playerTilePos;

    // -----------------------------------------------------------------
    //  INPUT
    // -----------------------------------------------------------------

    public override bool UpdateInput(Game game)
    {
        var input = game.Input;
        Vector2 currentMouse = new(input.MouseX, input.MouseY);

        HandleZoomAndPan(input, currentMouse);
        ClampCameraPosition();

        // Hover detection
        _hoveredObject = new(SurfaceMapObjectType.None);
        if (IsMouseInMap(currentMouse))
        {
            float bestDist = float.MaxValue;

            // Check settlements
            for (int i = 0; i < _surfaceData.Settlements.Count; i++)
            {
                var s = _surfaceData.Settlements[i];
                float cx = s.TileRect.X + s.TileRect.Width / 2f;
                float cy = s.TileRect.Y + s.TileRect.Height / 2f;
                var screenPos = Camera.WorldToScreen(new Vector2(cx, cy));
                float hitR = MathF.Max(
                    Math.Max(s.TileRect.Width, s.TileRect.Height) * Camera.Zoom / 2f, 12f);
                float dist = (currentMouse - screenPos).LengthSquared();
                if (dist < hitR * hitR && dist < bestDist)
                {
                    bestDist = dist;
                    _hoveredObject = new(SurfaceMapObjectType.Settlement, SettlementIndex: i);
                }
            }

            // Check ship
            var shipScreen = Camera.WorldToScreen(_shipTilePos);
            float shipHitR = MathF.Max(8f * Camera.Zoom, 12f);
            float shipDist = (currentMouse - shipScreen).LengthSquared();
            if (shipDist < shipHitR * shipHitR && shipDist < bestDist)
            {
                _hoveredObject = new(SurfaceMapObjectType.Ship);
            }
        }

        // Click to select / double-click to set target and close
        if (input.IsMouseReleased(1) && !IsPanning)
        {
            // Determine what was clicked: settlement/ship take priority, otherwise any terrain location
            SurfaceMapSelection clickTarget;
            if (_hoveredObject.Type != SurfaceMapObjectType.None)
            {
                clickTarget = _hoveredObject;
            }
            else if (IsMouseInMap(currentMouse))
            {
                // Click on empty terrain - select that location
                var worldPos = Camera.ScreenToWorld(currentMouse);
                int tileX = (int)worldPos.X;
                int tileY = (int)worldPos.Y;
                if (tileX >= 0 && tileX < _surfaceData.Width && tileY >= 0 && tileY < _surfaceData.Height)
                    clickTarget = new(SurfaceMapObjectType.Location, TileX: tileX, TileY: tileY);
                else
                    clickTarget = new(SurfaceMapObjectType.None);
            }
            else
            {
                clickTarget = new(SurfaceMapObjectType.None);
            }

            if (clickTarget.Type != SurfaceMapObjectType.None)
            {
                float now = (float)game.GlobalTime;
                if (clickTarget == _lastClickObject && (now - _lastClickTime) < DoubleClickTime)
                {
                    // Double-click: set as target and close
                    _selectedObject = clickTarget;
                    if (!IsCurrentNavTarget(game.Player, _selectedObject))
                        ToggleNavTarget(game.Player);
                    OnRequestClose?.Invoke(game);
                    return true;
                }
                _selectedObject = clickTarget;
                _lastClickObject = clickTarget;
                _lastClickTime = now;
            }
            else
            {
                _lastClickObject = new(SurfaceMapObjectType.None);
            }
            IsPanning = false;
        }
        else if (input.IsMouseReleased(1))
            IsPanning = false;

        // T or Enter to toggle nav target
        if ((input.IsKeyPressed(SDL.Scancode.T) || input.IsKeyPressed(SDL.Scancode.Return))
            && _selectedObject.Type != SurfaceMapObjectType.None)
        {
            ToggleNavTarget(game.Player);
        }

        return true;
    }

    // -----------------------------------------------------------------
    //  NAV TARGET HELPERS
    // -----------------------------------------------------------------

    private bool IsCurrentNavTarget(PlayerData player, SurfaceMapSelection sel)
    {
        if (player.NavTargetType != NavigationTargetType.SurfaceTarget) return false;

        return sel.Type switch
        {
            SurfaceMapObjectType.Settlement when sel.SettlementIndex >= 0
                && sel.SettlementIndex < _surfaceData.Settlements.Count =>
                player.NavTargetName == _surfaceData.Settlements[sel.SettlementIndex].Name,
            SurfaceMapObjectType.Ship => player.NavTargetName == "SHIP",
            SurfaceMapObjectType.Location => player.NavTargetName == $"({sel.TileX}, {sel.TileY})",
            _ => false
        };
    }

    private void ToggleNavTarget(PlayerData player)
    {
        if (_selectedObject.Type == SurfaceMapObjectType.None) return;

        if (IsCurrentNavTarget(player, _selectedObject))
        {
            player.ClearNavigationTarget();
            return;
        }

        float tileSize = GameConfig.TileSize;

        switch (_selectedObject.Type)
        {
            case SurfaceMapObjectType.Settlement when _selectedObject.SettlementIndex >= 0
                && _selectedObject.SettlementIndex < _surfaceData.Settlements.Count:
                var settlement = _surfaceData.Settlements[_selectedObject.SettlementIndex];
                float sx = (settlement.TileRect.X + settlement.TileRect.Width / 2f) * tileSize;
                float sy = (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * tileSize;
                player.SetNavTargetSurface(settlement.Name, new Color3(255, 220, 100), sx, sy);
                break;

            case SurfaceMapObjectType.Ship:
                player.SetNavTargetSurface("SHIP", new Color3(120, 200, 255),
                    _shipTilePos.X * tileSize, _shipTilePos.Y * tileSize);
                break;

            case SurfaceMapObjectType.Location when _selectedObject.TileX >= 0 && _selectedObject.TileY >= 0:
                float lx = (_selectedObject.TileX + 0.5f) * tileSize;
                float ly = (_selectedObject.TileY + 0.5f) * tileSize;
                player.SetNavTargetSurface($"({_selectedObject.TileX}, {_selectedObject.TileY})",
                    new Color3(200, 200, 100), lx, ly);
                break;
        }
    }

    // -----------------------------------------------------------------
    //  RENDERING
    // -----------------------------------------------------------------

    public override void RenderContent(Game game, SpriteRenderer renderer)
    {
        if (_surfaceData == null || _terrainTexture == nint.Zero) return;

        var camera = Camera;
        var (tileScreenW, tileScreenH) = RenderTerrainTexture(game);

        // Settlement markers
        for (int i = 0; i < _surfaceData.Settlements.Count; i++)
        {
            var settlement = _surfaceData.Settlements[i];

            bool isHovered = _hoveredObject.Type == SurfaceMapObjectType.Settlement
                             && _hoveredObject.SettlementIndex == i;
            bool isSelected = _selectedObject.Type == SurfaceMapObjectType.Settlement
                              && _selectedObject.SettlementIndex == i;
            bool isTarget = IsCurrentNavTarget(game.Player, new(SurfaceMapObjectType.Settlement, i));

            var outlineColor = (isHovered || isSelected)
                ? new Color4(255, 255, 200, 255)
                : new Color4(255, 220, 100, 180);

            RenderSettlementMarker(renderer, camera, settlement, tileScreenW, tileScreenH, outlineColor);

            if (isTarget)
            {
                float cx = settlement.TileRect.X + settlement.TileRect.Width / 2f;
                float cy = settlement.TileRect.Y + settlement.TileRect.Height / 2f;
                float sw = settlement.TileRect.Width * tileScreenW;
                float sh = settlement.TileRect.Height * tileScreenH;
                DrawTargetBrackets(renderer, camera, new Vector2(cx, cy),
                    Math.Max(sw, sh) / (2f * camera.Zoom) + 2, game);
            }
        }

        // Ship marker
        {
            var shipScreen = camera.WorldToScreen(_shipTilePos);
            float ds = Math.Max(6f, 3f / camera.Zoom);
            bool isShipHovered = _hoveredObject.Type == SurfaceMapObjectType.Ship;
            bool isShipSelected = _selectedObject.Type == SurfaceMapObjectType.Ship;
            bool isShipTarget = IsCurrentNavTarget(game.Player, new(SurfaceMapObjectType.Ship));

            renderer.DrawFilledCircleScreen(shipScreen.X, shipScreen.Y, ds * 0.6f, new Color4(120, 200, 255, 220));

            if (isShipHovered || isShipSelected)
                renderer.DrawCircle(camera, _shipTilePos, (ds + 4) / camera.Zoom, new Color3(120, 200, 255));
            if (isShipTarget)
                DrawTargetBrackets(renderer, camera, _shipTilePos, ds / camera.Zoom + 2, game);

            // Ship label
            string shipLabel = "SHIP";
            float shipLabelW = renderer.MeasureText(shipLabel, 1f);
            renderer.DrawTextScreen(shipScreen.X - shipLabelW / 2f, shipScreen.Y + ds + 2,
                shipLabel, new Color3(120, 200, 255), 1f);
        }

        // Vehicle marker
        if (_vehicleTilePos.HasValue)
        {
            var vehicleScreen = camera.WorldToScreen(_vehicleTilePos.Value);
            float ds = Math.Max(4f, 2f / camera.Zoom);
            renderer.DrawFilledCircleScreen(vehicleScreen.X, vehicleScreen.Y, ds * 0.6f, new Color4(200, 180, 100, 180));
            string vLabel = "VEHICLE";
            float vLabelW = renderer.MeasureText(vLabel, 1f);
            renderer.DrawTextScreen(vehicleScreen.X - vLabelW / 2f, vehicleScreen.Y + ds + 2,
                vLabel, new Color3(200, 180, 100), 1f);
        }

        // Player marker
        {
            var playerScreen = camera.WorldToScreen(_playerTilePos);
            float ps = Math.Max(4f, 2f / camera.Zoom);
            renderer.DrawFilledCircleScreen(playerScreen.X, playerScreen.Y, ps, new Color4(0, 255, 100, 230));
            renderer.DrawCircle(camera, _playerTilePos, (ps + 2) / camera.Zoom, new Color3(0, 255, 100));

            string youLabel = "YOU";
            float youW = renderer.MeasureText(youLabel, 1f);
            renderer.DrawTextScreen(playerScreen.X - youW / 2f, playerScreen.Y + ps + 4,
                youLabel, new Color3(0, 255, 100), 1f);
        }

        // Location selection marker
        if (_selectedObject.Type == SurfaceMapObjectType.Location)
        {
            float lx = _selectedObject.TileX + 0.5f;
            float ly = _selectedObject.TileY + 0.5f;
            var locScreen = camera.WorldToScreen(new Vector2(lx, ly));
            bool isLocTarget = IsCurrentNavTarget(game.Player, _selectedObject);

            RenderSelectionReticle(renderer, locScreen, _selectionPulse,
                new Color3(200, 200, 100), new Color3(255, 255, 200));

            if (isLocTarget)
                DrawTargetBrackets(renderer, camera, new Vector2(lx, ly), 4f / camera.Zoom + 2, game);
        }

        // Active nav target marker (if it is a location not currently selected)
        if (game.Player.NavTargetType == NavigationTargetType.SurfaceTarget)
        {
            bool alreadyShown = _selectedObject.Type != SurfaceMapObjectType.None
                                && IsCurrentNavTarget(game.Player, _selectedObject);
            if (!alreadyShown)
            {
                float ntx = game.Player.NavTargetWorldX / GameConfig.TileSize;
                float nty = game.Player.NavTargetWorldY / GameConfig.TileSize;
                DrawTargetBrackets(renderer, camera, new Vector2(ntx, nty), 4f / camera.Zoom, game);
            }
        }

        // Mission markers on settlements
        float mPulse = (float)(0.5 + 0.5 * Math.Sin(game.GlobalTime * 3.0));
        byte mAlpha = (byte)(140 + (int)(mPulse * 115));
        foreach (var mission in game.Player.ActiveMissions)
        {
            if (mission.Status != MissionStatus.Completed
                && mission.Type == MissionType.SettlementDelivery
                && mission.Target.IsPlanet(_starSystem.Index, _planet.Index))
            {
                var mc = mission.TypeColor;
                foreach (var settlement in _surfaceData.Settlements)
                {
                    float scx = settlement.TileRect.X + settlement.TileRect.Width / 2f;
                    float scy = settlement.TileRect.Y + settlement.TileRect.Height / 2f;
                    float sr = Math.Max(settlement.TileRect.Width, settlement.TileRect.Height) / 2f;
                    renderer.DrawCircle(camera, new Vector2(scx, scy), sr + 3,
                        new Color4(mc.R, mc.G, mc.B, mAlpha));
                    DrawMissionDiamond(renderer, camera, new Vector2(scx, scy), sr, mc, mAlpha, mission.TypeLabel);
                }
            }
        }
    }

    // -----------------------------------------------------------------
    //  INFO PANEL
    // -----------------------------------------------------------------

    public override void RenderInfoPanel(Game game, SpriteRenderer renderer)
    {
        RenderInfoPanelHeader(renderer, "SURFACE DATA");

        float px = IpX + 12;
        float py = IpY + 40;

        // Planet summary
        renderer.DrawTextScreen(px, py, _planet.Name.ToUpper(), _planet.Color, 1.8f);
        py += 24;
        renderer.DrawTextScreen(px, py, $"TYPE: {_planet.Type.ToString().ToUpper()}", new Color3(180, 180, 200), 1.3f);
        py += 16;
        renderer.DrawTextScreen(px, py, $"SIZE: {_surfaceData.Width} x {_surfaceData.Height} TILES", new Color3(180, 180, 200), 1.3f);
        py += 16;
        renderer.DrawTextScreen(px, py, $"SETTLEMENTS: {_surfaceData.Settlements.Count}", new Color3(180, 180, 200), 1.3f);
        py += 20;

        renderer.DrawRectScreen(px, py, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        py += 8;

        // Selected object details
        if (_selectedObject.Type != SurfaceMapObjectType.None)
        {
            RenderSelectedObjectInfo(game, renderer, px, py);
        }
        else
        {
            renderer.DrawTextScreen(px, py, "NO SELECTION", new Color3(100, 120, 160), 1.5f);
            py += 20;
            renderer.DrawTextScreen(px, py, "CLICK ANYWHERE ON THE", new Color3(140, 140, 160), 1.3f);
            py += 16;
            renderer.DrawTextScreen(px, py, "MAP TO SET A TARGET", new Color3(140, 140, 160), 1.3f);
        }

        // Nav target display
        py = IpY + IpH - 160;
        renderer.DrawRectScreen(px, py, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        py += 8;
        renderer.DrawTextScreen(px, py, "NAV TARGET", new Color3(100, 120, 160), 1.3f);
        py += 18;
        if (game.Player.HasNavigationTarget)
        {
            renderer.DrawTextScreen(px, py, game.Player.NavTargetName.ToUpper(),
                game.Player.NavTargetColor, 1.8f);
            py += 22;
            renderer.DrawTextScreen(px, py,
                $"TYPE: {game.Player.NavTargetType.ToString().ToUpper()}",
                new Color3(180, 180, 200), 1.3f);
        }
        else
        {
            renderer.DrawTextScreen(px, py, "NONE", new Color3(80, 80, 100), 1.5f);
        }

        // Controls
        float ctrlY = IpY + IpH - 80;
        renderer.DrawRectScreen(px, ctrlY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(px, ctrlY + 8, "WASD/DRAG: PAN", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlY + 24, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlY + 40, "T/ENTER: SET TARGET", new Color3(255, 200, 100), 1.3f);
        renderer.DrawTextScreen(px, ctrlY + 56, "M/ESC: CLOSE", new Color3(255, 150, 150), 1.3f);
    }

    private void RenderSelectedObjectInfo(Game game, SpriteRenderer renderer, float px, float py)
    {
        var sel = _selectedObject;
        bool isTarget = IsCurrentNavTarget(game.Player, sel);
        string targetTag = isTarget ? "  [TARGET]" : "";

        switch (sel.Type)
        {
            case SurfaceMapObjectType.Settlement when sel.SettlementIndex >= 0
                && sel.SettlementIndex < _surfaceData.Settlements.Count:
                var settlement = _surfaceData.Settlements[sel.SettlementIndex];
                renderer.DrawTextScreen(px, py, "SELECTED: SETTLEMENT", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, settlement.Name.ToUpper() + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(255, 220, 100), 1.8f);
                py += 26;
                renderer.DrawTextScreen(px, py, $"SIZE: {settlement.TileRect.Width}x{settlement.TileRect.Height}",
                    new Color3(200, 200, 200), 1.5f);
                py += 28;
                RenderTargetButton(renderer, px, py, isTarget);
                break;

            case SurfaceMapObjectType.Ship:
                renderer.DrawTextScreen(px, py, "SELECTED: SHIP", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, "YOUR SPACESHIP" + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(120, 200, 255), 1.8f);
                py += 26;
                renderer.DrawTextScreen(px, py, "BOARD TO FLY TO SPACE",
                    new Color3(200, 200, 200), 1.5f);
                py += 28;
                RenderTargetButton(renderer, px, py, isTarget);
                break;

            case SurfaceMapObjectType.Location when sel.TileX >= 0 && sel.TileY >= 0
                && sel.TileX < _surfaceData.Width && sel.TileY < _surfaceData.Height:
                var locTerrain = _surfaceData.Tiles[sel.TileX, sel.TileY];
                string locTerrainName = locTerrain.ToString().ToUpper();
                renderer.DrawTextScreen(px, py, "SELECTED: LOCATION", new Color3(100, 120, 160), 1.3f);
                py += 20;
                renderer.DrawTextScreen(px, py, $"({sel.TileX}, {sel.TileY})" + targetTag,
                    isTarget ? new Color3(255, 200, 50) : new Color3(200, 200, 100), 1.8f);
                py += 26;
                renderer.DrawTextScreen(px, py, $"TERRAIN: {locTerrainName}",
                    new Color3(200, 200, 200), 1.5f);
                py += 28;
                RenderTargetButton(renderer, px, py, isTarget);
                break;
        }
    }

    private void RenderTargetButton(SpriteRenderer renderer, float px, float py, bool isTarget)
    {
        string btnText = isTarget ? "[T] CLEAR TARGET" : "[T] SET AS TARGET";
        var btnColor = isTarget ? new Color3(255, 100, 100) : new Color3(255, 200, 100);
        renderer.DrawRectScreen(px, py, InfoPanelW - 24, 20, new Color4(40, 50, 80, 180));
        float btnW = renderer.MeasureText(btnText, 1.5f);
        renderer.DrawTextScreen(px + (InfoPanelW - 24) / 2f - btnW / 2f, py + 2, btnText, btnColor, 1.5f);
    }
}
