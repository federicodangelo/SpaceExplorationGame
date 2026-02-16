using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.States;

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

    // Saved camera state from the solar system (restored on close)
    private Vector2 _savedCameraPos;
    private float _savedCameraZoom;

    /// <summary>Open the galaxy map overlay.</summary>
    public void Open(Game game)
    {
        IsOpen = true;

        // Save solar system camera
        _savedCameraPos = game.Camera.Position;
        _savedCameraZoom = game.Camera.Zoom;

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

        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            _selectedSystemIndex = game.Player.CurrentStarSystemIndex;
            game.Camera.Position = _starSystems[_selectedSystemIndex].GalaxyPosition;
        }
        else
        {
            float centerX = GameConfig.GalaxyWidth * GameConfig.TileSize / 2f;
            float centerY = GameConfig.GalaxyHeight * GameConfig.TileSize / 2f;
            game.Camera.Position = new Vector2(centerX, centerY);
        }
        game.Camera.Zoom = 0.5f;
    }

    /// <summary>Close the overlay and restore the solar system camera.</summary>
    public void Close(Game game)
    {
        // Destroy cached textures
        foreach (var tex in _starTextures) game.StarRenderer.DestroyTexture(tex);
        _starTextures.Clear();
        _nebulae.Clear();
        _starSystems.Clear();
        _backgroundStars.Clear();

        // Restore solar system camera
        game.Camera.Position = _savedCameraPos;
        game.Camera.Zoom = _savedCameraZoom;

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
            _starSystems.Clear();
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
        var camera = game.Camera;

        // Close on M or Escape
        if (input.IsKeyPressed(SDL.Scancode.M) || input.IsKeyPressed(SDL.Scancode.Escape))
        {
            Close(game);
            return true;
        }

        // Zoom with mouse wheel
        if (input.MouseWheelY != 0)
        {
            camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            camera.ClampZoom();
        }

        // Mouse panning with left-click drag
        Vector2 currentMouse = new(input.MouseX, input.MouseY);
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
                camera.Position -= delta / camera.Zoom;
                _lastMouseScreen = currentMouse;
            }
        }

        // Mouse hover check
        var mouseWorld = camera.ScreenToWorld(currentMouse);
        _hoveredSystemIndex = -1;
        float bestDist = 30f / camera.Zoom;
        bestDist *= bestDist;

        for (int i = 0; i < _starSystems.Count; i++)
        {
            var diff = mouseWorld - _starSystems[i].GalaxyPosition;
            float dist = diff.LengthSquared();
            if (dist < bestDist)
            {
                bestDist = dist;
                _hoveredSystemIndex = i;
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
        var camera = game.Camera;

        // Camera movement with WASD/arrows
        float camSpeed = 500f / camera.Zoom;
        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
            camera.Position -= new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
            camera.Position += new Vector2(0, camSpeed * dt);
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            camera.Position -= new Vector2(camSpeed * dt, 0);
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            camera.Position += new Vector2(camSpeed * dt, 0);
    }

    /// <summary>Render the galaxy map overlay.</summary>
    public override void Render(Game game)
    {
        if (!IsOpen) return;

        var renderer = game.SpriteRenderer;
        var camera = game.Camera;

        // Dark background to cover the solar system
        renderer.DrawRectScreen(0, 0, GameConfig.WindowWidth, GameConfig.WindowHeight, new Color4(0, 0, 0, 240));

        // Draw background stars
        foreach (var (x, y, brightness) in _backgroundStars)
        {
            var screenPos = camera.WorldToScreen(new Vector2(x, y));
            if (screenPos.X >= 0 && screenPos.X < GameConfig.WindowWidth &&
                screenPos.Y >= 0 && screenPos.Y < GameConfig.WindowHeight)
            {
                renderer.DrawRectScreen(screenPos.X, screenPos.Y,
                    Math.Max(1, camera.Zoom), Math.Max(1, camera.Zoom),
                    new Color3(brightness, brightness, brightness));
            }
        }

        // Draw nebula clouds
        foreach (var (nx, ny, nr, nColor) in _nebulae)
        {
            renderer.DrawFilledCircle(camera, new Vector2(nx, ny), nr, nColor.WithAlpha(20));
            renderer.DrawFilledCircle(camera, new Vector2(nx + nr * 0.3f, ny - nr * 0.2f), nr * 0.7f, nColor.WithAlpha(15));
            renderer.DrawFilledCircle(camera, new Vector2(nx - nr * 0.4f, ny + nr * 0.3f), nr * 0.5f, nColor.WithAlpha(10));
        }

        // Draw FTL range circle around player's current system
        int currentSys = game.Player.CurrentStarSystemIndex;
        if (currentSys >= 0 && currentSys < _starSystems.Count)
        {
            var playerPos = _starSystems[currentSys].GalaxyPosition;
            float ftlRange = GetFtlRange(game);
            renderer.DrawCircle(camera, playerPos, ftlRange,
                new Color4(40, 80, 40, 200), 64);
            float fuelRange = game.Player.ShipFuel / GameConfig.FuelPerDistanceUnit;
            if (fuelRange < ftlRange)
            {
                renderer.DrawCircle(camera, playerPos, fuelRange,
                    new Color4(80, 160, 200, 80));
            }
        }

        // Draw star systems
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
            {
                renderer.DrawTexture(camera, _starTextures[i], sys.GalaxyPosition,
                    (int)texSize, (int)texSize, 0f, alpha);
            }

            if (inRange && !reachable && !isCurrentSystem)
            {
                renderer.DrawFilledCircle(camera, sys.GalaxyPosition, radius * 0.5f,
                    new Color4(255, 40, 40, 60));
            }

            if (isSelected)
            {
                byte ringR = reachable || isCurrentSystem ? (byte)255 : (byte)255;
                byte ringG = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                byte ringB = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                renderer.DrawCircle(camera, sys.GalaxyPosition, radius + 5, new Color3(ringR, ringG, ringB));
            }

            float textScale = Math.Max(1f, camera.Zoom);
            byte labelBright = (byte)(inRange ? 200 : 80);
            renderer.DrawText(camera,
                sys.GalaxyPosition + new Vector2(0, radius + 12),
                sys.Name, new Color3(labelBright, labelBright, labelBright), textScale);
        }

        // Draw mission target markers (pulsing rings on target systems)
        float pulse = (float)(0.5 + 0.5 * Math.Sin(game.GlobalTime * 3.0));
        byte missionAlpha = (byte)(100 + (int)(pulse * 155));
        foreach (var mission in game.Player.ActiveMissions)
        {
            // Target system marker (for incomplete missions)
            if (mission.Status != MissionStatus.Completed &&
                mission.Target.HasSystem && mission.Target.SystemIndex < _starSystems.Count)
            {
                var targetSys = _starSystems[mission.Target.SystemIndex];
                var mc = mission.TypeColor;
                float markerRadius = targetSys.StarRadius + 8;

                // Pulsing outer ring
                renderer.DrawCircle(camera, targetSys.GalaxyPosition, markerRadius,
                    new Color4(mc.R, mc.G, mc.B, missionAlpha));

                // Small diamond icon offset above the star
                DrawMissionDiamond(renderer, camera, targetSys.GalaxyPosition,
                    targetSys.StarRadius, mc, missionAlpha);
            }

            // Turn-in system marker (for completed missions)
            if (mission.Status == MissionStatus.Completed &&
                mission.TurnIn.HasSystem && mission.TurnIn.SystemIndex < _starSystems.Count)
            {
                var turnInSys = _starSystems[mission.TurnIn.SystemIndex];
                float markerRadius = turnInSys.StarRadius + 8;

                // Green pulsing ring for turn-in
                renderer.DrawCircle(camera, turnInSys.GalaxyPosition, markerRadius,
                    new Color4(100, 255, 100, missionAlpha));
                renderer.DrawCircle(camera, turnInSys.GalaxyPosition, markerRadius + 3,
                    new Color4(100, 255, 100, (byte)(missionAlpha / 3)));

                DrawMissionDiamond(renderer, camera, turnInSys.GalaxyPosition,
                    turnInSys.StarRadius, new Color3(100, 255, 100), missionAlpha);
            }
        }

        // Draw player location marker
        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            var playerSys = _starSystems[game.Player.CurrentStarSystemIndex];
            renderer.DrawCircle(camera, playerSys.GalaxyPosition,
                playerSys.StarRadius + 10, new Color3(0, 255, 100));
        }

        // HUD background
        renderer.DrawRectScreen(0, 0, 260, 115, new Color4(0, 0, 0, 160));

        // HUD
        renderer.DrawTextScreen(10, 10, "GALAXY MAP", new Color3(200, 200, 255), 2f);
        renderer.DrawTextScreen(10, 35, $"SEED: {game.Seeds.GalaxySeed}", new Color3(150, 150, 150), 1.5f);
        renderer.DrawTextScreen(10, 55, $"SYSTEMS: {_starSystems.Count}", new Color3(150, 150, 150), 1.5f);

        // Fuel gauge
        renderer.DrawTextScreen(10, 75, $"FUEL: {game.Player.ShipFuel:F1}/{game.Player.ShipMaxFuel:F0}", new Color3(100, 200, 255), 1.5f);
        float fuelBarW = 200;
        renderer.DrawRectScreen(10, 95, fuelBarW, 10, new Color3(40, 40, 40));
        renderer.DrawRectScreen(10, 95, fuelBarW * (game.Player.ShipFuel / game.Player.ShipMaxFuel), 10, new Color3(100, 200, 255));

        if (_selectedSystemIndex >= 0)
        {
            var sys = _starSystems[_selectedSystemIndex];
            bool isCurrentSystem = _selectedSystemIndex == game.Player.CurrentStarSystemIndex;
            float distance = isCurrentSystem ? 0 : GetSystemDistance(game.Player.CurrentStarSystemIndex, _selectedSystemIndex);
            float fuelCost = distance * GameConfig.FuelPerDistanceUnit;
            bool inRange = isCurrentSystem || distance <= GetFtlRange(game);
            bool canAfford = isCurrentSystem || game.Player.ShipFuel >= fuelCost;

            // Check for active missions targeting this system or needing turn-in here
            var missionsHere = game.Player.ActiveMissions.Where(m =>
                m.Target.IsSystem(_selectedSystemIndex) ||
                (m.Status == MissionStatus.Completed && m.TurnIn.IsSystem(_selectedSystemIndex))).ToList();
            int missionExtraHeight = missionsHere.Count > 0 ? (missionsHere.Count * 18 + 5) : 0;

            float panelHeight = 180 + missionExtraHeight;
            float panelY = GameConfig.WindowHeight - panelHeight;
            renderer.DrawRectScreen(0, panelY, 420, panelHeight, new Color4(10, 10, 30, 200));
            renderer.DrawTextScreen(10, panelY + 10, $"SELECTED: {sys.Name}", new Color3(255, 255, 255), 2f);
            renderer.DrawTextScreen(10, panelY + 35, $"CLASS: {sys.StarClass} STAR", new Color3(200, 200, 200), 1.5f);
            renderer.DrawTextScreen(10, panelY + 55, $"PLANETS: {sys.PlanetCount}", new Color3(200, 200, 200), 1.5f);
            renderer.DrawTextScreen(10, panelY + 75, $"STATION: {(sys.HasSpaceStation ? "YES" : "NO")}", new Color3(200, 200, 200), 1.5f);

            // Danger level with color coding
            string dangerText = $"DANGER: {new string('*', sys.DangerLevel)}{new string('.', 5 - sys.DangerLevel)} ({sys.DangerLevel}/5)";
            byte dangerR = sys.DangerLevel <= 2 ? (byte)100 : sys.DangerLevel <= 3 ? (byte)255 : (byte)255;
            byte dangerG = sys.DangerLevel <= 2 ? (byte)255 : sys.DangerLevel <= 3 ? (byte)200 : (byte)80;
            byte dangerB = sys.DangerLevel <= 2 ? (byte)100 : sys.DangerLevel <= 3 ? (byte)50 : (byte)80;
            renderer.DrawTextScreen(10, panelY + 95, dangerText, new Color3(dangerR, dangerG, dangerB), 1.5f);

            // Mission markers for this system
            float infoY = panelY + 115;
            if (missionsHere.Count > 0)
            {
                foreach (var m in missionsHere)
                {
                    var mc = m.TypeColor;
                    string statusTag = m.Status == MissionStatus.Completed ? " [DONE]" : "";
                    renderer.DrawTextScreen(10, infoY, $"[!] {m.TypeLabel}: {m.Title}{statusTag}", new Color3(mc.R, mc.G, mc.B), 1.5f);
                    infoY += 18;
                }
                infoY += 5;
            }

            if (isCurrentSystem)
            {
                renderer.DrawTextScreen(10, infoY, "YOU ARE HERE", new Color3(100, 255, 200), 1.5f);
                renderer.DrawTextScreen(10, infoY + 20, "[ENTER/DBLCLICK] CLOSE MAP", new Color3(100, 255, 100), 1.5f);
            }
            else
            {
                renderer.DrawTextScreen(10, infoY, $"DISTANCE: {distance:F0}", new Color3(200, 200, 200), 1.5f);
                byte fuelR = canAfford ? (byte)100 : (byte)255;
                byte fuelG = canAfford ? (byte)200 : (byte)80;
                byte fuelB = canAfford ? (byte)255 : (byte)80;
                renderer.DrawTextScreen(10, infoY + 20, $"FUEL COST: {fuelCost:F1}", new Color3(fuelR, fuelG, fuelB), 1.5f);

                if (!inRange)
                    renderer.DrawTextScreen(10, infoY + 40, "OUT OF FTL RANGE", new Color3(255, 80, 80), 1.5f);
                else if (!canAfford)
                    renderer.DrawTextScreen(10, infoY + 40, "NOT ENOUGH FUEL", new Color3(255, 80, 80), 1.5f);
                else
                    renderer.DrawTextScreen(10, infoY + 40, "[ENTER/DBLCLICK] TRAVEL", new Color3(100, 255, 100), 1.5f);
            }
        }

        // Controls help background
        renderer.DrawRectScreen(GameConfig.WindowWidth - 310, 5, 310, 110, new Color4(0, 0, 0, 160));

        // Controls help
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 10, "WASD/ARROWS/DRAG: PAN", new Color3(180, 180, 180), 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 30, "SCROLL: ZOOM", new Color3(180, 180, 180), 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 50, "CLICK: SELECT", new Color3(180, 180, 180), 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 70, "DBLCLICK/ENTER: TRAVEL", new Color3(180, 180, 180), 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 90, "M/ESC: CLOSE MAP", new Color3(180, 180, 180), 1.5f);
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
