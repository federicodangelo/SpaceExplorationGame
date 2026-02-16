using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.UI;

namespace SpaceExplorationGame.States;

public enum StartOption
{
    None = -1,
    GalaxyMap,
    StarSystem ,
    PlanetSurface,
    SpaceStation,
    SpaceStationInside,
    Settlement,
    SettlementInside
}

/// <summary>
/// Main menu: lets the player choose where to start the game.
/// </summary>
public class MainMenuState : GameState
{
    public override GameStateType Type => GameStateType.MainMenu;

    private float _animTimer = 0f;

    // Background stars for visual flair
    private List<AnimatedStar> _bgStars = [];

    private static readonly MenuOption<StartOption>[] MenuOptions =
    [
        new(StartOption.StarSystem, "STAR SYSTEM", "Start inside a random star system, ready to explore"),
        new(StartOption.GalaxyMap, "GALAXY MAP", "Begin at the galaxy overview and choose your destination"),
        new(StartOption.SpaceStation, "SPACE STATION", "Dock at a random space station"),
        new(StartOption.SpaceStationInside, "INSIDE SPACE STATION", "Walk around inside a random space station"),
        new(StartOption.PlanetSurface, "PLANET SURFACE", "Land directly on a random planet's surface"),
        new(StartOption.Settlement, "SETTLEMENT", "Start at a settlement on an inhabited planet"),
        new(StartOption.SettlementInside, "INSIDE SETTLEMENT", "Walk around inside a random settlement")
    ];

    private readonly MenuWidget<StartOption> _menu = new(MenuOptions)
    {
        CenterAlign = true,
        ItemHeight = 50f,
        SelectedScale = 2.5f,
        NormalScale = 2f,
        SelectedColor = new Color3(220, 240, 255),
        NormalColor = new Color3(140, 140, 160),
        HighlightBg = new Color3(40, 60, 120),
        HighlightAlpha = 180,
        DescriptionScale = 1.5f,
        DescriptionColor = new Color3(160, 160, 180)
    };

    // Auto-launch: if not None, skip menu and launch this option immediately
    private readonly StartOption _autoLaunchOption;

    public MainMenuState(StartOption autoLaunchOption = StartOption.None)
    {
        _autoLaunchOption = autoLaunchOption;
    }

    public override void Enter(Game game)
    {
        // Reset player data for a fresh start
        game.Player.Reset();

        // Auto-launch if requested (from command line)
        if (_autoLaunchOption != StartOption.None)
        {
            LaunchOption(game, _autoLaunchOption);
            return;
        }
        // Generate background stars
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
        var input = game.Input;

        float menuTotalHeight = MenuOptions.Length * _menu.ItemHeight;
        float menuStartY = (GameConfig.WindowHeight - menuTotalHeight) / 2f;
        float centerX = GameConfig.WindowWidth / 2f;
        float menuW = 420f;

        var confirmed = _menu.Update(input, centerX - menuW / 2f, menuStartY, menuW);
        if (confirmed is { } option)
            LaunchOption(game, option);
    }

    public override void Update(Game game, float dt)
    {
        _animTimer += dt;
    }

    private void LaunchOption(Game game, StartOption option)
    {
        switch (option)
        {
            case StartOption.GalaxyMap:
            {
                // Start in a random solar system with the galaxy map overlay auto-opened
                var system = PickRandomSystem(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system, autoOpenGalaxyMap: true));
                break;
            }

            case StartOption.StarSystem:
            {
                var system = PickRandomSystem(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system));
                break;
            }

            case StartOption.PlanetSurface:
            {
                var (system, planet) = PickRandomPlanet(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system, autoOpenPlanet: planet));
                break;
            }

            case StartOption.SpaceStation:
            {
                var (system, station) = PickRandomStation(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = station.Index;
                game.ChangeState(new SolarSystemState(system, station));
                break;
            }

            case StartOption.SpaceStationInside:
            {
                var (system, station) = PickRandomStation(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = station.Index;
                game.ChangeState(new InteriorState(
                    InteriorOrigin.Station, system, station: station));
                break;
            }

            case StartOption.Settlement:
            {
                var (system, planet) = PickRandomSettlement(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;

                // Generate surface to find a settlement position
                var surfRng = game.Seeds.GetPlanetSurfaceRandom(system.Index, planet.Index);
                var surfaceData = PlanetSurfaceGenerator.Generate(surfRng, planet);
                int lx = surfaceData.Width / 2;
                int ly = surfaceData.Height / 2;
                if (surfaceData.Settlements.Count > 0)
                {
                    var s = surfaceData.Settlements[0];
                    lx = s.TileRect.X + s.TileRect.Width / 2;
                    ly = s.TileRect.Y + s.TileRect.Height / 2;
                }
                game.ChangeState(new PlanetSurfaceState(system, planet, lx, ly));
                break;
            }

            case StartOption.SettlementInside:
            {
                var (system, planet, settlement) = PickRandomSettlementWithData(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromPlanet;
                game.Player.ReturnPlanetIndex = planet.Index;
                game.ChangeState(new InteriorState(
                    InteriorOrigin.Settlement, system, planet: planet, settlement: settlement));
                break;
            }
        }
    }

    private StarSystemData PickRandomSystem(Game game)
    {
        var galaxyRng = game.Seeds.GetGalaxyRandom();
        var systems = GalaxyGenerator.Generate(galaxyRng);
        var rng = new SeededRandom((ulong)(game.GlobalTime * 1000 + 1));
        return systems[rng.NextInt(0, systems.Count)];
    }

    private SystemPlanet PickRandomPlanet(Game game)
    {
        var galaxyRng = game.Seeds.GetGalaxyRandom();
        var systems = GalaxyGenerator.Generate(galaxyRng);
        var rng = new SeededRandom((ulong)(game.GlobalTime * 1000 + 2));

        // Try to find a system with a solid-surface planet
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var sysRng = game.Seeds.GetStarSystemRandom(system.Index);
            var (planets, _, _) = SolarSystemGenerator.Generate(sysRng, system);

            var landable = planets.Where(p => p.HasSolidSurface).ToList();
            if (landable.Count > 0)
            {
                return new SystemPlanet(system, landable[rng.NextInt(0, landable.Count)]);
            }
        }

        // Fallback: first system, first planet
        var fallbackSystem = systems[0];
        var fallbackRng = game.Seeds.GetStarSystemRandom(fallbackSystem.Index);
        var (fallbackPlanets, _, _) = SolarSystemGenerator.Generate(fallbackRng, fallbackSystem);
        return new SystemPlanet(fallbackSystem, fallbackPlanets[0]);
    }

    private SystemStation PickRandomStation(Game game)
    {
        var galaxyRng = game.Seeds.GetGalaxyRandom();
        var systems = GalaxyGenerator.Generate(galaxyRng);
        var rng = new SeededRandom((ulong)(game.GlobalTime * 1000 + 3));

        // Find a system with a station
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            if (!system.HasSpaceStation) continue;

            var sysRng = game.Seeds.GetStarSystemRandom(system.Index);
            var (_, _, stations) = SolarSystemGenerator.Generate(sysRng, system);

            if (stations.Count > 0)
            {
                return new SystemStation(system, stations[rng.NextInt(0, stations.Count)]);
            }
        }

        // Fallback: find any system with a station
        foreach (var system in systems.Where(s => s.HasSpaceStation))
        {
            var sysRng = game.Seeds.GetStarSystemRandom(system.Index);
            var (_, _, stations) = SolarSystemGenerator.Generate(sysRng, system);
            if (stations.Count > 0)
                return new SystemStation(system, stations[0]);
        }

        // Last resort: first system
        var fb = systems[0];
        var fbRng = game.Seeds.GetStarSystemRandom(fb.Index);
        var (_, _, fbStations) = SolarSystemGenerator.Generate(fbRng, fb);
        return new SystemStation(fb, fbStations[0]);
    }

    private SystemPlanet PickRandomSettlement(Game game)
    {
        var galaxyRng = game.Seeds.GetGalaxyRandom();
        var systems = GalaxyGenerator.Generate(galaxyRng);
        var rng = new SeededRandom((ulong)(game.GlobalTime * 1000 + 4));

        // Find a planet with settlements
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var sysRng = game.Seeds.GetStarSystemRandom(system.Index);
            var (planets, _, _) = SolarSystemGenerator.Generate(sysRng, system);

            var settled = planets.Where(p => p.HasSettlement && p.HasSolidSurface).ToList();
            if (settled.Count > 0)
            {
                return new SystemPlanet(system, settled[rng.NextInt(0, settled.Count)]);
            }
        }

        // Fallback: any solid planet
        return PickRandomPlanet(game);
    }

    private SystemPlanetSettlement PickRandomSettlementWithData(Game game)
    {
        var galaxyRng = game.Seeds.GetGalaxyRandom();
        var systems = GalaxyGenerator.Generate(galaxyRng);
        var rng = new SeededRandom((ulong)(game.GlobalTime * 1000 + 6));

        // Find a planet with settlements and return the settlement data
        for (int attempt = 0; attempt < 30; attempt++)
        {
            var system = systems[rng.NextInt(0, systems.Count)];
            var sysRng = game.Seeds.GetStarSystemRandom(system.Index);
            var (planets, _, _) = SolarSystemGenerator.Generate(sysRng, system);

            var settled = planets.Where(p => p.HasSettlement && p.HasSolidSurface).ToList();
            if (settled.Count > 0)
            {
                var planet = settled[rng.NextInt(0, settled.Count)];
                var surfRng = game.Seeds.GetPlanetSurfaceRandom(system.Index, planet.Index);
                var surfaceData = PlanetSurfaceGenerator.Generate(surfRng, planet);
                if (surfaceData.Settlements.Count > 0)
                {
                    var settlement = surfaceData.Settlements[rng.NextInt(0, surfaceData.Settlements.Count)];
                    return new SystemPlanetSettlement(system, planet, settlement);
                }
            }
        }

        // Fallback: use PickRandomSettlement and generate surface to get settlement data
        var (fbSystem, fbPlanet) = PickRandomSettlement(game);
        var fbSurfRng = game.Seeds.GetPlanetSurfaceRandom(fbSystem.Index, fbPlanet.Index);
        var fbSurface = PlanetSurfaceGenerator.Generate(fbSurfRng, fbPlanet);
        return new SystemPlanetSettlement(fbSystem, fbPlanet, fbSurface.Settlements[0]);
    }

    public override void Render(Game game)
    {
        var renderer = game.SpriteRenderer;

        // Dark space background
        SDL.SetRenderDrawColor(game.Renderer, 2, 2, 8, 255);
        SDL.RenderClear(game.Renderer);

        // Animated background stars
        foreach (var (x, y, brightness, speed) in _bgStars)
        {
            float blink = (byte)Math.Clamp(brightness + 30 * MathF.Sin(_animTimer * speed * 2f + x), 20, 200);
            renderer.DrawRectScreen(x, y, 2, 2, new Color3((byte)blink, (byte)blink, (byte)(blink * 0.9f)));
        }

        // Title
        string title = "SPACE EXPLORATION";
        float titleScale = 4f;
        float titleW = renderer.MeasureText(title, titleScale);
        float titleX = GameConfig.WindowWidth / 2f - titleW / 2f;

        float menuTotalHeight = MenuOptions.Length * _menu.ItemHeight;
        float menuStartY = (GameConfig.WindowHeight - menuTotalHeight) / 2f;
        float titleY = menuStartY - 100;

        // Title glow effect
        byte glowR = (byte)(120 + 40 * MathF.Sin(_animTimer * 0.8f));
        byte glowG = (byte)(150 + 40 * MathF.Sin(_animTimer * 0.8f + 0.5f));
        byte glowB = (byte)(220 + 35 * MathF.Sin(_animTimer * 0.8f + 1f));
        renderer.DrawTextScreen(titleX, titleY, title, new Color3(glowR, glowG, glowB), titleScale);

        // Subtitle
        string subtitle = "CHOOSE YOUR STARTING POINT";
        float subtitleScale = 1.8f;
        float subtitleW = renderer.MeasureText(subtitle, subtitleScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - subtitleW / 2f, titleY + 50,
            subtitle, new Color3(120, 120, 140), subtitleScale);

        // Menu options
        float centerX = GameConfig.WindowWidth / 2f;
        float menuW = 420f;

        _menu.Render(renderer, centerX - menuW / 2f, menuStartY, menuW);

        // Bottom info
        string seedInfo = $"SEED: {game.Seeds.GalaxySeed}";
        float seedScale = 1.3f;
        renderer.DrawTextScreen(10, GameConfig.WindowHeight - 25, seedInfo, new Color3(80, 80, 100), seedScale);

        // Controls hint
        string controls = "UP/DOWN: SELECT   ENTER: CONFIRM";
        float ctrlScale = 1.3f;
        float ctrlW = renderer.MeasureText(controls, ctrlScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth - ctrlW - 10, GameConfig.WindowHeight - 25,
            controls, new Color3(80, 80, 100), ctrlScale);
    }
}
