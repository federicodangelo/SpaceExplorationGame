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

    // Landing cursor
    private TilePos _cursorTile;
    private bool _hasCursor;

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


    /// <summary>Whether a landing cursor has been placed.</summary>
    public bool HasCursor => _hasCursor;

    /// <summary>Whether the cursor is on terrain that allows landing.</summary>
    public bool CanLandAtCursor
    {
        get
        {
            if (!_hasCursor || _surfaceData == null) return false;
            var terrain = _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y];
            return SurfaceTerrainRules.IsTraversable(terrain);
        }
    }

    /// <summary>Terrain type at cursor as uppercase string.</summary>
    public string CursorTerrainName
    {
        get
        {
            if (!_hasCursor || _surfaceData == null) return "";
            return _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y].ToString().ToUpper();
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
        _invalidSelectionHint = null;
        _invalidSelectionHintTimer = 0f;

        // Generate surface
        _surfaceData = game.UniverseGenerator.GeneratePlanetSurface(starSystem, planet);

        // Create terrain overview texture
        CreateTerrainTexture(game);

        // Default cursor at center
        _cursorTile = new TilePos(_surfaceData.Width / 2, _surfaceData.Height / 2);
        _hasCursor = true;
    }

    // -----------------------------------------------------------------
    //  INPUT
    // -----------------------------------------------------------------

    public override bool UpdateInput(Game game)
    {
        var input = game.Input;
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
        if (_invalidSelectionHintTimer > 0f)
        {
            _invalidSelectionHintTimer -= game.DeltaTime;
            if (_invalidSelectionHintTimer <= 0f)
            {
                _invalidSelectionHintTimer = 0f;
                _invalidSelectionHint = null;
            }
        }

        HandleZoomAndPan(input, currentMouse);
        ClampCameraPosition();

        // Click to select / double-click to land
        if (input.IsMouseReleased(MouseButton.Left) && !IsPanning)
        {
            if (IsMouseInMap(currentMouse))
            {
                var worldPos = Camera.ScreenToWorld(currentMouse);
                int tileX = (int)worldPos.X;
                int tileY = (int)worldPos.Y;

                if (tileX >= 0 && tileX < _surfaceData.Width &&
                    tileY >= 0 && tileY < _surfaceData.Height)
                {
                    if (!IsTileSelectableWithMargin(tileX, tileY, SelectionBorderMarginTiles, out var failureReason))
                    {
                        ShowInvalidSelectionHint(failureReason ?? "INVALID TARGET");
                        IsPanning = false;
                        return true;
                    }

                    var tilePos = new TilePos(tileX, tileY);
                    float now = (float)game.GlobalTime;

                    if (tilePos == _lastClickTile && (now - _lastClickTime) < DoubleClickTime && _hasCursor)
                    {
                        TryLand(game);
                        _lastClickTile = new TilePos(-1, -1);
                    }
                    else
                    {
                        _cursorTile = tilePos;
                        _hasCursor = true;
                        _lastClickTime = now;
                        _lastClickTile = tilePos;
                    }
                }
            }
            IsPanning = false;
        }
        else if (input.IsMouseReleased(MouseButton.Left))
            IsPanning = false;

        // Arrow keys to nudge cursor
        int nudgeX = 0, nudgeY = 0;
        if (input.IsActionPressed(InputAction.MenuLeft)) nudgeX = -5;
        if (input.IsActionPressed(InputAction.MenuRight)) nudgeX = 5;
        if (input.IsActionPressed(InputAction.MenuUp)) nudgeY = -5;
        if (input.IsActionPressed(InputAction.MenuDown)) nudgeY = 5;
        if (nudgeX != 0 || nudgeY != 0)
        {
            var candidate = new TilePos(
                Math.Clamp(_cursorTile.X + nudgeX, 0, _surfaceData.Width - 1),
                Math.Clamp(_cursorTile.Y + nudgeY, 0, _surfaceData.Height - 1));
            if (IsTileSelectableWithMargin(candidate.X, candidate.Y, SelectionBorderMarginTiles, out var failureReason))
            {
                _cursorTile = candidate;
                _hasCursor = true;
            }
            else if (failureReason != null)
            {
                ShowInvalidSelectionHint(failureReason);
            }
        }

        // Confirm landing
        if (_hasCursor && (input.IsActionPressed(InputAction.MenuConfirm) || input.IsActionPressed(InputAction.Interact)))
            TryLand(game);

        return true;
    }

    /// <summary>WASD for camera movement, arrow keys reserved for cursor nudge.</summary>
    public override void Update(Game game)
    {
        float dt = game.DeltaTime;

        _selectionPulse += dt * 3f;

        var input = game.Input;
        float camSpeed = 500f / Camera.Zoom;
        if (input.IsActionDown(InputAction.MoveUp)) Camera.Position -= new Vector2(0, camSpeed * dt);
        if (input.IsActionDown(InputAction.MoveDown)) Camera.Position += new Vector2(0, camSpeed * dt);
        if (input.IsActionDown(InputAction.MoveLeft)) Camera.Position -= new Vector2(camSpeed * dt, 0);
        if (input.IsActionDown(InputAction.MoveRight)) Camera.Position += new Vector2(camSpeed * dt, 0);

        ClampCameraPosition();
    }

    // -----------------------------------------------------------------
    //  LANDING
    // -----------------------------------------------------------------

    private void TryLand(Game game)
    {
        if (!_hasCursor) return;
        var terrain = _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y];
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
                TileX: _cursorTile.X,
                TileY: _cursorTile.Y,
                IsMoon: _isMoon,
                MoonPlanetIndex: _moonPlanetIndex,
                MoonIndex: _moonIndex);
            OnLandingConfirmed(game, landing);
        }
        else
        {
            game.ChangeState(new States.PlanetSurfaceState(_starSystem, _planet, _cursorTile.X, _cursorTile.Y));
        }
    }

    private void ShowInvalidSelectionHint(string text)
    {
        _invalidSelectionHint = text;
        _invalidSelectionHintTimer = InvalidSelectionHintDuration;
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

        // Landing cursor
        if (_hasCursor)
        {
            float cx = _cursorTile.X + 0.5f;
            float cy = _cursorTile.Y + 0.5f;
            var cursorScreen = Camera.WorldToScreen(new Vector2(cx, cy));
            RenderSelectionReticle(renderer, cursorScreen, _selectionPulse,
                new Color3(100, 255, 100), new Color3(200, 255, 200));
        }
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
        if (_hasCursor)
        {
            renderer.DrawRectScreen(px, nextY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
            nextY += 10;

            renderer.DrawTextScreen(px, nextY, "LANDING SITE", new Color3(100, 120, 160), 1.3f);
            nextY += 18;

            var terrain = _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y];
            string terrainName = terrain.ToString().ToUpper();
            bool canLand = SurfaceTerrainRules.IsTraversable(terrain);

            byte tr = canLand ? (byte)100 : (byte)255;
            byte tg = canLand ? (byte)255 : (byte)80;
            byte tb = canLand ? (byte)100 : (byte)80;
            renderer.DrawTextScreen(px, nextY, $"TERRAIN: {terrainName}", new Color3(tr, tg, tb), 1.5f);
            nextY += 18;

            renderer.DrawTextScreen(px, nextY, $"POS: ({_cursorTile.X}, {_cursorTile.Y})", new Color3(150, 150, 150), 1.3f);
            nextY += 18;

            // Check if cursor is inside a settlement
            foreach (var s in _surfaceData.Settlements)
            {
                if (_cursorTile.X >= s.TileRect.X && _cursorTile.X < s.TileRect.X + s.TileRect.Width &&
                    _cursorTile.Y >= s.TileRect.Y && _cursorTile.Y < s.TileRect.Y + s.TileRect.Height)
                {
                    renderer.DrawTextScreen(px, nextY, s.Name, new Color3(255, 220, 100), 1.5f);
                    nextY += 18;
                    break;
                }
            }
        }

        if (_invalidSelectionHintTimer > 0f && !string.IsNullOrWhiteSpace(_invalidSelectionHint))
        {
            renderer.DrawRectScreen(px, nextY, InfoPanelW - 24, 20, new Color4(70, 30, 30, 200));
            float hintW = renderer.MeasureText(_invalidSelectionHint, 1.3f);
            renderer.DrawTextScreen(px + (InfoPanelW - 24) / 2f - hintW / 2f, nextY + 3,
                _invalidSelectionHint, new Color3(255, 140, 120), 1.3f);
            nextY += 24;
        }

        // Controls
        float ctrlStartY = IpY + IpH - 110;
        string nudgeText =
            $"{game.Input.GetActionHelpText(InputAction.MenuUp)}/{game.Input.GetActionHelpText(InputAction.MenuDown)}/{game.Input.GetActionHelpText(InputAction.MenuLeft)}/{game.Input.GetActionHelpText(InputAction.MenuRight)}: NUDGE CURSOR";
        renderer.DrawRectScreen(px, ctrlStartY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        if (game.Input.ActiveInputMethod == InputMethod.Gamepad)
        {
            renderer.DrawTextScreen(px, ctrlStartY + 8, "MOUSE CLICK: SELECT SITE", new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 24, "LEFT STICK: PAN", new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 40, "LT/RT: ZOOM", new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 56, nudgeText, new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 72,
                $"{game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper()}: LAND",
                new Color3(100, 255, 100), 1.3f);
        }
        else
        {
            string panText =
                $"{game.Input.GetActionHelpText(InputAction.MoveUp)}/{game.Input.GetActionHelpText(InputAction.MoveDown)}/{game.Input.GetActionHelpText(InputAction.MoveLeft)}/{game.Input.GetActionHelpText(InputAction.MoveRight)}/{game.Input.GetMouseButtonHelpText(MouseButton.Left)}-DRAG: PAN";
            renderer.DrawTextScreen(px, ctrlStartY + 8,
                $"{game.Input.GetMouseButtonHelpText(MouseButton.Left)}: SELECT SITE",
                new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 24, panText, new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 40, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 56, nudgeText, new Color3(180, 180, 180), 1.3f);
            renderer.DrawTextScreen(px, ctrlStartY + 72,
                $"DBLCLICK/{game.Input.GetActionHelpText(InputAction.MenuConfirm).ToUpper()}: LAND",
                new Color3(100, 255, 100), 1.3f);
        }
        renderer.DrawTextScreen(px, ctrlStartY + 88,
            $"{game.Input.GetActionHelpText(InputAction.MenuBack)}: CANCEL",
            new Color3(255, 150, 150), 1.3f);
    }
}
