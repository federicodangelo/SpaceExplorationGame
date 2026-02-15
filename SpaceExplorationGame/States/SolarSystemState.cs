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
        _starTexture = game.Textures.CreateStarTexture(
            Math.Max(16, starTexSize),
            _starSystem.StarR, _starSystem.StarG, _starSystem.StarB);

        _planetTextures.Clear();
        _moonTextures.Clear();
        for (int i = 0; i < _planets.Count; i++)
        {
            var p = _planets[i];
            int texSize = Math.Max(8, (int)(p.Radius * 2) + 4);
            _planetTextures.Add(game.Textures.CreatePlanetTexture(
                texSize, p.R, p.G, p.B, (uint)(game.Seeds.GalaxySeed ^ (ulong)(i * 7919))));

            var moonTexList = new List<nint>();
            for (int m = 0; m < p.Moons.Count; m++)
            {
                var moon = p.Moons[m];
                int mTexSize = Math.Max(6, (int)(moon.Radius * 2) + 2);
                moonTexList.Add(game.Textures.CreatePlanetTexture(
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
            _planetLandingOverlay.Open(_starSystem, _autoOpenPlanet, game);
        }
    }

    public override void Exit(Game game)
    {
        // Destroy cached textures
        if (_starTexture != nint.Zero) { SDL.DestroyTexture(_starTexture); _starTexture = nint.Zero; }
        foreach (var tex in _planetTextures) SDL.DestroyTexture(tex);
        _planetTextures.Clear();
        foreach (var moonList in _moonTextures)
        {
            foreach (var tex in moonList) SDL.DestroyTexture(tex);
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
                _stationOverlay.Open(_starSystem, _stations[_nearbyStationIndex], game);
            }
            else if (_nearbyPlanetIndex >= 0)
            {
                _planetLandingOverlay.Open(_starSystem, _planets[_nearbyPlanetIndex], game);
            }
            else if (_nearbyMoonIndex >= 0)
            {
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
            return;
        }

        // Planet landing overlay takes priority
        if (_planetLandingOverlay.IsOpen)
        {
            _planetLandingOverlay.Update(game, dt);
            _orbitSystem.Update(in dt);
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
            _cameraFollowSystem.Update(in dt);
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
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;
        var camera = game.Camera;

        float starCenterX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float starCenterY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 starCenter = new(starCenterX, starCenterY);

        // Background stars (parallax - move at 50% camera speed)
        foreach (var (x, y, brightness) in _bgStars)
        {
            var parallaxPos = new Vector2(x, y);
            var screenPos = camera.WorldToScreen(parallaxPos);
            // Simple parallax: shift based on camera
            screenPos.X -= (camera.Position.X - starCenter.X) * 0.3f * camera.Zoom;
            screenPos.Y -= (camera.Position.Y - starCenter.Y) * 0.3f * camera.Zoom;

            if (screenPos.X >= 0 && screenPos.X < GameConfig.WindowWidth &&
                screenPos.Y >= 0 && screenPos.Y < GameConfig.WindowHeight)
            {
                renderer.DrawRectScreen(screenPos.X, screenPos.Y, 1, 1, brightness, brightness, brightness);
            }
        }

        // Draw orbit lines
        foreach (var planet in _planets)
        {
            renderer.DrawCircle(camera, starCenter, planet.OrbitRadius, 30, 30, 50, 255, 64);
        }

        // Draw asteroids (computed from globalTime) using texture
        float asteroidTime = (float)game.GlobalTime;
        var asteroidTex = game.Textures.GetTexture(Rendering.TextureManager.Asteroid);
        foreach (var (baseAngle, radius, speed, size) in _asteroids)
        {
            float angle = baseAngle + speed * asteroidTime;
            var pos = starCenter + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            float rot = angle * 180f / MathF.PI * 2f; // slow spin
            renderer.DrawTexture(camera, asteroidTex, pos, (int)size + 4, (int)size + 4, rot);
        }

        // Draw star with texture
        float starDisplayRadius = _starSystem.StarRadius * 2f;
        renderer.DrawTexture(camera, _starTexture, starCenter,
            (int)(starDisplayRadius * 3), (int)(starDisplayRadius * 3));

        // Draw planets with textures
        for (int i = 0; i < _planets.Count; i++)
        {
            if (i >= _planetEntities.Count) break;
            var pTransform = game.EcsWorld.Get<Transform>(_planetEntities[i]);
            var p = _planets[i];
            int texRenderSize = (int)(p.Radius * 2) + 4;

            // Planet texture
            if (i < _planetTextures.Count)
            {
                renderer.DrawTexture(camera, _planetTextures[i], pTransform.Position,
                    texRenderSize, texRenderSize);
            }

            // Settlement indicator (small diamond below planet)
            if (p.HasSettlement)
            {
                var indicatorPos = pTransform.Position + new Vector2(0, p.Radius + 6);
                float sz = 3f;
                // Draw a small yellow diamond
                renderer.DrawFilledCircle(camera, indicatorPos, sz, 255, 210, 200, 220);
            }

            // Rings
            if (p.HasRings)
            {
                renderer.DrawCircle(camera, pTransform.Position, p.Radius * 1.5f,
                    p.R, p.G, p.B, 120, 48);
                renderer.DrawCircle(camera, pTransform.Position, p.Radius * 1.8f,
                    p.R, p.G, p.B, 80, 48);
            }

            // Moon orbit lines
            foreach (var moon in p.Moons)
            {
                renderer.DrawCircle(camera, pTransform.Position, moon.OrbitRadius, 20, 20, 40, 255, 24);
            }

            // Moon textures
            if (i < _moonEntities.Count)
            {
                for (int m = 0; m < _moonEntities[i].Count; m++)
                {
                    var moonTransform = game.EcsWorld.Get<Transform>(_moonEntities[i][m]);
                    var moon = p.Moons[m];
                    int moonTexSize = (int)(moon.Radius * 2) + 2;
                    if (i < _moonTextures.Count && m < _moonTextures[i].Count)
                    {
                        renderer.DrawTexture(camera, _moonTextures[i][m], moonTransform.Position,
                            moonTexSize, moonTexSize);
                    }
                }
            }
        }

        // Draw stations with texture
        var stationTex = game.Textures.GetTexture(Rendering.TextureManager.Station);
        for (int i = 0; i < _stationEntities.Count; i++)
        {
            var stTransform = game.EcsWorld.Get<Transform>(_stationEntities[i]);
            float stRotation = (float)(game.GlobalTime * 10) % 360f; // slow station rotation
            renderer.DrawTexture(camera, stationTex, stTransform.Position, 28, 28, stRotation);
        }

        // Draw labels (via ECS system)
        float unusedDt = 0f;
        _labelRenderSystem.Update(in unusedDt);

        // Draw player ship with texture
        ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
        var shipTexKey = Rendering.TextureManager.GetShipSolarKey(game.Player.CurrentShipType.Id);
        var shipTex = game.Textures.GetTexture(shipTexKey);

        // Engine flame when thrusting (draw behind ship, offset backward)
        int shipSpriteSize = game.Player.CurrentShipType.SpriteSize;
        if (game.Input.IsKeyDown(SDL.Scancode.W) || game.Input.IsKeyDown(SDL.Scancode.Up))
        {
            var flameTex = game.Textures.GetTexture(Rendering.TextureManager.ShipFlame);
            float shipRad = shipTransform.Rotation * MathF.PI / 180f;
            // Offset the flame behind the ship's heading
            float flameOffset = shipSpriteSize * 0.56f;
            var flamePos = shipTransform.Position - new Vector2(MathF.Cos(shipRad), MathF.Sin(shipRad)) * flameOffset;
            int flameSize = (int)(shipSpriteSize * 1.25f);
            renderer.DrawTexture(camera, flameTex, flamePos, flameSize, flameSize, shipTransform.Rotation);
        }

        // Ship sprite (rotated to match heading)
        renderer.DrawTexture(camera, shipTex, shipTransform.Position, shipSpriteSize, shipSpriteSize, shipTransform.Rotation);

        // --- HUD ---
        renderer.DrawRectScreen(0, 0, 280, 75, 0, 0, 0, 160);
        renderer.DrawTextScreen(10, 10, $"SYSTEM: {_starSystem.Name}", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"CLASS {_starSystem.StarClass} STAR", 150, 150, 150, 1.5f);

        ref var vel = ref game.EcsWorld.Get<Velocity>(_playerShip);
        renderer.DrawTextScreen(10, 55, $"SPEED: {vel.Value.Length():F0}", 150, 150, 150, 1.5f);

        // Interaction prompts with body info
        if (_nearbyPlanetIndex >= 0)
        {
            var p = _planets[_nearbyPlanetIndex];
            string action = $"[E] LAND ON {p.Name.ToUpper()}";
            float tw = renderer.MeasureText(action, 2f);
            float panelW = Math.Max(tw + 20, 320);
            float panelH = 90;
            float px = GameConfig.WindowWidth / 2f - panelW / 2f;
            float py = GameConfig.WindowHeight - panelH - 15;
            renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);

            renderer.DrawTextScreen(px + 10, py + 6, action, 100, 255, 100, 2f);
            renderer.DrawTextScreen(px + 10, py + 30, $"TYPE: {p.Type.ToString().ToUpper()}", 180, 180, 180, 1.5f);
            string details = $"MOONS: {p.MoonCount}";
            if (p.HasRings) details += "  RINGS: YES";
            renderer.DrawTextScreen(px + 10, py + 48, details, 150, 150, 150, 1.5f);

            byte sr = p.HasSettlement ? (byte)255 : (byte)120;
            byte sg = p.HasSettlement ? (byte)220 : (byte)120;
            byte sb = p.HasSettlement ? (byte)100 : (byte)120;
            string settText = p.HasSettlement ? "SETTLEMENTS: YES" : "NO SETTLEMENTS";
            renderer.DrawTextScreen(px + 10, py + 66, settText, sr, sg, sb, 1.5f);
        }
        else if (_nearbyMoonIndex >= 0)
        {
            var moon = _planets[_nearbyMoonPlanetIndex].Moons[_nearbyMoonIndex];
            var parent = _planets[_nearbyMoonPlanetIndex];
            string action = $"[E] LAND ON {moon.Name.ToUpper()}";
            float tw = renderer.MeasureText(action, 2f);
            float panelW = Math.Max(tw + 20, 320);
            float panelH = 72;
            float px = GameConfig.WindowWidth / 2f - panelW / 2f;
            float py = GameConfig.WindowHeight - panelH - 15;
            renderer.DrawRectScreen(px, py, panelW, panelH, 0, 0, 0, 180);

            renderer.DrawTextScreen(px + 10, py + 6, action, 180, 255, 180, 2f);
            renderer.DrawTextScreen(px + 10, py + 30, $"TYPE: {moon.Type.ToString().ToUpper()}", 180, 180, 180, 1.5f);
            renderer.DrawTextScreen(px + 10, py + 48, $"ORBITS: {parent.Name.ToUpper()}", 150, 150, 150, 1.5f);
        }
        else if (_nearbyStationIndex >= 0)
        {
            string stationName = _stations[_nearbyStationIndex].Name;
            string text = $"[E] DOCK AT {stationName.ToUpper()}";
            float tw = renderer.MeasureText(text, 2f);
            float tx = GameConfig.WindowWidth / 2 - tw / 2;
            renderer.DrawRectScreen(tx - 10, GameConfig.WindowHeight - 70, tw + 20, 30, 0, 0, 0, 160);
            renderer.DrawTextScreen(tx, GameConfig.WindowHeight - 60, text, 100, 200, 255, 2f);
        }

        // Controls background
        renderer.DrawRectScreen(GameConfig.WindowWidth - 290, 5, 290, 130, 0, 0, 0, 160);

        // Controls
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 10, "W/UP: THRUST", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 30, "A/D: ROTATE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 50, "S/DOWN: BRAKE", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 70, "SCROLL: ZOOM", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 90, "M: GALAXY MAP", 180, 180, 180, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 110, "E: INTERACT", 180, 180, 180, 1.5f);

        // Station overlay drawn on top of everything
        _stationOverlay.Render(game);

        // Planet landing overlay drawn on top of everything
        _planetLandingOverlay.Render(game);

        // Galaxy map overlay drawn on top of everything
        _galaxyMapOverlay.Render(game);

        // In-game menu overlay drawn on top of everything
        _inGameMenuOverlay.Render(game);
    }
}
