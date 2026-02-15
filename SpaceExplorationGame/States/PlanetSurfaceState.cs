using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.UI.Overlays;

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
    private const float BaseAvatarSpeed = 200f;
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

    // Ship proximity tracking (for boarding prompt)
    private bool _nearShip;

    // Vehicle proximity tracking (for mount prompt)
    private bool _nearVehicle;

    // In-game menu overlay
    private readonly InGameMenuOverlay _inGameMenuOverlay = new();

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

        // Calculate avatar speed from equipped parts
        float avatarSpeed = BaseAvatarSpeed + game.Player.GetCombinedAvatarStats().WalkSpeed;

        _playerAvatar = EntityFactory.CreatePlayerAvatar(game.EcsWorld, lzX, lzY, avatarSpeed);

        // Place ship at landing zone
        _shipEntity = EntityFactory.CreateLandedShip(game.EcsWorld, lzX + 30, lzY);

        // Deploy vehicle near the ship if player has one
        _inVehicle = false;
        _vehicleDeployed = game.Player.HasVehicle;
        if (_vehicleDeployed)
        {
            _vehicleEntity = EntityFactory.CreateVehicle(game.EcsWorld, lzX - 30, lzY);
        }

        // Initialize ECS systems
        _movementSystem = new PlayerMovementSystem(game.EcsWorld, game.Input, avatarSpeed);
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

    public override void UpdateInput(Game game)
    {
        // In-game menu overlay (handles Escape toggle + menu navigation)
        if (_inGameMenuOverlay.UpdateInput(game))
            return;

        var input = game.Input;

        // Get player position for proximity checks
        ref var avatarTransform = ref game.EcsWorld.Get<Transform>(_playerAvatar);

        // Unified interaction with E key (priority: ship > vehicle > settlement)
        if (input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_inVehicle)
            {
                // Dismount vehicle: place avatar next to vehicle, reset rotation
                ref var vehicleTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
                avatarTransform.Position = vehicleTf.Position + new Vector2(20, 0);
                avatarTransform.Rotation = 0f;
                _vehicleMovementSystem!.Speed = 0f;
                _inVehicle = false;
                game.Player.InVehicle = false;
            }
            else if (_nearShip)
            {
                // Board ship (highest priority when on foot)
                game.Player.InVehicle = false;
                game.ChangeState(new SolarSystemState(_starSystem));
            }
            else if (_nearVehicle && _vehicleDeployed)
            {
                // Mount vehicle
                ref var vTf = ref game.EcsWorld.Get<Transform>(_vehicleEntity);
                avatarTransform.Position = vTf.Position;
                avatarTransform.Rotation = vTf.Rotation;
                var vStats = game.Player.GetCombinedVehicleStats();
                _vehicleMovementSystem = new VehicleMovementSystem(
                    game.EcsWorld, game.Input, _playerAvatar,
                    acceleration: vStats.Acceleration > 0 ? vStats.Acceleration : GameConfig.VehicleAcceleration,
                    maxSpeed: vStats.MaxSpeed > 0 ? vStats.MaxSpeed : GameConfig.VehicleMaxSpeed,
                    rotationSpeed: vStats.RotationSpeed > 0 ? vStats.RotationSpeed : GameConfig.VehicleRotationSpeed,
                    friction: GameConfig.VehicleFriction + vStats.Friction);
                _vehicleMovementSystem.CanMoveTo = CanMoveToTerrain;
                _inVehicle = true;
                game.Player.InVehicle = true;
            }
            else if (_nearSettlement != null)
            {
                // Enter settlement interior (lowest priority)
                game.ChangeState(new InteriorState(
                    InteriorOrigin.Settlement, _starSystem,
                    planet: _planet, settlement: _nearSettlement));
                return;
            }
        }
    }

    public override void Update(Game game, float dt)
    {
        // In-game menu active — no simulation
        if (_inGameMenuOverlay.IsOpen)
            return;

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

        // Check ship proximity
        var shipTransform = game.EcsWorld.Get<Transform>(_shipEntity);
        float distToShip = Vector2.Distance(avatarTransform.Position, shipTransform.Position);
        _nearShip = distToShip < BoardShipRadius;

        // Check vehicle proximity
        if (_vehicleDeployed)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            float distToVehicle = Vector2.Distance(avatarTransform.Position, vehicleTf.Position);
            _nearVehicle = distToVehicle < GameConfig.VehicleMountRadius;
        } 
        else        
        {
            _nearVehicle = false;
        }
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = game.Camera;

        // Draw terrain tiles
        PlanetSurfaceRenderer.RenderTerrain(renderer, camera, _surfaceData);

        // Draw settlements
        SettlementRenderer.Render(renderer, camera, _surfaceData);

        // Draw ship
        var shipTf = game.EcsWorld.Get<Transform>(_shipEntity);
        game.SpaceshipRenderer.RenderLanded(renderer, camera, shipTf.Position,
            game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);

        // Draw vehicle
        if (_vehicleDeployed)
        {
            var vehicleTf = game.EcsWorld.Get<Transform>(_vehicleEntity);
            game.VehicleRenderer.Render(renderer, camera, vehicleTf.Position,
                vehicleTf.Rotation, _inVehicle);
        }

        // Draw player avatar (only when on foot)
        var avatarTf = game.EcsWorld.Get<Transform>(_playerAvatar);
        if (!_inVehicle)
        {
            game.AvatarRenderer.Render(renderer, camera, avatarTf.Position);
        }

        // Interaction prompts
        PlanetSurfaceRenderer.RenderInteractionPrompt(renderer,
            _inVehicle, _nearShip, _nearVehicle, _vehicleDeployed, _nearSettlement);

        // HUD
        PlanetSurfaceRenderer.RenderHud(renderer, _planet, _inVehicle);

        // Minimap
        Vector2? vehiclePos = _vehicleDeployed && !_inVehicle
            ? game.EcsWorld.Get<Transform>(_vehicleEntity).Position
            : null;
        PlanetSurfaceRenderer.RenderMinimap(renderer, _surfaceData,
            avatarTf.Position, shipTf.Position, vehiclePos);

        // Controls
        PlanetSurfaceRenderer.RenderControls(renderer);

        // In-game menu overlay drawn on top of everything
        _inGameMenuOverlay.Render(game);
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
