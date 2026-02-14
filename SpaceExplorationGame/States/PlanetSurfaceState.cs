using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.States;

/// <summary>
/// Planet surface state: Top-down tilemap view where the player can walk/drive on a planet's surface.
/// </summary>
public class PlanetSurfaceState : GameState
{
    public override GameStateType Type => GameStateType.PlanetSurface;

    private readonly StarSystemData _starSystem;
    private readonly PlanetData _planet;
    private PlanetSurfaceData _surfaceData = null!;

    private Entity _playerAvatar;
    private Entity _shipEntity;
    private const float AvatarSpeed = 120f;
    private const float BoardShipRadius = 30f;


    public PlanetSurfaceState(StarSystemData starSystem, PlanetData planet)
    {
        _starSystem = starSystem;
        _planet = planet;
    }

    public override void Enter(Game game)
    {
        // Generate planet surface
        var rng = game.Seeds.GetPlanetSurfaceRandom(_starSystem.Index, _planet.Index);
        _surfaceData = PlanetSurfaceGenerator.Generate(rng, _planet);

        // Place player avatar at landing zone
        float lzX = _surfaceData.LandingZone.X * GameConfig.TileSize;
        float lzY = _surfaceData.LandingZone.Y * GameConfig.TileSize;

        _playerAvatar = game.EcsWorld.Create(
            new Transform(lzX, lzY),
            ECS.Components.Sprite.ColoredRect(12, 12, 100, 255, 100),
            new Velocity(AvatarSpeed),
            new PlayerControlled()
        );

        // Place ship at landing zone
        _shipEntity = game.EcsWorld.Create(
            new Transform(lzX + 30, lzY),
            ECS.Components.Sprite.ColoredRect(20, 16, 150, 150, 200),
            new Label { Text = "SHIP", OffsetY = 14 }
        );

        // Camera
        game.Camera.Position = new Vector2(lzX, lzY);
        game.Camera.Zoom = 2.0f;
    }

    public override void Exit(Game game)
    {
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void Update(Game game, float dt)
    {
        var input = game.Input;
        var camera = game.Camera;

        // Player movement (direct 4-way)
        ref var avatarTransform = ref game.EcsWorld.Get<Transform>(_playerAvatar);
        Vector2 moveDir = Vector2.Zero;

        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
            moveDir.Y -= 1;
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
            moveDir.Y += 1;
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            moveDir.X -= 1;
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            moveDir.X += 1;

        if (moveDir != Vector2.Zero)
        {
            moveDir = Vector2.Normalize(moveDir);
            var newPos = avatarTransform.Position + moveDir * AvatarSpeed * dt;

            // Bounds check
            int tileX = (int)(newPos.X / GameConfig.TileSize);
            int tileY = (int)(newPos.Y / GameConfig.TileSize);

            if (tileX >= 0 && tileX < _surfaceData.Width &&
                tileY >= 0 && tileY < _surfaceData.Height)
            {
                var terrain = _surfaceData.Tiles[tileX, tileY];
                // Block movement on water/lava
                if (terrain is not (TerrainType.Water or TerrainType.Lava))
                {
                    avatarTransform.Position = newPos;
                }
            }
        }

        // Camera follows avatar
        camera.LerpTo(avatarTransform.Position, 5f * dt);

        // Zoom
        if (input.MouseWheelY != 0)
        {
            camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            camera.ClampZoom();
        }

        // Board ship
        var shipTransform = game.EcsWorld.Get<Transform>(_shipEntity);
        float distToShip = Vector2.Distance(avatarTransform.Position, shipTransform.Position);
        if (distToShip < BoardShipRadius && input.IsKeyPressed(SDL.Scancode.E))
        {
            // Return to solar system
            game.ChangeState(new SolarSystemState(_starSystem));
        }

        // Quick exit
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            game.ChangeState(new SolarSystemState(_starSystem));
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = game.Camera;

        // Calculate visible tile range
        var (topLeft, bottomRight) = camera.GetVisibleBounds();
        int startTileX = Math.Max(0, (int)(topLeft.X / GameConfig.TileSize) - 1);
        int startTileY = Math.Max(0, (int)(topLeft.Y / GameConfig.TileSize) - 1);
        int endTileX = Math.Min(_surfaceData.Width - 1, (int)(bottomRight.X / GameConfig.TileSize) + 1);
        int endTileY = Math.Min(_surfaceData.Height - 1, (int)(bottomRight.Y / GameConfig.TileSize) + 1);

        // Draw tiles
        for (int x = startTileX; x <= endTileX; x++)
        {
            for (int y = startTileY; y <= endTileY; y++)
            {
                var terrain = _surfaceData.Tiles[x, y];
                var (r, g, b) = PlanetSurfaceGenerator.GetTerrainColor(terrain);

                var worldPos = new Vector2(x * GameConfig.TileSize + GameConfig.TileSize / 2f,
                                           y * GameConfig.TileSize + GameConfig.TileSize / 2f);
                renderer.DrawRect(camera, worldPos, GameConfig.TileSize, GameConfig.TileSize, r, g, b);
            }
        }

        // Draw settlements
        foreach (var settlement in _surfaceData.Settlements)
        {
            for (int sx = settlement.TileX; sx < settlement.TileX + settlement.Width && sx < _surfaceData.Width; sx++)
            {
                for (int sy = settlement.TileY; sy < settlement.TileY + settlement.Height && sy < _surfaceData.Height; sy++)
                {
                    var worldPos = new Vector2(sx * GameConfig.TileSize + GameConfig.TileSize / 2f,
                                               sy * GameConfig.TileSize + GameConfig.TileSize / 2f);
                    renderer.DrawRect(camera, worldPos, GameConfig.TileSize, GameConfig.TileSize, 100, 100, 120);
                }
            }

            // Settlement label
            var labelPos = new Vector2(
                (settlement.TileX + settlement.Width / 2f) * GameConfig.TileSize,
                settlement.TileY * GameConfig.TileSize - 10
            );
            renderer.DrawText(camera, labelPos, settlement.Name, 255, 255, 200);
        }

        // Draw ship
        var shipTf = game.EcsWorld.Get<Transform>(_shipEntity);
        renderer.DrawRect(camera, shipTf.Position, 20, 16, 150, 150, 200);
        renderer.DrawText(camera, shipTf.Position + new Vector2(-12, 14), "SHIP", 180, 180, 200);

        // Draw player avatar
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        renderer.DrawRect(camera, avatarTf.Position, 10, 10, 100, 255, 100);

        // Board ship prompt
        float distToShip = Vector2.Distance(avatarTf.Position, shipTf.Position);
        if (distToShip < BoardShipRadius)
        {
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 60,
                "[E] BOARD SHIP", 100, 255, 100, 2f);
        }

        // --- HUD ---
        renderer.DrawTextScreen(10, 10, $"PLANET: {_planet.Name.ToUpper()}", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"TYPE: {_planet.Type}", 150, 150, 150, 1.5f);

        // Minimap (small box in corner)
        float mmSize = 150;
        float mmX = GameConfig.WindowWidth - mmSize - 10;
        float mmY = 10;
        renderer.DrawRectScreen(mmX, mmY, mmSize, mmSize, 0, 0, 0, 200);

        float mmScaleX = mmSize / (_surfaceData.Width * GameConfig.TileSize);
        float mmScaleY = mmSize / (_surfaceData.Height * GameConfig.TileSize);

        // Player dot on minimap
        float pmx = mmX + avatarTf.Position.X * mmScaleX;
        float pmy = mmY + avatarTf.Position.Y * mmScaleY;
        renderer.DrawRectScreen(pmx - 2, pmy - 2, 4, 4, 100, 255, 100);

        // Ship dot on minimap
        float smx = mmX + shipTf.Position.X * mmScaleX;
        float smy = mmY + shipTf.Position.Y * mmScaleY;
        renderer.DrawRectScreen(smx - 2, smy - 2, 4, 4, 150, 150, 200);

        // Controls
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 20, "WASD: MOVE", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 40, "SCROLL: ZOOM", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 60, "E: BOARD SHIP", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 80, "ESC: LEAVE", 120, 120, 120, 1.5f);
    }
}
