using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>
/// Map panel showing a planet/moon terrain overview with Camera-based zoom and pan.
/// The player clicks to choose a landing site, double-clicks or presses Enter/E to confirm landing.
/// </summary>
public class PlanetLandingPanel : PlanetMapPanelBase
{
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
            return terrain is not (TerrainType.Water or TerrainType.Lava or TerrainType.Void);
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

        // Generate surface
        var rng = game.Seeds.GetPlanetSurfaceRandom(starSystem.Index, planet.Index);
        _surfaceData = PlanetSurfaceGenerator.Generate(rng, planet);

        // Create terrain overview texture
        _terrainTexture = CreateTerrainTexture(game);

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

        HandleZoomAndPan(input, currentMouse);
        ClampCameraPosition();

        // Click to select / double-click to land
        if (input.IsMouseReleased(1) && !IsPanning)
        {
            if (IsMouseInMap(currentMouse))
            {
                var worldPos = Camera.ScreenToWorld(currentMouse);
                int tileX = (int)worldPos.X;
                int tileY = (int)worldPos.Y;

                if (tileX >= 0 && tileX < _surfaceData.Width &&
                    tileY >= 0 && tileY < _surfaceData.Height)
                {
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
        else if (input.IsMouseReleased(1))
            IsPanning = false;

        // Arrow keys to nudge cursor
        int nudgeX = 0, nudgeY = 0;
        if (input.IsKeyPressed(SDL.Scancode.Left)) nudgeX = -5;
        if (input.IsKeyPressed(SDL.Scancode.Right)) nudgeX = 5;
        if (input.IsKeyPressed(SDL.Scancode.Up)) nudgeY = -5;
        if (input.IsKeyPressed(SDL.Scancode.Down)) nudgeY = 5;
        if (nudgeX != 0 || nudgeY != 0)
        {
            _cursorTile = new TilePos(
                Math.Clamp(_cursorTile.X + nudgeX, 0, _surfaceData.Width - 1),
                Math.Clamp(_cursorTile.Y + nudgeY, 0, _surfaceData.Height - 1));
            _hasCursor = true;
        }

        // Confirm landing
        if (_hasCursor && (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E)))
            TryLand(game);

        return true;
    }

    /// <summary>WASD for camera movement, arrow keys reserved for cursor nudge.</summary>
    public override void Update(Game game, float dt)
    {
        _selectionPulse += dt * 3f;

        var input = game.Input;
        float camSpeed = 500f / Camera.Zoom;
        if (input.IsKeyDown(SDL.Scancode.W)) Camera.Position -= new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.S)) Camera.Position += new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.A)) Camera.Position -= new Vector2(camSpeed * dt, 0);
        if (input.IsKeyDown(SDL.Scancode.D)) Camera.Position += new Vector2(camSpeed * dt, 0);

        ClampCameraPosition();
    }

    // -----------------------------------------------------------------
    //  LANDING
    // -----------------------------------------------------------------

    private void TryLand(Game game)
    {
        if (!_hasCursor) return;
        var terrain = _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y];
        if (terrain is TerrainType.Water or TerrainType.Lava or TerrainType.Void) return;

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
        game.ChangeState(new States.PlanetSurfaceState(_starSystem, _planet, _cursorTile.X, _cursorTile.Y));
    }

    // -----------------------------------------------------------------
    //  RENDERING
    // -----------------------------------------------------------------

    public override void RenderContent(Game game, SpriteRenderer renderer)
    {
        if (_surfaceData == null || _terrainTexture == nint.Zero) return;

        var (tileScreenW, tileScreenH) = RenderTerrainTexture(game);

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

    public override void RenderInfoPanel(Game game, SpriteRenderer renderer)
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
            bool canLand = terrain is not (TerrainType.Water or TerrainType.Lava or TerrainType.Void);

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

        // Controls
        float ctrlStartY = IpY + IpH - 110;
        renderer.DrawRectScreen(px, ctrlStartY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(px, ctrlStartY + 8, "CLICK: SELECT SITE", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlStartY + 24, "WASD/DRAG: PAN", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlStartY + 40, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlStartY + 56, "ARROWS: NUDGE CURSOR", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(px, ctrlStartY + 72, "DBLCLICK/ENTER: LAND", new Color3(100, 255, 100), 1.3f);
        renderer.DrawTextScreen(px, ctrlStartY + 88, "ESC: CANCEL", new Color3(255, 150, 150), 1.3f);
    }
}
