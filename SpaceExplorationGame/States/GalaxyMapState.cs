using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

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
        _starSystems.Clear();
        _backgroundStars.Clear();
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void Update(Game game, float dt)
    {
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

        // Zoom with mouse wheel
        if (input.MouseWheelY != 0)
        {
            camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            camera.ClampZoom();
        }

        // Mouse hover check
        var mouseWorld = camera.ScreenToWorld(new Vector2(input.MouseX, input.MouseY));
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

        // Click to select
        if (input.IsMousePressed(1) && _hoveredSystemIndex >= 0)
        {
            _selectedSystemIndex = _hoveredSystemIndex;
        }

        // Enter to travel to selected system
        if (input.IsKeyPressed(SDL.Scancode.Return) && _selectedSystemIndex >= 0)
        {
            game.Player.CurrentStarSystemIndex = _selectedSystemIndex;
            game.ChangeState(new SolarSystemState(_starSystems[_selectedSystemIndex]));
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

        // Draw connections between nearby systems (optional: FTL routes)
        for (int i = 0; i < _starSystems.Count; i++)
        {
            for (int j = i + 1; j < _starSystems.Count; j++)
            {
                var diff = _starSystems[i].GalaxyPosition - _starSystems[j].GalaxyPosition;
                if (diff.Length() < 300) // only show nearby connections
                {
                    renderer.DrawLine(camera,
                        _starSystems[i].GalaxyPosition,
                        _starSystems[j].GalaxyPosition,
                        30, 30, 50, 80);
                }
            }
        }

        // Draw star systems
        for (int i = 0; i < _starSystems.Count; i++)
        {
            var sys = _starSystems[i];
            bool isSelected = i == _selectedSystemIndex;
            bool isHovered = i == _hoveredSystemIndex;

            // Draw star circle
            float radius = sys.StarRadius;
            if (isHovered) radius *= 1.3f;
            renderer.DrawFilledCircle(camera, sys.GalaxyPosition, radius,
                sys.StarR, sys.StarG, sys.StarB);

            // Selection ring
            if (isSelected)
            {
                renderer.DrawCircle(camera, sys.GalaxyPosition, radius + 5, 255, 255, 255);
            }

            // Draw name label
            float textScale = Math.Max(1f, camera.Zoom);
            renderer.DrawText(camera,
                sys.GalaxyPosition + new Vector2(0, radius + 12),
                sys.Name, 200, 200, 200, textScale);
        }

        // Draw player location marker
        if (game.Player.CurrentStarSystemIndex >= 0 && game.Player.CurrentStarSystemIndex < _starSystems.Count)
        {
            var playerSys = _starSystems[game.Player.CurrentStarSystemIndex];
            renderer.DrawCircle(camera, playerSys.GalaxyPosition,
                playerSys.StarRadius + 10, 0, 255, 100);
        }

        // HUD
        renderer.DrawTextScreen(10, 10, "GALAXY MAP", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"SEED: {game.Seeds.GalaxySeed}", 150, 150, 150, 1.5f);
        renderer.DrawTextScreen(10, 55, $"SYSTEMS: {_starSystems.Count}", 150, 150, 150, 1.5f);

        if (_selectedSystemIndex >= 0)
        {
            var sys = _starSystems[_selectedSystemIndex];
            float panelY = GameConfig.WindowHeight - 120;
            renderer.DrawRectScreen(0, panelY, 400, 120, 10, 10, 30, 200);
            renderer.DrawTextScreen(10, panelY + 10, $"SELECTED: {sys.Name}", 255, 255, 255, 2f);
            renderer.DrawTextScreen(10, panelY + 35, $"CLASS: {sys.StarClass} STAR", 200, 200, 200, 1.5f);
            renderer.DrawTextScreen(10, panelY + 55, $"PLANETS: {sys.PlanetCount}", 200, 200, 200, 1.5f);
            renderer.DrawTextScreen(10, panelY + 75, $"STATION: {(sys.HasSpaceStation ? "YES" : "NO")}", 200, 200, 200, 1.5f);
            renderer.DrawTextScreen(10, panelY + 95, "[ENTER] TRAVEL TO SYSTEM", 100, 255, 100, 1.5f);
        }

        // Controls help
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 10, "WASD/ARROWS: PAN", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 30, "SCROLL: ZOOM", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 50, "CLICK: SELECT", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 300, 70, "ENTER: TRAVEL", 120, 120, 120, 1.5f);
    }
}
