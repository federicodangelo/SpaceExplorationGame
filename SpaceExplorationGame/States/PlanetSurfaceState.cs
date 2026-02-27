using System.Numerics;
using Arch.Core;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Movement;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.UI.Overlays.Map;
using SpaceExplorationGame.UI.Overlays.Menu;
using SpaceExplorationGame.Simulation.Base;

namespace SpaceExplorationGame.States;

public enum PlanetSurfaceStartMode
{
    InShip,
    OnFoot,
    OnVehicle,
}

/// <summary>
/// Planet surface state: Top-down tilemap view. Rendering and input only —
/// simulation logic lives in <see cref="PlanetSurfaceSimulation"/>.
/// </summary>
public class PlanetSurfaceState : GameState
{
    public override GameStateType Type => GameStateType.PlanetSurface;

    // ── Simulation ──────────────────────────────────────────────────
    private PlanetSurfaceSimulation _sim = null!;
    private SimulationPlayer _simPlayer = null!;

    private readonly StarSystemData _starSystem;
    private readonly PlanetData _planet;

    // ── Camera ──────────────────────────────────────────────────────
    private readonly Camera _camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
        GameConfig.PlanetSurfaceZoomMin, GameConfig.PlanetSurfaceZoomMax);

    // ── Input systems ───────────────────────────────────────────────
    private AvatarMovementSystem _movementSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private VehicleMovementSystem? _vehicleMovementSystem;

    // ── Visual effects ──────────────────────────────────────────────
    private readonly List<DamagePopup> _damagePopups = [];
    private readonly List<Explosion> _explosions = [];

    // ── Player state (rendering/input only) ─────────────────────────
    private bool _inVehicle;
    private bool _playerInsideShip = true;
    private float _playerFireCooldown;
    private Vector2 _lastMoveDir = new(0, -1);

    // ── Combat music ────────────────────────────────────────────────
    private MusicTheme _activeMusicTheme = MusicTheme.PlanetSurface;

    // ── Overlays ────────────────────────────────────────────────────
    private readonly InGameMenuOverlay _inGameMenuOverlay = new() { StateType = GameStateType.PlanetSurface };
    private PlanetSurfaceMapOverlay _surfaceMapOverlay = null!;
    private readonly StarshipMenuOverlay _starshipMenuOverlay = new();
    private bool _waitingToOpenStarshipMenuAfterLanding;
    private float _starshipMenuOpenDelayTimer;

    // ── Constructor params ──────────────────────────────────────────
    private readonly int _landingTileX;
    private readonly int _landingTileY;
    private readonly PlanetSurfaceData? _preGeneratedSurfaceData;
    private readonly PlanetSurfaceStartMode _startMode;
    private readonly float _landingDelay;

    public PlanetSurfaceState(StarSystemData starSystem, PlanetData planet, int landingTileX = -1, int landingTileY = -1,
        PlanetSurfaceData? preGeneratedSurfaceData = null, float landingDelay = 1.2f,
        PlanetSurfaceStartMode startMode = PlanetSurfaceStartMode.InShip)
    {
        _starSystem = starSystem;
        _planet = planet;
        _landingTileX = landingTileX;
        _landingTileY = landingTileY;
        _preGeneratedSurfaceData = preGeneratedSurfaceData;
        _starshipMenuOpenDelayTimer = landingDelay;
        _landingDelay = landingDelay;
        _startMode = startMode;
    }


    public override void Enter(Game game)
    {
        _surfaceMapOverlay = new PlanetSurfaceMapOverlay(game.Textures);
        _inGameMenuOverlay.OnMapRequested = g =>
        {
            var avatarPos = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;
            var shipPos = _sim.EcsWorld.Get<Transform>(_sim.LocalShipEntity).Position;
            Vector2? vehiclePos = _sim.LocalVehicleDeployed
                ? _sim.EcsWorld.Get<Transform>(_sim.LocalVehicleEntity).Position
                : null;
            _surfaceMapOverlay.Open(g, _starSystem, _planet, _sim.SurfaceData,
                shipPos, avatarPos, vehiclePos);
        };

        // Get or create the simulation
        var parentSim = game.Coordinator.Find<SolarSystemSimulation>(s => s.StarSystem.Index == _starSystem.Index);
        _sim = game.Coordinator.FindOrCreate<PlanetSurfaceSimulation>(
            s => s.StarSystem.Index == _starSystem.Index && s.Planet.Index == _planet.Index,
            () => new PlanetSurfaceSimulation(game, _starSystem, _planet, _preGeneratedSurfaceData, parentSim));

        // Add player
        _simPlayer = _sim.AddPlayer(game.Player, new AddContext(_landingTileX, _landingTileY));

        // Determine start mode
        bool hasSavedPositions = game.Player.HasSavedSurfacePositions;
        _playerInsideShip = !hasSavedPositions && _startMode == PlanetSurfaceStartMode.InShip;
        _inVehicle = false;

        if (hasSavedPositions)
        {
            _playerInsideShip = false;
            if (game.Player.SavedPlayerInVehicle && _sim.LocalVehicleDeployed)
            {
                MountVehicle(game);
            }
        }
        else if (_startMode == PlanetSurfaceStartMode.OnVehicle)
        {
            _playerInsideShip = false;
            MountVehicle(game);
        }

        game.Player.ClearSavedSurfacePositions();

        // Initialize input/camera systems on simulation's ECS world
        float avatarSpeed = game.Player.AvatarWalkSpeed;
        _movementSystem = new AvatarMovementSystem(_sim.EcsWorld, game.Input, avatarSpeed);
        _movementSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(_sim.EcsWorld, _camera);
        _cameraFollowSystem.Initialize();

        // Camera
        var startPos = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;
        _camera.Position = startPos;
        _camera.Zoom = GameConfig.PlanetSurfaceZoomDefault;
        _camera.ClampZoom();

        // Open starship menu on fresh landing
        if (_playerInsideShip)
            _waitingToOpenStarshipMenuAfterLanding = true;

        // Music
        game.Audio.SetMusicTheme(MusicTheme.PlanetSurface);
    }

    public override void Exit(Game game)
    {
        // Persist avatar health
        if (!_sim.PlayerDead && _sim.EcsWorld.IsAlive(_simPlayer.Entity) && _sim.EcsWorld.Has<Health>(_simPlayer.Entity))
        {
            var health = _sim.EcsWorld.Get<Health>(_simPlayer.Entity);
            game.Player.AvatarHealth = health.Hull;
        }

        if (game.Player.Navigation.Type == NavigationTargetType.SurfaceTarget)
            game.Player.Navigation.Clear();

        _surfaceMapOverlay.Cleanup();

        // Remove player from simulation
        if (_sim != null && _simPlayer != null)
            _sim.RemovePlayer(_simPlayer);
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void UpdateInput(Game game)
    {
        if (_waitingToOpenStarshipMenuAfterLanding) return;

        if (_starshipMenuOverlay.UpdateInput(game))
        {
            if (_starshipMenuOverlay.LastChoice.HasValue)
                HandleStarshipMenuChoice(game, _starshipMenuOverlay.LastChoice.Value);
            return;
        }

        if (_surfaceMapOverlay.UpdateInput(game)) return;
        if (_inGameMenuOverlay.UpdateInput(game)) return;

        var input = game.Input;

        if (input.IsActionPressed(InputAction.MenuBack))
        {
            _inGameMenuOverlay.Open(game);
            return;
        }

        // Block gameplay input when player dead
        if (_sim.PlayerDead || !_sim.EcsWorld.IsAlive(_simPlayer.Entity))
        {
            return;
        }

        if (input.IsActionPressed(InputAction.ToggleMap))
        {
            var avatarPos = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;
            var shipPos = _sim.EcsWorld.Get<Transform>(_sim.LocalShipEntity).Position;
            Vector2? vehiclePos = _sim.LocalVehicleDeployed
                ? _sim.EcsWorld.Get<Transform>(_sim.LocalVehicleEntity).Position
                : null;
            _surfaceMapOverlay.Open(game, _starSystem, _planet, _sim.SurfaceData,
                shipPos, avatarPos, vehiclePos);
            return;
        }

        // Interactions
        if (input.IsActionPressed(InputAction.Interact))
            HandleInteraction(game);

        // Camera zoom
        if (input.MouseWheelY != 0)
        {
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
        }

        // Track player facing
        Vector2 moveDir = input.GetActionAxisDirection(InputActionAxis.Movement);
        if (moveDir != Vector2.Zero) _lastMoveDir = moveDir;

        // Write movement input
        if (!_sim.PlayerDead && !_playerInsideShip)
        {
            float dt = game.DeltaTime;
            if (_inVehicle)
            {
                _vehicleMovementSystem?.Update(in dt);
            }
            else
            {
                _movementSystem.Update(in dt);
            }

            // Player shooting
            HandlePlayerShooting(game, dt);
        }
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;

        _inGameMenuOverlay.Update(game);

        // Delay before starship menu
        if (_waitingToOpenStarshipMenuAfterLanding)
        {
            _starshipMenuOpenDelayTimer -= dt;
            if (_starshipMenuOpenDelayTimer <= 0f)
            {
                _waitingToOpenStarshipMenuAfterLanding = false;
                _starshipMenuOverlay.HasVehicle = game.Player.HasVehicle;
                _starshipMenuOverlay.Open();
            }
            _cameraFollowSystem.Update(in dt);
            return;
        }

        // Skip post-processing when overlays are active
        if (_starshipMenuOverlay.IsOpen || _surfaceMapOverlay.IsOpen || _inGameMenuOverlay.IsOpen)
        {
            if (_surfaceMapOverlay.IsOpen) _surfaceMapOverlay.Update(game);
            return;
        }

        // Camera follows player
        _cameraFollowSystem.Update(in dt);

        // Process simulation events
        ProcessSimulationEvents(game);

        // Visual effects
        CombatHelper.UpdateVisualEffects(_damagePopups, _explosions, dt);

        // Combat music
        if (_sim.CombatMusicTimer > 0)
        {
            if (_activeMusicTheme != MusicTheme.Combat)
            {
                game.Audio.SetMusicTheme(MusicTheme.Combat);
                _activeMusicTheme = MusicTheme.Combat;
            }
        }
        else if (_activeMusicTheme != MusicTheme.PlanetSurface)
        {
            game.Audio.SetMusicTheme(MusicTheme.PlanetSurface);
            _activeMusicTheme = MusicTheme.PlanetSurface;
        }
    }

    private void ProcessSimulationEvents(Game game)
    {
        var playerPos = _sim.EcsWorld.IsAlive(_simPlayer.Entity)
            ? _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position
            : _camera.Position;

        // Enemy weapon fire SFX
        foreach (var spawn in _sim.EnemyProjectilesSpawnedLastUpdate)
            game.Audio.PlaySfxAtDistance(SfxType.EnemyLaser, spawn.Pos, playerPos, 0.4f);

        // Damage popups + SFX
        CombatHelper.CreateDamagePopups(_damagePopups, _sim.DamageEventsLastUpdate);
        CombatHelper.PlayDamageSfx(game.Audio, _sim.DamageEventsLastUpdate, playerPos, 0.5f);

        // Destroyed entities → explosions + SFX
        CombatHelper.ProcessDestroyedEntities(game.Audio, _explosions,
            _sim.DestroyedEntitiesLastUpdate, playerPos,
            faction => faction == Faction.Fauna
                ? new Color3(200, 80, 60) : new Color3(255, 150, 50),
            asteroidSize: 12f, playerSize: 25f, npcSize: 15f,
            playerExplosionColor: new Color3(255, 120, 80), npcSfxVolume: 0.7f);
    }

    private void HandlePlayerShooting(Game game, float dt)
    {
        if (_inVehicle || _sim.PlayerDead || _playerInsideShip) return;
        _playerFireCooldown -= dt;
        var input = game.Input;

        if (input.IsActionDown(InputAction.FireWeapon) && _playerFireCooldown <= 0)
        {
            var avatarStats = game.Player.GetCombinedAvatarStats();
            float weaponDamage = GameConfig.BaseAvatarWeaponDamage + avatarStats.WeaponDamage;
            _playerFireCooldown = GameConfig.AvatarFireRate;

            ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);

            Vector2 aimDir;
            var gamepadHeading = input.ActiveInputMethod == InputMethod.Gamepad
                ? input.GetActionAxisDirection(InputActionAxis.Heading) : Vector2.Zero;

            if (gamepadHeading != Vector2.Zero)
                aimDir = gamepadHeading;
            else if (input.IsMouseDown(1))
            {
                var mouseWorld = _camera.ScreenToWorld(new Vector2(input.MouseX, input.MouseY));
                aimDir = Vector2.Normalize(mouseWorld - avatarTf.Position);
                if (float.IsNaN(aimDir.X)) aimDir = _lastMoveDir;
            }
            else
                aimDir = _lastMoveDir;

            var spawnPos = avatarTf.Position + aimDir * 14f;
            EntityFactory.CreateProjectile(_sim.EcsWorld, _simPlayer.Entity, spawnPos, aimDir,
                weaponDamage, GameConfig.AvatarProjectileSpeed, Faction.Player,
                new Color3(100, 255, 100), GameConfig.AvatarProjectileLifetime, Vector2.Zero);
            game.Audio.PlaySfx(SfxType.LaserFire, 0.5f);
        }
    }

    private void HandleInteraction(Game game)
    {
        ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);

        if (_inVehicle)
        {
            if (_sim.NearShip)
            {
                // In vehicle near ship → stow vehicle, board ship
                DismountVehicle(game);
                _sim.StowVehicle();
                BoardShip(game);
            }
            else
            {
                // Dismount vehicle
                DismountVehicle(game);
            }
        }
        else if (_sim.NearShip)
        {
            BoardShip(game);
        }
        else if (_sim.NearVehicle && _sim.LocalVehicleDeployed)
        {
            MountVehicle(game);
        }
        else if (_sim.NearSettlement != null)
        {
            SaveSurfacePositions(game);
            game.ChangeState(new InteriorState(
                InteriorOrigin.Settlement, _starSystem,
                planet: _planet, settlement: _sim.NearSettlement));
        }
    }

    private void MountVehicle(Game game)
    {
        if (!_sim.LocalVehicleDeployed)
        {
            var shipTf = _sim.EcsWorld.Get<Transform>(_sim.LocalShipEntity);
            _sim.DeployVehicle(shipTf.Position.X, shipTf.Position.Y);
        }

        ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);
        ref var vTf = ref _sim.EcsWorld.Get<Transform>(_sim.LocalVehicleEntity);
        avatarTf.Position = vTf.Position;
        avatarTf.Rotation = vTf.Rotation;

        var vStats = game.Player.GetCombinedVehicleStats();
        _vehicleMovementSystem = new VehicleMovementSystem(
            _sim.EcsWorld, game.Input, _simPlayer.Entity,
            acceleration: vStats.Acceleration > 0 ? vStats.Acceleration : GameConfig.VehicleAcceleration,
            maxSpeed: vStats.MaxSpeed > 0 ? vStats.MaxSpeed : GameConfig.VehicleMaxSpeed,
            rotationSpeed: vStats.RotationSpeed > 0 ? vStats.RotationSpeed : GameConfig.VehicleRotationSpeed,
            friction: GameConfig.VehicleFriction + vStats.Friction);

        if (_sim.EcsWorld.Has<Velocity>(_simPlayer.Entity))
        {
            ref var avatarVelocity = ref _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity);
            avatarVelocity.MaxSpeed = vStats.MaxSpeed > 0 ? vStats.MaxSpeed : GameConfig.VehicleMaxSpeed;
            avatarVelocity.MaxRotationSpeed = vStats.RotationSpeed > 0 ? vStats.RotationSpeed : GameConfig.VehicleRotationSpeed;
        }

        _inVehicle = true;
        game.Player.InVehicle = true;
    }

    private void DismountVehicle(Game game)
    {
        ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);
        if (_sim.LocalVehicleDeployed)
        {
            ref var vehicleTf = ref _sim.EcsWorld.Get<Transform>(_sim.LocalVehicleEntity);
            avatarTf.Position = vehicleTf.Position + new Vector2(20, 0);
        }
        avatarTf.Rotation = 0f;

        if (_sim.EcsWorld.Has<Velocity>(_simPlayer.Entity))
        {
            ref var avatarVelocity = ref _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity);
            float avatarSpeed = game.Player.AvatarWalkSpeed;
            avatarVelocity.MaxSpeed = avatarSpeed;
            avatarVelocity.MaxRotationSpeed = 0f;
            avatarVelocity.Linear = Vector2.Zero;
            avatarVelocity.Acceleration = Vector2.Zero;
            avatarVelocity.RotationVelocity = 0f;
        }

        _inVehicle = false;
        game.Player.InVehicle = false;
    }

    private void BoardShip(Game game)
    {
        _playerInsideShip = true;
        _starshipMenuOverlay.HasVehicle = game.Player.HasVehicle;
        _starshipMenuOverlay.VehicleDeployed = _sim.LocalVehicleDeployed;
        _starshipMenuOverlay.Open();
    }

    private void HandleStarshipMenuChoice(Game game, StarshipMenuOption choice)
    {
        switch (choice)
        {
            case StarshipMenuOption.TakeOff:
                _playerInsideShip = true;
                game.Player.InVehicle = false;
                if (_sim.LocalVehicleDeployed)
                    _sim.StowVehicle();
                game.Player.ClearSavedSurfacePositions();

                var launchShipTf = _sim.EcsWorld.Get<Transform>(_sim.LocalShipEntity);
                int launchTileX = Math.Clamp((int)MathF.Round(launchShipTf.Position.X / GameConfig.TileSize), 0, Math.Max(0, _sim.SurfaceData.Width - 1));
                int launchTileY = Math.Clamp((int)MathF.Round(launchShipTf.Position.Y / GameConfig.TileSize), 0, Math.Max(0, _sim.SurfaceData.Height - 1));

                bool isMoon = game.Player.SolarSystemReturnContext == PlayerData.ReturnContext.FromMoon;
                int moonPlanetIndex = isMoon ? game.Player.ReturnMoonPlanetIndex : -1;
                int moonIndex = isMoon ? game.Player.ReturnMoonIndex : -1;

                game.ChangeState(new OrbitalSurfaceTransitionState(
                    _starSystem, _planet, _sim.SurfaceData,
                    launchTileX, launchTileY,
                    isMoon, moonPlanetIndex, moonIndex));
                break;

            case StarshipMenuOption.DisembarkOnFoot:
                _playerInsideShip = false;
                ref var shipTf = ref _sim.EcsWorld.Get<Transform>(_sim.LocalShipEntity);
                ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);
                avatarTf.Position = shipTf.Position + new Vector2(30, 0);
                avatarTf.Rotation = 0f;
                break;

            case StarshipMenuOption.DisembarkOnVehicle:
                _playerInsideShip = false;
                MountVehicle(game);
                break;
        }
    }

    private void SaveSurfacePositions(Game game)
    {
        var shipTf = _sim.EcsWorld.Get<Transform>(_sim.LocalShipEntity);
        float vehicleX = 0, vehicleY = 0;
        if (_sim.LocalVehicleDeployed)
        {
            var vehicleTf = _sim.EcsWorld.Get<Transform>(_sim.LocalVehicleEntity);
            vehicleX = vehicleTf.Position.X;
            vehicleY = vehicleTf.Position.Y;
        }
        var avatarTf = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);
        game.Player.SaveSurfacePositions(
            shipTf.Position.X, shipTf.Position.Y,
            vehicleX, vehicleY, _sim.LocalVehicleDeployed,
            avatarTf.Position.X, avatarTf.Position.Y,
            _inVehicle);
    }

    // ── Render ──────────────────────────────────────────────────────

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = _camera;
        var world = _sim.EcsWorld;

        // Terrain
        PlanetSurfaceRenderer.RenderTerrain(renderer, camera, _sim.SurfaceData);

        // Settlements
        SettlementRenderer.Render(renderer, camera, _sim.SurfaceData);

        // Mission markers
        HudIndicatorsRenderer.RenderPlanetSurfaceMissionMarkers(renderer, camera,
            game.Player, (float)game.GlobalTime, _starSystem.Index, _planet.Index,
            _sim.SurfaceData.Settlements);

        // Navigation target
        if (game.Player.Navigation.HasTarget && game.Player.Navigation.Type == NavigationTargetType.SurfaceTarget)
        {
            var targetPos = new Vector2(game.Player.Navigation.WorldX, game.Player.Navigation.WorldY);
            HudIndicatorsRenderer.RenderSurfaceNavTargetMarker(renderer, camera,
                targetPos, game.Player.Navigation.Name, game.Player.Navigation.Color,
                (float)game.GlobalTime);
        }

        // Ship
        var shipTf = world.Get<Transform>(_sim.LocalShipEntity);
        game.SpaceshipRenderer.RenderLanded(renderer, camera, shipTf.Position,
            game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);

        // Vehicle
        if (_sim.LocalVehicleDeployed)
        {
            var vehicleTf = world.Get<Transform>(_sim.LocalVehicleEntity);
            game.VehicleRenderer.Render(renderer, camera, vehicleTf.Position,
                vehicleTf.Rotation, _inVehicle);
        }

        // Player avatar
        Vector2 avatarPos;
        if (!_sim.PlayerDead && world.IsAlive(_simPlayer.Entity))
        {
            ref var avatarTf = ref world.Get<Transform>(_simPlayer.Entity);
            avatarPos = avatarTf.Position;
            if (!_inVehicle && !_playerInsideShip)
                game.AvatarRenderer.Render(renderer, camera, avatarTf.Position);
        }
        else
        {
            avatarPos = shipTf.Position; // fallback when dead
        }

        // Rocks, enemies, projectiles
        SurfaceRockRenderer.RenderRocks(renderer, camera, world);
        SurfaceEnemyRenderer.RenderEnemies(renderer, camera, world);
        ProjectileRenderer.RenderProjectiles(renderer, camera, world);

        // Damage/explosions
        ProjectileRenderer.RenderDamageEffects(renderer, camera, _damagePopups);
        ProjectileRenderer.RenderExplosions(renderer, camera, _explosions);

        // Interaction prompts
        if (!_sim.PlayerDead && !_playerInsideShip)
        {
            HudRenderer.RenderPlanetSurfacePrompt(renderer,
                _inVehicle, _sim.NearShip, _sim.NearVehicle, _sim.LocalVehicleDeployed, _sim.NearSettlement,
                game.Input.GetActionHelpText(InputAction.Interact));
        }

        // HUD
        if (!_sim.PlayerDead)
        {
            HudRenderer.RenderPlanetSurfaceHud(renderer, game.Player, _planet,
                _starSystem.DangerLevel, _inVehicle, world, _simPlayer.Entity);
        }

        // Combat message
        if (_sim.CombatMessage != null)
            HudRenderer.RenderCenteredMessage(renderer, _sim.CombatMessage, -40, new Color3(255, 220, 80), 2f);

        // Death message
        if (_sim.PlayerDead)
        {
            HudRenderer.RenderCenteredMessage(renderer, "YOU DIED", -20, new Color3(255, 80, 80), 3f);
            HudRenderer.RenderCenteredMessage(renderer, "RESPAWNING...", 20, new Color3(200, 200, 200), 1.5f);
        }

        // Minimap
        Vector2? vehiclePos = _sim.LocalVehicleDeployed && !_inVehicle
            ? world.Get<Transform>(_sim.LocalVehicleEntity).Position
            : null;
        HudMinimapRenderer.RenderPlanetSurfaceMinimap(renderer, _sim.SurfaceData,
            avatarPos, shipTf.Position, vehiclePos, world);

        // Off-screen indicators
        if (!_sim.PlayerDead && !_playerInsideShip)
        {
            HudIndicatorsRenderer.RenderSettlementOffscreenIndicators(renderer, camera,
                _sim.SurfaceData.Settlements, game.Player);
            if (!(game.Player.Navigation.HasTarget && game.Player.Navigation.Type == NavigationTargetType.SurfaceTarget
                && game.Player.Navigation.Name == "SHIP"))
                HudIndicatorsRenderer.RenderShipOffscreenIndicator(renderer, camera, shipTf.Position);
            HudIndicatorsRenderer.RenderPlanetSurfaceMissionOffscreenIndicators(renderer, camera,
                game.Player, _starSystem.Index, _planet.Index, _sim.SurfaceData.Settlements);

            if (game.Player.Navigation.HasTarget && game.Player.Navigation.Type == NavigationTargetType.SurfaceTarget)
            {
                var targetPos = new Vector2(game.Player.Navigation.WorldX, game.Player.Navigation.WorldY);
                HudIndicatorsRenderer.RenderNavTargetOffscreenIndicator(renderer, camera,
                    targetPos, game.Player.Navigation.Name, game.Player.Navigation.Color);
            }
        }

        // Overlays
        _inGameMenuOverlay.Render(game);
        _surfaceMapOverlay.Render(game);
        _starshipMenuOverlay.Render(game);
    }
}
