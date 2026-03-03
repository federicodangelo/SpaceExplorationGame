using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using Engine.Platform;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

public readonly record struct LandingSelectionRequest(
    StarSystemData StarSystem,
    PlanetData Planet,
    int TileX,
    int TileY,
    bool IsMoon,
    int MoonPlanetIndex,
    int MoonIndex
);

/// <summary>
/// Map panel showing a planet/moon terrain overview with Camera-based zoom and pan.
/// The player clicks to choose a landing site, double-clicks or presses Enter/E to confirm landing.
/// </summary>
public class PlanetLandingPanel : PlanetMapPanelBase
{
    private const int SelectionBorderMarginTiles = 2;
    private const float InvalidSelectionHintDuration = 2.2f;

    // Selected landing position (replaces the old cursor)
    private TilePos _selectedTile;
    private bool _hasSelection;

    // Hovered tile (under mouse, or under screen-centre when using gamepad)
    private TilePos _hoveredTile;
    private bool _hasHoveredTile;

    // Double-click tracking
    private float _lastClickTime;
    private TilePos _lastClickTile = new(-1, -1);

    // Moon tracking
    private bool _isMoon;
    private int _moonPlanetIndex;
    private int _moonIndex;

    private string? _invalidSelectionHint;
    private float _invalidSelectionHintTimer;

    /// <summary>
    /// Called when landing is confirmed. Allows parent state to run a custom transition.
    /// Parameters: game, landing selection payload.
    /// </summary>
    public Action<Game, LandingSelectionRequest>? OnLandingConfirmed { get; set; }

    public PlanetLandingPanel(ITextureManager textures) : base(textures)
    {
    }

    // -- Public state for the overlay's HUD --


    /// <summary>Whether a landing position has been selected.</summary>
    public bool HasSelection => _hasSelection;

    /// <summary>Whether the selected position is on terrain that allows landing.</summary>
    public bool CanLandAtSelection
    {
        get
        {
            if (!_hasSelection || _surfaceData == null) return false;
            return SurfaceTerrainRules.IsTraversable(_surfaceData.Tiles[_selectedTile.X, _selectedTile.Y]);
        }
    }

    /// <summary>Terrain type at selected position as uppercase string.</summary>
    public string SelectionTerrainName
    {
        get
        {
            if (!_hasSelection || _surfaceData == null) return "";
            return _surfaceData.Tiles[_selectedTile.X, _selectedTile.Y].ToString().ToUpper();
        }
    }

    // -----------------------------------------------------------------
    //  LIFECYCLE
    // -----------------------------------------------------------------

    /// <summary>Open the panel for a specific planet or moon.</summary>
    public void OpenWithPlanet(Game game, StarSystemData starSystem, PlanetData planet,
        bool isMoon = false, int moonPlanetIndex = -1, int moonIndex = -1)
    {
        _starSystem = starSystem;
        _planet = planet;
        _isMoon = isMoon;
        _moonPlanetIndex = moonPlanetIndex;
        _moonIndex = moonIndex;

        _selectionPulse = 0f;
        _lastClickTime = 0f;
        _lastClickTile = new TilePos(-1, -1);
        _hasSelection = false;
        _hasHoveredTile = false;
        _invalidSelectionHint = null;
        _invalidSelectionHintTimer = 0f;

        // Generate surface
        _surfaceData = game.UniverseGenerator.GeneratePlanetSurface(starSystem, planet);

        // Create terrain overview texture
        CreateTerrainTexture(game);
    }

    // -----------------------------------------------------------------
    //  INPUT
    // -----------------------------------------------------------------

    public override bool UpdateInput(Game game)
    {
        base.UpdateInput(game);
        var input = game.Input;
        bool usingGamepad = UsingGamepad;
        Vector2 currentMouse = CurrentMouse;
        Vector2 selectionPoint = SelectionPoint;

        if (_invalidSelectionHintTimer > 0f)
        {
            _invalidSelectionHintTimer -= game.DeltaTime;
            if (_invalidSelectionHintTimer <= 0f)
            {
                _invalidSelectionHintTimer = 0f;
                _invalidSelectionHint = null;
            }
        }

        // Compute hovered tile from selection point (mouse or screen centre for gamepad)
        _hasHoveredTile = false;
        if (usingGamepad || IsMouseInMap(currentMouse))
        {
            var worldPos = Camera.ScreenToWorld(selectionPoint);
            int tx = (int)worldPos.X;
            int ty = (int)worldPos.Y;
            if (tx >= 0 && tx < _surfaceData.Width && ty >= 0 && ty < _surfaceData.Height)
            {
                if (_surfaceData.Tiles[tx, ty] == TerrainType.Settlement)
                    (tx, ty) = GetTileBelowSettlement(tx, ty);
                _hoveredTile = new TilePos(tx, ty);
                _hasHoveredTile = true;
            }
        }

        // Mouse: click to select, double-click to land
        if (input.IsMouseReleased(MouseButton.Left) && !IsPanning)
        {
            if (IsMouseInMap(currentMouse) && _hasHoveredTile)
            {
                if (!IsTileSelectableWithMargin(_hoveredTile.X, _hoveredTile.Y, SelectionBorderMarginTiles, out var failureReason))
                {
                    ShowInvalidSelectionHint(failureReason ?? "INVALID TARGET");
                    IsPanning = false;
                    return true;
                }

                float now = (float)game.GlobalTime;
                if (_hoveredTile == _lastClickTile && (now - _lastClickTime) < DoubleClickTime && _hasSelection)
                {
                    TryLand(game);
                    _lastClickTile = new TilePos(-1, -1);
                }
                else
                {
                    _selectedTile = _hoveredTile;
                    _hasSelection = true;
                    _lastClickTime = now;
                    _lastClickTile = _hoveredTile;
                }
            }
            IsPanning = false;
        }
        else if (input.IsMouseReleased(MouseButton.Left))
            IsPanning = false;

        // Gamepad: confirm selects hovered tile; confirm on already-selected tile = land
        if (usingGamepad && input.IsActionPressed(InputAction.MenuConfirm))
        {
            if (_hasHoveredTile)
            {
                if (!IsTileSelectableWithMargin(_hoveredTile.X, _hoveredTile.Y, SelectionBorderMarginTiles, out var failureReason))
                    ShowInvalidSelectionHint(failureReason ?? "INVALID TARGET");
                else if (_hasSelection && _hoveredTile == _selectedTile)
                    TryLand(game);
                else
                {
                    _selectedTile = _hoveredTile;
                    _hasSelection = true;
                }
            }
        }

        // Keyboard / non-gamepad confirm: land at selected position
        if (!usingGamepad && _hasSelection &&
            (input.IsActionPressed(InputAction.MenuConfirm) || input.IsActionPressed(InputAction.Interact)))
            TryLand(game);

        return true;
    }

    // -----------------------------------------------------------------
    //  LANDING
    // -----------------------------------------------------------------

    private void TryLand(Game game)
    {
        if (!_hasSelection) return;
        var terrain = _surfaceData.Tiles[_selectedTile.X, _selectedTile.Y];
        if (!SurfaceTerrainRules.IsTraversable(terrain)) return;

        if (_isMoon)
        {
            game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromMoon;
            game.Player.ReturnMoonPlanetIndex = _moonPlanetIndex;
            game.Player.ReturnMoonIndex = _moonIndex;
        }
        else
        {
            game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
            game.Player.ReturnPlanetIndex = _planet.Index;
        }

        Cleanup();
        OnRequestClose?.Invoke(game);

        if (OnLandingConfirmed != null)
        {
            var landing = new LandingSelectionRequest(
                StarSystem: _starSystem,
                Planet: _planet,
                TileX: _selectedTile.X,
                TileY: _selectedTile.Y,
                IsMoon: _isMoon,
                MoonPlanetIndex: _moonPlanetIndex,
                MoonIndex: _moonIndex);
            OnLandingConfirmed(game, landing);
        }
        else
        {
            game.ChangeState(new States.PlanetSurfaceState(_starSystem, _planet, _selectedTile.X, _selectedTile.Y));
        }
    }

    private void ShowInvalidSelectionHint(string text)
    {
        _invalidSelectionHint = text;
        _invalidSelectionHintTimer = InvalidSelectionHintDuration;
    }

    /// <summary>
    /// Given a tile known to be inside a settlement, returns the tile immediately
    /// below the settlement's bottom edge at the nearest X within the settlement's width.
    /// </summary>
    private (int x, int y) GetTileBelowSettlement(int tileX, int tileY)
    {
        foreach (var s in _surfaceData.Settlements)
        {
            if (tileX >= s.TileRect.X && tileX < s.TileRect.X + s.TileRect.Width &&
                tileY >= s.TileRect.Y && tileY < s.TileRect.Y + s.TileRect.Height)
            {
                int clampedX = Math.Clamp(tileX, s.TileRect.X, s.TileRect.X + s.TileRect.Width - 1);
                int belowY = Math.Min(s.TileRect.Y + s.TileRect.Height, _surfaceData.Height - 1);
                return (clampedX, belowY);
            }
        }
        return (tileX, tileY);
    }

    // -----------------------------------------------------------------
    //  RENDERING
    // -----------------------------------------------------------------

    public override void RenderContent(Game game, ISpriteRenderer renderer)
    {
        if (_surfaceData == null || _terrainTexture == nint.Zero) return;

        var (tileScreenW, tileScreenH) = RenderTerrainTexture(game);
        RenderPlanetDiscHalo(renderer, tileScreenW);

        // Settlement markers
        foreach (var settlement in _surfaceData.Settlements)
        {
            RenderSettlementMarker(renderer, Camera, settlement, tileScreenW, tileScreenH,
                new Color4(255, 220, 100, 180));
        }

        // Selected landing position
        if (_hasSelection)
        {
            float cx = _selectedTile.X + 0.5f;
            float cy = _selectedTile.Y + 0.5f;
            var selScreen = Camera.WorldToScreen(new Vector2(cx, cy));
            RenderSelectionReticle(renderer, selScreen, _selectionPulse,
                new Color3(100, 255, 100), new Color3(200, 255, 200));
        }

        // Gamepad: crosshair at screen centre to show what will be selected
        if (game.Input.ActiveInputMethod == InputMethod.Gamepad)
            RenderCenterSelectionReticle(renderer, new Color4(255, 230, 120, 220));
    }

    // -----------------------------------------------------------------
    //  INFO PANEL
    // -----------------------------------------------------------------

    public override void RenderInfoPanel(Game game, ISpriteRenderer renderer)
    {
        RenderInfoPanelHeader(renderer, "PLANET DATA");

        float px = IpX + 12;
        float py = IpY + 40;

        float nextY = RenderPlanetInfoBlock(renderer, px, py);
        nextY = RenderSettlementList(renderer, px, nextY);

        // -- Landing site info --
        if (_hasSelection)
        {
            renderer.DrawRectScreen(px, nextY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
            nextY += 10;

            renderer.DrawTextScreen(px, nextY, "LANDING SITE", new Color3(100, 120, 160), 1.3f, InfoPanelW - 24);
            nextY += 18;

            var terrain = _surfaceData.Tiles[_selectedTile.X, _selectedTile.Y];
            string terrainName = terrain.ToString().ToUpper();
            bool canLand = SurfaceTerrainRules.IsTraversable(terrain);

            byte tr = canLand ? (byte)100 : (byte)255;
            byte tg = canLand ? (byte)255 : (byte)80;
            byte tb = canLand ? (byte)100 : (byte)80;
            renderer.DrawTextScreen(px, nextY, $"TERRAIN: {terrainName}", new Color3(tr, tg, tb), 1.5f, InfoPanelW - 24);
            nextY += 18;

            renderer.DrawTextScreen(px, nextY, $"POS: ({_selectedTile.X}, {_selectedTile.Y})", new Color3(150, 150, 150), 1.3f, InfoPanelW - 24);
            nextY += 18;

            if (terrain == TerrainType.Settlement)
            {
                renderer.DrawTextScreen(px, nextY, "NO LANDING IN SETTLEMENT", new Color3(255, 100, 80), 1.3f, InfoPanelW - 24);
                nextY += 18;
            }
        }

        if (_invalidSelectionHintTimer > 0f && !string.IsNullOrWhiteSpace(_invalidSelectionHint))
        {
            renderer.DrawRectScreen(px, nextY, InfoPanelW - 24, 20, new Color4(70, 30, 30, 200));
            float hintW = renderer.MeasureText(_invalidSelectionHint, 1.3f);
            renderer.DrawTextScreen(px + (InfoPanelW - 24) / 2f - hintW / 2f, nextY + 3,
                _invalidSelectionHint, new Color3(255, 140, 120), 1.3f, InfoPanelW - 24);
            nextY += 24;
        }

        // Controls
        float ctrlStartY = IpY + IpH - 88;
        renderer.DrawRectScreen(px, ctrlStartY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        if (game.Input.ActiveInputMethod == InputMethod.Gamepad)
        {
            renderer.DrawTextScreen(px, ctrlStartY + 8, "LEFT STICK: PAN", new Color3(180, 180, 180), 1.3f, InfoPanelW - 24);
            renderer.DrawTextScreen(px, ctrlStartY + 24, "LT/RT: ZOOM", new Color3(180, 180, 180), 1.3f, InfoPanelW - 24);
            renderer.DrawTextScreen(px, ctrlStartY + 40,
                $"{game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper()}: SELECT CENTRE TILE",
                new Color3(180, 180, 180), 1.3f, InfoPanelW - 24);
            renderer.DrawTextScreen(px, ctrlStartY + 56,
                $"{game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper()} AGAIN: LAND",
                new Color3(100, 255, 100), 1.3f, InfoPanelW - 24);
        }
        else
        {
            string panText =
                $"{game.Input.GetActionHelpText(InputAction.MoveUp)}/{game.Input.GetActionHelpText(InputAction.MoveDown)}/{game.Input.GetActionHelpText(InputAction.MoveLeft)}/{game.Input.GetActionHelpText(InputAction.MoveRight)}/{game.Input.GetMouseButtonHelpText(MouseButton.Left)}-DRAG: PAN";
            renderer.DrawTextScreen(px, ctrlStartY + 8,
                $"{game.Input.GetMouseButtonHelpText(MouseButton.Left)}: SELECT SITE",
                new Color3(180, 180, 180), 1.3f, InfoPanelW - 24);
            renderer.DrawTextScreen(px, ctrlStartY + 24, panText, new Color3(180, 180, 180), 1.3f, InfoPanelW - 24);
            renderer.DrawTextScreen(px, ctrlStartY + 40, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f, InfoPanelW - 24);
            renderer.DrawTextScreen(px, ctrlStartY + 56,
                $"DBLCLICK/{game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper()}: LAND",
                new Color3(100, 255, 100), 1.3f, InfoPanelW - 24);
        }
        renderer.DrawTextScreen(px, ctrlStartY + 72,
            $"{game.Input.GetActionHelpText(InputAction.MenuBack)}: CANCEL",
            new Color3(255, 150, 150), 1.3f, InfoPanelW - 24);
    }
}
