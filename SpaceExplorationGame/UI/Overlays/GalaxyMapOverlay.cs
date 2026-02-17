using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Base;

namespace SpaceExplorationGame.UI.Overlays;

/// <summary>
/// Full-screen overlay that shows the galaxy map on top of the solar system.
/// The player can browse star systems and travel to them (spending fuel).
/// Opened with M key from SolarSystemState.
/// </summary>
public class GalaxyMapOverlay : OverlayBase
{
    private List<StarSystemData> _starSystems = [];
    private int _selectedSystemIndex = -1;
    private int _hoveredSystemIndex = -1;

    // Background stars (cosmetic)
    private List<BackgroundStar> _backgroundStars = [];

    // Mouse panning state
    private bool _isPanning;
    private Vector2 _lastMouseScreen;

    // Double-click detection
    private float _lastClickTime;
    private int _lastClickSystem = -1;
    private const float DoubleClickTime = 0.4f;

    // Cached star textures
    private List<nint> _starTextures = [];

    // Nebula decorations
    private List<NebulaCloud> _nebulae = [];

    // Camera
    private readonly Camera _camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
        GameConfig.GalaxyMapZoomMin, GameConfig.GalaxyMapZoomMax);

    // Map panel layout
    private const float MapWidth = 800f;
    private const float MapHeight = 700f;
    private const float MapPad = 12f;
    private const float MapHeaderH = 30f;
    private const float InfoPanelWidth = 280f;
    private const float InfoPanelGap = 20f;

    // Computed layout positions (set in Open)
    private float _mapX, _mapY;
    private float _frameX, _frameY, _frameW, _frameH;
    private float _ipX, _ipY, _ipH;

    /// <summary>Open the galaxy map overlay.</summary>
    public void Open(Game game)
    {
        IsOpen = true;

        // Use cached galaxy data
        _starSystems = game.GalaxyData;

        // Generate background stars
        var bgRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xDEADBEEF);
        _backgroundStars.Clear();
        for (int i = 0; i < 500; i++)
        {
            _backgroundStars.Add(new BackgroundStar(
                bgRng.NextFloat(0, GameConfig.GalaxyWidth * GameConfig.TileSize),
                bgRng.NextFloat(0, GameConfig.GalaxyHeight * GameConfig.TileSize),
                (byte)bgRng.NextInt(30, 120)
            ));
        }

        // Generate nebula clouds
        var nebRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xFACEFEED);
        _nebulae.Clear();
        for (int i = 0; i < 8; i++)
        {
            byte[] choices = [(byte)nebRng.NextInt(20, 60), (byte)nebRng.NextInt(10, 40), (byte)nebRng.NextInt(30, 70)];
            int ci = nebRng.NextInt(0, 3);
            _nebulae.Add(new NebulaCloud(
                nebRng.NextFloat(0, GameConfig.GalaxyWidth * GameConfig.TileSize),
                nebRng.NextFloat(0, GameConfig.GalaxyHeight * GameConfig.TileSize),
                nebRng.NextFloat(200, 600),
                new Color3(
                    ci == 0 ? choices[0] : (byte)10,
                    ci == 1 ? choices[1] : (byte)10,
                    ci == 2 ? choices[2] : (byte)15)
            ));
        }

        // Create star textures — use small sizes for the galaxy map since stars
        // appear tiny at galaxy-scale zoom (full-resolution textures are unnecessary
        // and very expensive to generate for 40-80 stars).
        foreach (var tex in _starTextures) game.StarRenderer.DestroyTexture(tex);
        _starTextures.Clear();
        foreach (var system in _starSystems)
        {
            int texSize = Math.Clamp((int)(system.StarRadius * 0.5f), 32, 128);
            _starTextures.Add(game.StarRenderer.CreateTexture(
                texSize, system.StarColor));
        }

        // Select current system and center camera on it
        _selectedSystemIndex = -1;
        _hoveredSystemIndex = -1;
        _lastClickSystem = -1;
        _isPanning = false;

        // Compute panel layout
        _frameW = MapWidth + MapPad * 2;
        _frameH = MapHeight + MapPad * 2 + MapHeaderH;
        float totalW = _frameW + InfoPanelGap + InfoPanelWidth;
        _frameX = (GameConfig.WindowWidth - totalW) / 2f;
        _frameY = (GameConfig.WindowHeight - _frameH) / 2f;
        _mapX = _frameX + MapPad;
        _mapY = _frameY + MapPad + MapHeaderH;
        _ipX = _frameX + _frameW + InfoPanelGap;
        _ipY = _frameY;
        _ipH = _frameH;

        // Configure camera for the map panel
        _camera.ViewportWidth = (int)MapWidth;
        _camera.ViewportHeight = (int)MapHeight;
        _camera.ViewportOffsetX = _mapX;
        _camera.ViewportOffsetY = _mapY;

        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            _selectedSystemIndex = game.Player.CurrentStarSystemIndex;
            _camera.Position = _starSystems[_selectedSystemIndex].GalaxyPosition;
        }
        else
        {
            float centerX = GameConfig.GalaxyWidth * GameConfig.TileSize / 2f;
            float centerY = GameConfig.GalaxyHeight * GameConfig.TileSize / 2f;
            _camera.Position = new Vector2(centerX, centerY);
        }
        _camera.Zoom = GameConfig.GalaxyMapZoomDefault;
    }

    /// <summary>Close the overlay and restore the solar system camera.</summary>
    public void Close(Game game)
    {
        // Destroy cached textures
        foreach (var tex in _starTextures) game.StarRenderer.DestroyTexture(tex);
        _starTextures.Clear();
        _nebulae.Clear();
        _backgroundStars.Clear();

        base.Close();
    }

    /// <summary>Calculate distance between two star systems in world pixels.</summary>
    private float GetSystemDistance(int indexA, int indexB)
    {
        if (indexA < 0 || indexB < 0 || indexA >= _starSystems.Count || indexB >= _starSystems.Count)
            return float.MaxValue;
        return (_starSystems[indexA].GalaxyPosition - _starSystems[indexB].GalaxyPosition).Length();
    }

    /// <summary>Calculate fuel cost for a jump between two systems.</summary>
    private float GetFuelCost(int fromIndex, int toIndex)
    {
        return GetSystemDistance(fromIndex, toIndex) * GameConfig.FuelPerDistanceUnit;
    }

    /// <summary>Get the effective FTL range from equipped parts.</summary>
    private float GetFtlRange(Game game)
    {
        var stats = game.Player.GetCombinedStats();
        return stats.FtlRange > 0 ? stats.FtlRange : GameConfig.FtlMaxRange;
    }

    /// <summary>Check if a system is reachable from the player's current system.</summary>
    private bool IsSystemReachable(Game game, int targetIndex)
    {
        int current = game.Player.CurrentStarSystemIndex;
        if (current == targetIndex) return true;
        float distance = GetSystemDistance(current, targetIndex);
        float fuelCost = distance * GameConfig.FuelPerDistanceUnit;
        return distance <= GetFtlRange(game) && game.Player.ShipFuel >= fuelCost;
    }

    /// <summary>Check if a system is within FTL range (ignoring fuel).</summary>
    private bool IsInFtlRange(Game game, int fromIndex, int targetIndex)
    {
        return GetSystemDistance(fromIndex, targetIndex) <= GetFtlRange(game);
    }

    /// <summary>Attempt to travel to the currently selected system.</summary>
    private void TravelToSelected(Game game)
    {
        if (_selectedSystemIndex < 0) return;
        int current = game.Player.CurrentStarSystemIndex;
        if (_selectedSystemIndex == current)
        {
            // Already here — just close the overlay
            Close(game);
        }
        else if (IsSystemReachable(game, _selectedSystemIndex))
        {
            float fuelCost = GetFuelCost(current, _selectedSystemIndex);
            game.Player.TrySpendFuel(fuelCost);
            game.Player.CurrentStarSystemIndex = _selectedSystemIndex;
            var targetSystem = _starSystems[_selectedSystemIndex];
            // Clean up overlay resources before changing state
            foreach (var tex in _starTextures) game.StarRenderer.DestroyTexture(tex);
            _starTextures.Clear();
            _nebulae.Clear();
            _backgroundStars.Clear();
            IsOpen = false;
            game.ChangeState(new SolarSystemState(targetSystem));
        }
    }

    /// <summary>
    /// Handle input once per frame. Returns true if overlay consumed input.
    /// </summary>
    public override bool UpdateInput(Game game)
    {
        if (!IsOpen) return false;

        var input = game.Input;

        // Close on M or Escape
        if (input.IsKeyPressed(SDL.Scancode.M) || input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close(game);
            return true;
        }

        // Zoom with mouse wheel — zoom toward cursor position
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
        if (input.MouseWheelY != 0)
        {
            var worldBeforeZoom = _camera.ScreenToWorld(currentMouse);
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
            var worldAfterZoom = _camera.ScreenToWorld(currentMouse);
            _camera.Position += worldBeforeZoom - worldAfterZoom;
        }

        // Mouse panning with left-click drag
        if (input.IsMousePressed(1))
        {
            _lastMouseScreen = currentMouse;
            _isPanning = false;
        }
        if (input.IsMouseDown(1))
        {
            Vector2 delta = currentMouse - _lastMouseScreen;
            if (delta.LengthSquared() > 4f)
            {
                _isPanning = true;
                _camera.Position -= delta / _camera.Zoom;
                _lastMouseScreen = currentMouse;
            }
        }

        // Mouse hover check — only within the map panel area.
        // Uses screen-space distance so hit area feels consistent regardless
        // of zoom level, and at least as big as the star's rendered radius
        // (StarRadius * 2 * zoom) or a minimum of 20 screen px.
        _hoveredSystemIndex = -1;
        bool mouseInMap = currentMouse.X >= _mapX && currentMouse.X < _mapX + MapWidth &&
                          currentMouse.Y >= _mapY && currentMouse.Y < _mapY + MapHeight;
        if (mouseInMap)
        {
            float bestScreenDistSq = float.MaxValue;
            for (int i = 0; i < _starSystems.Count; i++)
            {
                var screenPos = _camera.WorldToScreen(_starSystems[i].GalaxyPosition);
                var screenDiff = currentMouse - screenPos;
                float screenDistSq = screenDiff.LengthSquared();
                float starScreenRadius = _starSystems[i].StarRadius * 2f * _camera.Zoom;
                float hitRadius = MathF.Max(starScreenRadius, 20f);
                if (screenDistSq < hitRadius * hitRadius && screenDistSq < bestScreenDistSq)
                {
                    bestScreenDistSq = screenDistSq;
                    _hoveredSystemIndex = i;
                }
            }
        }

        // Click to select / double-click to travel
        if (input.IsMouseReleased(1))
        {
            if (!_isPanning && _hoveredSystemIndex >= 0)
            {
                float now = (float)game.GlobalTime;
                if (_hoveredSystemIndex == _lastClickSystem && (now - _lastClickTime) < DoubleClickTime)
                {
                    _selectedSystemIndex = _hoveredSystemIndex;
                    TravelToSelected(game);
                    _lastClickSystem = -1;
                }
                else
                {
                    _selectedSystemIndex = _hoveredSystemIndex;
                    _lastClickTime = now;
                    _lastClickSystem = _hoveredSystemIndex;
                }
            }
            else if (!_isPanning)
            {
                _lastClickSystem = -1;
            }
            _isPanning = false;
        }

        // Enter to travel to selected system
        if (input.IsKeyPressed(SDL.Scancode.Return) && _selectedSystemIndex >= 0)
        {
            TravelToSelected(game);
        }

        return true;
    }

    /// <summary>
    /// Fixed timestep update for camera movement.
    /// </summary>
    public override void Update(Game game, float dt)
    {
        if (!IsOpen) return;

        var input = game.Input;

        // Camera movement with WASD/arrows
        float camSpeed = 500f / _camera.Zoom;
        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
            _camera.Position -= new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
            _camera.Position += new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            _camera.Position -= new Vector2(camSpeed * dt, 0);
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            _camera.Position += new Vector2(camSpeed * dt, 0);
    }

    /// <summary>Render the galaxy map overlay.</summary>
    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        var camera = _camera;

        // Semi-transparent dark overlay
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, new Color4(0, 0, 0, 180));

        // ── Map container frame ──
        DrawFrameWithHeader(renderer, _frameX, _frameY, _frameW, _frameH, "STAR CHART");

        // Inner map border
        renderer.DrawRectScreen(_mapX - 1, _mapY - 1, MapWidth + 2, MapHeight + 2, new Color4(50, 65, 110, 180));

        // ── Galaxy content (clipped to map panel) ──
        renderer.SetClipRect(_mapX, _mapY, MapWidth, MapHeight);

        // Background stars
        foreach (var (x, y, brightness) in _backgroundStars)
        {
            var screenPos = camera.WorldToScreen(new Vector2(x, y));
            renderer.DrawRectScreen(screenPos.X, screenPos.Y,
                Math.Max(1, camera.Zoom), Math.Max(1, camera.Zoom),
                new Color3(brightness, brightness, brightness));
        }

        // Nebula clouds
        foreach (var (nx, ny, nr, nColor) in _nebulae)
        {
            renderer.DrawFilledCircle(camera, new Vector2(nx, ny), nr, nColor.WithAlpha(20));
            renderer.DrawFilledCircle(camera, new Vector2(nx + nr * 0.3f, ny - nr * 0.2f), nr * 0.7f, nColor.WithAlpha(15));
            renderer.DrawFilledCircle(camera, new Vector2(nx - nr * 0.4f, ny + nr * 0.3f), nr * 0.5f, nColor.WithAlpha(10));
        }

        // FTL range circle around player's current system
        int currentSys = game.Player.CurrentStarSystemIndex;
        if (currentSys >= 0 && currentSys < _starSystems.Count)
        {
            var playerPos = _starSystems[currentSys].GalaxyPosition;
            float ftlRange = GetFtlRange(game);
            renderer.DrawCircle(camera, playerPos, ftlRange, new Color4(40, 80, 40, 200), 64);
            float fuelRange = game.Player.ShipFuel / GameConfig.FuelPerDistanceUnit;
            if (fuelRange < ftlRange)
                renderer.DrawCircle(camera, playerPos, fuelRange, new Color4(80, 160, 200, 80));
        }

        // Star systems
        for (int i = 0; i < _starSystems.Count; i++)
        {
            var sys = _starSystems[i];
            bool isSelected = i == _selectedSystemIndex;
            bool isHovered = i == _hoveredSystemIndex;
            bool isCurrentSystem = i == currentSys;
            bool inRange = currentSys >= 0 && (isCurrentSystem || IsInFtlRange(game, currentSys, i));
            bool reachable = currentSys >= 0 && IsSystemReachable(game, i);

            float radius = sys.StarRadius;
            float texSize = radius * 4;
            if (isHovered) texSize *= 1.3f;

            byte alpha = 255;
            if (!inRange) alpha = 80;
            else if (!reachable && !isCurrentSystem) alpha = 160;

            if (i < _starTextures.Count)
                renderer.DrawTexture(camera, _starTextures[i], sys.GalaxyPosition, (int)texSize, (int)texSize, 0f, alpha);

            if (inRange && !reachable && !isCurrentSystem)
                renderer.DrawFilledCircle(camera, sys.GalaxyPosition, radius * 0.5f, new Color4(255, 40, 40, 60));

            if (isSelected)
            {
                byte ringG = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                byte ringB = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                renderer.DrawCircle(camera, sys.GalaxyPosition, radius + 5, new Color3(255, ringG, ringB));
            }

            float textScale = Math.Max(1f, camera.Zoom);
            byte labelBright = (byte)(inRange ? 200 : 80);
            renderer.DrawText(camera, sys.GalaxyPosition + new Vector2(0, radius + 12),
                sys.Name, new Color3(labelBright, labelBright, labelBright), textScale);
        }

        // Mission target markers
        float pulse = (float)(0.5 + 0.5 * Math.Sin(game.GlobalTime * 3.0));
        byte missionAlpha = (byte)(100 + (int)(pulse * 155));
        foreach (var mission in game.Player.ActiveMissions)
        {
            if (mission.Status != MissionStatus.Completed &&
                mission.Target.HasSystem && mission.Target.SystemIndex < _starSystems.Count)
            {
                var targetSys = _starSystems[mission.Target.SystemIndex];
                var mc = mission.TypeColor;
                renderer.DrawCircle(camera, targetSys.GalaxyPosition, targetSys.StarRadius + 8,
                    new Color4(mc.R, mc.G, mc.B, missionAlpha));
                DrawMissionDiamond(renderer, camera, targetSys.GalaxyPosition, targetSys.StarRadius, mc, missionAlpha);
            }

            if (mission.Status == MissionStatus.Completed &&
                mission.TurnIn.HasSystem && mission.TurnIn.SystemIndex < _starSystems.Count)
            {
                var turnInSys = _starSystems[mission.TurnIn.SystemIndex];
                renderer.DrawCircle(camera, turnInSys.GalaxyPosition, turnInSys.StarRadius + 8,
                    new Color4(100, 255, 100, missionAlpha));
                renderer.DrawCircle(camera, turnInSys.GalaxyPosition, turnInSys.StarRadius + 11,
                    new Color4(100, 255, 100, (byte)(missionAlpha / 3)));
                DrawMissionDiamond(renderer, camera, turnInSys.GalaxyPosition, turnInSys.StarRadius,
                    new Color3(100, 255, 100), missionAlpha);
            }
        }

        // Player location marker
        if (currentSys >= 0 && currentSys < _starSystems.Count)
        {
            var playerSys = _starSystems[currentSys];
            renderer.DrawCircle(camera, playerSys.GalaxyPosition, playerSys.StarRadius + 10, new Color3(0, 255, 100));
        }

        renderer.ClearClipRect();

        // ── Info panel (right side) ──
        DrawFrame(renderer, _ipX, _ipY, InfoPanelWidth, _ipH, 220);

        // Info panel header
        renderer.DrawRectScreen(_ipX, _ipY, InfoPanelWidth, 30, new Color4(30, 40, 70, 240));
        renderer.DrawRectScreen(_ipX, _ipY + 29, InfoPanelWidth, 1, new Color4(60, 80, 140, 200));
        string navLabel = "NAVIGATION DATA";
        float navLabelW = renderer.MeasureText(navLabel, 1.8f);
        renderer.DrawTextScreen(_ipX + InfoPanelWidth / 2f - navLabelW / 2f, _ipY + 6, navLabel, new Color3(140, 170, 220), 1.8f);

        float cx = _ipX + 12;
        float cy = _ipY + 40;

        // Galaxy info
        renderer.DrawTextScreen(cx, cy, "SYSTEMS", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(cx, cy + 16, _starSystems.Count.ToString(), new Color3(200, 220, 255), 1.8f);

        renderer.DrawRectScreen(cx, cy + 42, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));

        // Fuel gauge
        renderer.DrawTextScreen(cx, cy + 52, "FUEL", new Color3(100, 120, 160), 1.3f);
        renderer.DrawTextScreen(cx, cy + 68, $"{game.Player.ShipFuel:F1} / {game.Player.ShipMaxFuel:F0}", new Color3(100, 200, 255), 1.8f);
        float fuelBarW = InfoPanelWidth - 24;
        renderer.DrawRectScreen(cx, cy + 94, fuelBarW, 10, new Color3(40, 40, 40));
        float fuelPct = game.Player.ShipMaxFuel > 0 ? game.Player.ShipFuel / game.Player.ShipMaxFuel : 0;
        renderer.DrawRectScreen(cx, cy + 94, fuelBarW * fuelPct, 10, new Color3(100, 200, 255));

        // Show fuel cost preview on bar if a system is selected
        if (_selectedSystemIndex >= 0 && _selectedSystemIndex != game.Player.CurrentStarSystemIndex)
        {
            float jumpDist = GetSystemDistance(game.Player.CurrentStarSystemIndex, _selectedSystemIndex);
            float jumpCost = jumpDist * GameConfig.FuelPerDistanceUnit;
            float costPct = game.Player.ShipMaxFuel > 0 ? jumpCost / game.Player.ShipMaxFuel : 0;
            float remainPct = fuelPct - costPct;
            if (remainPct < 0) remainPct = 0;
            // Draw the consumed segment in orange/red between remaining and current fuel
            bool canAffordJump = game.Player.ShipFuel >= jumpCost;
            var costColor = canAffordJump ? new Color4(255, 160, 40, 200) : new Color4(255, 60, 60, 200);
            float costStartX = cx + fuelBarW * remainPct;
            float costW = fuelBarW * fuelPct - fuelBarW * remainPct;
            if (costW > 0)
                renderer.DrawRectScreen(costStartX, cy + 94, costW, 10, costColor);
        }

        renderer.DrawRectScreen(cx, cy + 114, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));

        // Selected system info
        float selY = cy + 124;
        if (_selectedSystemIndex >= 0)
        {
            var sys = _starSystems[_selectedSystemIndex];
            bool isCurrentSystem = _selectedSystemIndex == game.Player.CurrentStarSystemIndex;
            float distance = isCurrentSystem ? 0 : GetSystemDistance(game.Player.CurrentStarSystemIndex, _selectedSystemIndex);
            float fuelCost = distance * GameConfig.FuelPerDistanceUnit;
            bool inRange = isCurrentSystem || distance <= GetFtlRange(game);
            bool canAfford = isCurrentSystem || game.Player.ShipFuel >= fuelCost;

            renderer.DrawTextScreen(cx, selY, "SELECTED", new Color3(100, 120, 160), 1.3f);
            renderer.DrawTextScreen(cx, selY + 16, sys.Name.ToUpper(), new Color3(200, 220, 255), 1.8f);

            renderer.DrawRectScreen(cx, selY + 42, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));

            renderer.DrawTextScreen(cx, selY + 52, $"CLASS {sys.StarClass} STAR", new Color3(200, 200, 200), 1.5f);
            renderer.DrawTextScreen(cx, selY + 72, $"PLANETS: {sys.PlanetCount}", new Color3(200, 200, 200), 1.5f);
            renderer.DrawTextScreen(cx, selY + 92,
                $"STATION: {(sys.HasSpaceStation ? "YES" : "NO")}",
                sys.HasSpaceStation ? new Color3(100, 255, 200) : new Color3(120, 120, 120), 1.5f);

            // Danger level
            string dangerText = $"DANGER: {new string('*', sys.DangerLevel)}{new string('.', 5 - sys.DangerLevel)}";
            byte dangerR = sys.DangerLevel <= 2 ? (byte)100 : sys.DangerLevel <= 3 ? (byte)255 : (byte)255;
            byte dangerG = sys.DangerLevel <= 2 ? (byte)255 : sys.DangerLevel <= 3 ? (byte)200 : (byte)80;
            byte dangerB = sys.DangerLevel <= 2 ? (byte)100 : sys.DangerLevel <= 3 ? (byte)50 : (byte)80;
            renderer.DrawTextScreen(cx, selY + 112, dangerText, new Color3(dangerR, dangerG, dangerB), 1.5f);

            // Missions targeting this system
            var missionsHere = game.Player.ActiveMissions.Where(m =>
                m.Target.IsSystem(_selectedSystemIndex) ||
                (m.Status == MissionStatus.Completed && m.TurnIn.IsSystem(_selectedSystemIndex))).ToList();

            float missionY = selY + 136;
            if (missionsHere.Count > 0)
            {
                renderer.DrawRectScreen(cx, missionY - 4, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
                foreach (var m in missionsHere)
                {
                    var mc = m.TypeColor;
                    string statusTag = m.Status == MissionStatus.Completed ? " [DONE]" : "";
                    renderer.DrawTextScreen(cx, missionY, $"[!] {m.TypeLabel}{statusTag}", new Color3(mc.R, mc.G, mc.B), 1.3f);
                    missionY += 16;
                }
                missionY += 4;
            }

            renderer.DrawRectScreen(cx, missionY, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
            missionY += 10;

            if (isCurrentSystem)
            {
                renderer.DrawTextScreen(cx, missionY, "YOU ARE HERE", new Color3(100, 255, 200), 1.5f);
                renderer.DrawTextScreen(cx, missionY + 20, "[ENTER] CLOSE MAP", new Color3(100, 255, 100), 1.5f);
            }
            else
            {
                renderer.DrawTextScreen(cx, missionY, $"DISTANCE: {distance:F0}", new Color3(200, 200, 200), 1.5f);
                byte fuelR = canAfford ? (byte)100 : (byte)255;
                byte fuelG = canAfford ? (byte)200 : (byte)80;
                byte fuelB = canAfford ? (byte)255 : (byte)80;
                renderer.DrawTextScreen(cx, missionY + 20, $"FUEL COST: {fuelCost:F1}", new Color3(fuelR, fuelG, fuelB), 1.5f);

                if (!inRange)
                    renderer.DrawTextScreen(cx, missionY + 40, "OUT OF FTL RANGE", new Color3(255, 80, 80), 1.5f);
                else if (!canAfford)
                    renderer.DrawTextScreen(cx, missionY + 40, "NOT ENOUGH FUEL", new Color3(255, 80, 80), 1.5f);
                else
                    renderer.DrawTextScreen(cx, missionY + 40, "[ENTER] TRAVEL", new Color3(100, 255, 100), 1.5f);
            }
        }
        else
        {
            renderer.DrawTextScreen(cx, selY, "NO SYSTEM SELECTED", new Color3(100, 120, 160), 1.5f);
            renderer.DrawTextScreen(cx, selY + 20, "CLICK A STAR TO SELECT", new Color3(140, 140, 160), 1.3f);
        }

        // Controls (bottom of info panel)
        float ctrlY = _ipY + _ipH - 110;
        renderer.DrawRectScreen(cx, ctrlY, InfoPanelWidth - 24, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(cx, ctrlY + 8, "WASD/ARROWS/DRAG: PAN", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 24, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 40, "CLICK: SELECT SYSTEM", new Color3(180, 180, 180), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 56, "DBLCLICK/ENTER: TRAVEL", new Color3(100, 255, 100), 1.3f);
        renderer.DrawTextScreen(cx, ctrlY + 72, "M/ESC: CLOSE MAP", new Color3(255, 150, 150), 1.3f);
    }

    private static void DrawMissionDiamond(SpriteRenderer renderer, Camera camera,
        Vector2 starPos, float starRadius, Color3 color, byte alpha)
    {
        var iconPos = starPos + new Vector2(0, -(starRadius + 16));
        float diamondSize = 4f;
        var screenIcon = camera.WorldToScreen(iconPos);
        if (screenIcon.X >= -20 && screenIcon.X < GameConfig.WindowWidth + 20 &&
            screenIcon.Y >= -20 && screenIcon.Y < GameConfig.WindowHeight + 20)
        {
            float ds = diamondSize * Math.Max(1f, camera.Zoom * 0.5f);
            var c = new Color4(color.R, color.G, color.B, alpha);
            renderer.DrawLineScreen(screenIcon.X, screenIcon.Y - ds, screenIcon.X + ds, screenIcon.Y, c);
            renderer.DrawLineScreen(screenIcon.X + ds, screenIcon.Y, screenIcon.X, screenIcon.Y + ds, c);
            renderer.DrawLineScreen(screenIcon.X, screenIcon.Y + ds, screenIcon.X - ds, screenIcon.Y, c);
            renderer.DrawLineScreen(screenIcon.X - ds, screenIcon.Y, screenIcon.X, screenIcon.Y - ds, c);
        }
    }
}
