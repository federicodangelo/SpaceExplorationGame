using System.Numerics;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Input;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.UI.Overlays.Map;
using SpaceExplorationGame.UI.Overlays.Menu;
using Engine.Platform;
using SpaceExplorationGame.Core.Config;

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
    private readonly Camera _camera = new(WindowConfig.DefaultWindowWidth, WindowConfig.DefaultWindowHeight,
        CameraConfig.PlanetSurfaceZoomMin, CameraConfig.PlanetSurfaceZoomMax);

    // ── Input systems ───────────────────────────────────────────────
    private PlayerAvatarInputSystem _inputSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private PlayerVehicleInputSystem? _vehicleMovementSystem;

    // ── Background stars ──────────────────────────────────────────────
    private StarsBackgroundRenderer? _starsBackground;

    // ── Visual effects ──────────────────────────────────────────────
    private readonly List<DamagePopup> _damagePopups = [];
    private readonly List<Explosion> _explosions = [];
    private readonly TireMarkRenderer _tireMarkRenderer = new();

    // ── Player state (rendering/input only) ─────────────────────────
    private bool _inVehicle;
    private bool _playerInsideShip = true;
    private float _playerFireCooldown;
    private Vector2 _lastMoveDir = new(0, -1);

    // ── Combat music ────────────────────────────────────────────────
    private string _activeMusicTheme = AudioThemes.PlanetSurface;

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

    private bool AnyOverlayOpen =>
        _inGameMenuOverlay.IsOpen || _surfaceMapOverlay.IsOpen || _starshipMenuOverlay.IsOpen;

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

        // Generate background star field outside the planet disc (Poisson disk, fixed seed)
        {
            float ts = WindowConfig.TileSize;
            float discCX = (_sim.SurfaceData.Width - 1) * 0.5f * ts;
            float discCY = (_sim.SurfaceData.Height - 1) * 0.5f * ts;
            float discR = (MathF.Min(_sim.SurfaceData.Width, _sim.SurfaceData.Height) * 0.5f - 2f) * ts;
            float discRSq = discR * discR;
            _starsBackground = new StarsBackgroundRenderer(parallaxFactor: 0.12f);
            _starsBackground.Generate(
                discCX - discR, discCY - discR, discCX + discR, discCY + discR,
                seed: 0xC1A551C_5AFED1CuL,
                spriteRenderer: game.SpriteRenderer,
                minDist: 1200f,
                filter: p => { float dx = p.X - discCX, dy = p.Y - discCY; return dx * dx + dy * dy > discRSq; });
        }

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
        _inputSystem = new PlayerAvatarInputSystem(_sim.EcsWorld, game.Input, avatarSpeed);
        _inputSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(_sim.EcsWorld, _camera);
        _cameraFollowSystem.Initialize();

        // Camera
        var startPos = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;
        _camera.Position = startPos;
        _camera.Zoom = CameraConfig.PlanetSurfaceZoomDefault;
        _camera.ClampZoom();

        // Open starship menu on fresh landing
        if (_playerInsideShip)
            _waitingToOpenStarshipMenuAfterLanding = true;

        // Music
        game.Audio.SetMusicTheme(AudioThemes.PlanetSurface);
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
        _tireMarkRenderer.Clear();

        // Remove player from simulation
        if (_sim != null && _simPlayer != null)
            _sim.RemovePlayer(_simPlayer);
    }

    public override void UpdateInput(Game game)
    {
        if (_waitingToOpenStarshipMenuAfterLanding) return;

        if (AnyOverlayOpen)
            ZeroPlayerMovementAcceleration();

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
            _camera.Zoom *= 1f + input.MouseWheelY * CameraConfig.CameraZoomFactor;
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
                _inputSystem.Update(in dt);
            }

            // Player shooting
            HandlePlayerShooting(game, dt);
        }
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;
        var t = _debugTimer;
        t.Begin();

        t.Time("MenuOverlay", () => _inGameMenuOverlay.Update(game));

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

        _starshipMenuOverlay.Update(game);
        _surfaceMapOverlay.Update(game);
        _inGameMenuOverlay.Update(game);

        // Camera follows player
        t.Time("CameraFollow", () => _cameraFollowSystem.Update(in dt));

        // Process simulation events
        t.Time("SimEvents", () => ProcessSimulationEvents(game));

        // Visual effects
        t.Time("VisualFX", () => CombatHelper.UpdateVisualEffects(_damagePopups, _explosions, dt));

        // Tire marks
        if (_inVehicle && _sim.LocalVehicleDeployed &&
            !_sim.PlayerDead && _sim.EcsWorld.IsAlive(_simPlayer.Entity))
        {
            var vehicleTf = _sim.EcsWorld.Get<Transform>(_sim.LocalVehicleEntity);
            float speed = _sim.EcsWorld.Has<Velocity>(_simPlayer.Entity)
                ? _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity).Linear.Length()
                : 0f;
            _tireMarkRenderer.Update(dt, vehicleTf.Position, vehicleTf.Rotation, true, speed);
        }
        else
        {
            _tireMarkRenderer.Update(dt, Vector2.Zero, 0f, false, 0f);
        }

        // Combat music
        if (_sim.CombatMusicTimer > 0)
        {
            if (_activeMusicTheme != AudioThemes.Combat)
            {
                game.Audio.SetMusicTheme(AudioThemes.Combat);
                _activeMusicTheme = AudioThemes.Combat;
            }
        }
        else if (_activeMusicTheme != AudioThemes.PlanetSurface)
        {
            game.Audio.SetMusicTheme(AudioThemes.PlanetSurface);
            _activeMusicTheme = AudioThemes.PlanetSurface;
        }
    }

    private void ProcessSimulationEvents(Game game)
    {
        var playerPos = _sim.EcsWorld.IsAlive(_simPlayer.Entity)
            ? _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position
            : _camera.Position;

        // Enemy weapon fire SFX
        foreach (var spawn in _sim.EnemyProjectilesSpawnedLastUpdate)
            game.Audio.PlaySfxAtDistance(AudioSfx.EnemyLaser, spawn.Pos, playerPos, 0.4f);

        // Damage popups + SFX
        CombatHelper.CreateDamagePopups(_damagePopups, _sim.DamageEventsLastUpdate);
        CombatHelper.PlayDamageSfx(game.Audio, _sim.DamageEventsLastUpdate, playerPos, 0.5f);

        // Destroyed entities → explosions + SFX
        CombatHelper.ProcessDestroyedEntities(game.Audio, _explosions,
            _sim.DestroyedEntitiesLastUpdate, playerPos,
            faction => faction == Faction.Pirate
                ? new Color3(255, 100, 50) : new Color3(200, 180, 100),
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
            float weaponDamage = CombatConfig.BaseAvatarWeaponDamage + avatarStats.WeaponDamage;
            _playerFireCooldown = CombatConfig.AvatarFireRate;

            ref var avatarTf = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);

            Vector2 aimDir;
            var gamepadHeading = input.ActiveInputMethod == InputMethod.Gamepad
                ? input.GetActionAxisDirection(InputActionAxis.Heading) : Vector2.Zero;

            if (gamepadHeading != Vector2.Zero)
                aimDir = gamepadHeading;
            else if (input.IsMouseDown(MouseButton.Left))
            {
                var mouseWorld = _camera.ScreenToWorld(new Vector2(input.MouseX, input.MouseY));
                aimDir = Vector2.Normalize(mouseWorld - avatarTf.Position);
                if (float.IsNaN(aimDir.X)) aimDir = _lastMoveDir;
            }
            else
                aimDir = _lastMoveDir;

            var spawnPos = avatarTf.Position + aimDir * 14f;
            EntityFactory.CreateProjectile(_sim.EcsWorld, _simPlayer.Entity, spawnPos, aimDir,
                weaponDamage, CombatConfig.AvatarProjectileSpeed, Faction.Player,
                new Color3(100, 255, 100), CombatConfig.AvatarProjectileLifetime, Vector2.Zero);
            game.Audio.PlaySfx(AudioSfx.LaserFire, 0.5f);
        }
    }

    private void ZeroPlayerMovementAcceleration()
    {
        if (_sim == null || _simPlayer == null || !_sim.EcsWorld.IsAlive(_simPlayer.Entity)) return;
        ref var vel = ref _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity);
        vel.Acceleration = Vector2.Zero;
        if (!_inVehicle)
        {
            vel.Linear = Vector2.Zero;
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
        _vehicleMovementSystem = new PlayerVehicleInputSystem(
            _sim.EcsWorld, game.Input, _simPlayer.Entity,
            acceleration: vStats.Acceleration > 0 ? vStats.Acceleration : AvatarConfig.VehicleAcceleration,
            maxSpeed: vStats.MaxSpeed > 0 ? vStats.MaxSpeed : AvatarConfig.VehicleMaxSpeed,
            rotationSpeed: vStats.RotationSpeed > 0 ? vStats.RotationSpeed : AvatarConfig.VehicleRotationSpeed,
            friction: AvatarConfig.VehicleFriction + vStats.Friction);

        if (_sim.EcsWorld.Has<Velocity>(_simPlayer.Entity))
        {
            ref var avatarVelocity = ref _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity);
            avatarVelocity.MaxSpeed = vStats.MaxSpeed > 0 ? vStats.MaxSpeed : AvatarConfig.VehicleMaxSpeed;
            avatarVelocity.MaxRotationSpeed = vStats.RotationSpeed > 0 ? vStats.RotationSpeed : AvatarConfig.VehicleRotationSpeed;
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
                int launchTileX = Math.Clamp((int)MathF.Round(launchShipTf.Position.X / WindowConfig.TileSize), 0, Math.Max(0, _sim.SurfaceData.Width - 1));
                int launchTileY = Math.Clamp((int)MathF.Round(launchShipTf.Position.Y / WindowConfig.TileSize), 0, Math.Max(0, _sim.SurfaceData.Height - 1));

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
                avatarTf.Position = shipTf.Position;
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

    public override void RenderGame(Game game)
    {
        _camera.Update(game.SpriteRenderer.WindowWidth, game.SpriteRenderer.WindowHeight);
        var renderer = game.SpriteRenderer;
        var camera = _camera;
        var world = _sim.EcsWorld;

        // Background stars in the void outside the planet disc
        _starsBackground?.RenderParallax(renderer, camera, (float)game.GlobalTime);

        // Terrain
        PlanetSurfaceRenderer.RenderTerrain(renderer, camera, _sim.SurfaceData,
            game.GlobalTime, _planet.Type);

        // Atmosphere halo – soft glow at the disc boundary to mask jagged tile edges
        PlanetSurfaceRenderer.RenderAtmosphere(renderer, camera, _sim.SurfaceData,
            _planet.Type, game.GlobalTime);

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

        // NPC ships landed on the surface
        game.EnemyShipRenderer.RenderLandedShips(renderer, camera, world);

        // Ship
        var shipTf = world.Get<Transform>(_sim.LocalShipEntity);
        game.SpaceshipRenderer.RenderShadow(renderer, camera, shipTf.Position, game.Player.CurrentShipType.SpriteSize);
        game.SpaceshipRenderer.RenderWithLabel(renderer, camera, shipTf.Position, shipTf.Rotation,
            game.Player.CurrentShipType.Id, game.Player.CurrentShipType.SpriteSize);

        // Tire marks (drawn above terrain, below vehicle)
        _tireMarkRenderer.Render(renderer, camera);

        // Vehicle
        if (_sim.LocalVehicleDeployed)
        {
            var vehicleTf = world.Get<Transform>(_sim.LocalVehicleEntity);
            // Derive visual steering angle from current rotation velocity (lives on player entity).
            float steerAngle = 0f;
            if (_inVehicle && world.IsAlive(_simPlayer.Entity) && world.Has<Velocity>(_simPlayer.Entity))
            {
                ref var vehicleVel = ref world.Get<Velocity>(_simPlayer.Entity);
                float maxRot = vehicleVel.MaxRotationSpeed > 0f ? vehicleVel.MaxRotationSpeed : 1f;
                float normalised = Math.Clamp(vehicleVel.RotationVelocity / maxRot, -1f, 1f);
                steerAngle = normalised * 30f;   // ±30° max visual turn
            }
            game.VehicleRenderer.Render(renderer, camera, vehicleTf.Position,
                vehicleTf.Rotation, _inVehicle, steerAngle);
        }

        // NPCs on foot
        SurfaceEnemyRenderer.RenderEnemies(renderer, camera, world, _planet.Type);


        // Player avatar
        if (!_sim.PlayerDead && world.IsAlive(_simPlayer.Entity))
        {
            ref var avatarTf = ref world.Get<Transform>(_simPlayer.Entity);
            if (!_inVehicle && !_playerInsideShip)
                game.AvatarRenderer.Render(renderer, camera, avatarTf.Position);
        }

        // Rocks, enemies, NPC ships, projectiles
        SurfaceRockRenderer.RenderRocks(renderer, camera, world);
        ProjectileRenderer.RenderProjectiles(renderer, camera, world);

        // Damage/explosions
        ProjectileRenderer.RenderDamageEffects(renderer, camera, _damagePopups);
        ProjectileRenderer.RenderExplosions(renderer, camera, _explosions);

        // Weather overlay based on planet biome
        int screenW = renderer.WindowWidth;
        int screenH = renderer.WindowHeight;
        WeatherRenderer.Render(renderer, screenW, screenH, _planet, game.GlobalTime,
            _camera.Position.X, _camera.Position.Y);
    }

    public override void RenderHud(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = _camera;
        var world = _sim.EcsWorld;

        // Resolve positions needed for HUD
        var shipTf = world.Get<Transform>(_sim.LocalShipEntity);
        Vector2 avatarPos = !_sim.PlayerDead && world.IsAlive(_simPlayer.Entity)
            ? world.Get<Transform>(_simPlayer.Entity).Position
            : shipTf.Position; // fallback when dead

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

    public override IReadOnlyList<string>? GetDebugInfo()
    {
        _debugInfo.Begin();
        _debugInfo.Add($"Planet: {_planet.Name}  Type: {_planet.Type}");
        _debugInfo.Add($"Camera: ({_camera.Position.X:F0}, {_camera.Position.Y:F0}) Zoom: {_camera.Zoom:F2}");
        _debugInfo.Add($"InShip: {_playerInsideShip}  InVehicle: {_inVehicle}");
        _debugInfo.Add($"Popups: {_damagePopups.Count}  Explosions: {_explosions.Count}");
        return _debugInfo.Entries;
    }
}
