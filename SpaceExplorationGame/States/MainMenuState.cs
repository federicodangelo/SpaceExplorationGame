using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.UI;
using SpaceExplorationGame.UI.Overlays.Menu;
using SpaceExplorationGame.Generation.Showcase;

namespace SpaceExplorationGame.States;

/// <summary>Starting location types (also used for CLI auto-launch).</summary>
public enum StartOption
{
    None = -1,
    StarSystem,
    Planet,
    PlanetSurface,
    SpaceStation,
    SpaceStationInside,
    Settlement,
    SettlementInside,
}

/// <summary>
/// Main menu: lets the player configure danger, location type, seed, then start.
/// </summary>
public class MainMenuState : GameState
{
    public override GameStateType Type => GameStateType.MainMenu;

    // Persist debug-menu reopen behavior across MainMenuState instances.
    private static bool s_reopenDebugMenu;

    private float _animTimer;

    // Background stars for visual flair
    private List<AnimatedStar> _bgStars = [];

    private readonly MainMenuOverlay _menuOverlay = new();
    private readonly DebugMenuOverlay _debugOverlay = new();

    // Auto-launch: if not None, skip menu and launch this option immediately
    private readonly StartOption _autoLaunchOption;

    private Camera _fakeCamera = new(GameConfig.WindowWidth, GameConfig.WindowHeight)
    {
        Position = new Vector2(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f),
        Zoom = 1f
    };

    // Preview state for the selected starting location
    private StarSystemData? _previewSystem;
    private PlanetData? _previewPlanet;
    private SpaceStationData? _previewStation;
    private int _rerollCounter;
    private ulong _lastPreviewSeed;
    private StartOption _lastPreviewedLocationType = StartOption.None;
    private int _lastPreviewedDanger = -1;

    public MainMenuState(StartOption autoLaunchOption = StartOption.None)
    {
        _autoLaunchOption = autoLaunchOption;
    }

    public override void Enter(Game game)
    {
        game.Player.Reset();
        game.Audio.SetMusicTheme(MusicTheme.MainMenu);

        if (_autoLaunchOption != StartOption.None)
        {
            LaunchGame(game, _autoLaunchOption, dangerFilter: 0);
            return;
        }

        _menuOverlay.Open();
        UpdateStartingShipOverrideLabel();
        UpdateLocationPreview(game);

        if (s_reopenDebugMenu)
        {
            _debugOverlay.Open();
        }

        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            _bgStars.Add(new AnimatedStar(
                rng.Next(0, GameConfig.WindowWidth),
                rng.Next(0, GameConfig.WindowHeight),
                (byte)rng.Next(40, 160),
                0.2f + (float)rng.NextDouble() * 0.8f
            ));
        }
    }

    public override void Exit(Game game) { }
    public override void HandleEvent(Game game, SDL.Event e) { }

    public override void UpdateInput(Game game)
    {
        // Debug overlay takes priority over main menu input
        if (_debugOverlay.UpdateInput(game))
        {
            if (_debugOverlay.StartStarTypeShowcaseRequested)
            {
                _debugOverlay.StartStarTypeShowcaseRequested = false;
                game.Audio.PlaySfx(SfxType.MenuSelect);
                LaunchStarTypeShowcase(game, _debugOverlay.SelectedStarType);
            }

            if (_debugOverlay.StartPlanetTypeShowcaseRequested)
            {
                _debugOverlay.StartPlanetTypeShowcaseRequested = false;
                game.Audio.PlaySfx(SfxType.MenuSelect);
                LaunchPlanetTypeShowcase(game);
            }

            if (_debugOverlay.StartAsteroidShowcaseRequested)
            {
                _debugOverlay.StartAsteroidShowcaseRequested = false;
                game.Audio.PlaySfx(SfxType.MenuSelect);
                LaunchAsteroidShowcase(game);
            }

            if (_debugOverlay.StartSurfaceMiningShowcaseRequested)
            {
                _debugOverlay.StartSurfaceMiningShowcaseRequested = false;
                game.Audio.PlaySfx(SfxType.MenuSelect);
                LaunchSurfaceMiningShowcase(game);
            }
            return;
        }

        _menuOverlay.UpdateInput(game);

        if (_menuOverlay.DebugRequested)
        {
            _menuOverlay.DebugRequested = false;
            game.Audio.PlaySfx(SfxType.MenuSelect);
            _debugOverlay.Open();
            return;
        }

        // Handle seed changes
        if (_menuOverlay.NewSeed.HasValue)
        {
            game.RegenerateGalaxy(_menuOverlay.NewSeed.Value);
            _menuOverlay.NewSeed = null;
            _menuOverlay.CurrentSeed = game.Seeds.GalaxySeed;
            game.Audio.PlaySfx(SfxType.MenuSelect);
            UpdateLocationPreview(game);
        }

        if (_menuOverlay.RandomizeSeed)
        {
            ulong newSeed = (ulong)Random.Shared.NextInt64();
            game.RegenerateGalaxy(newSeed);
            _menuOverlay.RandomizeSeed = false;
            _menuOverlay.CurrentSeed = game.Seeds.GalaxySeed;
            game.Audio.PlaySfx(SfxType.MenuSelect);
            UpdateLocationPreview(game);
        }

        // Handle randomize location (re-roll)
        if (_menuOverlay.RandomizeLocation)
        {
            _rerollCounter++;
            _menuOverlay.RandomizeLocation = false;
            game.Audio.PlaySfx(SfxType.MenuSelect);
            UpdateLocationPreview(game);
        }

        // Handle filter changes (danger or location type cycling)
        if (_menuOverlay.FiltersChanged)
        {
            _menuOverlay.FiltersChanged = false;
            _rerollCounter = 0; // Reset re-roll on filter change
            UpdateLocationPreview(game);
        }

        // Handle start game
        if (_menuOverlay.StartRequested)
        {
            game.Audio.PlaySfx(SfxType.MenuSelect);
            _menuOverlay.StartRequested = false;
            LaunchGame(game, _menuOverlay.LocationType, _menuOverlay.DangerFilter);
        }
    }

    public override void Update(Game game)
    {
        _animTimer += game.DeltaTime;
        _menuOverlay.Update(game);
        _debugOverlay.Update(game);
        _menuOverlay.CurrentSeed = game.Seeds.GalaxySeed;
        UpdateStartingShipOverrideLabel();

        // Refresh preview when filters or seed change
        var locType = _menuOverlay.LocationType;
        var danger = _menuOverlay.DangerFilter;
        var seed = game.Seeds.GalaxySeed;
        if (locType != _lastPreviewedLocationType || danger != _lastPreviewedDanger || seed != _lastPreviewSeed)
        {
            _lastPreviewedLocationType = locType;
            _lastPreviewedDanger = danger;
            _lastPreviewSeed = seed;
            UpdateLocationPreview(game);
        }
    }

    // ── Launch ──

    private void LaunchGame(Game game, StartOption locationType, int dangerFilter)
    {
        // Any non-debug launch returns to normal main-menu behavior.
        s_reopenDebugMenu = false;
        game.UseProceduralWorldGenerator();
        ApplySelectedStartingShip(game);

        // Ensure we have a preview matching the requested type
        if (_previewSystem == null || _lastPreviewedLocationType != locationType)
            UpdateLocationPreview(game);

        switch (locationType)
        {
            case StartOption.StarSystem:
            {
                var system = _previewSystem ?? PickRandomSystem(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system));
                break;
            }

            case StartOption.Planet:
            {
                var (system, planet) = _previewSystem != null && _previewPlanet != null
                    ? new SystemPlanet(_previewSystem, _previewPlanet)
                    : PickRandomPlanet(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;
                game.ChangeState(new SolarSystemState(system));
                break;
            }

            case StartOption.PlanetSurface:
            {
                var (system, planet) = _previewSystem != null && _previewPlanet != null
                    ? new SystemPlanet(_previewSystem, _previewPlanet)
                    : PickRandomPlanet(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system, autoOpenPlanet: planet));
                break;
            }

            case StartOption.SpaceStation:
            {
                var (system, station) = _previewSystem != null && _previewStation != null
                    ? new SystemStation(_previewSystem, _previewStation)
                    : PickRandomStation(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = station.Index;
                game.ChangeState(new SolarSystemState(system, station));
                break;
            }

            case StartOption.SpaceStationInside:
            {
                var (system, station) = _previewSystem != null && _previewStation != null
                    ? new SystemStation(_previewSystem, _previewStation)
                    : PickRandomStation(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = station.Index;
                game.ChangeState(new InteriorState(InteriorOrigin.Station, system, station: station));
                break;
            }

            case StartOption.Settlement:
            {
                var (system, planet, settlement) = _previewSystem != null && _previewPlanet != null
                    ? GetSettlementData(game, _previewSystem, _previewPlanet)
                    : PickRandomSettlementWithData(game, dangerFilter);

                int lx = settlement.TileRect.CenterX;
                int ly = settlement.TileRect.CenterY;
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;
                game.ChangeState(new PlanetSurfaceState(system, planet, lx, ly));
                break;
            }

            case StartOption.SettlementInside:
            {
                var (system, planet, settlement) = _previewSystem != null && _previewPlanet != null
                    ? GetSettlementData(game, _previewSystem, _previewPlanet)
                    : PickRandomSettlementWithData(game, dangerFilter);

                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;
                game.ChangeState(new InteriorState(InteriorOrigin.Settlement, system, planet: planet, settlement: settlement));
                break;
            }

            default:
            {
                var system = _previewSystem ?? PickRandomSystem(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system));
                break;
            }
        }
    }

    private void LaunchPlanetTypeShowcase(Game game)
    {
        s_reopenDebugMenu = true;
        game.SetWorldGenerator(new PlanetTypeShowcaseWorldGenerator());
        ApplySelectedStartingShip(game);
        var debugSystem = game.GalaxyData[0];
        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.ChangeState(new SolarSystemState(debugSystem));
    }

    private void LaunchStarTypeShowcase(Game game, StarClass starClass)
    {
        s_reopenDebugMenu = true;
        game.SetWorldGenerator(new StarTypeShowcaseWorldGenerator(starClass));
        ApplySelectedStartingShip(game);
        var debugSystem = game.GalaxyData[0];
        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.ChangeState(new SolarSystemState(debugSystem));
    }

    private void LaunchAsteroidShowcase(Game game)
    {
        s_reopenDebugMenu = true;
        game.SetWorldGenerator(new AsteroidMiningShowcaseWorldGenerator());
        ApplySelectedStartingShip(game);
        var debugSystem = game.GalaxyData[0];
        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.ChangeState(new SolarSystemState(debugSystem));
    }

    private void LaunchSurfaceMiningShowcase(Game game)
    {
        s_reopenDebugMenu = true;
        game.SetWorldGenerator(new SurfaceMiningShowcaseWorldGenerator());
        ApplySelectedStartingShip(game);

        var debugSystem = game.GalaxyData[0];
        var planets = game.WorldGenerator.GenerateSolarSystem(game.Seeds, debugSystem).Planets;
        var targetPlanet = planets.FirstOrDefault(p => p.HasSolidSurface) ?? planets[0];

        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
        game.Player.ReturnPlanetIndex = targetPlanet.Index;
        game.ChangeState(new PlanetSurfaceState(debugSystem, targetPlanet));
    }

    private void ApplySelectedStartingShip(Game game)
    {
        var selectedShip = _debugOverlay.SelectedStartingShip;
        if (game.Player.CurrentShipType.Id != selectedShip.Id)
        {
            game.Player.SwitchShipType(selectedShip);
        }

        game.Player.ShipHealth = game.Player.ShipMaxHealth;
        game.Player.ShipFuel = game.Player.ShipMaxFuel;
    }

    private void UpdateStartingShipOverrideLabel()
    {
        var selectedShip = _debugOverlay.SelectedStartingShip;
        _menuOverlay.StartingShipOverrideText = selectedShip.Id == ShipTypeCatalog.StarterShip.Id
            ? null
            : $"Starting ship override: {selectedShip.Name}";
    }

    // ── Preview ──

    private void UpdateLocationPreview(Game game)
    {
        var locationType = _menuOverlay.LocationType;
        int danger = _menuOverlay.DangerFilter;

        switch (locationType)
        {
            case StartOption.StarSystem:
            {
                _previewSystem = PickRandomSystem(game, danger);
                _previewPlanet = null;
                _previewStation = null;
                _menuOverlay.LocationPreview = $"System: {_previewSystem.Name} (Danger {_previewSystem.DangerLevel})\nCoords: ({_previewSystem.GalaxyPosition.X:F0}, {_previewSystem.GalaxyPosition.Y:F0})";
                break;
            }

            case StartOption.Planet:
            {
                var (system, planet) = PickRandomPlanet(game, danger);
                _previewSystem = system;
                _previewPlanet = planet;
                _previewStation = null;
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nPlanet: {planet.Name} ({planet.Type}) [Orbit]";
                break;
            }

            case StartOption.PlanetSurface:
            {
                var (system, planet) = PickRandomPlanet(game, danger);
                _previewSystem = system;
                _previewPlanet = planet;
                _previewStation = null;
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nPlanet: {planet.Name} ({planet.Type})";
                break;
            }

            case StartOption.SpaceStation:
            case StartOption.SpaceStationInside:
            {
                var (system, station) = PickRandomStation(game, danger);
                _previewSystem = system;
                _previewPlanet = null;
                _previewStation = station;
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nStation: {station.Name}";
                break;
            }

            case StartOption.Settlement:
            case StartOption.SettlementInside:
            {
                var (system, planet) = PickRandomSettlement(game, danger);
                _previewSystem = system;
                _previewPlanet = planet;
                _previewStation = null;
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nPlanet: {planet.Name} (Settlement)";
                break;
            }
        }
    }

    private SystemPlanetSettlement GetSettlementData(Game game, StarSystemData system, PlanetData planet)
    {
        var surfaceData = game.WorldGenerator.GeneratePlanetSurface(game.Seeds, system, planet);
        if (surfaceData.Settlements.Count == 0)
            throw new InvalidOperationException($"Planet {planet.Name} has no settlements but was selected as a settlement start");
        return new SystemPlanetSettlement(system, planet, surfaceData.Settlements[0]);
    }

    // ── Filtered random pickers ──

    private List<StarSystemData> GetFilteredSystems(Game game, int dangerFilter)
    {
        if (dangerFilter == 0)
            return game.GalaxyData;
        return game.GalaxyData.Where(s => s.DangerLevel == dangerFilter).ToList();
    }

    private StarSystemData PickRandomSystem(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter);
        if (systems.Count == 0) systems = game.GalaxyData; // fallback if no match
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(1 + _rerollCounter * 7));
        return systems[rng.NextInt(0, systems.Count)];
    }

    private SystemPlanet PickRandomPlanet(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter);
        if (systems.Count == 0) systems = game.GalaxyData;
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(2 + _rerollCounter * 7));

        for (int attempt = 0; attempt < 20; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var planets = game.WorldGenerator.GenerateSolarSystem(game.Seeds, system).Planets;

            var landable = planets.Where(p => p.HasSolidSurface).ToList();
            if (landable.Count > 0)
                return new SystemPlanet(system, landable[rng.NextInt(0, landable.Count)]);
        }

        var fb = systems[0];
        var fbPlanets = game.WorldGenerator.GenerateSolarSystem(game.Seeds, fb).Planets;
        return new SystemPlanet(fb, fbPlanets[0]);
    }

    private SystemStation PickRandomStation(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter).Where(s => s.HasSpaceStation).ToList();
        if (systems.Count == 0) systems = game.GalaxyData.Where(s => s.HasSpaceStation).ToList();
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(3 + _rerollCounter * 7));

        for (int attempt = 0; attempt < 20; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var stations = game.WorldGenerator.GenerateSolarSystem(game.Seeds, system).Stations;

            if (stations.Count > 0)
                return new SystemStation(system, stations[rng.NextInt(0, stations.Count)]);
        }

        foreach (var system in systems)
        {
            var stations = game.WorldGenerator.GenerateSolarSystem(game.Seeds, system).Stations;
            if (stations.Count > 0)
                return new SystemStation(system, stations[0]);
        }

        var fb = game.GalaxyData[0];
        var fbStations = game.WorldGenerator.GenerateSolarSystem(game.Seeds, fb).Stations;
        return new SystemStation(fb, fbStations[0]);
    }

    private SystemPlanet PickRandomSettlement(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter);
        if (systems.Count == 0) systems = game.GalaxyData;
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(4 + _rerollCounter * 7));

        for (int attempt = 0; attempt < 30; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var planets = game.WorldGenerator.GenerateSolarSystem(game.Seeds, system).Planets;

            var settled = planets.Where(p => p.HasSettlement && p.HasSolidSurface).ToList();
            if (settled.Count > 0)
                return new SystemPlanet(system, settled[rng.NextInt(0, settled.Count)]);
        }

        return PickRandomPlanet(game, dangerFilter);
    }

    private SystemPlanetSettlement PickRandomSettlementWithData(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter);
        if (systems.Count == 0) systems = game.GalaxyData;
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(6 + _rerollCounter * 7));

        for (int attempt = 0; attempt < 30; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var planets = game.WorldGenerator.GenerateSolarSystem(game.Seeds, system).Planets;

            var settled = planets.Where(p => p.HasSettlement && p.HasSolidSurface).ToList();
            if (settled.Count > 0)
            {
                var planet = settled[rng.NextInt(0, settled.Count)];
                var surfaceData = game.WorldGenerator.GeneratePlanetSurface(game.Seeds, system, planet);
                if (surfaceData.Settlements.Count > 0)
                {
                    var settlement = surfaceData.Settlements[rng.NextInt(0, surfaceData.Settlements.Count)];
                    return new SystemPlanetSettlement(system, planet, settlement);
                }
            }
        }

        var (fbSystem, fbPlanet) = PickRandomSettlement(game, dangerFilter);
        var fbSurface = game.WorldGenerator.GeneratePlanetSurface(game.Seeds, fbSystem, fbPlanet);
        return new SystemPlanetSettlement(fbSystem, fbPlanet, fbSurface.Settlements[0]);
    }

    // ── Render ──

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;

        SDL.SetRenderDrawColor(game.Renderer, 2, 2, 8, 255);
        SDL.RenderClear(game.Renderer);

        foreach (var (x, y, brightness, speed) in _bgStars)
        {
            float blink = (byte)Math.Clamp(brightness + 30 * MathF.Sin(_animTimer * speed * 2f + x), 20, 200);
            renderer.DrawRectScreen(x, y, 2, 2, new Color3((byte)blink, (byte)blink, (byte)(blink * 0.9f)));
        }

        float centerX = GameConfig.WindowWidth / 2f;
        float centerY = GameConfig.WindowHeight / 2f;
        game.StationRenderer.RenderStation(renderer, _fakeCamera,
            new Vector2(centerX - 600f, centerY - 180f), game.GlobalTime);
        game.StationRenderer.RenderStation(renderer, _fakeCamera,
            new Vector2(centerX + 600f, centerY + 180f), game.GlobalTime + 0.5f);

        // Title
        string title = "SPACE EXPLORATION";
        float titleScale = 4f;
        float titleW = renderer.MeasureText(title, titleScale);
        float titleX = GameConfig.WindowWidth / 2f - titleW / 2f;
        float titleY = _menuOverlay.PanelTop - 80;

        byte glowR = (byte)(120 + 40 * MathF.Sin(_animTimer * 0.8f));
        byte glowG = (byte)(150 + 40 * MathF.Sin(_animTimer * 0.8f + 0.5f));
        byte glowB = (byte)(220 + 35 * MathF.Sin(_animTimer * 0.8f + 1f));
        renderer.DrawTextScreen(titleX, titleY, title, new Color3(glowR, glowG, glowB), titleScale);

        _menuOverlay.Render(game);
        _debugOverlay.Render(game);
    }
}
