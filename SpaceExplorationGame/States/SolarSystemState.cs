using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;

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

    public SolarSystemState(StarSystemData starSystem)
    {
        _starSystem = starSystem;
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
        _playerShip = game.EcsWorld.Create(
            new Transform(shipStartPos),
            ECS.Components.Sprite.ColoredRect(32, 32, 100, 255, 100),
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

        // Camera follows player
        game.Camera.Position = shipStartPos;
        game.Camera.Zoom = 1f;
    }

    public override void Exit(Game game)
    {
        _planets.Clear();
        _asteroidBelts.Clear();
        _stations.Clear();
        _planetEntities.Clear();
        _stationEntities.Clear();
        _moonEntities.Clear();
        _asteroids.Clear();
        _bgStars.Clear();
    }

    public override void HandleEvent(Game game, SDL.Event e)
    {
    }

    public override void Update(Game game, float dt)
    {
        var input = game.Input;
        var camera = game.Camera;

        // --- Player ship controls ---
        ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
        ref var shipVelocity = ref game.EcsWorld.Get<Velocity>(_playerShip);

        // Rotation
        if (input.IsKeyDown(SDL.Scancode.A) || input.IsKeyDown(SDL.Scancode.Left))
            shipTransform.Rotation -= GameConfig.ShipRotationSpeed * dt;
        if (input.IsKeyDown(SDL.Scancode.D) || input.IsKeyDown(SDL.Scancode.Right))
            shipTransform.Rotation += GameConfig.ShipRotationSpeed * dt;

        // Thrust
        if (input.IsKeyDown(SDL.Scancode.W) || input.IsKeyDown(SDL.Scancode.Up))
        {
            float rad = shipTransform.Rotation * MathF.PI / 180f;
            var thrust = new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * GameConfig.ShipAcceleration * dt;
            shipVelocity.Value += thrust;
        }

        // Brake
        if (input.IsKeyDown(SDL.Scancode.S) || input.IsKeyDown(SDL.Scancode.Down))
        {
            shipVelocity.Value *= 0.95f;
        }

        // Apply friction and speed limit
        shipVelocity.Value *= GameConfig.ShipFriction;
        if (shipVelocity.Value.Length() > shipVelocity.MaxSpeed)
        {
            shipVelocity.Value = Vector2.Normalize(shipVelocity.Value) * shipVelocity.MaxSpeed;
        }

        // Move ship
        shipTransform.Position += shipVelocity.Value * dt;

        // --- Update orbits using global time (deterministic) ---
        float starCenterX = GameConfig.SolarSystemWidth * GameConfig.TileSize / 2f;
        float starCenterY = GameConfig.SolarSystemHeight * GameConfig.TileSize / 2f;
        Vector2 starCenter = new(starCenterX, starCenterY);
        float time = (float)game.GlobalTime;

        var orbitQuery = new QueryDescription().WithAll<Transform, Orbit>();
        game.EcsWorld.Query(in orbitQuery, (Entity entity, ref Transform transform, ref Orbit orbit) =>
        {
            orbit.CurrentAngle = orbit.BaseAngle + orbit.OrbitSpeed * time;

            Vector2 parentPos;
            if (game.EcsWorld.IsAlive(orbit.Parent))
            {
                parentPos = game.EcsWorld.Get<Transform>(orbit.Parent).Position;
            }
            else
            {
                parentPos = starCenter;
            }

            transform.Position = parentPos + new Vector2(
                MathF.Cos(orbit.CurrentAngle) * orbit.OrbitRadius,
                MathF.Sin(orbit.CurrentAngle) * orbit.OrbitRadius
            );
        });

        // --- Camera follows player ---
        camera.LerpTo(shipTransform.Position, 5f * dt);

        // Zoom
        if (input.MouseWheelY != 0)
        {
            camera.Zoom += input.MouseWheelY * GameConfig.CameraZoomSpeed;
            camera.ClampZoom();
        }

        // --- Check proximity for interactions ---
        // Unified: find the single closest interactable object (by distance to center),
        // among all planets, moons, and stations within interaction range.
        _nearbyPlanetIndex = -1;
        _nearbyStationIndex = -1;
        _nearbyMoonPlanetIndex = -1;
        _nearbyMoonIndex = -1;

        float bestDist = float.MaxValue;

        // Check planets
        for (int i = 0; i < _planetEntities.Count; i++)
        {
            var body = game.EcsWorld.Get<CelestialBody>(_planetEntities[i]);
            if (!body.HasSolidSurface) continue;
            var pos = game.EcsWorld.Get<Transform>(_planetEntities[i]).Position;
            float dist = Vector2.Distance(shipTransform.Position, pos);
            if (dist < body.Radius + InteractionRadius && dist < bestDist)
            {
                bestDist = dist;
                _nearbyPlanetIndex = i;
                _nearbyStationIndex = -1;
                _nearbyMoonPlanetIndex = -1;
                _nearbyMoonIndex = -1;
            }
        }

        // Check moons
        for (int pi = 0; pi < _moonEntities.Count; pi++)
        {
            for (int mi = 0; mi < _moonEntities[pi].Count; mi++)
            {
                var moonBody = game.EcsWorld.Get<CelestialBody>(_moonEntities[pi][mi]);
                var pos = game.EcsWorld.Get<Transform>(_moonEntities[pi][mi]).Position;
                float dist = Vector2.Distance(shipTransform.Position, pos);
                if (dist < moonBody.Radius + InteractionRadius && dist < bestDist)
                {
                    bestDist = dist;
                    _nearbyMoonPlanetIndex = pi;
                    _nearbyMoonIndex = mi;
                    _nearbyPlanetIndex = -1;
                    _nearbyStationIndex = -1;
                }
            }
        }

        // Check stations
        for (int i = 0; i < _stationEntities.Count; i++)
        {
            var stBody = game.EcsWorld.Get<CelestialBody>(_stationEntities[i]);
            var pos = game.EcsWorld.Get<Transform>(_stationEntities[i]).Position;
            float dist = Vector2.Distance(shipTransform.Position, pos);
            if (dist < stBody.Radius + InteractionRadius && dist < bestDist)
            {
                bestDist = dist;
                _nearbyStationIndex = i;
                _nearbyPlanetIndex = -1;
                _nearbyMoonPlanetIndex = -1;
                _nearbyMoonIndex = -1;
            }
        }

        // Interact
        if (input.IsKeyPressed(SDL.Scancode.E))
        {
            if (_nearbyStationIndex >= 0)
            {
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = _nearbyStationIndex;
                game.ChangeState(new SpaceStationState(_starSystem, _stations[_nearbyStationIndex]));
            }
            else if (_nearbyPlanetIndex >= 0)
            {
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = _nearbyPlanetIndex;
                game.ChangeState(new PlanetSurfaceState(_starSystem, _planets[_nearbyPlanetIndex]));
            }
            else if (_nearbyMoonIndex >= 0)
            {
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromMoon;
                game.Player.ReturnMoonPlanetIndex = _nearbyMoonPlanetIndex;
                game.Player.ReturnMoonIndex = _nearbyMoonIndex;
                var moonData = _planets[_nearbyMoonPlanetIndex].Moons[_nearbyMoonIndex];
                game.ChangeState(new PlanetSurfaceState(_starSystem, moonData.ToPlanetData(_nearbyMoonPlanetIndex)));
            }
        }

        // Back to galaxy map
        if (input.IsKeyPressed(SDL.Scancode.M))
        {
            game.ChangeState(new GalaxyMapState());
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
            renderer.DrawCircle(camera, starCenter, planet.OrbitRadius, 30, 30, 50, 60, 64);
        }

        // Draw asteroids (computed from globalTime)
        float asteroidTime = (float)game.GlobalTime;
        foreach (var (baseAngle, radius, speed, size) in _asteroids)
        {
            float angle = baseAngle + speed * asteroidTime;
            var pos = starCenter + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            renderer.DrawRect(camera, pos, (int)size, (int)size, 140, 120, 100);
        }

        // Draw star with glow
        var starScreenPos = camera.WorldToScreen(starCenter);
        // Glow layers
        renderer.DrawFilledCircle(camera, starCenter, _starSystem.StarRadius * 2.5f,
            _starSystem.StarR, _starSystem.StarG, _starSystem.StarB, 20);
        renderer.DrawFilledCircle(camera, starCenter, _starSystem.StarRadius * 1.5f,
            _starSystem.StarR, _starSystem.StarG, _starSystem.StarB, 60);
        renderer.DrawFilledCircle(camera, starCenter, _starSystem.StarRadius,
            _starSystem.StarR, _starSystem.StarG, _starSystem.StarB, 255);

        // Draw celestial bodies
        var bodyQuery = new QueryDescription().WithAll<Transform, ECS.Components.Sprite, CelestialBody>();
        game.EcsWorld.Query(in bodyQuery, (Entity entity, ref Transform transform, ref ECS.Components.Sprite sprite, ref CelestialBody body) =>
        {
            if (body.Type == CelestialType.Star) return; // already drawn with glow

            if (body.Type == CelestialType.SpaceStation)
            {
                // Draw station as a small diamond shape
                var sPos = camera.WorldToScreen(transform.Position);
                float sz = 6 * camera.Zoom;
                renderer.DrawRectScreen(sPos.X - sz / 2, sPos.Y - sz / 2, sz, sz, 200, 200, 255);
            }
            else
            {
                // Draw planet/moon as filled circle
                renderer.DrawFilledCircle(camera, transform.Position, body.Radius,
                    sprite.R, sprite.G, sprite.B);
            }
        });

        // Draw planet rings
        for (int i = 0; i < _planets.Count; i++)
        {
            if (_planets[i].HasRings)
            {
                var pTransform = game.EcsWorld.Get<Transform>(_planetEntities[i]);
                renderer.DrawCircle(camera, pTransform.Position, _planets[i].Radius * 1.5f,
                    _planets[i].R, _planets[i].G, _planets[i].B, 120, 48);
                renderer.DrawCircle(camera, pTransform.Position, _planets[i].Radius * 1.8f,
                    _planets[i].R, _planets[i].G, _planets[i].B, 80, 48);
            }
        }

        // Draw moon orbit lines
        for (int i = 0; i < _planetEntities.Count; i++)
        {
            var pTransform = game.EcsWorld.Get<Transform>(_planetEntities[i]);
            foreach (var moon in _planets[i].Moons)
            {
                renderer.DrawCircle(camera, pTransform.Position, moon.OrbitRadius, 20, 20, 40, 40, 24);
            }
        }

        // Draw labels
        var labelQuery = new QueryDescription().WithAll<Transform, Label>();
        game.EcsWorld.Query(in labelQuery, (Entity entity, ref Transform transform, ref Label label) =>
        {
            var textPos = transform.Position + new Vector2(0, label.OffsetY);
            float textScale = Math.Max(0.8f, camera.Zoom * 0.8f);
            float textWidth = renderer.MeasureText(label.Text, textScale);
            renderer.DrawText(camera, textPos - new Vector2(textWidth / (2 * camera.Zoom), 0),
                label.Text, 180, 180, 180, textScale);
        });

        // Draw player ship
        ref var shipTransform = ref game.EcsWorld.Get<Transform>(_playerShip);
        float shipRad = shipTransform.Rotation * MathF.PI / 180f;

        // Ship triangle
        var nose = shipTransform.Position + new Vector2(MathF.Cos(shipRad), MathF.Sin(shipRad)) * 12;
        var left = shipTransform.Position + new Vector2(MathF.Cos(shipRad + 2.5f), MathF.Sin(shipRad + 2.5f)) * 8;
        var right = shipTransform.Position + new Vector2(MathF.Cos(shipRad - 2.5f), MathF.Sin(shipRad - 2.5f)) * 8;

        renderer.DrawLine(camera, nose, left, 100, 255, 100);
        renderer.DrawLine(camera, nose, right, 100, 255, 100);
        renderer.DrawLine(camera, left, right, 100, 255, 100);

        // Engine flame when thrusting
        if (game.Input.IsKeyDown(SDL.Scancode.W) || game.Input.IsKeyDown(SDL.Scancode.Up))
        {
            var exhaust = shipTransform.Position - new Vector2(MathF.Cos(shipRad), MathF.Sin(shipRad)) * 14;
            renderer.DrawLine(camera, left, exhaust, 255, 150, 50);
            renderer.DrawLine(camera, right, exhaust, 255, 150, 50);
        }

        // --- HUD ---
        renderer.DrawTextScreen(10, 10, $"SYSTEM: {_starSystem.Name}", 200, 200, 255, 2f);
        renderer.DrawTextScreen(10, 35, $"CLASS {_starSystem.StarClass} STAR", 150, 150, 150, 1.5f);

        ref var vel = ref game.EcsWorld.Get<Velocity>(_playerShip);
        renderer.DrawTextScreen(10, 55, $"SPEED: {vel.Value.Length():F0}", 150, 150, 150, 1.5f);

        // Interaction prompts
        if (_nearbyPlanetIndex >= 0)
        {
            string planetName = _planets[_nearbyPlanetIndex].Name;
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 60,
                $"[E] LAND ON {planetName.ToUpper()}", 100, 255, 100, 2f);
        }
        else if (_nearbyMoonIndex >= 0)
        {
            string moonName = _planets[_nearbyMoonPlanetIndex].Moons[_nearbyMoonIndex].Name;
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 60,
                $"[E] LAND ON {moonName.ToUpper()}", 180, 255, 180, 2f);
        }
        else if (_nearbyStationIndex >= 0)
        {
            string stationName = _stations[_nearbyStationIndex].Name;
            renderer.DrawTextScreen(GameConfig.WindowWidth / 2 - 100, GameConfig.WindowHeight - 60,
                $"[E] DOCK AT {stationName.ToUpper()}", 100, 200, 255, 2f);
        }

        // Controls
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 10, "W/UP: THRUST", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 30, "A/D: ROTATE", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 50, "S/DOWN: BRAKE", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 70, "SCROLL: ZOOM", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 90, "M: GALAXY MAP", 120, 120, 120, 1.5f);
        renderer.DrawTextScreen(GameConfig.WindowWidth - 280, 110, "E: INTERACT", 120, 120, 120, 1.5f);
    }
}
