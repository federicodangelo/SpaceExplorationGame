using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
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
    PlanetSurfaceOnFoot,
    PlanetSurfaceOnVehicle,
    SpaceStation,
    SpaceStationDocked,
    SpaceStationInside,
    Settlement,
    SettlementOnFoot,
    SettlementOnVehicle,
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
    private readonly DebugLaunchRequest _autoDebugLaunchRequest;
    private readonly StarClass _autoDebugStarType;

    private Camera _fakeCamera = new(GameConfig.WindowWidth, GameConfig.WindowHeight)
    {
        Position = new Vector2(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f),
        Zoom = 1f
    };

    // Preview state for the selected starting location
    private StarSystemData? _previewSystem;
    private PlanetData? _previewPlanet;
    private SpaceStationData? _previewSpaceStation;
    private int _rerollCounter;
    private ulong _lastPreviewSeed;
    private StartOption _lastPreviewedLocationType = StartOption.None;
    private int _lastPreviewedDanger = -1;

    public MainMenuState(
        StartOption autoLaunchOption = StartOption.None,
        DebugLaunchRequest autoDebugLaunchRequest = DebugLaunchRequest.None,
        StarClass autoDebugStarType = StarClass.G)
    {
        _autoLaunchOption = autoLaunchOption;
        _autoDebugLaunchRequest = autoDebugLaunchRequest;
        _autoDebugStarType = autoDebugStarType;
    }

    public override void Enter(Game game)
    {
        game.Player.Reset();
        game.Coordinator.DestroyAll();
        game.Audio.SetMusicTheme(MusicTheme.MainMenu);

        if (_autoDebugLaunchRequest != DebugLaunchRequest.None)
        {
            switch (_autoDebugLaunchRequest)
            {
                case DebugLaunchRequest.StarTypeShowcase:
                    LaunchStarTypeShowcase(game, _autoDebugStarType);
                    break;
                case DebugLaunchRequest.PlanetTypeShowcase:
                    LaunchPlanetTypeShowcase(game);
                    break;
                case DebugLaunchRequest.AsteroidShowcase:
                    LaunchAsteroidShowcase(game);
                    break;
                case DebugLaunchRequest.SurfaceMiningShowcase:
                    LaunchSurfaceMiningShowcase(game);
                    break;
            }
            return;
        }

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

    public override void UpdateInput(Game game)
    {
        // Debug overlay takes priority over main menu input
        if (_debugOverlay.UpdateInput(game))
        {
            switch (_debugOverlay.TakePendingLaunchRequest())
            {
                case DebugLaunchRequest.StarTypeShowcase:
                    game.Audio.PlaySfx(SfxType.MenuSelect);
                    LaunchStarTypeShowcase(game, _debugOverlay.SelectedStarType);
                    break;
                case DebugLaunchRequest.PlanetTypeShowcase:
                    game.Audio.PlaySfx(SfxType.MenuSelect);
                    LaunchPlanetTypeShowcase(game);
                    break;
                case DebugLaunchRequest.AsteroidShowcase:
                    game.Audio.PlaySfx(SfxType.MenuSelect);
                    LaunchAsteroidShowcase(game);
                    break;
                case DebugLaunchRequest.SurfaceMiningShowcase:
                    game.Audio.PlaySfx(SfxType.MenuSelect);
                    LaunchSurfaceMiningShowcase(game);
                    break;
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
        game.UseProceduralUniverseGenerator();
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
            case StartOption.PlanetSurfaceOnFoot:
            case StartOption.PlanetSurfaceOnVehicle:
            {
                var (system, planet) = _previewSystem != null && _previewPlanet != null
                    ? new SystemPlanet(_previewSystem, _previewPlanet)
                    : PickRandomPlanet(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = system.Index;
                if (locationType == StartOption.PlanetSurface)
                {
                    game.ChangeState(new SolarSystemState(system, autoOpenPlanet: planet));
                }
                else
                {
                    var startMode = locationType == StartOption.PlanetSurfaceOnVehicle
                        ? PlanetSurfaceStartMode.OnVehicle
                        : PlanetSurfaceStartMode.OnFoot;
                    game.ChangeState(new PlanetSurfaceState(system, planet, landingDelay: 0, startMode: startMode));
                }
                break;
            }

            case StartOption.SpaceStation:
            {
                var (starSystem, spaceStation) = _previewSystem != null && _previewSpaceStation != null
                    ? new SystemSpaceStation(_previewSystem, _previewSpaceStation)
                    : PickRandomSpaceStation(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = starSystem.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
                game.Player.ReturnSpaceStationIndex = spaceStation.Index;
                game.ChangeState(new SolarSystemState(starSystem));
                break;
            }

            case StartOption.SpaceStationDocked:
            {
                var (starSystem, spaceStation) = _previewSystem != null && _previewSpaceStation != null
                    ? new SystemSpaceStation(_previewSystem, _previewSpaceStation)
                    : PickRandomSpaceStation(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = starSystem.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
                game.Player.ReturnSpaceStationIndex = spaceStation.Index;
                game.ChangeState(new SolarSystemState(starSystem, spaceStation));
                break;
            }

            case StartOption.SpaceStationInside:
            {
                var (starSystem, spaceStation) = _previewSystem != null && _previewSpaceStation != null
                    ? new SystemSpaceStation(_previewSystem, _previewSpaceStation)
                    : PickRandomSpaceStation(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = starSystem.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromSpaceStation;
                game.Player.ReturnSpaceStationIndex = spaceStation.Index;
                game.ChangeState(new InteriorState(InteriorOrigin.SpaceStation, starSystem, spaceStation: spaceStation));
                break;
            }

            case StartOption.Settlement:
            case StartOption.SettlementOnFoot:
            case StartOption.SettlementOnVehicle:
            {
                var (starSystem, planet, settlement) = _previewSystem != null && _previewPlanet != null
                    ? GetSettlementData(game, _previewSystem, _previewPlanet)
                    : PickRandomSettlementWithData(game, dangerFilter);

                int lx = settlement.TileRect.CenterX;
                int ly = settlement.TileRect.CenterY;
                game.Player.CurrentStarSystemIndex = starSystem.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;
                var startMode = locationType switch
                {
                    StartOption.SettlementOnFoot => PlanetSurfaceStartMode.OnFoot,
                    StartOption.SettlementOnVehicle => PlanetSurfaceStartMode.OnVehicle,
                    _ => PlanetSurfaceStartMode.InShip
                };
                game.ChangeState(new PlanetSurfaceState(starSystem, planet, lx, ly, landingDelay: 0, startMode: startMode));
                break;
            }

            case StartOption.SettlementInside:
            {
                var (starSystem, planet, settlement) = _previewSystem != null && _previewPlanet != null
                    ? GetSettlementData(game, _previewSystem, _previewPlanet)
                    : PickRandomSettlementWithData(game, dangerFilter);

                game.Player.CurrentStarSystemIndex = starSystem.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;
                game.ChangeState(new InteriorState(InteriorOrigin.Settlement, starSystem, planet: planet, settlement: settlement));
                break;
            }

            default:
            {
                var starSystem = _previewSystem ?? PickRandomSystem(game, dangerFilter);
                game.Player.CurrentStarSystemIndex = starSystem.Index;
                game.ChangeState(new SolarSystemState(starSystem));
                break;
            }
        }
    }

    private void LaunchPlanetTypeShowcase(Game game)
    {
        s_reopenDebugMenu = true;
        game.SetUniverseGenerator(new PlanetTypeShowcaseUniverseGenerator(game.Seeds));
        ApplySelectedStartingShip(game);
        var debugSystem = game.GalaxyData[0];
        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.ChangeState(new SolarSystemState(debugSystem));
    }

    private void LaunchStarTypeShowcase(Game game, StarClass starClass)
    {
        s_reopenDebugMenu = true;
        game.SetUniverseGenerator(new StarTypeShowcaseUniverseGenerator(game.Seeds, starClass));
        ApplySelectedStartingShip(game);
        var debugSystem = game.GalaxyData[0];
        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.ChangeState(new SolarSystemState(debugSystem));
    }

    private void LaunchAsteroidShowcase(Game game)
    {
        s_reopenDebugMenu = true;
        game.SetUniverseGenerator(new AsteroidMiningShowcaseUniverseGenerator(game.Seeds));
        ApplySelectedStartingShip(game);
        var debugSystem = game.GalaxyData[0];
        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.ChangeState(new SolarSystemState(debugSystem));
    }

    private void LaunchSurfaceMiningShowcase(Game game)
    {
        s_reopenDebugMenu = true;
        game.SetUniverseGenerator(new SurfaceMiningShowcaseUniverseGenerator(game.Seeds));
        ApplySelectedStartingShip(game);

        var debugSystem = game.GalaxyData[0];
        var planets = game.UniverseGenerator.GenerateSolarSystem(debugSystem).Planets;
        var targetPlanet = planets.FirstOrDefault(p => p.HasSolidSurface) ?? planets[0];

        game.Player.CurrentStarSystemIndex = debugSystem.Index;
        game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
        game.Player.ReturnPlanetIndex = targetPlanet.Index;
        game.ChangeState(new PlanetSurfaceState(debugSystem, targetPlanet, landingDelay: 0, startMode: PlanetSurfaceStartMode.OnFoot));
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
                _previewSpaceStation = null;
                _menuOverlay.LocationPreview = $"System: {_previewSystem.Name} (Danger {_previewSystem.DangerLevel})\nCoords: ({_previewSystem.GalaxyPosition.X:F0}, {_previewSystem.GalaxyPosition.Y:F0})";
                break;
            }

            case StartOption.Planet:
            {
                var (system, planet) = PickRandomPlanet(game, danger);
                _previewSystem = system;
                _previewPlanet = planet;
                _previewSpaceStation = null;
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nPlanet: {planet.Name} ({planet.Type}) [Orbit]";
                break;
            }

            case StartOption.PlanetSurface:
            case StartOption.PlanetSurfaceOnFoot:
            case StartOption.PlanetSurfaceOnVehicle:
            {
                var (system, planet) = PickRandomPlanet(game, danger);
                _previewSystem = system;
                _previewPlanet = planet;
                _previewSpaceStation = null;
                string planetMode = locationType switch
                {
                    StartOption.PlanetSurfaceOnFoot => " [On Foot]",
                    StartOption.PlanetSurfaceOnVehicle => " [On Vehicle]",
                    _ => " [Landed]"
                };
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nPlanet: {planet.Name} ({planet.Type}){planetMode}";
                break;
            }

            case StartOption.SpaceStation:
            case StartOption.SpaceStationDocked:
            case StartOption.SpaceStationInside:
            {
                var (system, station) = PickRandomSpaceStation(game, danger);
                _previewSystem = system;
                _previewPlanet = null;
                _previewSpaceStation = station;
                string stationMode = locationType switch
                {
                    StartOption.SpaceStation => " [Orbit]",
                    StartOption.SpaceStationDocked => " [Docked]",
                    _ => " [Interior]"
                };
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nSpace Station: {station.Name}{stationMode}";
                break;
            }

            case StartOption.Settlement:
            case StartOption.SettlementOnFoot:
            case StartOption.SettlementOnVehicle:
            case StartOption.SettlementInside:
            {
                var (system, planet) = PickRandomSettlement(game, danger);
                _previewSystem = system;
                _previewPlanet = planet;
                _previewSpaceStation = null;
                string settlementMode = locationType switch
                {
                    StartOption.Settlement => " [Above]",
                    StartOption.SettlementInside => " [Inside]",
                    StartOption.SettlementOnFoot => " [On Foot]",
                    _ => " [On Vehicle]"
                };
                _menuOverlay.LocationPreview = $"System: {system.Name} (Danger {system.DangerLevel})\nPlanet: {planet.Name} ({planet.Type}) (Settlement){settlementMode}";
                break;
            }
        }
    }

    private SystemPlanetSettlement GetSettlementData(Game game, StarSystemData system, PlanetData planet)
    {
        var surfaceData = game.UniverseGenerator.GeneratePlanetSurface(system, planet);
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
            var planets = game.UniverseGenerator.GenerateSolarSystem(system).Planets;

            var landable = planets.Where(p => p.HasSolidSurface).ToList();
            if (landable.Count > 0)
                return new SystemPlanet(system, landable[rng.NextInt(0, landable.Count)]);
        }

        var fb = systems[0];
        var fbPlanets = game.UniverseGenerator.GenerateSolarSystem(fb).Planets;
        return new SystemPlanet(fb, fbPlanets[0]);
    }

    private SystemSpaceStation PickRandomSpaceStation(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter).Where(s => s.HasSpaceStation).ToList();
        if (systems.Count == 0) systems = game.GalaxyData.Where(s => s.HasSpaceStation).ToList();
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(3 + _rerollCounter * 7));

        for (int attempt = 0; attempt < 20; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var stations = game.UniverseGenerator.GenerateSolarSystem(system).SpaceStations;

            if (stations.Count > 0)
                return new SystemSpaceStation(system, stations[rng.NextInt(0, stations.Count)]);
        }

        foreach (var system in systems)
        {
            var stations = game.UniverseGenerator.GenerateSolarSystem(system).SpaceStations;
            if (stations.Count > 0)
                return new SystemSpaceStation(system, stations[0]);
        }

        var fb = game.GalaxyData[0];
        var fbStations = game.UniverseGenerator.GenerateSolarSystem(fb).SpaceStations;
        return new SystemSpaceStation(fb, fbStations[0]);
    }

    private SystemPlanet PickRandomSettlement(Game game, int dangerFilter)
    {
        var systems = GetFilteredSystems(game, dangerFilter);
        if (systems.Count == 0) systems = game.GalaxyData;
        var rng = new SeededRandom(game.Seeds.GalaxySeed ^ (ulong)(4 + _rerollCounter * 7));

        for (int attempt = 0; attempt < 30; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var planets = game.UniverseGenerator.GenerateSolarSystem(system).Planets;

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
            var planets = game.UniverseGenerator.GenerateSolarSystem(system).Planets;

            var settled = planets.Where(p => p.HasSettlement && p.HasSolidSurface).ToList();
            if (settled.Count > 0)
            {
                var planet = settled[rng.NextInt(0, settled.Count)];
                var surfaceData = game.UniverseGenerator.GeneratePlanetSurface(system, planet);
                if (surfaceData.Settlements.Count > 0)
                {
                    var settlement = surfaceData.Settlements[rng.NextInt(0, surfaceData.Settlements.Count)];
                    return new SystemPlanetSettlement(system, planet, settlement);
                }
            }
        }

        var (fbSystem, fbPlanet) = PickRandomSettlement(game, dangerFilter);
        var fbSurface = game.UniverseGenerator.GeneratePlanetSurface(fbSystem, fbPlanet);
        return new SystemPlanetSettlement(fbSystem, fbPlanet, fbSurface.Settlements[0]);
    }

    // ── Render ──

    public override void RenderGame(Game game)
    {
        var renderer = game.SpriteRenderer;

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
    }

    public override void RenderHud(Game game)
    {
        var renderer = game.SpriteRenderer;

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
