using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Orbital landing site selection overlay: shows a zoomed-out terrain map of the planet.
/// The player clicks to choose a landing site and confirms with Enter/E.
/// </summary>
public class PlanetLandingOverlay : OverlayBase
{
    private StarSystemData _starSystem = null!;
    private PlanetData _planet = null!;
    private PlanetSurfaceData _surfaceData = null!;

    // Orbital map rendering
    private nint _terrainTexture;
    private const float MapDisplaySize = 700f;  // display size in pixels

    // Landing cursor
    private TilePos _cursorTile;
    private bool _hasCursor = false;
    private float _cursorPulse = 0f;  // animation timer

    // Double-click tracking
    private float _lastClickTime;
    private TilePos _lastClickTile = new(-1, -1);
    private const float DoubleClickTime = 0.4f;

    // Moon tracking (if landing on a moon)
    private bool _isMoon;
    private int _moonPlanetIndex;
    private int _moonIndex;

    public void Open(StarSystemData starSystem, PlanetData planet, Game game,
        bool isMoon = false, int moonPlanetIndex = -1, int moonIndex = -1)
    {
        _starSystem = starSystem;
        _planet = planet;
        _isMoon = isMoon;
        _moonPlanetIndex = moonPlanetIndex;
        _moonIndex = moonIndex;

        // Reset state
        _cursorPulse = 0f;
        _lastClickTime = 0f;
        _lastClickTile = new TilePos(-1, -1);

        // Generate the same surface we'll land on
        var rng = game.Seeds.GetPlanetSurfaceRandom(_starSystem.Index, _planet.Index);
        _surfaceData = PlanetSurfaceGenerator.Generate(rng, _planet);

        // Create a terrain overview texture (1 pixel = 1 tile)
        _terrainTexture = CreateTerrainTexture(game);

        // Default cursor at center
        _cursorTile = new TilePos(_surfaceData.Width / 2, _surfaceData.Height / 2);
        _hasCursor = true;

        IsOpen = true;
    }

    public override void Close()
    {
        base.Close();
    }

    /// <summary>Destroy the cached terrain texture. Call when leaving the solar system.</summary>
    public void Cleanup()
    {
        if (_terrainTexture != nint.Zero)
        {
            SDL.DestroyTexture(_terrainTexture);
            _terrainTexture = nint.Zero;
        }
    }

    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        // Click to select / double-click to land
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
        if (input.IsMouseReleased(1))
        {
            var tilePos = ScreenToTile(currentMouse);

            if (tilePos.X >= 0 && tilePos.X < _surfaceData.Width &&
                tilePos.Y >= 0 && tilePos.Y < _surfaceData.Height)
            {
                float now = (float)game.GlobalTime;
                if (tilePos == _lastClickTile && (now - _lastClickTime) < DoubleClickTime && _hasCursor)
                {
                    // Double-click: confirm landing
                    TryLand(game);
                    _lastClickTile = new TilePos(-1, -1);
                }
                else
                {
                    // Single click: select
                    _cursorTile = tilePos;
                    _hasCursor = true;
                    _lastClickTime = now;
                    _lastClickTile = tilePos;
                }
            }
        }

        // WASD to nudge cursor
        int nudgeX = 0, nudgeY = 0;
        if (input.IsKeyPressed(SDL.Scancode.Left) || input.IsKeyPressed(SDL.Scancode.A)) nudgeX = -5;
        if (input.IsKeyPressed(SDL.Scancode.Right) || input.IsKeyPressed(SDL.Scancode.D)) nudgeX = 5;
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W)) nudgeY = -5;
        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S)) nudgeY = 5;

        if (nudgeX != 0 || nudgeY != 0)
        {
            _cursorTile = new TilePos(
                Math.Clamp(_cursorTile.X + nudgeX, 0, _surfaceData.Width - 1),
                Math.Clamp(_cursorTile.Y + nudgeY, 0, _surfaceData.Height - 1));
            _hasCursor = true;
        }

        // Confirm landing
        if (_hasCursor && (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E)))
        {
            TryLand(game);
        }

        // Cancel — close overlay and return to solar system view
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Cleanup();
            Close();
        }

        return true; // overlay consumes all input while open
    }

    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;
        _cursorPulse += dt * 3f;
    }

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
        Close();
        game.ChangeState(new States.PlanetSurfaceState(_starSystem, _planet, _cursorTile.X, _cursorTile.Y));
    }

    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        int w = GameConfig.WindowWidth;
        int h = GameConfig.WindowHeight;

        // Semi-transparent dark overlay so the solar system is visible behind
        renderer.DrawRectScreen(0, 0, w, h, new Color4(0, 0, 0, 180));

        // Draw the terrain map (centered, shifted left to make room for info panel)
        float displayW = MapDisplaySize;
        float displayH = MapDisplaySize;
        var mapCenter = GetMapCenter();
        float mapX = mapCenter.X - displayW / 2f - 130f;
        float mapY = mapCenter.Y - displayH / 2f;

        // ── Map container frame ──
        const float pad = 12f;           // padding between border and map
        const float headerH = 30f;      // header strip height
        float frameX = mapX - pad;
        float frameY = mapY - pad - headerH;
        float frameW = displayW + pad * 2;
        float frameH = displayH + pad * 2 + headerH;

        DrawFrameWithHeader(renderer, frameX, frameY, frameW, frameH, $"SURFACE SCAN - {_planet.Name.ToUpper()}");

        // Inner map border (thin bright line right around the terrain)
        renderer.DrawRectScreen(mapX - 1, mapY - 1, displayW + 2, displayH + 2, new Color4(50, 65, 110, 180));

        // Terrain texture
        var dstRect = new SDL.FRect { X = mapX, Y = mapY, W = displayW, H = displayH };
        SDL.RenderTexture(game.Renderer, _terrainTexture, nint.Zero, in dstRect);

        // Scale factors: tile to screen pixel
        float tileToScreenX = displayW / _surfaceData.Width;
        float tileToScreenY = displayH / _surfaceData.Height;

        // Draw settlement markers
        foreach (var settlement in _surfaceData.Settlements)
        {
            float sx = mapX + (settlement.TileRect.X + settlement.TileRect.Width / 2f) * tileToScreenX;
            float sy = mapY + (settlement.TileRect.Y + settlement.TileRect.Height / 2f) * tileToScreenY;
            float sw = settlement.TileRect.Width * tileToScreenX;
            float sh = settlement.TileRect.Height * tileToScreenY;

            // Settlement outline
            renderer.DrawRectScreen(sx - sw / 2f - 1, sy - sh / 2f - 1, sw + 2, sh + 2, new Color4(255, 220, 100, 180));

            // Settlement label
            float labelScale = 1f;
            float textW = renderer.MeasureText(settlement.Name, labelScale);
            renderer.DrawRectScreen(sx - textW / 2f - 2, sy - sh / 2f - 14 * labelScale, textW + 4, 12 * labelScale, new Color4(0, 0, 0, 160));
            renderer.DrawTextScreen(sx - textW / 2f, sy - sh / 2f - 13 * labelScale, settlement.Name, new Color3(255, 220, 100), labelScale);
        }

        // Draw landing cursor
        if (_hasCursor)
        {
            float cx = mapX + (_cursorTile.X + 0.5f) * tileToScreenX;
            float cy = mapY + (_cursorTile.Y + 0.5f) * tileToScreenY;
            float pulseSize = 6f + MathF.Sin(_cursorPulse) * 2f;
            float outerSize = pulseSize + 4f;

            // Outer reticle
            byte alpha = (byte)(180 + 40 * MathF.Sin(_cursorPulse));
            renderer.DrawRectScreen(cx - outerSize, cy - 1, outerSize * 2, 2, new Color4(100, 255, 100, alpha));
            renderer.DrawRectScreen(cx - 1, cy - outerSize, 2, outerSize * 2, new Color4(100, 255, 100, alpha));

            // Corner brackets
            float bLen = 5f;
            // Top-left
            renderer.DrawRectScreen(cx - outerSize, cy - outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cx - outerSize, cy - outerSize, 2, bLen, new Color3(200, 255, 200));
            // Top-right
            renderer.DrawRectScreen(cx + outerSize - bLen, cy - outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cx + outerSize, cy - outerSize, 2, bLen, new Color3(200, 255, 200));
            // Bottom-left
            renderer.DrawRectScreen(cx - outerSize, cy + outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cx - outerSize, cy + outerSize - bLen, 2, bLen, new Color3(200, 255, 200));
            // Bottom-right
            renderer.DrawRectScreen(cx + outerSize - bLen, cy + outerSize, bLen, 2, new Color3(200, 255, 200));
            renderer.DrawRectScreen(cx + outerSize, cy + outerSize - bLen, 2, bLen, new Color3(200, 255, 200));

            // Terrain info at cursor
            var terrain = _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y];
            string terrainName = terrain.ToString().ToUpper();
            bool canLand = terrain is not (TerrainType.Water or TerrainType.Lava or TerrainType.Void);

            // Check if cursor is near a settlement
            string? nearSettlement = null;
            foreach (var s in _surfaceData.Settlements)
            {
                if (_cursorTile.X >= s.TileRect.X && _cursorTile.X < s.TileRect.X + s.TileRect.Width &&
                    _cursorTile.Y >= s.TileRect.Y && _cursorTile.Y < s.TileRect.Y + s.TileRect.Height)
                {
                    nearSettlement = s.Name;
                    break;
                }
            }

            // Info panel near cursor (offset to bottom-right)
            float infoPanelX = cx + 15;
            float infoPanelY = cy + 15;

            // Keep panel on screen
            float panelW = 180;
            float panelH = nearSettlement != null ? 60 : 45;
            if (infoPanelX + panelW > GameConfig.WindowWidth - 10) infoPanelX = cx - panelW - 15;
            if (infoPanelY + panelH > GameConfig.WindowHeight - 10) infoPanelY = cy - panelH - 15;

            DrawFrame(renderer, infoPanelX - 4, infoPanelY - 4, panelW, panelH, 200);

            byte tr = canLand ? (byte)100 : (byte)255;
            byte tg = canLand ? (byte)255 : (byte)80;
            byte tb = canLand ? (byte)100 : (byte)80;
            renderer.DrawTextScreen(infoPanelX, infoPanelY, $"TERRAIN: {terrainName}", new Color3(tr, tg, tb), 1.5f);
            renderer.DrawTextScreen(infoPanelX, infoPanelY + 16,
                $"POS: ({_cursorTile.X}, {_cursorTile.Y})", new Color3(150, 150, 150), 1.3f);

            if (nearSettlement != null)
            {
                renderer.DrawTextScreen(infoPanelX, infoPanelY + 32, nearSettlement, new Color3(255, 220, 100), 1.5f);
            }
        }

        // --- HUD ---
        const float hudMargin = 5f;

        // Title (centered above map)
        string title = $"ORBITAL VIEW - {_planet.Name.ToUpper()}";
        float titleW = renderer.MeasureText(title, 2.5f);
        float titleBgW = titleW + 30;
        DrawFrame(renderer, GameConfig.WindowWidth / 2f - titleBgW / 2f, hudMargin + 3, titleBgW, 32, 200);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - titleW / 2f, hudMargin + 9, title, new Color3(180, 200, 255), 2.5f);

        // Planet info panel (positioned to the right of the map)
        float infoPanelW = 260;
        float ipX = mapX + displayW + pad + 20f;
        float ipY = frameY;
        float ipH = frameH;
        DrawFrame(renderer, ipX, ipY, infoPanelW, ipH, 220);

        // Planet info header
        renderer.DrawRectScreen(ipX, ipY, infoPanelW, 30, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(ipX, ipY + 29, infoPanelW, 1, new Color4(60, 80, 140, 200));
        string infoLabel = "PLANET DATA";
        float infoLabelW = renderer.MeasureText(infoLabel, 1.8f);
        renderer.DrawTextScreen(ipX + infoPanelW / 2f - infoLabelW / 2f, ipY + 6, infoLabel, new Color3(140, 170, 220), 1.8f);

        // Planet info content
        float ipContentY = ipY + 40;
        renderer.DrawTextScreen(ipX + 12, ipContentY, "NAME", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(ipX + 12, ipContentY + 16, _planet.Name.ToUpper(), new Color3(200, 220, 255), 1.8f);

        renderer.DrawRectScreen(ipX + 12, ipContentY + 42, infoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        renderer.DrawTextScreen(ipX + 12, ipContentY + 52, "TYPE", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(ipX + 12, ipContentY + 68, _planet.Type.ToString().ToUpper(), new Color3(200, 200, 200), 1.8f);

        renderer.DrawRectScreen(ipX + 12, ipContentY + 94, infoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        renderer.DrawTextScreen(ipX + 12, ipContentY + 104, "SIZE", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(ipX + 12, ipContentY + 120, $"{_surfaceData.Width} x {_surfaceData.Height} TILES", new Color3(200, 200, 200), 1.8f);

        renderer.DrawRectScreen(ipX + 12, ipContentY + 146, infoPanelW - 24, 1, new Color4(40, 55, 90, 150));

        int settlementCount = _surfaceData.Settlements.Count;
        renderer.DrawTextScreen(ipX + 12, ipContentY + 156, "SETTLEMENTS", new Color3(100, 120, 160), 1.3f);
        string settText = settlementCount > 0 ? settlementCount.ToString() : "NONE";
        byte settR = settlementCount > 0 ? (byte)255 : (byte)120;
        byte settG = settlementCount > 0 ? (byte)220 : (byte)120;
        byte settB = settlementCount > 0 ? (byte)100 : (byte)120;
        renderer.DrawTextScreen(ipX + 12, ipContentY + 172, settText, new Color3(settR, settG, settB), 1.8f);

        // Settlement names (if any)
        if (settlementCount > 0)
        {
            renderer.DrawRectScreen(ipX + 12, ipContentY + 198, infoPanelW - 24, 1, new Color4(40, 55, 90, 150));
            float settListY = ipContentY + 208;
            foreach (var s in _surfaceData.Settlements)
            {
                renderer.DrawTextScreen(ipX + 16, settListY, $"> {s.Name}", new Color3(255, 220, 100), 1.3f);
                settListY += 16;
            }
        }

        // Controls (bottom of info panel)
        float ctrlStartY = ipY + ipH - 110;
        renderer.DrawRectScreen(ipX + 12, ctrlStartY, infoPanelW - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(ipX + 12, ctrlStartY + 8, "CLICK: SELECT SITE", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(ipX + 12, ctrlStartY + 24, "WASD/ARROWS: NUDGE", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(ipX + 12, ctrlStartY + 40, "DBLCLICK/ENTER: LAND", new Color3(100, 255, 100), 1.3f);
        renderer.DrawTextScreen(ipX + 12, ctrlStartY + 56, "ESC: CANCEL", new Color3(255, 150, 150), 1.3f);

        // Landing prompt
        if (_hasCursor)
        {
            var terrain = _surfaceData.Tiles[_cursorTile.X, _cursorTile.Y];
            bool canLand = terrain is not (TerrainType.Water or TerrainType.Lava or TerrainType.Void);
            string prompt = canLand ? "[DBLCLICK/ENTER] CONFIRM LANDING" : "CANNOT LAND ON " + terrain.ToString().ToUpper();
            byte pr = canLand ? (byte)100 : (byte)255;
            byte pg = canLand ? (byte)255 : (byte)80;
            byte pb = canLand ? (byte)100 : (byte)80;
            float promptW = renderer.MeasureText(prompt, 2f);
            DrawFrame(renderer, GameConfig.WindowWidth / 2f - promptW / 2f - 6, GameConfig.WindowHeight - 50 - hudMargin, promptW + 12, 28, 200);
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - promptW / 2f, GameConfig.WindowHeight - 45 - hudMargin, prompt, new Color3(pr, pg, pb), 2f);
        }
    }

    /// <summary>Creates a texture where each pixel represents one terrain tile.</summary>
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

                // Add subtle per-tile variation (same hash as PlanetSurfaceState)
                int hash = (x * 374761393 + y * 668265263) ^ (x * y);
                float variation = ((hash & 0xFF) - 128) / 800f;

                int idx = (y * w + x) * 4;
                pixels[idx + 0] = (byte)Math.Clamp(r + r * variation, 0, 255);
                pixels[idx + 1] = (byte)Math.Clamp(g + g * variation, 0, 255);
                pixels[idx + 2] = (byte)Math.Clamp(b + b * variation, 0, 255);
                pixels[idx + 3] = 255;
            }
        }

        // Mark settlement tiles brighter
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

        // Create SDL texture from pixel data
        unsafe
        {
            fixed (byte* pixelPtr = pixels)
            {
                var surface = SDL.CreateSurfaceFrom(w, h, SDL.PixelFormat.ABGR8888, (nint)pixelPtr, w * 4);
                if (surface == nint.Zero)
                    return nint.Zero;

                // Use nearest-neighbor filtering for crispy pixels
                var texture = SDL.CreateTextureFromSurface(game.Renderer, surface);
                SDL.DestroySurface(surface);

                if (texture != nint.Zero)
                {
                    SDL.SetTextureScaleMode(texture, SDL.ScaleMode.Nearest);
                }

                return texture;
            }
        }
    }

    private Vector2 GetMapCenter()
    {
        return new Vector2(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f + 10);
    }

    private TilePos ScreenToTile(Vector2 screenPos)
    {
        var mapCenter = GetMapCenter();
        float displayW = MapDisplaySize;
        float displayH = MapDisplaySize;
        float mapX = mapCenter.X - displayW / 2f - 130f;
        float mapY = mapCenter.Y - displayH / 2f;

        float relX = (screenPos.X - mapX) / displayW;
        float relY = (screenPos.Y - mapY) / displayH;

        int tileX = (int)(relX * _surfaceData.Width);
        int tileY = (int)(relY * _surfaceData.Height);

        return new TilePos(tileX, tileY);
    }
}
