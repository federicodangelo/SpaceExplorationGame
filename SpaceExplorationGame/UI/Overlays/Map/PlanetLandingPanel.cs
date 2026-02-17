using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Overlays.Map.Base;

namespace SpaceExplorationGame.UI.Overlays.Map;

/// <summary>
/// Map panel showing a planet/moon terrain overview with Camera-based zoom and pan.
/// The player clicks to choose a landing site, double-clicks or presses Enter/E to confirm landing.
/// </summary>
public class PlanetLandingPanel : MapPanelBase
{
    private StarSystemData _starSystem = null!;
    private PlanetData _planet = null!;
    private PlanetSurfaceData _surfaceData = null!;

    // Terrain texture (1 pixel = 1 tile)
    private nint _terrainTexture;

    // Landing cursor
    private TilePos _cursorTile;
    private bool _hasCursor;
    private float _cursorPulse;

    // Double-click tracking
    private float _lastClickTime;
    private TilePos _lastClickTile = new(-1, -1);

    // Moon tracking
    private bool _isMoon;
    private int _moonPlanetIndex;
    private int _moonIndex;

    // ── Public state for the overlay's HUD ──

    /// <summary>Name of the planet/moon being scanned.</summary>
    public string PlanetName => _planet?.Name ?? "UNKNOWN";

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

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    /// <summary>Not used directly. Call <see cref="OpenWithPlanet"/> instead.</summary>
    public override void Open(Game game) { }

    /// <summary>Open the panel for a specific planet or moon.</summary>
    public void OpenWithPlanet(Game game, StarSystemData starSystem, PlanetData planet,
        bool isMoon = false, int moonPlanetIndex = -1, int moonIndex = -1)
    {
        _starSystem = starSystem;
        _planet = planet;
        _isMoon = isMoon;
        _moonPlanetIndex = moonPlanetIndex;
        _moonIndex = moonIndex;

        _cursorPulse = 0f;
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

    public override void Close(Game game) { }

    /// <summary>Destroy the cached terrain texture.</summary>
    public void Cleanup()
    {
        if (_terrainTexture != nint.Zero)
        {
            SDL.DestroyTexture(_terrainTexture);
            _terrainTexture = nint.Zero;
        }
    }

    public override void SetupCamera(Game game)
    {
        if (_surfaceData == null) return;

        // World: 1 unit = 1 tile
        float worldW = _surfaceData.Width;
        float worldH = _surfaceData.Height;

        // Fit the entire terrain in the viewport
        float fitZoom = Math.Min(MapW / worldW, MapH / worldH);
        Camera.Zoom = fitZoom;
        Camera.ZoomMin = fitZoom;       // can't zoom out past full terrain
        Camera.ZoomMax = fitZoom * 8f;
        Camera.Position = new Vector2(worldW / 2f, worldH / 2f);
        Camera.ClampZoom();
        ClampCameraPosition();
    }

    // ─────────────────────────────────────────────────────────────
    //  INPUT
    // ─────────────────────────────────────────────────────────────

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
        _cursorPulse += dt * 3f;

        var input = game.Input;
        float camSpeed = 500f / Camera.Zoom;
        if (input.IsKeyDown(SDL.Scancode.W)) Camera.Position -= new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.S)) Camera.Position += new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.A)) Camera.Position -= new Vector2(camSpeed * dt, 0);
        if (input.IsKeyDown(SDL.Scancode.D)) Camera.Position += new Vector2(camSpeed * dt, 0);

        ClampCameraPosition();
    }

    /// <summary>Keep the camera within the terrain bounds so no empty space is visible.</summary>
    private void ClampCameraPosition()
    {
        if (_surfaceData == null) return;

        float worldW = _surfaceData.Width;
        float worldH = _surfaceData.Height;

        // Half the viewport in world units
        float halfViewW = MapW / (2f * Camera.Zoom);
        float halfViewH = MapH / (2f * Camera.Zoom);

        // If terrain is smaller than viewport (fully zoomed out), center it
        float minX = halfViewW;
        float maxX = worldW - halfViewW;
        float minY = halfViewH;
        float maxY = worldH - halfViewH;

        float cx = (minX <= maxX) ? Math.Clamp(Camera.Position.X, minX, maxX) : worldW / 2f;
        float cy = (minY <= maxY) ? Math.Clamp(Camera.Position.Y, minY, maxY) : worldH / 2f;

        Camera.Position = new Vector2(cx, cy);
    }

    // ─────────────────────────────────────────────────────────────
    //  LANDING
    // ─────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────
    //  RENDERING
    // ─────────────────────────────────────────────────────────────

    public override void RenderContent(Game game, SpriteRenderer renderer)
    {
        if (_surfaceData == null || _terrainTexture == nint.Zero) return;

        var camera = Camera;
        float worldW = _surfaceData.Width;
        float worldH = _surfaceData.Height;

        // Terrain texture via Camera transform
        var topLeft = camera.WorldToScreen(Vector2.Zero);
        var bottomRight = camera.WorldToScreen(new Vector2(worldW, worldH));
        var dstRect = new SDL.FRect
        {
            X = topLeft.X, Y = topLeft.Y,
            W = bottomRight.X - topLeft.X, H = bottomRight.Y - topLeft.Y
        };
        SDL.RenderTexture(game.Renderer, _terrainTexture, nint.Zero, in dstRect);

        // Tile scale factors (for marker sizing)
        float tileScreenW = (bottomRight.X - topLeft.X) / worldW;
        float tileScreenH = (bottomRight.Y - topLeft.Y) / worldH;

        // Settlement markers
        foreach (var settlement in _surfaceData.Settlements)
        {
            float sx = settlement.TileRect.X + settlement.TileRect.Width / 2f;
            float sy = settlement.TileRect.Y + settlement.TileRect.Height / 2f;
            var settScreen = camera.WorldToScreen(new Vector2(sx, sy));

            float sw = settlement.TileRect.Width * tileScreenW;
            float sh = settlement.TileRect.Height * tileScreenH;

            // Settlement outline
            renderer.DrawRectScreen(settScreen.X - sw / 2f - 1, settScreen.Y - sh / 2f - 1,
                sw + 2, sh + 2, new Color4(255, 220, 100, 180));

            // Settlement label
            float labelScale = 1f;
            float textW = renderer.MeasureText(settlement.Name, labelScale);
            renderer.DrawRectScreen(settScreen.X - textW / 2f - 2, settScreen.Y - sh / 2f - 14 * labelScale,
                textW + 4, 12 * labelScale, new Color4(0, 0, 0, 160));
            renderer.DrawTextScreen(settScreen.X - textW / 2f, settScreen.Y - sh / 2f - 13 * labelScale,
                settlement.Name, new Color3(255, 220, 100), labelScale);
        }

        // Landing cursor
        if (_hasCursor)
        {
            float cx = _cursorTile.X + 0.5f;
            float cy = _cursorTile.Y + 0.5f;
            var cursorScreen = camera.WorldToScreen(new Vector2(cx, cy));

            float pulseSize = 6f + MathF.Sin(_cursorPulse) * 2f;
            float outerSize = pulseSize + 4f;
            byte alpha = (byte)(180 + 40 * MathF.Sin(_cursorPulse));

            // Reticle cross
            renderer.DrawRectScreen(cursorScreen.X - outerSize, cursorScreen.Y - 1, outerSize * 2, 2, new Color4(100, 255, 100, alpha));
            renderer.DrawRectScreen(cursorScreen.X - 1, cursorScreen.Y - outerSize, 2, outerSize * 2, new Color4(100, 255, 100, alpha));

            // Corner brackets
            float bLen = 5f;
            renderer.DrawRectScreen(cursorScreen.X - outerSize, cursorScreen.Y - outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X - outerSize, cursorScreen.Y - outerSize, 2, bLen, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X + outerSize - bLen, cursorScreen.Y - outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X + outerSize, cursorScreen.Y - outerSize, 2, bLen, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X - outerSize, cursorScreen.Y + outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X - outerSize, cursorScreen.Y + outerSize - bLen, 2, bLen, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X + outerSize - bLen, cursorScreen.Y + outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cursorScreen.X + outerSize, cursorScreen.Y + outerSize - bLen, 2, bLen, new Color3(200, 255, 200));
        }
    }



    // ─────────────────────────────────────────────────────────────
    //  INFO PANEL
    // ─────────────────────────────────────────────────────────────

    public override void RenderInfoPanel(Game game, SpriteRenderer renderer)
    {
        RenderInfoPanelHeader(renderer, "PLANET DATA");

        float px = IpX + 12;
        float py = IpY + 40;

        renderer.DrawTextScreen(px, py, "NAME", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(px, py + 16, _planet.Name.ToUpper(), new Color3(200, 220, 255), 1.8f);

        renderer.DrawRectScreen(px, py + 42, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        renderer.DrawTextScreen(px, py + 52, "TYPE", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(px, py + 68, _planet.Type.ToString().ToUpper(), new Color3(200, 200, 200), 1.8f);

        renderer.DrawRectScreen(px, py + 94, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        renderer.DrawTextScreen(px, py + 104, "SIZE", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(px, py + 120, $"{_surfaceData.Width} x {_surfaceData.Height} TILES", new Color3(200, 200, 200), 1.8f);

        renderer.DrawRectScreen(px, py + 146, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        int settlementCount = _surfaceData.Settlements.Count;
        renderer.DrawTextScreen(px, py + 156, "SETTLEMENTS", new Color3(100, 120, 160), 1.3f);
        string settText = settlementCount > 0 ? settlementCount.ToString() : "NONE";
        byte settR = settlementCount > 0 ? (byte)255 : (byte)120;
        byte settG = settlementCount > 0 ? (byte)220 : (byte)120;
        byte settB = settlementCount > 0 ? (byte)100 : (byte)120;
        renderer.DrawTextScreen(px, py + 172, settText, new Color3(settR, settG, settB), 1.8f);

        // Settlement names
        float nextY = py + 198;
        if (settlementCount > 0)
        {
            renderer.DrawRectScreen(px, nextY, InfoPanelW - 24, 1, new Color4(40, 55, 90, 150));
            float settListY = nextY + 10;
            foreach (var s in _surfaceData.Settlements)
            {
                renderer.DrawTextScreen(px + 4, settListY, $"> {s.Name}", new Color3(255, 220, 100), 1.3f);
                settListY += 16;
            }
            nextY = settListY + 6;
        }

        // ── Landing site info ──
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

    // ─────────────────────────────────────────────────────────────
    //  TERRAIN TEXTURE
    // ─────────────────────────────────────────────────────────────

    private nint CreateTerrainTexture(Game game)
    {
        int w = _surfaceData.Width;
        int h = _surfaceData.Height;
        var pixels = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var terrain = _surfaceData.Tiles[x, y];
                var (r, g, b) = PlanetSurfaceGenerator.GetTerrainColor(terrain);

                int hash = (x * 374761393 + y * 668265263) ^ (x * y);
                float variation = ((hash & 0xFF) - 128) / 800f;

                int idx = (y * w + x) * 4;
                pixels[idx + 0] = (byte)Math.Clamp(r + r * variation, 0, 255);
                pixels[idx + 1] = (byte)Math.Clamp(g + g * variation, 0, 255);
                pixels[idx + 2] = (byte)Math.Clamp(b + b * variation, 0, 255);
                pixels[idx + 3] = 255;
            }
        }

        // Mark settlement tiles
        foreach (var s in _surfaceData.Settlements)
        {
            for (int sx = s.TileRect.X; sx < s.TileRect.X + s.TileRect.Width && sx < w; sx++)
            {
                for (int sy = s.TileRect.Y; sy < s.TileRect.Y + s.TileRect.Height && sy < h; sy++)
                {
                    int idx = (sy * w + sx) * 4;
                    pixels[idx + 0] = 100;
                    pixels[idx + 1] = 100;
                    pixels[idx + 2] = 120;
                    pixels[idx + 3] = 255;
                }
            }
        }

        unsafe
        {
            fixed (byte* pixelPtr = pixels)
            {
                var surface = SDL.CreateSurfaceFrom(w, h, SDL.PixelFormat.ABGR8888, (nint)pixelPtr, w * 4);
                if (surface == nint.Zero) return nint.Zero;

                var texture = SDL.CreateTextureFromSurface(game.Renderer, surface);
                SDL.DestroySurface(surface);

                if (texture != nint.Zero)
                    SDL.SetTextureScaleMode(texture, SDL.ScaleMode.Nearest);

                return texture;
            }
        }
    }
}
