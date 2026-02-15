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
using SpaceExplorationGame.UI.Overlays.Customization;

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
    private List<(float X, float Y, byte Brightness)> _bgStars = [];

    // Station overlay (docked at station)
    private readonly SpaceStationOverlay _stationOverlay = new();
    private readonly SpaceStationData? _autoOpenStation;

    // Galaxy map overlay
    private readonly GalaxyMapOverlay _galaxyMapOverlay = new();
    private readonly bool _autoOpenGalaxyMap;

    // Planet landing overlay
    private readonly PlanetLandingOverlay _planetLandingOverlay = new();
    private readonly PlanetData? _autoOpenPlanet;

    // In-game menu overlay
    private readonly InGameMenuOverlay _inGameMenuOverlay = new();

    // Anchor: keeps the ship at a fixed offset from a target while overlays are open
    private Entity _anchorEntity;
    private Vector2 _anchorOffset;

    // ECS Systems
    private OrbitSystem _orbitSystem = null!;
    private VelocitySystem _velocitySystem = null!;
    private CameraFollowSystem _cameraFollowSystem = null!;
    private LabelRenderSystem _labelRenderSystem = null!;
    private InteractionProximitySystem _proximitySystem = null!;

    // Combat systems
    private ProjectileSystem _projectileSystem = null!;
    private ShieldRegenSystem _shieldRegenSystem = null!;
    private EnemyAISystem _enemyAISystem = null!;

    // Combat state
    private List<Entity> _enemyEntities = [];
    private float _playerFireCooldown;
    private bool _playerDead;
    private float _respawnTimer;
    private const float RespawnDelay = 3f;
    private string? _combatMessage;
    private float _combatMessageTimer;

    // Visual effects
    private readonly List<DamagePopup> _damagePopups = [];
    private readonly List<Explosion> _explosions = [];

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
            _starSystem.Name, _starSystem.StarR, _starSystem.StarG, _starSystem.StarB, _starSystem.Index);

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
                planet.Name, planet.Radius, planet.R, planet.G, planet.B,
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
                    moon.Name, moon.Radius, moon.R, moon.G, moon.B,
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
                float size = asteroidRng.NextFloat(4, 10);
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
            // Default: start near the star
            shipStartPos = center + new Vector2(400, 0);
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
        float playerMaxShield = playerStats.ShieldStrength;
        _playerShip = EntityFactory.CreatePlayerShip(game.EcsWorld, shipStartPos, shipSize,
            game.Player.ShipMaxHealth, game.Player.ShipHealth, playerMaxShield, GameConfig.ShipMaxSpeed);

        // Background stars
        var bgRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xCAFEBABE);
        float mapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float mapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;

        // --- Spawn NPC ships (pirates, traders, patrols) based on danger level ---
        SpawnNPCShips(game, center, mapW, mapH);
        for (int i = 0; i < 800; i++)
        {
            _bgStars.Add((
                bgRng.NextFloat(-mapW * 0.5f, mapW * 1.5f),
                bgRng.NextFloat(-mapH * 0.5f, mapH * 1.5f),
                (byte)bgRng.NextInt(20, 100)
            ));
        }

        // Create textures for celestial bodies
        int starTexSize = (int)(_starSystem.StarRadius * 6);
        _starTexture = game.StarRenderer.CreateTexture(
            Math.Max(16, starTexSize),
            _starSystem.StarR, _starSystem.StarG, _starSystem.StarB);

        _planetTextures.Clear();
        _moonTextures.Clear();
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            int texSize = Math.Max(8, (int)(p.Radius * 2) + 4);
            _planetTextures.Add(game.PlanetRenderer.CreateTexture(
                texSize, p.R, p.G, p.B, (uint)(game.Seeds.GalaxySeed ^ (ulong)(i * 7919))));

            var moonTexList = new List<nint>();
            for (int m = 0; m < p.Moons.Count; m++)
            {
                var moon = p.Moons[m];
                int mTexSize = Math.Max(6, (int)(moon.Radius * 2) + 2);
                moonTexList.Add(game.PlanetRenderer.CreateTexture(
                    mTexSize, moon.R, moon.G, moon.B, (uint)(game.Seeds.GalaxySeed ^ (ulong)(i * 1000 + m * 31))));
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

        _cameraFollowSystem = new CameraFollowSystem(game.EcsWorld, game.Camera);
        _cameraFollowSystem.Initialize();

        _labelRenderSystem = new LabelRenderSystem(game.EcsWorld, game.SpriteRenderer, game.Camera);
        _labelRenderSystem.Initialize();

        _proximitySystem = new InteractionProximitySystem(game.EcsWorld, InteractionRadius);

        // Combat systems
        _projectileSystem = new ProjectileSystem(game.EcsWorld);
        _shieldRegenSystem = new ShieldRegenSystem(game.EcsWorld);
        _shieldRegenSystem.Initialize();

        float totalMapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float totalMapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;
        _enemyAISystem = new EnemyAISystem(game.EcsWorld,
            () => game.EcsWorld.IsAlive(_playerShip) ? game.EcsWorld.Get<Transform>(_playerShip).Position : center,
            () => !_playerDead && game.EcsWorld.IsAlive(_playerShip),
            totalMapW, totalMapH);

        // Camera follows player
        game.Camera.Position = shipStartPos;
        game.Camera.Zoom = 1f;

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

        // In-game menu overlay (handles Escape toggle + menu navigation)
        if (_inGameMenuOverlay.UpdateInput(game))
            return;

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
            game.Camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            game.Camera.ClampZoom();
        }
    }

    public override void Update(Game game, float dt)
    {
        var input = game.Input;

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

        // Clear anchor when returning to normal gameplay
        ClearAnchor(game);

        // --- Handle player death / respawn ---
        if (_playerDead)
        {
            _respawnTimer -= dt;
            _orbitSystem.Update(in dt);
            // Still run enemy AI and projectiles during death animation
            _enemyAISystem.Update(dt);
            _projectileSystem.Update(dt);
            _shieldRegenSystem.Update(in dt);

            if (_respawnTimer <= 0)
            {
                HandlePlayerRespawn(game);
            }
            return;
        }

        // --- Player ship controls ---
        ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
        ref var shipVelocity = ref game.EcsWorld.Get<Velocity>(_playerShip);
        var shipStats = game.Player.GetCombinedStats();

        // Update max speed from parts
        shipVelocity.MaxSpeed = shipStats.MaxSpeed > 0 ? shipStats.MaxSpeed : GameConfig.ShipMaxSpeed;

        // Rotation
        float rotSpeed = shipStats.RotationSpeed > 0 ? shipStats.RotationSpeed : GameConfig.ShipRotationSpeed;
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            shipTransform.Rotation -= rotSpeed * dt;
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            shipTransform.Rotation += rotSpeed * dt;

        // Thrust
        float accel = shipStats.Acceleration > 0 ? shipStats.Acceleration : GameConfig.ShipAcceleration;
        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
        {
            float rad = shipTransform.Rotation * MathF.PI / 180f;
            var thrust = new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * accel * dt;
            shipVelocity.Value += thrust;
        }

        // Brake
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
        {
            shipVelocity.Value *= 0.95f;
        }

        // Apply friction
        shipVelocity.Value *= GameConfig.ShipFriction;

        // Apply velocity (speed clamping + position update via system)
        _velocitySystem.Update(in dt);

        // --- Update orbits using global time (deterministic) ---
        _orbitSystem.Update(in dt);

        // --- Camera follows player + handles zoom ---
        _cameraFollowSystem.Update(in dt);

        // --- Check proximity for interactions ---
        ref var shipTransformForProximity = ref game.EcsWorld.Get<Transform>(_playerShip);
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
        float spawnRadius = MathF.Max(maxOrbit + 400f, 800f); // at least 800px, or outermost orbit + 400

        // Scale enemy count and stats by danger level
        int pirateCount = GameConfig.MinEnemiesPerSystem + (int)((GameConfig.MaxEnemiesPerSystem - GameConfig.MinEnemiesPerSystem) * (dangerLevel - 1f) / 4f);
        int traderCount = enemyRng.NextInt(GameConfig.MinTradersPerSystem, GameConfig.MaxTradersPerSystem + 1);
        int patrolCount = enemyRng.NextInt(GameConfig.MinPatrolsPerSystem, GameConfig.MaxPatrolsPerSystem + 1);

        float hullMultiplier = 1f + (dangerLevel - 1) * 0.4f;
        float damageMultiplier = 1f + (dangerLevel - 1) * 0.3f;
        int creditMultiplier = dangerLevel;

        // Spawn pirates
        for (int i = 0; i < pirateCount; i++)
        {
            var pos = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 250f);

            var entity = EntityFactory.CreatePirateShip(game.EcsWorld, pos,
                enemyRng.NextFloat(0, 360), dangerLevel, hullMultiplier, damageMultiplier,
                creditMultiplier, enemyRng.NextFloat(0, 2f));
            _enemyEntities.Add(entity);
        }

        // Spawn traders
        for (int i = 0; i < traderCount; i++)
        {
            var pos = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 300f);

            var entity = EntityFactory.CreateTraderShip(game.EcsWorld, pos,
                enemyRng.NextFloat(0, 360));
            _enemyEntities.Add(entity);
        }

        // Spawn patrols
        for (int i = 0; i < patrolCount; i++)
        {
            var pos = SpawnPositionInOrbitZone(enemyRng, center, spawnRadius, 300f);

            var entity = EntityFactory.CreatePatrolShip(game.EcsWorld, pos,
                enemyRng.NextFloat(0, 360));
            _enemyEntities.Add(entity);
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
        _playerFireCooldown -= dt;
        if (input.IsKeyDown(SDL.Scancode.Space) && _playerFireCooldown <= 0)
        {
            var stats = game.Player.GetCombinedStats();
            float weaponDamage = stats.WeaponDamage;
            if (weaponDamage > 0)
            {
                _playerFireCooldown = GameConfig.PlayerFireRate;
                ref var shipT = ref game.EcsWorld.Get<Transform>(_playerShip);
                float rad = shipT.Rotation * MathF.PI / 180f;
                var dir = new Vector2(MathF.Cos(rad), MathF.Sin(rad));
                var spawnPos = shipT.Position + dir * 20f;

                EntityFactory.CreateProjectile(game.EcsWorld, spawnPos, dir,
                    weaponDamage, GameConfig.ProjectileSpeed, Faction.Player, 100, 255, 100);
            }
        }

        // Run AI system
        _enemyAISystem.Update(dt);

        // --- Asteroid-projectile collision is now handled by ProjectileSystem (asteroids have Health) ---

        // Run projectile system (collision detection with ships + asteroids)
        _projectileSystem.Update(dt);

        // Run shield regen
        _shieldRegenSystem.Update(in dt);

        // Process damage events (visual effects + mining HUD tracking)
        foreach (var (pos, damage, shieldHit, target) in _projectileSystem.DamageEventsLastUpdate)
        {
            _damagePopups.Add(new DamagePopup(pos, damage, shieldHit));

            // Track last asteroid hit for mining HUD
            if (game.EcsWorld.IsAlive(target) && game.EcsWorld.Has<AsteroidField>(target))
            {
                _lastHitAsteroid = target;
                _miningHudTimer = 2f;
            }
        }

        // Process destroyed entities
        var combatRng = new SeededRandom((ulong)(game.GlobalTime * 1000) ^ 0xDEADBEEF);
        foreach (var (entity, pos, faction, loot, asteroidData) in _projectileSystem.DestroyedThisFrame)
        {
            if (asteroidData.HasValue)
            {
                // Asteroid destroyed — collect resources
                var asteroid = asteroidData.Value;
                _explosions.Add(new Explosion(pos, 15f, 140, 120, 100, 0.5f));

                int added = game.Player.AddCargo(asteroid.Resource, asteroid.ResourceAmount);
                var resInfo = ResourceCatalog.Get(asteroid.Resource);
                if (added > 0)
                {
                    _miningMessage = $"+{added} {resInfo.Name.ToUpper()}";
                    _miningMessageTimer = 2.5f;
                }
                else
                {
                    _miningMessage = "CARGO FULL!";
                    _miningMessageTimer = 2.5f;
                }

                // Clear mining HUD since asteroid is gone
                if (_lastHitAsteroid == entity) _miningHudTimer = 0;

                if (game.EcsWorld.IsAlive(entity))
                {
                    _asteroidEntities.Remove(entity);
                    game.EcsWorld.Destroy(entity);
                }
            }
            else if (faction == Faction.Player)
            {
                // Player died
                HandlePlayerDeath(game, pos);
            }
            else
            {
                // Enemy died — create explosion and drop loot
                byte expR = faction == Faction.Pirate ? (byte)255 : (byte)200;
                byte expG = faction == Faction.Pirate ? (byte)120 : (byte)200;
                byte expB = faction == Faction.Pirate ? (byte)80 : (byte)200;
                _explosions.Add(new Explosion(pos, 30f, expR, expG, expB));

                if (loot.HasValue)
                {
                    ProcessLootDrop(game, loot.Value, combatRng, pos);
                }

                // Destroy the entity
                if (game.EcsWorld.IsAlive(entity))
                {
                    _enemyEntities.Remove(entity);
                    game.EcsWorld.Destroy(entity);
                }
            }
        }

        // Combat message timer
        if (_combatMessageTimer > 0)
        {
            _combatMessageTimer -= dt;
            if (_combatMessageTimer <= 0) _combatMessage = null;
        }

        // Update visual effects (timers, positions, removal)
        ProjectileRenderer.UpdateDamageEffects(_damagePopups, dt);
        ProjectileRenderer.UpdateExplosions(_explosions, dt);
    }

    /// <summary>Process loot when an enemy is destroyed.</summary>
    private void ProcessLootDrop(Game game, LootDrop loot, SeededRandom rng, Vector2 pos)
    {
        // Credits
        int credits = rng.NextInt(loot.MinCredits, loot.MaxCredits + 1);
        game.Player.Credits += credits;
        string message = $"+{credits} CREDITS";

        // Resource drop
        if (rng.NextFloat() < loot.ResourceDropChance)
        {
            var resource = (ResourceType)rng.NextInt(0, Enum.GetValues<ResourceType>().Length);
            int amount = rng.NextInt(1, 5 + loot.DangerLevel * 2);
            int added = game.Player.AddCargo(resource, amount);
            if (added > 0)
            {
                var resName = ResourceCatalog.Get(resource).Name;
                message += $"  +{added} {resName.ToUpper()}";
            }
        }

        // Part drop
        if (rng.NextFloat() < loot.PartDropChance)
        {
            // Pick a random part with tier scaled to danger level
            int maxTier = Math.Min(3, 1 + loot.DangerLevel / 2);
            var candidates = ShipPartCatalog.AllParts
                .Where(p => p.Tier > 0 && p.Tier <= maxTier)
                .ToArray();

            if (candidates.Length > 0)
            {
                var droppedPart = candidates[rng.NextInt(0, candidates.Length)];
                if (!game.Player.OwnedParts.Contains(droppedPart) &&
                    !game.Player.EquippedParts.ContainsValue(droppedPart))
                {
                    game.Player.OwnedParts.Add(droppedPart);
                    message += $"  +{droppedPart.Name.ToUpper()}!";
                }
            }
        }

        _combatMessage = message;
        _combatMessageTimer = 3f;
    }

    /// <summary>Handle player death — apply penalties and start respawn timer.</summary>
    private void HandlePlayerDeath(Game game, Vector2 deathPos)
    {
        _playerDead = true;
        _respawnTimer = RespawnDelay;
        _explosions.Add(new Explosion(deathPos, 50f, 255, 200, 80, 1.5f));

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

    /// <summary>Respawn the player at the nearest station (or center of system).</summary>
    private void HandlePlayerRespawn(Game game)
    {
        _playerDead = false;

        // Determine respawn position (nearest station, or system center)
        float centerX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float centerY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 respawnPos = new(centerX + 400, centerY);

        if (_stationEntities.Count > 0)
        {
            // Find nearest station from last known player position
            float bestDist = float.MaxValue;
            foreach (var stEntity in _stationEntities)
            {
                if (!game.EcsWorld.IsAlive(stEntity)) continue;
                var stPos = game.EcsWorld.Get<Transform>(stEntity).Position;
                float dist = Vector2.Distance(stPos, respawnPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    respawnPos = stPos + new Vector2(50, 0);
                }
            }
        }

        // Restore hull to 50%
        game.Player.ShipHealth = game.Player.ShipMaxHealth * GameConfig.DeathHullPercent;

        // Recreate player ship entity
        int shipSize = game.Player.CurrentShipType.SpriteSize;
        var playerStats = game.Player.GetCombinedStats();
        float playerMaxShield = playerStats.ShieldStrength;

        _playerShip = EntityFactory.CreatePlayerShip(game.EcsWorld, respawnPos, shipSize,
            game.Player.ShipMaxHealth, game.Player.ShipHealth, playerMaxShield, GameConfig.ShipMaxSpeed);

        game.Camera.Position = respawnPos;
        _combatMessage = "RESPAWNED — HULL AT 50%";
        _combatMessageTimer = 3f;
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
        vel.Value = Vector2.Zero;
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
        var camera = game.Camera;

        float starCenterX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float starCenterY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 starCenter = new(starCenterX, starCenterY);

        // Background stars (parallax)
        SolarSystemRenderer.RenderBackgroundStars(renderer, camera, _bgStars, starCenter);

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

        // Labels (via ECS system)
        float unusedDt = 0f;
        _labelRenderSystem.Update(in unusedDt);

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

        // HUD
        if (!_playerDead && game.EcsWorld.IsAlive(_playerShip))
        {
            ref var vel = ref game.EcsWorld.Get<Velocity>(_playerShip);
            SolarSystemRenderer.RenderHud(renderer, _starSystem.Name, _starSystem.StarClass, vel.Value.Length());
        }
        else
        {
            SolarSystemRenderer.RenderHud(renderer, _starSystem.Name, _starSystem.StarClass, 0f);
        }

        // Cargo HUD (below system HUD)
        SolarSystemRenderer.RenderCargoHud(renderer, game.Player);

        // Combat HUD (hull/shield bars + danger level)
        SolarSystemRenderer.RenderCombatHud(renderer, game.Player, game.EcsWorld,
            _playerShip, _starSystem.DangerLevel);

        // Off-screen indicators at screen borders
        if (!_playerDead)
        {
            SolarSystemRenderer.RenderOffscreenIndicators(renderer, camera, game.EcsWorld,
                _enemyEntities);
            SolarSystemRenderer.RenderStarOffscreenIndicator(renderer, camera, starCenter);
        }

        // Death screen
        if (_playerDead)
        {
            SolarSystemRenderer.RenderDeathScreen(renderer, _respawnTimer);
        }

        // Mining target info panel (shown for 2s after a projectile hit)
        if (_miningHudTimer > 0 && game.EcsWorld.IsAlive(_lastHitAsteroid)
            && game.EcsWorld.Has<AsteroidField>(_lastHitAsteroid))
        {
            ref var asteroidField = ref game.EcsWorld.Get<AsteroidField>(_lastHitAsteroid);
            ref var asteroidHealth = ref game.EcsWorld.Get<Health>(_lastHitAsteroid);
            SolarSystemRenderer.RenderMiningPanel(renderer, asteroidField.Resource,
                asteroidHealth.Hull, asteroidHealth.MaxHull, asteroidField.ResourceAmount);
        }

        // Mining feedback message
        if (_miningMessage != null)
            SolarSystemRenderer.RenderCenteredMessage(renderer, _miningMessage, -40, 255, 220, 80, 2.5f);

        // Combat feedback message
        if (_combatMessage != null)
            SolarSystemRenderer.RenderCenteredMessage(renderer, _combatMessage, 30, 255, 200, 80, 2f);

        // Interaction prompts
        if (_nearbyPlanetIndex >= 0)
        {
            SolarSystemRenderer.RenderPlanetPanel(renderer, _planets[_nearbyPlanetIndex]);
        }
        else if (_nearbyMoonIndex >= 0)
        {
            SolarSystemRenderer.RenderMoonPanel(renderer,
                _planets[_nearbyMoonPlanetIndex].Moons[_nearbyMoonIndex],
                _planets[_nearbyMoonPlanetIndex]);
        }
        else if (_nearbyStationIndex >= 0)
        {
            SolarSystemRenderer.RenderStationPanel(renderer, _stations[_nearbyStationIndex].Name);
        }

        // Controls
        SolarSystemRenderer.RenderControls(renderer);

        // Overlays drawn on top of everything
        _stationOverlay.Render(game);
        _planetLandingOverlay.Render(game);
        _galaxyMapOverlay.Render(game);
        _inGameMenuOverlay.Render(game);
    }
}
