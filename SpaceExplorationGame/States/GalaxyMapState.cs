using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI;
using SpaceExplorationGame.UI.Overlays;

namespace SpaceExplorationGame.States;

/// <summary>
/// Galaxy map state: Shows all star systems, player can select and travel to them.
/// </summary>
public class GalaxyMapState : GameState
{
    public override GameStateType Type => GameStateType.GalaxyMap;

    private List<StarSystemData> _starSystems = [];
    private int _selectedSystemIndex = -1;
    private int _hoveredSystemIndex = -1;

    // Background stars (cosmetic)
    private List<(float X, float Y, byte Brightness)> _backgroundStars = [];

    // Mouse panning state
    private bool _isPanning;
    private Vector2 _lastMouseScreen;

    // Double-click detection
    private float _lastClickTime;
    private int _lastClickSystem = -1;
    private const float DoubleClickTime = 0.4f; // seconds

    // Cached star textures for the galaxy map
    private List<nint> _starTextures = [];

    // Nebula decorations
    private List<(float X, float Y, float Radius, byte R, byte G, byte B)> _nebulae = [];

    // Pause menu overlay
    private readonly PauseMenuOverlay _pauseOverlay = new();

    public override void Enter(Game game)
    {
        // Generate galaxy
        var galaxyRng = game.Seeds.GetGalaxyRandom();
        _starSystems = GalaxyGenerator.Generate(galaxyRng);

        // Center camera on galaxy
        float centerX = GameConfig.GalaxyWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.GalaxyHeight * GameConfig.TileSize / 2f;
        game.Camera.Position = new Vector2(centerX, centerY);
        game.Camera.Zoom = 0.5f;

        // Create star system entities
        foreach (var system in _starSystems)
        {
            game.EcsWorld.Create(
                new Transform(system.GalaxyPosition),
                new ECS.Components.Sprite
                {
                    Width = (int)(system.StarRadius * 2),
                    Height = (int)(system.StarRadius * 2),
                    R = system.StarR,
                    G = system.StarG,
                    B = system.StarB,
                    A = 255,
                    UseColor = true
                },
                new StarSystemMarker
                {
                    SystemIndex = system.Index,
                    Name = system.Name,
                    StarClass = system.StarClass
                },
                new Label { Text = system.Name, OffsetY = (int)(system.StarRadius + 12) }
            );
        }

        // Generate background stars
        var bgRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xDEADBEEF);
        for (int i = 0; i < 500; i++)
        {
            _backgroundStars.Add((
                bgRng.NextFloat(0, GameConfig.GalaxyWidth * GameConfig.TileSize),
                bgRng.NextFloat(0, GameConfig.GalaxyHeight * GameConfig.TileSize),
                (byte)bgRng.NextInt(30, 120)
            ));
        }

        // Generate nebula clouds for visual depth
        var nebRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xFACEFEED);
        for (int i = 0; i < 8; i++)
        {
            byte[] choices = [(byte)nebRng.NextInt(20, 60), (byte)nebRng.NextInt(10, 40), (byte)nebRng.NextInt(30, 70)];
            int ci = nebRng.NextInt(0, 3);
            _nebulae.Add((
                nebRng.NextFloat(0, GameConfig.GalaxyWidth * GameConfig.TileSize),
                nebRng.NextFloat(0, GameConfig.GalaxyHeight * GameConfig.TileSize),
                nebRng.NextFloat(200, 600),
                ci == 0 ? choices[0] : (byte)10,
                ci == 1 ? choices[1] : (byte)10,
                ci == 2 ? choices[2] : (byte)15
            ));
        }

        // Create star textures for each system
        _starTextures.Clear();
        foreach (var system in _starSystems)
        {
            int texSize = Math.Max(12, (int)(system.StarRadius * 4));
            _starTextures.Add(game.Textures.CreateStarTexture(
                texSize, system.StarR, system.StarG, system.StarB));
        }

        // If player has a current system, select it
        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            _selectedSystemIndex = game.Player.CurrentStarSystemIndex;
        }
        else
        {
            // Start at a random system near the center
            _selectedSystemIndex = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _starSystems.Count; i++)
            {
                float dx = _starSystems[i].GalaxyPosition.X - centerX;
                float dy = _starSystems[i].GalaxyPosition.Y - centerY;
                float dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    _selectedSystemIndex = i;
                }
            }
            game.Player.CurrentStarSystemIndex = _selectedSystemIndex;
        }
    }

    public override void Exit(Game game)
    {
        // Destroy cached textures
        foreach (var tex in _starTextures) SDL.DestroyTexture(tex);
        _starTextures.Clear();
        _nebulae.Clear();
        _starSystems.Clear();
        _backgroundStars.Clear();
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
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
            game.ChangeState(new SolarSystemState(_starSystems[_selectedSystemIndex]));
        }
        else if (IsSystemReachable(game, _selectedSystemIndex))
        {
            float fuelCost = GetFuelCost(current, _selectedSystemIndex);
            game.Player.TrySpendFuel(fuelCost);
            game.Player.CurrentStarSystemIndex = _selectedSystemIndex;
            game.ChangeState(new SolarSystemState(_starSystems[_selectedSystemIndex]));
        }
    }

    public override void Update(Game game, float dt)
    {
        var input = game.Input;
        var camera = game.Camera;

        // Pause menu overlay
        if (_pauseOverlay.Update(game, input))
            return;

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
            if (delta.LengthSquared() > 4f) // moved more than 2px
            {
                _isPanning = true;
                camera.Position -= delta / camera.Zoom;
                _lastMouseScreen = currentMouse;
            }
        }

        // Mouse hover check
        var mouseWorld = camera.ScreenToWorld(currentMouse);
        _hoveredSystemIndex = -1;
        float bestDist = 30f / camera.Zoom; // 30 pixel hit radius
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

        // Click to select / double-click to travel (on mouse release, only if not panning)
        if (input.IsMouseReleased(1))
        {
            if (!_isPanning && _hoveredSystemIndex >= 0)
            {
                float now = (float)game.GlobalTime;
                if (_hoveredSystemIndex == _lastClickSystem && (now - _lastClickTime) < DoubleClickTime)
                {
                    // Double-click: travel to system
                    _selectedSystemIndex = _hoveredSystemIndex;
                    TravelToSelected(game);
                    _lastClickSystem = -1;
                }
                else
                {
                    // Single click: select
                    _selectedSystemIndex = _hoveredSystemIndex;
                    _lastClickTime = now;
                    _lastClickSystem = _hoveredSystemIndex;
                }
            }
            else if (!_isPanning)
            {
                // Clicked on empty space, reset double-click
                _lastClickSystem = -1;
            }
            _isPanning = false;
        }

        // Enter to travel to selected system
        if (input.IsKeyPressed(SDL.Scancode.Return) && _selectedSystemIndex >= 0)
        {
            TravelToSelected(game);
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = game.Camera;

        // Draw background stars
        foreach (var (x, y, brightness) in _backgroundStars)
        {
            var screenPos = camera.WorldToScreen(new Vector2(x, y));
            if (screenPos.X >= 0 && screenPos.X < GameConfig.WindowWidth &&
                screenPos.Y >= 0 && screenPos.Y < GameConfig.WindowHeight)
            {
                renderer.DrawRectScreen(screenPos.X, screenPos.Y,
                    Math.Max(1, camera.Zoom), Math.Max(1, camera.Zoom),
                    brightness, brightness, brightness);
            }
        }

        // Draw nebula clouds
        foreach (var (nx, ny, nr, nrr, ng, nb) in _nebulae)
        {
            renderer.DrawFilledCircle(camera, new Vector2(nx, ny), nr, nrr, ng, nb, 20);
            renderer.DrawFilledCircle(camera, new Vector2(nx + nr * 0.3f, ny - nr * 0.2f), nr * 0.7f, nrr, ng, nb, 15);
            renderer.DrawFilledCircle(camera, new Vector2(nx - nr * 0.4f, ny + nr * 0.3f), nr * 0.5f, nrr, ng, nb, 10);
        }

        // Draw FTL range circle around player's current system
        int currentSys = game.Player.CurrentStarSystemIndex;
        if (currentSys >= 0 && currentSys < _starSystems.Count)
        {
            var playerPos = _starSystems[currentSys].GalaxyPosition;
            float ftlRange = GetFtlRange(game);
            // Max FTL range circle
            renderer.DrawCircle(camera, playerPos, ftlRange,
                40, 80, 40, 200, 64);
            // Fuel-limited range circle (may be smaller than FTL max)
            float fuelRange = game.Player.ShipFuel / GameConfig.FuelPerDistanceUnit;
            if (fuelRange < ftlRange)
            {
                renderer.DrawCircle(camera, playerPos, fuelRange,
                    80, 160, 200, 80);
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

            // Alpha based on reachability
            byte alpha = 255;
            if (!inRange) alpha = 80;
            else if (!reachable && !isCurrentSystem) alpha = 160;

            // Draw star texture
            if (i < _starTextures.Count)
            {
                renderer.DrawTexture(camera, _starTextures[i], sys.GalaxyPosition,
                    (int)texSize, (int)texSize, 0f, alpha);
            }

            // Red tint overlay for not-enough-fuel stars
            if (inRange && !reachable && !isCurrentSystem)
            {
                renderer.DrawFilledCircle(camera, sys.GalaxyPosition, radius * 0.5f,
                    255, 40, 40, 60);
            }

            // Selection ring
            if (isSelected)
            {
                byte ringR = reachable || isCurrentSystem ? (byte)255 : (byte)255;
                byte ringG = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                byte ringB = reachable || isCurrentSystem ? (byte)255 : (byte)80;
                renderer.DrawCircle(camera, sys.GalaxyPosition, radius + 5, ringR, ringG, ringB);
            }

            // Draw name label
            float textScale = Math.Max(1f, camera.Zoom);
            byte labelBright = (byte)(inRange ? 200 : 80);
            renderer.DrawText(camera,
                sys.GalaxyPosition + new Vector2(0, radius + 12),
                sys.Name, labelBright, labelBright, labelBright, textScale);
        }

        // Draw player location marker
        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            var playerSys = _starSystems[game.Player.CurrentStarSystemIndex];
            renderer.DrawCircle(camera, playerSys.GalaxyPosition,
                playerSys.StarRadius + 10, 0, 255, 100);
        }

        // HUD background
        renderer.DrawRectScreen(0, 0, 260, 115, 0, 0, 0, 160);

        // HUD
        renderer.DrawTextScreen(10, 10, "GALAXY MAP", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"SEED: {game.Seeds.GalaxySeed}", 150, 150, 150, 1.5f);
        renderer.DrawTextScreen(10, 55, $"SYSTEMS: {_starSystems.Count}", 150, 150, 150, 1.5f);

        // Fuel gauge
        renderer.DrawTextScreen(10, 75, $"FUEL: {game.Player.ShipFuel:F1}/{game.Player.ShipMaxFuel:F0}", 100, 200, 255, 1.5f);
        float fuelBarW = 200;
        renderer.DrawRectScreen(10, 95, fuelBarW, 10, 40, 40, 40);
        renderer.DrawRectScreen(10, 95, fuelBarW * (game.Player.ShipFuel / game.Player.ShipMaxFuel), 10, 100, 200, 255);

        if (_selectedSystemIndex >= 0)
        {
            var sys = _starSystems[_selectedSystemIndex];
            bool isCurrentSystem = _selectedSystemIndex == game.Player.CurrentStarSystemIndex;
            float distance = isCurrentSystem ? 0 : GetSystemDistance(game.Player.CurrentStarSystemIndex, _selectedSystemIndex);
            float fuelCost = distance * GameConfig.FuelPerDistanceUnit;
            bool inRange = isCurrentSystem || distance <= GetFtlRange(game);
            bool canAfford = isCurrentSystem || game.Player.ShipFuel >= fuelCost;

            float panelY = GameConfig.WindowHeight - 160;
            renderer.DrawRectScreen(0, panelY, 420, 160, 10, 10, 30, 200);
            renderer.DrawTextScreen(10, panelY + 10, $"SELECTED: {sys.Name}", 255, 255, 255, 2f);
            renderer.DrawTextScreen(10, panelY + 35, $"CLASS: {sys.StarClass} STAR", 200, 200, 200, 1.5f);
            renderer.DrawTextScreen(10, panelY + 55, $"PLANETS: {sys.PlanetCount}", 200, 200, 200, 1.5f);
            renderer.DrawTextScreen(10, panelY + 75, $"STATION: {(sys.HasSpaceStation ? "YES" : "NO")}", 200, 200, 200, 1.5f);

            if (isCurrentSystem)
            {
                renderer.DrawTextScreen(10, panelY + 95, "YOU ARE HERE", 100, 255, 200, 1.5f);
                renderer.DrawTextScreen(10, panelY + 115, "[ENTER/DBLCLICK] ENTER SYSTEM", 100, 255, 100, 1.5f);
            }
            else
            {
                renderer.DrawTextScreen(10, panelY + 95, $"DISTANCE: {distance:F0}", 200, 200, 200, 1.5f);
                byte fuelR = canAfford ? (byte)100 : (byte)255;
                byte fuelG = canAfford ? (byte)200 : (byte)80;
                byte fuelB = canAfford ? (byte)255 : (byte)80;
                renderer.DrawTextScreen(10, panelY + 115, $"FUEL COST: {fuelCost:F1}", fuelR, fuelG, fuelB, 1.5f);

                if (!inRange)
                    renderer.DrawTextScreen(10, panelY + 135, "OUT OF FTL RANGE", 255, 80, 80, 1.5f);
                else if (!canAfford)
                    renderer.DrawTextScreen(10, panelY + 135, "NOT ENOUGH FUEL", 255, 80, 80, 1.5f);
                else
                    renderer.DrawTextScreen(10, panelY + 135, "[ENTER/DBLCLICK] TRAVEL", 100, 255, 100, 1.5f);
            }
        }

        // Controls help background
        renderer.DrawRectScreen(GameConfig.WindowWidth - 310, 5, 310, 110, 0, 0, 0, 160);

        // Controls help
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 10, "WASD/ARROWS/DRAG: PAN", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 30, "SCROLL: ZOOM", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 50, "CLICK: SELECT", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 70, "DBLCLICK/ENTER: TRAVEL", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 90, "ESC: MENU", 180, 180, 180, 1.5f);

        // Pause menu overlay
        _pauseOverlay.Render(renderer);
    }
}
