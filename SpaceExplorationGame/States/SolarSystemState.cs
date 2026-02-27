using System.Numerics;
using Arch.Core;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.ECS.Systems.Movement;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.UI.Overlays.Menu;
using SpaceExplorationGame.UI.Overlays.Map;

namespace SpaceExplorationGame.States;

/// <summary>
/// Solar system state: Player flies their ship around a solar system with orbiting bodies.
/// Rendering-only — all simulation logic lives in <see cref="SolarSystemSimulation"/>.
/// </summary>
public class SolarSystemState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

    // ── Simulation ──────────────────────────────────────────────────
    private SolarSystemSimulation _sim = null!;
    private SimulationPlayer _simPlayer = null!;

    private readonly StarSystemData _starSystem;

    // ── Camera ──────────────────────────────────────────────────────
    private readonly Camera _camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
        GameConfig.SolarSystemZoomMin, GameConfig.SolarSystemZoomMax);

    // ── Input system (runs on simulation ECS world) ─────────────────
    private PlayerShipInputSystem _playerShipInputSystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private LabelRenderer _labelRenderer = null!;

    // ── Visual effects (rendering-only) ─────────────────────────────
    private readonly List<DamagePopup> _damagePopups = [];
    private readonly List<Explosion> _explosions = [];

    // ── Combat music ────────────────────────────────────────────────
    private MusicTheme _activeMusicTheme = MusicTheme.SolarSystem;

    // ── Overlays ────────────────────────────────────────────────────
    private readonly SpaceStationOverlay _stationOverlay = new();
    private readonly SpaceStationData? _autoOpenStation;
    private readonly GalaxyMapOverlay _galaxyMapOverlay = new();
    private readonly bool _autoOpenGalaxyMap;
    private PlanetLandingOverlay _planetLandingOverlay = null!;
    private readonly PlanetData? _autoOpenPlanet;
    private readonly InGameMenuOverlay _inGameMenuOverlay = new() { StateType = GameStateType.SolarSystem };

    // ── Anchor (keeps ship at fixed offset from target while overlays open) ──
    private Entity _anchorEntity;
    private Vector2 _anchorOffset;

    public SolarSystemState(StarSystemData starSystem, SpaceStationData? autoOpenStation = null,
        bool autoOpenGalaxyMap = false, PlanetData? autoOpenPlanet = null)
    {
        _starSystem = starSystem;
        _autoOpenStation = autoOpenStation;
        _autoOpenGalaxyMap = autoOpenGalaxyMap;
        _autoOpenPlanet = autoOpenPlanet;
    }

    public override void Enter(Game game)
    {
        _planetLandingOverlay = new PlanetLandingOverlay(game.Textures);
        _planetLandingOverlay.OnLandingConfirmed = (g, landing) => BeginSeamlessLanding(g, landing);
        _inGameMenuOverlay.OnMapRequested = g => _galaxyMapOverlay.Open(g);

        // Music
        game.Audio.SetMusicTheme(MusicTheme.SolarSystem);

        // Get or create the simulation
        _sim = game.Coordinator.FindOrCreate<SolarSystemSimulation>(
            s => s.StarSystem.Index == _starSystem.Index,
            () => new SolarSystemSimulation(game, _starSystem));

        // Add player to simulation
        _simPlayer = _sim.AddPlayer(game.Player);

        // Create input/camera systems that operate on the simulation's ECS world
        _playerShipInputSystem = new PlayerShipInputSystem(_sim.EcsWorld, game.Input);
        _playerShipInputSystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(_sim.EcsWorld, _camera);
        _cameraFollowSystem.Initialize();

        _labelRenderer = new LabelRenderer(_sim.EcsWorld, game.SpriteRenderer, _camera);

        // Camera initial position
        var shipPos = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;
        _camera.Position = shipPos;
        _camera.Zoom = GameConfig.SolarSystemZoomDefault;
        _camera.ClampZoom();

        // Auto-open overlays
        if (_autoOpenStation != null)
        {
            int stIdx = _sim.Stations.FindIndex(s => s.Index == _autoOpenStation.Index);
            if (stIdx >= 0 && stIdx < _sim.StationEntities.Count)
                SetAnchor(_sim.StationEntities[stIdx]);
            _stationOverlay.Open(_starSystem, _autoOpenStation, game);
        }

        if (_autoOpenGalaxyMap)
            _galaxyMapOverlay.Open(game);

        if (_autoOpenPlanet != null)
        {
            int pIdx = _sim.Planets.FindIndex(p => p.Name == _autoOpenPlanet.Name);
            if (pIdx >= 0 && pIdx < _sim.PlanetEntities.Count)
                SetAnchor(_sim.PlanetEntities[pIdx]);
            _planetLandingOverlay.Open(_starSystem, _autoOpenPlanet, game);
        }
    }

    public override void Exit(Game game)
    {
        _damagePopups.Clear();
        _explosions.Clear();
        _planetLandingOverlay.Cleanup();

        // Remove player from simulation (simulation stays alive in coordinator)
        if (_sim != null && _simPlayer != null)
            _sim.RemovePlayer(_simPlayer);
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void UpdateInput(Game game)
    {
        var input = game.Input;

        // Overlays take priority
        if (_planetLandingOverlay.UpdateInput(game)) return;
        if (_galaxyMapOverlay.UpdateInput(game)) return;
        if (_stationOverlay.UpdateInput(game)) return;

        if (_inGameMenuOverlay.UpdateInput(game)) return;
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

        // Interact
        if (input.IsActionPressed(InputAction.Interact))
        {
            if (_sim.NearbyStationIndex >= 0)
            {
                SetAnchor(_sim.StationEntities[_sim.NearbyStationIndex]);
                _stationOverlay.Open(_starSystem, _sim.Stations[_sim.NearbyStationIndex], game);
            }
            else if (_sim.NearbyPlanetIndex >= 0)
            {
                SetAnchor(_sim.PlanetEntities[_sim.NearbyPlanetIndex]);
                _planetLandingOverlay.Open(_starSystem, _sim.Planets[_sim.NearbyPlanetIndex], game);
            }
            else if (_sim.NearbyMoonIndex >= 0)
            {
                SetAnchor(_sim.MoonEntities[_sim.NearbyMoonPlanetIndex][_sim.NearbyMoonIndex]);
                var moonData = _sim.Planets[_sim.NearbyMoonPlanetIndex].Moons[_sim.NearbyMoonIndex];
                _planetLandingOverlay.Open(_starSystem, moonData.ToPlanetData(_sim.NearbyMoonPlanetIndex), game,
                    isMoon: true, moonPlanetIndex: _sim.NearbyMoonPlanetIndex, moonIndex: _sim.NearbyMoonIndex);
            }
        }

        // Open galaxy map
        if (input.IsActionPressed(InputAction.ToggleMap))
            _galaxyMapOverlay.Open(game);

        // Camera zoom
        if (input.MouseWheelY != 0)
        {
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
        }

        // Write player ship input (only when no overlay is blocking)
        if (!_sim.PlayerDead && _sim.EcsWorld.IsAlive(_simPlayer.Entity))
        {
            _sim.SyncPlayerShipComponent(_simPlayer);
            float dt = game.DeltaTime;
            _playerShipInputSystem.Update(in dt);
        }
    }

    public override void Update(Game game)
    {
        float dt = game.DeltaTime;

        _inGameMenuOverlay.Update(game);

        // Apply anchor (keep ship at station/planet while overlay open)
        ApplyAnchor();

        // Camera follows player
        _cameraFollowSystem.Update(in dt);

        // Handle respawn station auto-dock
        if (_sim.RespawnStationIndex >= 0)
        {
            int idx = _sim.RespawnStationIndex;
            _sim.RespawnStationIndex = -1;
            _simPlayer.Entity = _sim.Players.Count > 0 ? _sim.Players[0].Entity : default;
            _camera.Position = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;

            if (idx < _sim.Stations.Count)
            {
                SetAnchor(_sim.StationEntities[idx]);
                _stationOverlay.Open(_starSystem, _sim.Stations[idx], game);
            }
        }

        // Overlays active — still process but don't do gameplay interaction
        if (_inGameMenuOverlay.IsOpen)
        {
            _inGameMenuOverlay.Update(game);
            return;
        }
        if (_planetLandingOverlay.IsOpen) { _planetLandingOverlay.Update(game); return; }
        if (_galaxyMapOverlay.IsOpen) { _galaxyMapOverlay.Update(game); return; }
        if (_stationOverlay.IsOpen) { _stationOverlay.Update(game); return; }

        // Clear anchor when returning to normal gameplay
        ClearAnchor();

        // Process simulation events for audio/visual effects
        ProcessSimulationEvents(game);

        // Update visual effects
        CombatHelper.UpdateVisualEffects(_damagePopups, _explosions, dt);

        // Combat music tracking
        if (_sim.CombatMusicTimer > 0)
        {
            if (_activeMusicTheme != MusicTheme.Combat)
            {
                game.Audio.SetMusicTheme(MusicTheme.Combat);
                _activeMusicTheme = MusicTheme.Combat;
            }
        }
        else if (_activeMusicTheme != MusicTheme.SolarSystem)
        {
            game.Audio.SetMusicTheme(MusicTheme.SolarSystem);
            _activeMusicTheme = MusicTheme.SolarSystem;
        }
    }

    private void ProcessSimulationEvents(Game game)
    {
        // SFX for weapon fire
        foreach (var spawn in _sim.ProjectilesSpawnedLastUpdate)
        {
            game.Audio.PlaySfxAtDistance(
                spawn.Faction == Faction.Player ? SfxType.LaserFire : SfxType.EnemyLaser,
                spawn.Pos, _camera.Position, 0.5f);
        }

        // Damage events → visual popups + SFX
        CombatHelper.CreateDamagePopups(_damagePopups, _sim.DamageEventsLastUpdate);
        CombatHelper.PlayDamageSfx(game.Audio, _sim.DamageEventsLastUpdate, _camera.Position, 0.6f);

        // Destroyed entities → explosions + SFX
        CombatHelper.ProcessDestroyedEntities(game.Audio, _explosions,
            _sim.DestroyedEntitiesLastUpdate, _camera.Position,
            faction => faction == Faction.Pirate
                ? new Color3(255, 120, 80) : new Color3(200, 200, 200));
    }

    private void BeginSeamlessLanding(Game game, LandingSelectionRequest landing)
    {
        Vector2 shipWorldPos = _sim.EcsWorld.IsAlive(_simPlayer.Entity)
            ? _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position
            : game.Player.ShipWorldPosition;

        Vector2 targetBodyPos = shipWorldPos;
        if (landing.IsMoon)
        {
            if (landing.MoonPlanetIndex >= 0 && landing.MoonPlanetIndex < _sim.MoonEntities.Count
                && landing.MoonIndex >= 0 && landing.MoonIndex < _sim.MoonEntities[landing.MoonPlanetIndex].Count)
            {
                var moonEntity = _sim.MoonEntities[landing.MoonPlanetIndex][landing.MoonIndex];
                if (_sim.EcsWorld.IsAlive(moonEntity))
                    targetBodyPos = _sim.EcsWorld.Get<Transform>(moonEntity).Position;
            }
        }
        else
        {
            int pIdx = _sim.Planets.FindIndex(p => p.Index == landing.Planet.Index);
            if (pIdx >= 0 && pIdx < _sim.PlanetEntities.Count)
            {
                var planetEntity = _sim.PlanetEntities[pIdx];
                if (_sim.EcsWorld.IsAlive(planetEntity))
                    targetBodyPos = _sim.EcsWorld.Get<Transform>(planetEntity).Position;
            }
        }

        game.ChangeState(new OrbitalSurfaceTransitionState(
            landing.StarSystem, landing.Planet,
            landing.TileX, landing.TileY,
            shipWorldPos, targetBodyPos,
            _camera.Position, _camera.Zoom,
            landing.IsMoon, landing.MoonPlanetIndex, landing.MoonIndex));
    }

    // ── Anchor ──────────────────────────────────────────────────────

    private void SetAnchor(Entity target)
    {
        _anchorEntity = target;
        var targetPos = _sim.EcsWorld.Get<Transform>(target).Position;
        var shipPos = _sim.EcsWorld.Get<Transform>(_simPlayer.Entity).Position;
        _anchorOffset = shipPos - targetPos;

        ref var vel = ref _sim.EcsWorld.Get<Velocity>(_simPlayer.Entity);
        vel.Linear = Vector2.Zero;
        vel.Acceleration = Vector2.Zero;
        vel.RotationVelocity = 0f;

        if (_sim.EcsWorld.Has<ShipInputComponent>(_simPlayer.Entity))
        {
            ref var shipInput = ref _sim.EcsWorld.Get<ShipInputComponent>(_simPlayer.Entity);
            shipInput.AccelerationDirection = Vector2.Zero;
            shipInput.RotationSpeed = 0f;
            shipInput.Shoot = false;
        }
    }

    private void ApplyAnchor()
    {
        if (_anchorEntity == default || !_sim.EcsWorld.IsAlive(_anchorEntity))
            return;
        var targetPos = _sim.EcsWorld.Get<Transform>(_anchorEntity).Position;
        ref var shipTransform = ref _sim.EcsWorld.Get<Transform>(_simPlayer.Entity);
        shipTransform.Position = targetPos + _anchorOffset;
    }

    private void ClearAnchor()
    {
        if (_anchorEntity != default && _sim.EcsWorld.IsAlive(_anchorEntity))
            ApplyAnchor();
        _anchorEntity = default;
        _anchorOffset = Vector2.Zero;
    }

    // ── Render ──────────────────────────────────────────────────────

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = _camera;
        var world = _sim.EcsWorld;

        float starCenterX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float starCenterY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 starCenter = new(starCenterX, starCenterY);

        // Background
        SolarSystemRenderer.RenderBackgroundStars(renderer, camera, _sim.BackgroundStars);
        SolarSystemRenderer.RenderBackgroundNebulae(renderer, camera, _sim.BackgroundNebulae);
        SolarSystemRenderer.RenderOrbitLines(renderer, camera, _sim.Planets, starCenter);

        // Asteroids
        game.AsteroidRenderer.RenderAsteroids(renderer, camera, world, _sim.AsteroidEntities);

        // Star
        float starDisplayRadius = _starSystem.StarRadius * 2f;
        game.StarRenderer.Render(renderer, camera, starCenter, starDisplayRadius, _starSystem.StarColor,
            (float)game.GlobalTime);

        // Planets and moons
        game.PlanetRenderer.RenderPlanetsAndMoons(renderer, camera, world,
            _sim.Planets, _sim.PlanetEntities, _sim.MoonEntities, (float)game.GlobalTime);

        // Stations
        game.StationRenderer.RenderStations(renderer, camera, world,
            _sim.StationEntities, game.GlobalTime);

        // Labels
        _labelRenderer.Render();

        // Mission markers
        HudIndicatorsRenderer.RenderSolarSystemMissionMarkers(renderer, camera,
            game.Player, (float)game.GlobalTime, _starSystem.Index,
            _sim.StationEntities, _sim.PlanetEntities, _sim.Planets, world);

        // Thruster particles
        ParticleRenderer.RenderParticles(renderer, camera, world);

        // NPC ships
        SolarSystemRenderer.RenderNPCShips(renderer, camera, world,
            _sim.EnemyEntities, game.EnemyShipRenderer);

        // Projectiles
        ProjectileRenderer.RenderProjectiles(renderer, camera, world);

        // Player ship
        if (!_sim.PlayerDead && world.IsAlive(_simPlayer.Entity))
        {
            ref var shipTransform = ref world.Get<Transform>(_simPlayer.Entity);
            int shipSpriteSize = game.Player.CurrentShipType.SpriteSize;
            game.SpaceshipRenderer.RenderFlying(renderer, camera, shipTransform.Position,
                shipTransform.Rotation, game.Player.CurrentShipType.Id, shipSpriteSize);
        }

        // Visual effects
        ProjectileRenderer.RenderDamageEffects(renderer, camera, _damagePopups);
        ProjectileRenderer.RenderExplosions(renderer, camera, _explosions);

        // HUD
        {
            float speed = 0f;
            if (!_sim.PlayerDead && world.IsAlive(_simPlayer.Entity))
            {
                ref var vel = ref world.Get<Velocity>(_simPlayer.Entity);
                speed = vel.Linear.Length();
            }
            HudRenderer.RenderSolarSystemHud(renderer, game.Player, _starSystem,
                world, _simPlayer.Entity, speed);
        }

        // Minimap
        HudMinimapRenderer.RenderSolarSystemMinimap(renderer, _sim.Planets, _sim.PlanetEntities,
            _sim.MoonEntities, _sim.StationEntities, _sim.AsteroidEntities, _sim.EnemyEntities,
            _simPlayer.Entity, _sim.StarEntity, world);

        // Off-screen indicators
        if (!_sim.PlayerDead)
        {
            HudIndicatorsRenderer.RenderOffscreenIndicators(renderer, camera, world,
                _sim.EnemyEntities, _simPlayer.Entity, 2500f);
            if (!(game.Player.Navigation.HasTarget && game.Player.Navigation.Type == NavigationTargetType.Star))
                HudIndicatorsRenderer.RenderStarOffscreenIndicator(renderer, camera, starCenter);
            HudIndicatorsRenderer.RenderSolarSystemObjectOffscreenIndicators(renderer, camera,
                _simPlayer.Entity, world, _sim.PlanetEntities, _sim.Planets,
                _sim.StationEntities, _sim.Stations, 5000f, game.Player);
            HudIndicatorsRenderer.RenderSolarSystemMissionOffscreenIndicators(renderer, camera,
                game.Player, _starSystem.Index, _sim.StationEntities, _sim.PlanetEntities, world);

            if (game.Player.Navigation.HasTarget)
            {
                Vector2? targetPos = ResolveNavTargetPosition(game);
                if (targetPos.HasValue)
                    HudIndicatorsRenderer.RenderNavTargetOffscreenIndicator(renderer, camera,
                        targetPos.Value, game.Player.Navigation.Name, game.Player.Navigation.Color);
            }
        }

        // Death screen
        if (_sim.PlayerDead)
            HudRenderer.RenderDeathScreen(renderer, _sim.RespawnTimer);

        // Mining panel
        if (_sim.MiningHudTimer > 0 && world.IsAlive(_sim.LastHitAsteroid)
            && world.Has<AsteroidField>(_sim.LastHitAsteroid))
        {
            ref var af = ref world.Get<AsteroidField>(_sim.LastHitAsteroid);
            ref var ah = ref world.Get<Health>(_sim.LastHitAsteroid);
            HudRenderer.RenderMiningPanel(renderer, af.Resource, ah.Hull, ah.MaxHull, af.ResourceAmount);
        }

        // Mining message
        if (_sim.MiningMessage != null)
            HudRenderer.RenderCenteredMessage(renderer, _sim.MiningMessage, -40, new Color3(255, 220, 80), 2.5f);

        // Combat message
        if (_sim.CombatMessage != null)
            HudRenderer.RenderCenteredMessage(renderer, _sim.CombatMessage, 30, new Color3(255, 200, 80), 2f);

        // Interaction prompts
        HudRenderer.RenderSolarSystemPrompt(renderer,
            _sim.NearbyPlanetIndex, _sim.NearbyMoonIndex, _sim.NearbyMoonPlanetIndex,
            _sim.NearbyStationIndex, _sim.Planets, _sim.Stations,
            game.Input.GetActionHelpText(InputAction.Interact));

        // Overlays
        _stationOverlay.Render(game);
        _planetLandingOverlay.Render(game);
        _galaxyMapOverlay.Render(game);
        _inGameMenuOverlay.Render(game);
    }

    private Vector2? ResolveNavTargetPosition(Game game)
    {
        var player = game.Player;
        var world = _sim.EcsWorld;
        switch (player.Navigation.Type)
        {
            case NavigationTargetType.Star:
                if (world.IsAlive(_sim.StarEntity))
                    return world.Get<Transform>(_sim.StarEntity).Position;
                break;
            case NavigationTargetType.Planet:
                if (player.Navigation.PlanetIndex >= 0 && player.Navigation.PlanetIndex < _sim.PlanetEntities.Count
                    && world.IsAlive(_sim.PlanetEntities[player.Navigation.PlanetIndex]))
                    return world.Get<Transform>(_sim.PlanetEntities[player.Navigation.PlanetIndex]).Position;
                break;
            case NavigationTargetType.Moon:
                if (player.Navigation.PlanetIndex >= 0 && player.Navigation.PlanetIndex < _sim.MoonEntities.Count
                    && player.Navigation.MoonIndex >= 0 && player.Navigation.MoonIndex < _sim.MoonEntities[player.Navigation.PlanetIndex].Count
                    && world.IsAlive(_sim.MoonEntities[player.Navigation.PlanetIndex][player.Navigation.MoonIndex]))
                    return world.Get<Transform>(_sim.MoonEntities[player.Navigation.PlanetIndex][player.Navigation.MoonIndex]).Position;
                break;
            case NavigationTargetType.Station:
                if (player.Navigation.StationIndex >= 0 && player.Navigation.StationIndex < _sim.StationEntities.Count
                    && world.IsAlive(_sim.StationEntities[player.Navigation.StationIndex]))
                    return world.Get<Transform>(_sim.StationEntities[player.Navigation.StationIndex]).Position;
                break;
        }
        return null;
    }
}
