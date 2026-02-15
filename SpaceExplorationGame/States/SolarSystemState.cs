using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
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

    // Asteroid entities (for visual) — stores base angles, positions computed from globalTime
    private List<(float BaseAngle, float Radius, float Speed, float Size)> _asteroids = [];

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
        _starEntity = game.EcsWorld.Create(
            new Transform(center),
            ECS.Components.Sprite.ColoredRect((int)(starDisplayRadius * 2), (int)(starDisplayRadius * 2),
                _starSystem.StarR, _starSystem.StarG, _starSystem.StarB),
            new CelestialBody
            {
                Type = CelestialType.Star,
                Name = _starSystem.Name,
                Radius = starDisplayRadius,
                DataIndex = _starSystem.Index
            },
            new Label { Text = _starSystem.Name, OffsetY = (int)(starDisplayRadius + 15) }
        );

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

            var planetEntity = game.EcsWorld.Create(
                new Transform(pos),
                ECS.Components.Sprite.ColoredRect((int)(planet.Radius * 2), (int)(planet.Radius * 2),
                    planet.R, planet.G, planet.B),
                new CelestialBody
                {
                    Type = CelestialType.Planet,
                    Name = planet.Name,
                    Radius = planet.Radius,
                    DataIndex = i,
                    HasSolidSurface = planet.HasSolidSurface
                },
                new Orbit(_starEntity, planet.OrbitRadius, planet.OrbitSpeed, planet.StartAngle),
                new Label { Text = planet.Name, OffsetY = (int)(planet.Radius + 10) }
            );

            if (planet.HasSolidSurface)
            {
                game.EcsWorld.Add(planetEntity, new Interactable
                {
                    Type = InteractionType.LandOnPlanet,
                    Label = "Land"
                });
            }

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

                var moonEntity = game.EcsWorld.Create(
                    new Transform(moonPos),
                    ECS.Components.Sprite.ColoredRect((int)(moon.Radius * 2), (int)(moon.Radius * 2),
                        moon.R, moon.G, moon.B),
                    new CelestialBody
                    {
                        Type = CelestialType.Moon,
                        Name = moon.Name,
                        Radius = moon.Radius,
                        DataIndex = moon.Index,
                        HasSolidSurface = true
                    },
                    new Orbit(planetEntity, moon.OrbitRadius, moon.OrbitSpeed, moon.StartAngle),
                    new Label { Text = moon.Name, OffsetY = (int)(moon.Radius + 8) },
                    new Interactable
                    {
                        Type = InteractionType.LandOnPlanet,
                        Label = "Land"
                    }
                );
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

            var stEntity = game.EcsWorld.Create(
                new Transform(stPos),
                ECS.Components.Sprite.ColoredRect(24, 24, 200, 200, 255),
                new CelestialBody
                {
                    Type = CelestialType.SpaceStation,
                    Name = station.Name,
                    Radius = 12,
                    DataIndex = station.Index
                },
                new Orbit(parent, station.OrbitRadius, station.OrbitSpeed, station.StartAngle),
                new Label { Text = station.Name, OffsetY = 28 },
                new Interactable
                {
                    Type = InteractionType.DockAtStation,
                    Label = "Dock"
                }
            );
            _stationEntities.Add(stEntity);
        }

        // Generate asteroid positions (base angles, rendered from globalTime)
        var asteroidRng = new SeededRandom(rng.DeriveChildSeed(999));
        foreach (var belt in _asteroidBelts)
        {
            for (int i = 0; i < belt.AsteroidCount; i++)
            {
                _asteroids.Add((
                    asteroidRng.NextFloat(0, MathF.PI * 2),
                    asteroidRng.NextFloat(belt.InnerRadius, belt.OuterRadius),
                    asteroidRng.NextFloat(0.002f, 0.008f),
                    asteroidRng.NextFloat(4, 10)
                ));
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
        _playerShip = game.EcsWorld.Create(
            new Transform(shipStartPos),
            ECS.Components.Sprite.ColoredRect(shipSize, shipSize, 100, 255, 100),
            new Velocity(GameConfig.ShipMaxSpeed),
            new PlayerControlled()
        );

        // Background stars
        var bgRng = new SeededRandom(game.Seeds.GalaxySeed ^ 0xCAFEBABE);
        float mapW = GameConfig.SolarSystemWidth * GameConfig.TileSize;
        float mapH = GameConfig.SolarSystemHeight * GameConfig.TileSize;
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

        _cameraFollowSystem = new CameraFollowSystem(game.EcsWorld, game.Camera, game.Input);
        _cameraFollowSystem.Initialize();

        _labelRenderSystem = new LabelRenderSystem(game.EcsWorld, game.SpriteRenderer, game.Camera);
        _labelRenderSystem.Initialize();

        _proximitySystem = new InteractionProximitySystem(game.EcsWorld, InteractionRadius);

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
        _asteroids.Clear();
        _bgStars.Clear();

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
        game.AsteroidRenderer.RenderAsteroids(renderer, camera, _asteroids, starCenter, game.GlobalTime);

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

        // Player ship
        ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
        int shipSpriteSize = game.Player.CurrentShipType.SpriteSize;
        bool isThrusting = game.Input.IsKeyDown(SDL.Scancode.W) || game.Input.IsKeyDown(SDL.Scancode.Up);
        game.SpaceshipRenderer.RenderFlying(renderer, camera, shipTransform.Position,
            shipTransform.Rotation, game.Player.CurrentShipType.Id, shipSpriteSize, isThrusting);

        // HUD
        ref var vel = ref game.EcsWorld.Get<Velocity>(_playerShip);
        SolarSystemRenderer.RenderHud(renderer, _starSystem.Name, _starSystem.StarClass, vel.Value.Length());

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
