using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.ECS.Systems;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Rendering;
using SpaceExplorationGame.Rendering.Base;
using SpaceExplorationGame.UI.Hud;
using SpaceExplorationGame.ECS.Systems.Movement;
using SpaceExplorationGame.ECS.Systems.AI;
using SpaceExplorationGame.ECS.Systems.Combat;
using SpaceExplorationGame.ECS.Systems.Effects;
using SpaceExplorationGame.UI.Overlays.Menu;
using SpaceExplorationGame.UI.Overlays.Map;

namespace SpaceExplorationGame.States;

/// <summary>
/// Solar system state: Player flies their ship around a solar system with orbiting bodies.
/// </summary>
public class SolarSystemState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

    private readonly StarSystemData _starSystem;
    private List<PlanetData> _planets = [];
    private List<AsteroidBeltData> _asteroidBelts = [];
    private List<SpaceStationData> _stations = [];

    private Entity _playerShip;
    private Entity _starEntity;
    private List<Entity> _planetEntities = [];
    private List<Entity> _stationEntities = [];
    private List<List<Entity>> _moonEntities = [];

    // Mineable asteroids (ECS entities)
    private List<Entity> _asteroidEntities = [];

    // Mining state (projectile-based)
    private Entity _lastHitAsteroid;      // last asteroid hit by projectile (for HUD)
    private float _miningHudTimer;        // how long to show the mining panel
    private string? _miningMessage;       // feedback message
    private float _miningMessageTimer;    // how long to show the message

    // Interaction
    private int _nearbyPlanetIndex = -1;
    private int _nearbyStationIndex = -1;
    private int _nearbyMoonPlanetIndex = -1;  // planet index of nearby moon
    private int _nearbyMoonIndex = -1;        // moon index within that planet
    private const float InteractionRadius = 20f;

    // Background stars
    private List<BackgroundStar> _bgStars = [];
    private List<NebulaCloud> _bgNebulae = [];

    // Camera
    private readonly Camera _camera = new(GameConfig.WindowWidth, GameConfig.WindowHeight,
        GameConfig.SolarSystemZoomMin, GameConfig.SolarSystemZoomMax);

    // Station overlay (docked at station)
    private readonly SpaceStationOverlay _stationOverlay = new();
    private readonly SpaceStationData? _autoOpenStation;

    // Galaxy map overlay
    private readonly GalaxyMapOverlay _galaxyMapOverlay = new();
    private readonly bool _autoOpenGalaxyMap;

    // Planet landing overlay
    private PlanetLandingOverlay _planetLandingOverlay = null!;
    private readonly PlanetData? _autoOpenPlanet;

    // In-game menu overlay
    private readonly InGameMenuOverlay _inGameMenuOverlay = new() { StateType = GameStateType.SolarSystem };

    // Anchor: keeps the ship at a fixed offset from a target while overlays are open
    private Entity _anchorEntity;
    private Vector2 _anchorOffset;

    // ECS Systems
    private OrbitSystem _orbitSystem = null!;
    private VelocitySystem _velocitySystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private LabelRenderer _labelRenderer = null!;
    private InteractionProximitySystem _proximitySystem = null!;
    private ShipMovementSystem _shipMovementSystem = null!;

    // Combat systems
    private ProjectileSystem _projectileSystem = null!;
    private ShieldRegenSystem _shieldRegenSystem = null!;
    private EnemyAISystem _enemyAISystem = null!;
    private ParticleSystem _particleSystem = null!;

    // Combat state
    private List<Entity> _enemyEntities = [];
    private float[] _playerWeaponCooldowns = new float[2];
    private bool _playerDead;
    private float _respawnTimer;
    private const float RespawnDelay = 3f;
    private string? _combatMessage;
    private float _combatMessageTimer;

    // Visual effects
    private readonly List<DamagePopup> _damagePopups = [];
    private readonly List<Explosion> _explosions = [];

    // Thruster emitter defaults
    private const float ThrusterSpawnIntervalSeconds = 0.03f;

    // Combat music tracking
    private float _combatMusicTimer;
    private MusicTheme _activeMusicTheme = MusicTheme.SolarSystem;

    // Cached textures for this solar system
    private nint _starTexture;
    private List<nint> _planetTextures = [];
    private List<List<nint>> _moonTextures = [];

    public SolarSystemState(StarSystemData starSystem, SpaceStationData? autoOpenStation = null, bool autoOpenGalaxyMap = false, PlanetData? autoOpenPlanet = null)
    {
        _starSystem = starSystem;
        _autoOpenStation = autoOpenStation;
        _autoOpenGalaxyMap = autoOpenGalaxyMap;
        _autoOpenPlanet = autoOpenPlanet;
    }

    public override void Enter(Game game)
    {
        _planetLandingOverlay = new PlanetLandingOverlay(game.Textures);

        // Wire up map option in the in-game menu
        _inGameMenuOverlay.OnMapRequested = g => _galaxyMapOverlay.Open(g);

        // Music
        game.Audio.SetMusicTheme(MusicTheme.SolarSystem);

        var rng = game.Seeds.GetStarSystemRandom(_starSystem.Index);
        var (planets, belts, stations) = SolarSystemGenerator.Generate(rng, _starSystem);
        _planets = planets;
        _asteroidBelts = belts;
        _stations = stations;

        float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 center = new(centerX, centerY);
        float time = (float)game.GlobalTime;

        // Create star entity (doubled for solar system view)
        float starDisplayRadius = _starSystem.StarRadius * 2f;
        _starEntity = EntityFactory.CreateStar(game.EcsWorld, center, starDisplayRadius,
            _starSystem.Name, _starSystem.StarColor, _starSystem.Index);

        // Create planet entities — compute positions from global time
        _planetEntities.Clear();
        _moonEntities.Clear();
        for (int i = 0; i < _planets.Count; i++)
        {
            var planet = _planets[i];
            float angle = planet.StartAngle + planet.OrbitSpeed * time;
            var pos = center + new Vector2(
                MathF.Cos(angle) * planet.OrbitRadius,
                MathF.Sin(angle) * planet.OrbitRadius
            );

            var planetEntity = EntityFactory.CreatePlanet(game.EcsWorld, pos, _starEntity,
                planet.Name, planet.Radius, planet.Color,
                planet.OrbitRadius, planet.OrbitSpeed, planet.StartAngle,
                i, planet.HasSolidSurface);

            _planetEntities.Add(planetEntity);

            // Moons — also computed from global time
            var moons = new List<Entity>();
            foreach (var moon in planet.Moons)
            {
                float moonAngle = moon.StartAngle + moon.OrbitSpeed * time;
                var moonPos = pos + new Vector2(
                    MathF.Cos(moonAngle) * moon.OrbitRadius,
                    MathF.Sin(moonAngle) * moon.OrbitRadius
                );

                var moonEntity = EntityFactory.CreateMoon(game.EcsWorld, moonPos, planetEntity,
                    moon.Name, moon.Radius, moon.Color,
                    moon.OrbitRadius, moon.OrbitSpeed, moon.StartAngle, moon.Index);
                moons.Add(moonEntity);
            }
            _moonEntities.Add(moons);
        }

        // Create space station entities — positions from global time
        _stationEntities.Clear();
        foreach (var station in _stations)
        {
            Entity parent;
            if (station.OrbitParentPlanetIndex >= 0 && station.OrbitParentPlanetIndex < _planetEntities.Count)
            {
                parent = _planetEntities[station.OrbitParentPlanetIndex];
            }
            else
            {
                parent = _starEntity;
            }

            var parentTransform = game.EcsWorld.Get<Transform>(parent);
            float stAngle = station.StartAngle + station.OrbitSpeed * time;
            var stPos = parentTransform.Position + new Vector2(
                MathF.Cos(stAngle) * station.OrbitRadius,
                MathF.Sin(stAngle) * station.OrbitRadius
            );

            var stEntity = EntityFactory.CreateStation(game.EcsWorld, stPos, parent,
                station.Name, station.OrbitRadius, station.OrbitSpeed, station.StartAngle, station.Index);
            _stationEntities.Add(stEntity);
        }

        // Generate mineable asteroids as ECS entities
        var asteroidRng = new SeededRandom(rng.DeriveChildSeed(999));
        foreach (var belt in _asteroidBelts)
        {
            for (int i = 0; i < belt.AsteroidCount; i++)
            {
                float size = asteroidRng.NextFloat(40, 100);
                float hp = size * 5f; // bigger asteroids have more HP

                // Pick resource type based on weighted probabilities
                var resource = asteroidRng.NextFloat() switch
                {
                    < 0.30f => ResourceType.Iron,
                    < 0.55f => ResourceType.Nickel,
                    < 0.70f => ResourceType.Ice,
                    < 0.85f => ResourceType.Gold,
                    < 0.95f => ResourceType.Platinum,
                    _       => ResourceType.Crystal
                };

                int resourceAmount = (int)(size * asteroidRng.NextFloat(1f, 3f));

                var entity = EntityFactory.CreateAsteroid(game.EcsWorld, _starEntity, size, hp,
                    resource, resourceAmount,
                    asteroidRng.NextFloat(belt.InnerRadius, belt.OuterRadius),
                    asteroidRng.NextFloat(0.002f, 0.008f),
                    asteroidRng.NextFloat(0, MathF.PI * 2));
                _asteroidEntities.Add(entity);
            }
        }

        // --- Determine player ship starting position ---
        Vector2 shipStartPos;
        var returnCtx = game.Player.SolarSystemReturnContext;

        if (returnCtx == PlayerData.ReturnContext.FromStation && game.Player.ReturnStationIndex >= 0
            && game.Player.ReturnStationIndex < _stationEntities.Count)
        {
            // Place ship exactly on the station the player just exited
            shipStartPos = game.EcsWorld.Get<Transform>(_stationEntities[game.Player.ReturnStationIndex]).Position;
        }
        else if (returnCtx == PlayerData.ReturnContext.FromPlanet && game.Player.ReturnPlanetIndex >= 0
            && game.Player.ReturnPlanetIndex < _planetEntities.Count)
        {
            // Place ship exactly on the planet the player just launched from
            shipStartPos = game.EcsWorld.Get<Transform>(_planetEntities[game.Player.ReturnPlanetIndex]).Position;
        }
        else if (returnCtx == PlayerData.ReturnContext.FromMoon
            && game.Player.ReturnMoonPlanetIndex >= 0 && game.Player.ReturnMoonPlanetIndex < _moonEntities.Count
            && game.Player.ReturnMoonIndex >= 0 && game.Player.ReturnMoonIndex < _moonEntities[game.Player.ReturnMoonPlanetIndex].Count)
        {
            // Place ship exactly on the moon the player just launched from
            shipStartPos = game.EcsWorld.Get<Transform>(_moonEntities[game.Player.ReturnMoonPlanetIndex][game.Player.ReturnMoonIndex]).Position;
        }
        else
        {
            // Default: start near the star (close enough to see it on arrival)
            shipStartPos = center + new Vector2(-400, 0);
        }

        // Clear return context
        game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.Default;
        game.Player.ReturnStationIndex = -1;
        game.Player.ReturnPlanetIndex = -1;
        game.Player.ReturnMoonPlanetIndex = -1;
        game.Player.ReturnMoonIndex = -1;

        // Create player ship
        int shipSize = game.Player.CurrentShipType.SpriteSize;
        var playerStats = game.Player.GetCombinedStats();
        _playerShip = EntityFactory.CreatePlayerShip(game.EcsWorld, shipStartPos, shipSize,
            game.Player.ShipMaxHealth, game.Player.ShipHealth, playerStats.ShieldStrength, playerStats.MaxSpeed);
        ConfigureThrusterEmitter(game, _playerShip, shipSize, new Color3(130, 220, 255));

        // Background stars and nebulae — seeded by galaxy seed for consistency across visits to this system
        var bgRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xCAFEBABE);
        var nebRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xFACEFEED);
        
        float mapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float mapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;

        for (int i = 0; i < 4000; i++)
        {
            _bgStars.Add(new BackgroundStar(
                bgRng.NextFloat(-mapW * 0.5f, mapW * 1.5f),
                bgRng.NextFloat(-mapH * 0.5f, mapH * 1.5f),
                (byte)bgRng.NextInt(50, 150)
            ));
        }

        for (int i = 0; i < 32; i++)
        {
            byte[] choices = [(byte)nebRng.NextInt(20, 60), (byte)nebRng.NextInt(10, 40), (byte)nebRng.NextInt(30, 70)];
            int ci = nebRng.NextInt(0, 3);
            _bgNebulae.Add(new NebulaCloud(
                bgRng.NextFloat(-mapW * 0.5f, mapW * 1.5f),
                bgRng.NextFloat(-mapH * 0.5f, mapH * 1.5f),
                nebRng.NextFloat(1200, 5000),
                new Color3(ci == 0 ? choices[0] : (byte)10, ci == 1 ? choices[1] : (byte)10, ci == 2 ? choices[2] : (byte)15)));
        }

        // --- Spawn NPC ships (pirates, traders, patrols) based on danger level ---
        SpawnNPCShips(game, center, mapW, mapH);

        // Create textures for celestial bodies
        int starTexSize = (int)(_starSystem.StarRadius * 6);
        _starTexture = game.StarRenderer.CreateTexture(
            Math.Max(16, starTexSize),
            _starSystem.StarColor);

        _planetTextures.Clear();
        _moonTextures.Clear();
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            int texSize = Math.Max(8, (int)(p.Radius * 2) + 4);
            _planetTextures.Add(game.PlanetRenderer.CreateTexture(
                texSize, p.Color, (uint)(game.Seeds.GalaxySeed ^ (ulong)(i * 7919))));

            var moonTexList = new List<nint>();
            for (int m = 0; m < p.Moons.Count; m++)
            {
                var moon = p.Moons[m];
                int mTexSize = Math.Max(6, (int)(moon.Radius * 2) + 2);
                moonTexList.Add(game.PlanetRenderer.CreateTexture(
                    mTexSize, moon.Color, (uint)(game.Seeds.GalaxySeed ^ (ulong)(i * 1000 + m * 31))));
            }
            _moonTextures.Add(moonTexList);
        }

        // Initialize ECS systems
        float sysW = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float sysH = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        _orbitSystem = new OrbitSystem(
            game.EcsWorld,
            () => (float)game.GlobalTime,
            () => new Vector2(sysW, sysH));
        _orbitSystem.Initialize();

        _velocitySystem = new VelocitySystem(game.EcsWorld);
        _velocitySystem.Initialize();

        _cameraFollowSystem = new CameraFollowSystem(game.EcsWorld, _camera);
        _cameraFollowSystem.Initialize();

        _labelRenderer = new LabelRenderer(game.EcsWorld, game.SpriteRenderer, _camera);

        _proximitySystem = new InteractionProximitySystem(game.EcsWorld, InteractionRadius);
        _proximitySystem.Initialize();

        // Ship movement system
        _shipMovementSystem = new ShipMovementSystem(game.EcsWorld, game.Input, _playerShip);
        _shipMovementSystem.Initialize();

        // Combat systems
        _projectileSystem = new ProjectileSystem(game.EcsWorld);
        _projectileSystem.Initialize();
        _shieldRegenSystem = new ShieldRegenSystem(game.EcsWorld);
        _shieldRegenSystem.Initialize();

        float totalMapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float totalMapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;
        _enemyAISystem = new EnemyAISystem(game.EcsWorld,
            () => game.EcsWorld.IsAlive(_playerShip) ? game.EcsWorld.Get<Transform>(_playerShip).Position : center,
            () => !_playerDead && game.EcsWorld.IsAlive(_playerShip),
            totalMapW, totalMapH);
        _enemyAISystem.Initialize();

        _particleSystem = new ParticleSystem(game.EcsWorld);
        _particleSystem.Initialize();

        // Camera follows player
        _camera.Position = shipStartPos;
        _camera.Zoom = GameConfig.SolarSystemZoomDefault;
        _camera.ClampZoom();

        // Notify mission system that we entered this star system
        game.Player.NotifySystemEntered(_starSystem.Index);

        // Auto-open station overlay if we were asked to (e.g. returning from interior)
        if (_autoOpenStation != null)
        {
            // Anchor ship to the matching station entity
            int stIdx = _stations.FindIndex(s => s.Index == _autoOpenStation.Index);
            if (stIdx >= 0 && stIdx < _stationEntities.Count)
                SetAnchor(game, _stationEntities[stIdx]);

            _stationOverlay.Open(_starSystem, _autoOpenStation, game);
        }

        // Auto-open galaxy map overlay if requested (e.g. from main menu 'Galaxy Map')
        if (_autoOpenGalaxyMap)
        {
            _galaxyMapOverlay.Open(game);
        }

        // Auto-open planet landing overlay if requested (e.g. from main menu 'Planet Surface')
        if (_autoOpenPlanet != null)
        {
            // Anchor ship to the matching planet entity
            int pIdx = _planets.FindIndex(p => p.Name == _autoOpenPlanet.Name);
            if (pIdx >= 0 && pIdx < _planetEntities.Count)
                SetAnchor(game, _planetEntities[pIdx]);

            _planetLandingOverlay.Open(_starSystem, _autoOpenPlanet, game);
        }
    }

    public override void Exit(Game game)
    {
        // Destroy cached textures
        if (_starTexture != nint.Zero) { game.StarRenderer.DestroyTexture(_starTexture); _starTexture = nint.Zero; }
        foreach (var tex in _planetTextures) game.PlanetRenderer.DestroyTexture(tex);
        _planetTextures.Clear();
        foreach (var moonList in _moonTextures)
        {
            foreach (var tex in moonList) game.PlanetRenderer.DestroyTexture(tex);
        }
        _moonTextures.Clear();

        _planets.Clear();
        _asteroidBelts.Clear();
        _stations.Clear();
        _planetEntities.Clear();
        _stationEntities.Clear();
        _moonEntities.Clear();
        _asteroidEntities.Clear();
        _bgStars.Clear();
        _bgNebulae.Clear();
        _enemyEntities.Clear();
        _damagePopups.Clear();
        _explosions.Clear();
        _playerDead = false;

        _planetLandingOverlay.Cleanup();
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void UpdateInput(Game game)
    {
        var input = game.Input;

        // Overlays take priority over game input
        if (_planetLandingOverlay.UpdateInput(game))
            return;
        if (_galaxyMapOverlay.UpdateInput(game))
            return;
        if (_stationOverlay.UpdateInput(game))
            return;

        // In-game menu overlay
        if (_inGameMenuOverlay.UpdateInput(game))
            return;
        if (input.IsKeyPressed(SDL.Scancode.Escape))
        {
            _inGameMenuOverlay.Open(game);
            return;
        }

        // Interact
        if (input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_nearbyStationIndex >= 0)
            {
                SetAnchor(game, _stationEntities[_nearbyStationIndex]);
                _stationOverlay.Open(_starSystem, _stations[_nearbyStationIndex], game);
            }
            else if (_nearbyPlanetIndex >= 0)
            {
                SetAnchor(game, _planetEntities[_nearbyPlanetIndex]);
                _planetLandingOverlay.Open(_starSystem, _planets[_nearbyPlanetIndex], game);
            }
            else if (_nearbyMoonIndex >= 0)
            {
                SetAnchor(game, _moonEntities[_nearbyMoonPlanetIndex][_nearbyMoonIndex]);
                var moonData = _planets[_nearbyMoonPlanetIndex].Moons[_nearbyMoonIndex];
                _planetLandingOverlay.Open(_starSystem, moonData.ToPlanetData(_nearbyMoonPlanetIndex), game,
                    isMoon: true, moonPlanetIndex: _nearbyMoonPlanetIndex, moonIndex: _nearbyMoonIndex);
            }
        }

        // Open galaxy map overlay
        if (input.IsKeyPressed(SDL.Scancode.M))
        {
            _galaxyMapOverlay.Open(game);
        }

        // Camera zoom (handled per-frame so scroll events aren't missed)
        if (input.MouseWheelY != 0)
        {
            _camera.Zoom *= 1f + input.MouseWheelY * GameConfig.CameraZoomFactor;
            _camera.ClampZoom();
        }
    }

    public override void Update(Game game, float dt)
    {
        // In-game menu active — no simulation
        if (_inGameMenuOverlay.IsOpen)
        {
            _orbitSystem.Update(in dt);
            ApplyAnchor(game);
            return;
        }

        // Planet landing overlay takes priority
        if (_planetLandingOverlay.IsOpen)
        {
            _planetLandingOverlay.Update(game, dt);
            _orbitSystem.Update(in dt);
            ApplyAnchor(game);
            return;
        }

        // Galaxy map overlay takes priority
        if (_galaxyMapOverlay.IsOpen)
        {
            _galaxyMapOverlay.Update(game, dt);
            _orbitSystem.Update(in dt);
            return;
        }

        // Station overlay takes priority over all solar system input
        if (_stationOverlay.IsOpen)
        {
            _stationOverlay.Update(game, dt);

            // Still update orbits so the background stays alive
            _orbitSystem.Update(in dt);
            ApplyAnchor(game);
            _cameraFollowSystem.Update(in dt);
            return;
        }

        // Update emitter state and particles even while overlays are open (for smooth fade-out).
        _particleSystem.SetEmitterValidationBounds(_camera.GetVisibleBounds(), 0.2f);
        _particleSystem.Update(in dt);

        // Clear anchor when returning to normal gameplay
        ClearAnchor(game);

        // --- Handle player death / respawn ---
        if (_playerDead)
        {
            _respawnTimer -= dt;
            _orbitSystem.Update(in dt);
            // Still run enemy AI and projectiles during death animation
            _enemyAISystem.Update(in dt);
            _projectileSystem.Update(in dt);
            _shieldRegenSystem.Update(in dt);

            if (_respawnTimer <= 0)
            {
                HandlePlayerRespawn(game);
            }
            return;
        }

        // --- Player ship controls ---
        var shipStats = game.Player.GetCombinedStats();
        _shipMovementSystem.MaxSpeed = shipStats.MaxSpeed;
        _shipMovementSystem.RotationSpeed = shipStats.RotationSpeed;
        _shipMovementSystem.Acceleration = shipStats.Acceleration;
        if (game.EcsWorld.IsAlive(_playerShip) && game.EcsWorld.Has<Velocity>(_playerShip))
        {
            ref var playerVelocity = ref game.EcsWorld.Get<Velocity>(_playerShip);
            playerVelocity.MaxSpeed = shipStats.MaxSpeed;
            playerVelocity.MaxRotationSpeed = shipStats.RotationSpeed;
        }
        _shipMovementSystem.Update(in dt);

        // Apply velocity (speed clamping + position update via system)
        _velocitySystem.Update(in dt);

        // --- Update orbits using global time (deterministic) ---
        _orbitSystem.Update(in dt);

        // --- Camera follows player + handles zoom ---
        _cameraFollowSystem.Update(in dt);

        // --- Check proximity for interactions ---
        ref var shipTransformForProximity = ref game.EcsWorld.Get<Transform>(_playerShip);
        game.Player.ShipWorldPosition = shipTransformForProximity.Position;
        _proximitySystem.FindNearest(shipTransformForProximity.Position);
        _nearbyPlanetIndex = -1;
        _nearbyStationIndex = -1;
        _nearbyMoonPlanetIndex = -1;
        _nearbyMoonIndex = -1;

        if (_proximitySystem.HasNearest)
        {
            var nearBody = game.EcsWorld.Get<CelestialBody>(_proximitySystem.NearestEntity);
            switch (nearBody.Type)
            {
                case CelestialType.Planet:
                    _nearbyPlanetIndex = nearBody.DataIndex;
                    break;
                case CelestialType.Moon:
                    for (int pi = 0; pi < _moonEntities.Count; pi++)
                    {
                        int mi = _moonEntities[pi].IndexOf(_proximitySystem.NearestEntity);
                        if (mi >= 0) { _nearbyMoonPlanetIndex = pi; _nearbyMoonIndex = mi; break; }
                    }
                    break;
                case CelestialType.SpaceStation:
                    _nearbyStationIndex = _stationEntities.IndexOf(_proximitySystem.NearestEntity);
                    break;
            }
        }

        // --- Combat systems (includes asteroid mining via projectiles) ---
        UpdateCombat(game, dt);
    }

    /// <summary>Spawn NPC ships based on system danger level.</summary>
    private void SpawnNPCShips(Game game, Vector2 center, float mapW, float mapH)
    {
        _enemyEntities.Clear();
        var enemyRng = new SeededRandom(game.Seeds.GetStarSystemRandom(_starSystem.Index).DeriveChildSeed(5000));
        int dangerLevel = _starSystem.DangerLevel;

        // Determine spawn radius based on outermost planet orbit (+ margin)
        float maxOrbit = 0f;
        foreach (var planet in _planets)
            maxOrbit = MathF.Max(maxOrbit, planet.OrbitRadius);
        float spawnRadius = MathF.Max(maxOrbit + 4000f, 8000f); // at least 8000px, or outermost orbit + 4000

        // Scale enemy count and stats by danger level
        int pirateCount = GameConfig.MinEnemiesPerSystem + (int)((GameConfig.MaxEnemiesPerSystem - GameConfig.MinEnemiesPerSystem) * (dangerLevel - 1f) / 4f);
        int traderCount = enemyRng.NextInt(GameConfig.MinTradersPerSystem, GameConfig.MaxTradersPerSystem + 1);
        int patrolCount = enemyRng.NextInt(GameConfig.MinPatrolsPerSystem, GameConfig.MaxPatrolsPerSystem + 1);
        int qualityTier = NpcShipLoadoutHelper.GetNpcQualityTier(dangerLevel);

        // Spawn pirates
        for (int i = 0; i < pirateCount; i++)
        {
            var pos = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 250f);

            var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(Faction.Pirate, dangerLevel, enemyRng);
            var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, Faction.Pirate, qualityTier, enemyRng);
            var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
            var weapons = NpcShipLoadoutHelper.BuildWeaponSpecs(loadout);
            int lootCredits = NpcShipLoadoutHelper.ComputeNpcLootCredits(shipType, loadout);

            var entity = EntityFactory.CreatePirateShip(game.EcsWorld, pos,
                enemyRng.NextFloat(0, 360), stats, dangerLevel, lootCredits, enemyRng.NextFloat(0, 2f), weapons);
            _enemyEntities.Add(entity);
            ConfigureThrusterEmitter(game, entity, stats.SpriteSize, new Color3(255, 130, 110));
        }

        // Spawn traders
        for (int i = 0; i < traderCount; i++)
        {
            var pos = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 300f);

            var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(Faction.Trader, dangerLevel, enemyRng);
            var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, Faction.Trader, qualityTier, enemyRng);
            var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
            var weapons = NpcShipLoadoutHelper.BuildWeaponSpecs(loadout);

            var entity = EntityFactory.CreateTraderShip(game.EcsWorld, pos,
                enemyRng.NextFloat(0, 360), stats, weapons);
            _enemyEntities.Add(entity);
            ConfigureThrusterEmitter(game, entity, stats.SpriteSize, new Color3(255, 210, 120));
        }

        // Spawn patrols
        for (int i = 0; i < patrolCount; i++)
        {
            var pos = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 300f);

            var shipType = NpcShipLoadoutHelper.ChooseNpcShipType(Faction.Patrol, dangerLevel, enemyRng);
            var loadout = NpcShipLoadoutHelper.BuildNpcLoadout(shipType, Faction.Patrol, qualityTier, enemyRng);
            var stats = NpcShipLoadoutHelper.BuildNpcShipStats(shipType, loadout);
            var weapons = NpcShipLoadoutHelper.BuildWeaponSpecs(loadout);

            var entity = EntityFactory.CreatePatrolShip(game.EcsWorld, pos,
                enemyRng.NextFloat(0, 360), stats, weapons);
            _enemyEntities.Add(entity);
            ConfigureThrusterEmitter(game, entity, stats.SpriteSize, new Color3(130, 200, 255));
        }
    }

    /// <summary>Pick a random position within the orbit zone, avoiding the star.</summary>
    private static Vector2 SpawnPositionInOrbitZone(SeededRandom rng, Vector2 center, float maxRadius, float minRadius)
    {
        // Random angle + distance between minRadius and maxRadius from center
        float angle = rng.NextFloat(0, MathF.PI * 2f);
        float dist = rng.NextFloat(minRadius, maxRadius);
        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
    }

    private static bool TryGetWeaponSpec(Dictionary<ShipSlotType, ShipPart> equipped,
        ShipSlotType slot, out ShipWeaponSpec weapon)
    {
        weapon = default;
        if (!equipped.TryGetValue(slot, out var part)) return false;

        var stats = part.Stats;
        if (stats.WeaponDamage <= 0f || stats.WeaponFireRate <= 0f ||
            stats.WeaponRange <= 0f || stats.ProjectileSpeed <= 0f)
            return false;

        weapon = new ShipWeaponSpec(
            stats.WeaponDamage,
            stats.WeaponFireRate,
            stats.WeaponRange,
            stats.ProjectileSpeed);
        return true;
    }

    /// <summary>Update combat: player shooting, AI, projectiles, asteroid mining, damage, death.</summary>
    private void UpdateCombat(Game game, float dt)
    {
        var input = game.Input;

        // Tick mining timers
        if (_miningHudTimer > 0) _miningHudTimer -= dt;
        if (_miningMessageTimer > 0)
        {
            _miningMessageTimer -= dt;
            if (_miningMessageTimer <= 0) _miningMessage = null;
        }

        // Sync player health from Health component → PlayerData
        if (game.EcsWorld.IsAlive(_playerShip) && game.EcsWorld.Has<Health>(_playerShip))
        {
            ref var playerHealth = ref game.EcsWorld.Get<Health>(_playerShip);
            game.Player.ShipHealth = playerHealth.Hull;
        }

        // Player shooting (Space key)
        for (int i = 0; i < _playerWeaponCooldowns.Length; i++)
            _playerWeaponCooldowns[i] -= dt;

        if (input.IsKeyDown(SDL.Scancode.Space))
        {
            var equipped = game.Player.EquippedParts;
            bool hasWeapon1 = TryGetWeaponSpec(equipped, ShipSlotType.Weapon1, out var weapon1);
            bool hasWeapon2 = TryGetWeaponSpec(equipped, ShipSlotType.Weapon2, out var weapon2);
            if (!hasWeapon1) _playerWeaponCooldowns[0] = 0f;
            if (!hasWeapon2) _playerWeaponCooldowns[1] = 0f;

            int activeWeapons = (hasWeapon1 ? 1 : 0) + (hasWeapon2 ? 1 : 0);
            if (activeWeapons > 0)
            {
                ref var shipT = ref game.EcsWorld.Get<Transform>(_playerShip);
                float rad = shipT.Rotation * MathF.PI / 180f;
                var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
                var forwardPos = shipT.Position + dir * 20f;
                var lateralDir = new Vector2(-dir.Y, dir.X);
                float lateralOffset = activeWeapons > 1 ? 6f : 0f;
                bool firedAny = false;

                if (hasWeapon1 && _playerWeaponCooldowns[0] <= 0f)
                {
                    float lifetime = CombatHelper.ResolveProjectileLifetime(weapon1.Range, weapon1.ProjectileSpeed);
                    var spawnPos = forwardPos + lateralDir * -lateralOffset;
                    EntityFactory.CreateProjectile(game.EcsWorld, spawnPos, dir,
                        weapon1.Damage, weapon1.ProjectileSpeed, Faction.Player, new Color3(100, 255, 100), lifetime);
                    _playerWeaponCooldowns[0] = weapon1.FireRate;
                    firedAny = true;
                }

                if (hasWeapon2 && _playerWeaponCooldowns[1] <= 0f)
                {
                    float lifetime = CombatHelper.ResolveProjectileLifetime(weapon2.Range, weapon2.ProjectileSpeed);
                    var spawnPos = forwardPos + lateralDir * lateralOffset;
                    EntityFactory.CreateProjectile(game.EcsWorld, spawnPos, dir,
                        weapon2.Damage, weapon2.ProjectileSpeed, Faction.Player, new Color3(100, 255, 100), lifetime);
                    _playerWeaponCooldowns[1] = weapon2.FireRate;
                    firedAny = true;
                }

                if (firedAny)
                    game.Audio.PlaySfx(SfxType.LaserFire, 0.5f);
            }
        }

        // Run AI system
        _enemyAISystem.Update(in dt);

        // SFX for NPC weapon fire (distance-attenuated)
        var playerPos = game.EcsWorld.IsAlive(_playerShip)
            ? game.EcsWorld.Get<Transform>(_playerShip).Position
            : _camera.Position;
        foreach (var spawn in _enemyAISystem.ProjectilesSpawnedLastUpdate)
            game.Audio.PlaySfxAtDistance(SfxType.EnemyLaser, spawn.Pos, playerPos, 0.5f);

        // --- Asteroid-projectile collision is now handled by ProjectileSystem (asteroids have Health) ---

        // Run projectile system (collision detection with ships + asteroids)
        _projectileSystem.Update(in dt);

        // Run shield regen
        _shieldRegenSystem.Update(in dt);

        // Process damage events (visual effects + mining HUD tracking)
        CombatHelper.CreateDamagePopups(_damagePopups, _projectileSystem.DamageEventsLastUpdate);
        foreach (var evt in _projectileSystem.DamageEventsLastUpdate)
        {
            // SFX for damage hits — volume attenuated by distance to the player
            game.Audio.PlaySfxAtDistance(
                evt.ShieldHit ? SfxType.ShieldHit : SfxType.HullDamage,
                evt.Position, playerPos, 0.6f);

            // Only trigger combat music when the player is directly involved
            bool playerInvolved = evt.OwnerFaction == Faction.Player
                || (game.EcsWorld.IsAlive(evt.Target) && game.EcsWorld.Has<PlayerControlled>(evt.Target));
            if (playerInvolved)
                _combatMusicTimer = GameConfig.CombatMusicDelay;

            // Track last asteroid hit for mining HUD
            if (game.EcsWorld.IsAlive(evt.Target) && game.EcsWorld.Has<AsteroidField>(evt.Target))
            {
                _lastHitAsteroid = evt.Target;
                _miningHudTimer = 2f;
            }
        }

        // Process destroyed entities
        var combatRng = new SeededRandom((ulong)(game.GlobalTime * 1000) ^ 0xDEADBEEF);
        foreach (var destroyed in _projectileSystem.DestroyedLastUpdate)
        {
            if (destroyed.Asteroid.HasValue)
            {
                // Asteroid destroyed — collect resources only if player mined it
                var asteroid = destroyed.Asteroid.Value;
                _explosions.Add(new Explosion(destroyed.Position, 15f, new Color3(140, 120, 100), 0.5f));
                game.Audio.PlaySfxAtDistance(SfxType.SmallExplosion, destroyed.Position, playerPos, 0.5f);

                if (destroyed.KillerFaction == Faction.Player)
                {
                    int added = game.Player.AddCargo(asteroid.Resource, asteroid.ResourceAmount);
                    var resInfo = ResourceCatalog.Get(asteroid.Resource);
                    if (added > 0)
                    {
                        _miningMessage = $"+{added} {resInfo.Name.ToUpper()}";
                        _miningMessageTimer = 2.5f;

                        // Track resource mining for missions
                        game.Player.NotifyResourceMined(asteroid.Resource, added);
                    }
                    else
                    {
                        _miningMessage = "CARGO FULL!";
                        _miningMessageTimer = 2.5f;
                    }
                }

                // Clear mining HUD since asteroid is gone
                if (_lastHitAsteroid == destroyed.Entity) _miningHudTimer = 0;

                if (game.EcsWorld.IsAlive(destroyed.Entity))
                {
                    _asteroidEntities.Remove(destroyed.Entity);
                    game.EcsWorld.Destroy(destroyed.Entity);
                }
            }
            else if (destroyed.Faction == Faction.Player)
            {
                // Player died
                HandlePlayerDeath(game, destroyed.Position);
            }
            else
            {
                // Enemy died — create explosion and drop loot only if player killed it
                byte expR = destroyed.Faction == Faction.Pirate ? (byte)255 : (byte)200;
                byte expG = destroyed.Faction == Faction.Pirate ? (byte)120 : (byte)200;
                byte expB = destroyed.Faction == Faction.Pirate ? (byte)80 : (byte)200;
                _explosions.Add(new Explosion(destroyed.Position, 30f, new Color3(expR, expG, expB)));
                game.Audio.PlaySfxAtDistance(SfxType.Explosion, destroyed.Position, playerPos);

                if (destroyed.KillerFaction == Faction.Player && destroyed.Loot.HasValue)
                {
                    _combatMessage = CombatHelper.ProcessLootDrop(game, destroyed.Loot.Value, combatRng,
                        resourceAmountMax: 5 + destroyed.Loot.Value.DangerLevel * 2, enablePartDrops: true);
                    _combatMessageTimer = 3f;
                }

                // Track pirate kills for bounty missions
                if (destroyed.KillerFaction == Faction.Player && destroyed.Faction == Faction.Pirate)
                {
                    game.Player.NotifyPirateKilled();
                }

                // Destroy the entity
                if (game.EcsWorld.IsAlive(destroyed.Entity))
                {
                    _enemyEntities.Remove(destroyed.Entity);
                    game.EcsWorld.Destroy(destroyed.Entity);
                }
            }
        }

        // Combat message timer
        CombatHelper.UpdateCombatMessageTimer(ref _combatMessage, ref _combatMessageTimer, dt);

        // Update visual effects (timers, positions, removal)
        CombatHelper.UpdateVisualEffects(_damagePopups, _explosions, dt);

        // Combat music tracking
        if (_combatMusicTimer > 0)
        {
            _combatMusicTimer -= dt;
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

    /// <summary>Handle player death — apply penalties and start respawn timer.</summary>
    private void HandlePlayerDeath(Game game, Vector2 deathPos)
    {
        _playerDead = true;
        _respawnTimer = RespawnDelay;
        _explosions.Add(new Explosion(deathPos, 50f, new Color3(255, 200, 80), 1.5f));
        game.Audio.PlaySfx(SfxType.Explosion, 1.2f);

        // Destroy the old player ship entity so CameraFollowSystem doesn't track it
        if (game.EcsWorld.IsAlive(_playerShip))
            game.EcsWorld.Destroy(_playerShip);

        // Apply death penalties
        int creditsLost = (int)(game.Player.Credits * GameConfig.DeathCreditsLossPercent);
        game.Player.Credits -= creditsLost;

        // Lose some cargo
        var cargoKeys = game.Player.Cargo.Keys.ToList();
        foreach (var key in cargoKeys)
        {
            int loss = (int)(game.Player.Cargo[key] * GameConfig.DeathCargoLossPercent);
            game.Player.Cargo[key] -= loss;
            if (game.Player.Cargo[key] <= 0) game.Player.Cargo.Remove(key);
        }

        _combatMessage = $"DESTROYED! -{creditsLost} CREDITS";
        _combatMessageTimer = RespawnDelay;
    }

    /// <summary>Respawn the player at the nearest station with the station menu open.</summary>
    private void HandlePlayerRespawn(Game game)
    {
        _playerDead = false;

        // Determine respawn position (nearest station, or system center)
        float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 respawnPos = new(centerX + 400, centerY);

        int nearestStationIdx = -1;

        if (_stationEntities.Count > 0)
        {
            // Find nearest station from last known player position
            float bestDist = float.MaxValue;
            for (int i = 0; i < _stationEntities.Count; i++)
            {
                var stEntity = _stationEntities[i];
                if (!game.EcsWorld.IsAlive(stEntity)) continue;
                var stPos = game.EcsWorld.Get<Transform>(stEntity).Position;
                float dist = Vector2.Distance(stPos, respawnPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    respawnPos = stPos + new Vector2(50, 0);
                    nearestStationIdx = i;
                }
            }
        }

        // Restore hull to 50%
        game.Player.ShipHealth = game.Player.ShipMaxHealth * GameConfig.DeathHullPercent;

        // Recreate player ship entity
        int shipSize = game.Player.CurrentShipType.SpriteSize;
        var playerStats = game.Player.GetCombinedStats();
        _playerShip = EntityFactory.CreatePlayerShip(game.EcsWorld, respawnPos, shipSize,
            game.Player.ShipMaxHealth, game.Player.ShipHealth, playerStats.ShieldStrength, playerStats.MaxSpeed);
        ConfigureThrusterEmitter(game, _playerShip, shipSize, new Color3(130, 220, 255));

        // Recreate movement system with new entity
        _shipMovementSystem = new ShipMovementSystem(game.EcsWorld, game.Input, _playerShip);

        _camera.Position = respawnPos;
        _combatMessage = "RESPAWNED — HULL AT 50%";
        _combatMessageTimer = 3f;

        // Dock at the nearest station and open the station menu
        if (nearestStationIdx >= 0 && nearestStationIdx < _stations.Count)
        {
            SetAnchor(game, _stationEntities[nearestStationIdx]);
            _stationOverlay.Open(_starSystem, _stations[nearestStationIdx], game);
        }
    }

    /// <summary>Record the entity the ship should follow and the offset from it.</summary>
    private void SetAnchor(Game game, Entity target)
    {
        _anchorEntity = target;
        var targetPos = game.EcsWorld.Get<Transform>(target).Position;
        var shipPos = game.EcsWorld.Get<Transform>(_playerShip).Position;
        _anchorOffset = shipPos - targetPos;

        // Zero the ship velocity so it doesn't drift when the overlay closes
        ref var vel = ref game.EcsWorld.Get<Velocity>(_playerShip);
        vel.Velocity = Vector2.Zero;
        vel.Acceleration = Vector2.Zero;
        vel.RotationVelocity = 0f;
    }

    /// <summary>Move the ship to keep its offset from the anchor entity (call after OrbitSystem).</summary>
    private void ApplyAnchor(Game game)
    {
        if (_anchorEntity == default || !game.EcsWorld.IsAlive(_anchorEntity))
            return;

        var targetPos = game.EcsWorld.Get<Transform>(_anchorEntity).Position;
        ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
        shipTransform.Position = targetPos + _anchorOffset;
    }

    /// <summary>Clear the anchor so the ship returns to normal movement.</summary>
    private void ClearAnchor(Game game)
    {
        if (_anchorEntity != default && game.EcsWorld.IsAlive(_anchorEntity))
        {
            // Snap one last time before releasing
            ApplyAnchor(game);
        }
        _anchorEntity = default;
        _anchorOffset = Vector2.Zero;
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = _camera;

        float starCenterX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float starCenterY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 starCenter = new(starCenterX, starCenterY);

        // Background stars and nebulae
        SolarSystemRenderer.RenderBackgroundStars(renderer, camera, _bgStars);
        SolarSystemRenderer.RenderBackgroundNebulae(renderer, camera, _bgNebulae);

        // Orbit lines
        SolarSystemRenderer.RenderOrbitLines(renderer, camera, _planets, starCenter);

        // Asteroids
        game.AsteroidRenderer.RenderAsteroids(renderer, camera, game.EcsWorld, _asteroidEntities);

        // Star
        float starDisplayRadius = _starSystem.StarRadius * 2f;
        game.StarRenderer.Render(renderer, camera, _starTexture, starCenter, starDisplayRadius);

        // Planets and moons
        game.PlanetRenderer.RenderPlanetsAndMoons(renderer, camera, game.EcsWorld,
            _planets, _planetEntities, _moonEntities, _planetTextures, _moonTextures);

        // Stations
        game.StationRenderer.RenderStations(renderer, camera, game.EcsWorld,
            _stationEntities, game.GlobalTime);

        // Labels
        _labelRenderer.Render();

        // Mission target markers (pulsing indicators on target planets/stations)
        HudRenderer.RenderSolarSystemMissionMarkers(renderer, camera,
            game.Player, (float)game.GlobalTime, _starSystem.Index,
            _stationEntities, _planetEntities, _planets, game.EcsWorld);

        // Thruster particles (draw before ships so ships appear on top)
        ParticleRenderer.RenderParticles(renderer, camera, game.EcsWorld);

        // NPC ships (enemies, traders, patrols)
        SolarSystemRenderer.RenderNPCShips(renderer, camera, game.EcsWorld,
            _enemyEntities, game.EnemyShipRenderer);

        // Projectiles
        ProjectileRenderer.RenderProjectiles(renderer, camera, game.EcsWorld);

        // Player ship (only when alive)
        if (!_playerDead && game.EcsWorld.IsAlive(_playerShip))
        {
            ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
            int shipSpriteSize = game.Player.CurrentShipType.SpriteSize;
            bool isThrusting = game.Input.IsKeyDown(SDL.Scancode.W) || game.Input.IsKeyDown(SDL.Scancode.Up);
            game.SpaceshipRenderer.RenderFlying(renderer, camera, shipTransform.Position,
                shipTransform.Rotation, game.Player.CurrentShipType.Id, shipSpriteSize, isThrusting);
        }

        // Visual effects (damage popups, explosions)
        ProjectileRenderer.RenderDamageEffects(renderer, camera, _damagePopups);
        ProjectileRenderer.RenderExplosions(renderer, camera, _explosions);

        // Unified HUD (top-left: location, player info, hull/shields)
        {
            float speed = 0f;
            if (!_playerDead && game.EcsWorld.IsAlive(_playerShip))
            {
                ref var vel = ref game.EcsWorld.Get<Velocity>(_playerShip);
                speed = vel.Velocity.Length();
            }
            HudRenderer.RenderSolarSystemHud(renderer, game.Player, _starSystem,
                game.EcsWorld, _playerShip, speed);
        }

        // Minimap (top-right)
        HudMinimapRenderer.RenderSolarSystemMinimap(renderer, _planets, _planetEntities,
            _moonEntities, _stationEntities, _asteroidEntities, _enemyEntities,
            _playerShip, _starEntity, game.EcsWorld, _starSystem.StarRadius);

        // Off-screen indicators at screen borders
        if (!_playerDead)
        {
            HudRenderer.RenderOffscreenIndicators(renderer, camera, game.EcsWorld,
                _enemyEntities, _playerShip, 2500f);
            if (!(game.Player.HasNavigationTarget && game.Player.NavTargetType == NavigationTargetType.Star))
                HudRenderer.RenderStarOffscreenIndicator(renderer, camera, starCenter);
            HudRenderer.RenderSolarSystemObjectOffscreenIndicators(renderer, camera,
                _playerShip, game.EcsWorld, _planetEntities, _planets,
                _stationEntities, _stations, 5000f, game.Player);
            HudRenderer.RenderSolarSystemMissionOffscreenIndicators(renderer, camera,
                game.Player, _starSystem.Index, _stationEntities, _planetEntities,
                _planets, game.EcsWorld);

            // Navigation target indicator
            if (game.Player.HasNavigationTarget)
            {
                Vector2? targetPos = ResolveNavTargetPosition(game);
                if (targetPos.HasValue)
                    HudRenderer.RenderNavTargetOffscreenIndicator(renderer, camera,
                        targetPos.Value, game.Player.NavTargetName, game.Player.NavTargetColor);
            }
        }

        // Death screen
        if (_playerDead)
        {
            HudRenderer.RenderDeathScreen(renderer, _respawnTimer);
        }

        // Mining target info panel (shown for 2s after a projectile hit)
        if (_miningHudTimer > 0 && game.EcsWorld.IsAlive(_lastHitAsteroid)
            && game.EcsWorld.Has<AsteroidField>(_lastHitAsteroid))
        {
            ref var asteroidField = ref game.EcsWorld.Get<AsteroidField>(_lastHitAsteroid);
            ref var asteroidHealth = ref game.EcsWorld.Get<Health>(_lastHitAsteroid);
            HudRenderer.RenderMiningPanel(renderer, asteroidField.Resource,
                asteroidHealth.Hull, asteroidHealth.MaxHull, asteroidField.ResourceAmount);
        }

        // Mining feedback message
        if (_miningMessage != null)
            HudRenderer.RenderCenteredMessage(renderer, _miningMessage, -40, new Color3(255, 220, 80), 2.5f);

        // Combat feedback message
        if (_combatMessage != null)
            HudRenderer.RenderCenteredMessage(renderer, _combatMessage, 30, new Color3(255, 200, 80), 2f);

        // Interaction prompts
        HudRenderer.RenderSolarSystemPrompt(renderer,
            _nearbyPlanetIndex, _nearbyMoonIndex, _nearbyMoonPlanetIndex,
            _nearbyStationIndex, _planets, _stations);



        // Overlays drawn on top of everything
        _stationOverlay.Render(game);
        _planetLandingOverlay.Render(game);
        _galaxyMapOverlay.Render(game);
        _inGameMenuOverlay.Render(game);
    }

    /// <summary>Resolve the world position of the current navigation target.</summary>
    private Vector2? ResolveNavTargetPosition(Game game)
    {
        var player = game.Player;
        switch (player.NavTargetType)
        {
            case NavigationTargetType.Star:
                if (game.EcsWorld.IsAlive(_starEntity))
                    return game.EcsWorld.Get<Transform>(_starEntity).Position;
                break;

            case NavigationTargetType.Planet:
                if (player.NavTargetPlanetIndex >= 0 && player.NavTargetPlanetIndex < _planetEntities.Count
                    && game.EcsWorld.IsAlive(_planetEntities[player.NavTargetPlanetIndex]))
                    return game.EcsWorld.Get<Transform>(_planetEntities[player.NavTargetPlanetIndex]).Position;
                break;

            case NavigationTargetType.Moon:
                if (player.NavTargetPlanetIndex >= 0 && player.NavTargetPlanetIndex < _moonEntities.Count
                    && player.NavTargetMoonIndex >= 0 && player.NavTargetMoonIndex < _moonEntities[player.NavTargetPlanetIndex].Count
                    && game.EcsWorld.IsAlive(_moonEntities[player.NavTargetPlanetIndex][player.NavTargetMoonIndex]))
                    return game.EcsWorld.Get<Transform>(_moonEntities[player.NavTargetPlanetIndex][player.NavTargetMoonIndex]).Position;
                break;

            case NavigationTargetType.Station:
                if (player.NavTargetStationIndex >= 0 && player.NavTargetStationIndex < _stationEntities.Count
                    && game.EcsWorld.IsAlive(_stationEntities[player.NavTargetStationIndex]))
                    return game.EcsWorld.Get<Transform>(_stationEntities[player.NavTargetStationIndex]).Position;
                break;
        }
        return null;
    }

    private void ConfigureThrusterEmitter(Game game, Entity entity, int shipSize, Color3 color)
    {
        if (!game.EcsWorld.IsAlive(entity)) return;

        var emitter = new ParticleEmitter
        {
            EmitCondition = EmitCondition.WhenAccelerating,
            SpawnInterval = ThrusterSpawnIntervalSeconds,
            SpawnAccumulator = 0f,
            SternOffset = shipSize * 0.56f,
            EjectSpeedMin = 115f,
            EjectSpeedMax = 185f,
            LateralDrift = 25f,
            ParticleLifeMin = 0.65f,
            ParticleLifeMax = 0.95f,
            ParticleSizeMin = 1.4f,
            ParticleSizeMax = 2.8f,
            ParticleDrag = 1.4f,
            ParticleColor = color
        };

        if (game.EcsWorld.Has<ParticleEmitter>(entity))
            game.EcsWorld.Set(entity, emitter);
        else
            game.EcsWorld.Add(entity, emitter);
    }

}
