using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
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
    private Entity _vehicleEntity;
    private const float AvatarSpeed = 200f;
    private const float BoardShipRadius = 30f;

    // Vehicle state
    private bool _inVehicle;
    private bool _vehicleDeployed;

    // ECS Systems
    private PlayerMovementSystem _movementSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private VehicleMovementSystem? _vehicleMovementSystem;

    // Settlement proximity tracking
    private SettlementData? _nearSettlement;

    // Landing site (tile coordinates, -1 = default center)
    private readonly int _landingTileX;
    private readonly int _landingTileY;

    public PlanetSurfaceState(StarSystemData starSystem, PlanetData planet, int landingTileX = -1, int landingTileY = -1)
    {
        _starSystem = starSystem;
        _planet = planet;
        _landingTileX = landingTileX;
        _landingTileY = landingTileY;
    }

    public override void Enter(Game game)
    {
        // Generate planet surface
        var rng = game.Seeds.GetPlanetSurfaceRandom(_starSystem.Index, _planet.Index);
        _surfaceData = PlanetSurfaceGenerator.Generate(rng, _planet);

        // Place player avatar at landing zone (use chosen site or default center)
        int lzTileX = _landingTileX >= 0 ? _landingTileX : _surfaceData.LandingZone.X;
        int lzTileY = _landingTileY >= 0 ? _landingTileY : _surfaceData.LandingZone.Y;
        float lzX = lzTileX * GameConfig.TileSize;
        float lzY = lzTileY * GameConfig.TileSize;

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

        // Deploy vehicle near the ship if player has one
        _inVehicle = false;
        _vehicleDeployed = game.Player.HasVehicle;
        if (_vehicleDeployed)
        {
            _vehicleEntity = game.EcsWorld.Create(
                new Transform(lzX - 30, lzY),
                ECS.Components.Sprite.ColoredRect(16, 16, 180, 140, 80),
                new Label { Text = "VEHICLE", OffsetY = 14 }
            );
        }

        // Initialize ECS systems
        _movementSystem = new PlayerMovementSystem(game.EcsWorld, game.Input, AvatarSpeed);
        _movementSystem.CanMoveTo = MakeTerrainCollisionCheck();
        _movementSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(game.EcsWorld, game.Camera, game.Input);
        _cameraFollowSystem.Initialize();

        // Camera
        game.Camera.Position = new Vector2(lzX, lzY);
        game.Camera.Zoom = 1.5f;
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

        if (_inVehicle)
        {
            // Vehicle movement (thrust/rotation)
            _vehicleMovementSystem!.Update(dt);
        }
        else
        {
            // Normal avatar movement (4-way WASD)
            _movementSystem.Update(in dt);
        }

        // Camera follows player + handles zoom
        _cameraFollowSystem.Update(in dt);

        // Get player position for proximity checks
        ref var avatarTransform = ref game.EcsWorld.Get<Transform>(_playerAvatar);

        // Vehicle mount/dismount with V key
        if (input.IsKeyPressed(SDL.Scancode.V) && _vehicleDeployed)
        {
            if (_inVehicle)
            {
                // Dismount: place avatar next to vehicle, reset rotation
                ref var vehicleTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
                avatarTransform.Position = vehicleTf.Position + new Vector2(20, 0);
                avatarTransform.Rotation = 0f;
                _vehicleMovementSystem!.Speed = 0f;
                _inVehicle = false;
                game.Player.InVehicle = false;
            }
            else
            {
                // Mount: check proximity to vehicle
                var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
                float distToVehicle = Vector2.Distance(avatarTransform.Position, vehicleTf.Position);
                if (distToVehicle < GameConfig.VehicleMountRadius)
                {
                    // Snap player to vehicle position and adopt its rotation
                    ref var vTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
                    avatarTransform.Position = vTf.Position;
                    avatarTransform.Rotation = vTf.Rotation;
                    _vehicleMovementSystem = new VehicleMovementSystem(
                        game.EcsWorld, game.Input, _playerAvatar);
                    _vehicleMovementSystem.CanMoveTo = CanMoveToTerrain;
                    _inVehicle = true;
                    game.Player.InVehicle = true;
                }
            }
        }

        // Keep vehicle position and rotation synced when driving
        if (_inVehicle && _vehicleDeployed)
        {
            ref var vehicleTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
            vehicleTf.Position = avatarTransform.Position;
            vehicleTf.Rotation = avatarTransform.Rotation;
        }

        // Check settlement proximity
        _nearSettlement = null;
        foreach (var settlement in _surfaceData.Settlements)
        {
            float sx = (settlement.TileX + settlement.Width / 2f) * GameConfig.TileSize;
            float sy = (settlement.TileY + settlement.Height / 2f) * GameConfig.TileSize;
            float distToSettlement = Vector2.Distance(avatarTransform.Position, new Vector2(sx, sy));
            float settlementRadius = Math.Max(settlement.Width, settlement.Height) * GameConfig.TileSize / 2f + 20f;
            if (distToSettlement < settlementRadius)
            {
                _nearSettlement = settlement;
                break;
            }
        }

        // Board ship / enter settlement
        var shipTransform = game.EcsWorld.Get<Transform>(_shipEntity);
        float distToShip = Vector2.Distance(avatarTransform.Position, shipTransform.Position);
        if (input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_nearSettlement != null && !_inVehicle)
            {
                // Enter settlement interior (must be on foot)
                game.ChangeState(new InteriorState(
                    InteriorOrigin.Settlement, _starSystem,
                    planet: _planet, settlement: _nearSettlement));
                return;
            }
            else if (distToShip < BoardShipRadius && !_inVehicle)
            {
                // Return to solar system (must be on foot)
                game.Player.InVehicle = false;
                game.ChangeState(new SolarSystemState(_starSystem));
            }
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

        // Draw tiles with variation via shared renderer
        TileMapRenderer.RenderTiles(renderer, camera, _surfaceData.Width, _surfaceData.Height,
            (x, y) => PlanetSurfaceGenerator.GetTerrainColor(_surfaceData.Tiles[x, y]),
            800f,
            (x, y, worldPos, hash) =>
            {
                var terrain = _surfaceData.Tiles[x, y];
                var (r, g, b) = PlanetSurfaceGenerator.GetTerrainColor(terrain);

                if (terrain == TerrainType.Grass && (hash & 0x7) == 0)
                {
                    byte dr = (byte)Math.Clamp(r - 20, 0, 255);
                    byte dg = (byte)Math.Clamp(g + 30, 0, 255);
                    byte db = (byte)Math.Clamp(b - 10, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 8) & 0xF) - 8, ((hash >> 12) & 0xF) - 8),
                        6, 6, dr, dg, db);
                }
                else if (terrain == TerrainType.Rock && (hash & 0xF) == 0)
                {
                    byte dr = (byte)Math.Clamp(r + 20, 0, 255);
                    byte dg = (byte)Math.Clamp(g + 15, 0, 255);
                    byte db = (byte)Math.Clamp(b + 10, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 8) & 0xF) - 8, ((hash >> 12) & 0xF) - 8),
                        4, 4, dr, dg, db);
                }
                else if (terrain == TerrainType.Water && (hash & 0x3) == 0)
                {
                    byte wr = (byte)Math.Clamp(r + 30, 0, 255);
                    byte wg = (byte)Math.Clamp(g + 30, 0, 255);
                    byte wb = (byte)Math.Clamp(b + 40, 0, 255);
                    renderer.DrawRect(camera, worldPos + new Vector2(((hash >> 4) & 0xF) - 8, ((hash >> 8) & 0x7) - 4),
                        8, 2, wr, wg, wb, 100);
                }
            });

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

        // Draw ship with texture
        var shipTf = game.EcsWorld.Get<Transform>(_shipEntity);
        var landedShipTex = game.Textures.GetTexture(Rendering.TextureManager.ShipLanded);
        renderer.DrawTexture(camera, landedShipTex, shipTf.Position, 48, 48);
        renderer.DrawText(camera, shipTf.Position + new Vector2(-12, 14), "SHIP", 180, 180, 200);

        // Draw vehicle (when not mounted, or when mounted draw it at player position)
        if (_vehicleDeployed)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            var vehicleTex = game.Textures.GetTexture(Rendering.TextureManager.Vehicle);
            // Vehicle texture points up (north) so add 90° offset to align with 0°=right convention
            renderer.DrawTexture(camera, vehicleTex, vehicleTf.Position, 40, 40, vehicleTf.Rotation + 90f);
            if (!_inVehicle)
            {
                renderer.DrawText(camera, vehicleTf.Position + new Vector2(-20, 14), "VEHICLE", 180, 160, 100);
            }
        }

        // Draw player avatar (only when on foot)
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        if (!_inVehicle)
        {
            var avatarTex = game.Textures.GetTexture(Rendering.TextureManager.AvatarDown);
            renderer.DrawTexture(camera, avatarTex, avatarTf.Position, 28, 28);
        }

        // Board ship / settlement entry prompt
        float distToShip = Vector2.Distance(avatarTf.Position, shipTf.Position);

        // Vehicle mount/dismount prompt
        if (_vehicleDeployed && !_inVehicle)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            float distToVehicle = Vector2.Distance(avatarTf.Position, vehicleTf.Position);
            if (distToVehicle < GameConfig.VehicleMountRadius)
            {
                renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 90,
                    "[V] MOUNT VEHICLE", 255, 200, 100, 2f);
            }
        }
        else if (_inVehicle)
        {
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 90,
                "[V] DISMOUNT", 255, 200, 100, 2f);
        }

        if (_nearSettlement != null && !_inVehicle)
        {
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 120, GameConfig.WindowHeight - 60,
                $"[E] ENTER {_nearSettlement.Name.ToUpper()}", 255, 255, 100, 2f);
        }
        else if (_nearSettlement != null && _inVehicle)
        {
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 140, GameConfig.WindowHeight - 60,
                "DISMOUNT TO ENTER SETTLEMENT", 255, 100, 100, 2f);
        }
        else if (distToShip < BoardShipRadius && !_inVehicle)
        {
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 60,
                "[E] BOARD SHIP", 100, 255, 100, 2f);
        }
        else if (distToShip < BoardShipRadius && _inVehicle)
        {
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 140, GameConfig.WindowHeight - 60,
                "DISMOUNT TO BOARD SHIP", 255, 100, 100, 2f);
        }

        // --- HUD ---
        renderer.DrawTextScreen(10, 10, $"PLANET: {_planet.Name.ToUpper()}", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"TYPE: {_planet.Type}", 150, 150, 150, 1.5f);
        if (_inVehicle)
        {
            renderer.DrawTextScreen(10, 55, "DRIVING VEHICLE", 255, 200, 100, 1.5f);
        }

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

        // Vehicle dot on minimap
        if (_vehicleDeployed && !_inVehicle)
        {
            var vTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            float vmx = mmX + vTf.Position.X * mmScaleX;
            float vmy = mmY + vTf.Position.Y * mmScaleY;
            renderer.DrawRectScreen(vmx - 2, vmy - 2, 4, 4, 180, 140, 80);
        }

        // Controls background
        renderer.DrawRectScreen(GameConfig.WindowWidth - 260, mmY + mmSize + 15, 260, 120, 0, 0, 0, 160);

        // Controls
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 20, "WASD: MOVE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 40, "SCROLL: ZOOM", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 60, "V: VEHICLE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 80, "E: INTERACT", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 250, mmY + mmSize + 100, "ESC: LEAVE", 180, 180, 180, 1.5f);
    }

    /// <summary>Creates a terrain collision delegate for the movement system.</summary>
    private Func<Vector2, bool> MakeTerrainCollisionCheck()
    {
        return CanMoveToTerrain;
    }

    /// <summary>Checks whether a position is on walkable/drivable terrain.</summary>
    private bool CanMoveToTerrain(Vector2 newPos)
    {
        int tileX = (int)(newPos.X / GameConfig.TileSize);
        int tileY = (int)(newPos.Y / GameConfig.TileSize);
        if (tileX < 0 || tileX >= _surfaceData.Width || tileY < 0 || tileY >= _surfaceData.Height)
            return false;
        var terrain = _surfaceData.Tiles[tileX, tileY];
        return terrain is not (TerrainType.Water or TerrainType.Lava);
    }
}
