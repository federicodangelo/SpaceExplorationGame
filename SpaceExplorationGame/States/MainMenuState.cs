using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame.States;

/// <summary>
/// Main menu: lets the player choose where to start the game.
/// </summary>
public class MainMenuState : GameState
{
    public override GameStateType Type => GameStateType.MainMenu;

    private int _selectedOption = 0;
    private float _animTimer = 0f;

    // Background stars for visual flair
    private List<(float X, float Y, byte Brightness, float Speed)> _bgStars = [];

    private static readonly string[] MenuOptions =
    [
        "GALAXY MAP",
        "STAR SYSTEM",
        "PLANET SURFACE",
        "SPACE STATION",
        "SETTLEMENT"
    ];

    private static readonly string[] MenuDescriptions =
    [
        "Begin at the galaxy overview and choose your destination",
        "Start inside a random star system, ready to explore",
        "Land directly on a random planet's surface",
        "Dock at a random space station",
        "Start at a settlement on an inhabited planet"
    ];

    // Auto-launch: if >= 0, skip menu and launch this option immediately
    private readonly int _autoLaunchOption;

    public MainMenuState(int autoLaunchOption = -1)
    {
        _autoLaunchOption = autoLaunchOption;
    }

    public override void Enter(Game game)
    {
        // Auto-launch if requested (from command line)
        if (_autoLaunchOption >= 0 && _autoLaunchOption < MenuOptions.Length)
        {
            LaunchOption(game, _autoLaunchOption);
            return;
        }
        // Generate background stars
        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            _bgStars.Add((
                rng.Next(0, GameConfig.WindowWidth),
                rng.Next(0, GameConfig.WindowHeight),
                (byte)rng.Next(40, 160),
                0.2f + (float)rng.NextDouble() * 0.8f
            ));
        }
    }

    public override void Exit(Game game) { }

    public override void HandleEvent(Game game, SDL.Event e) { }

    public override void Update(Game game, float dt)
    {
        var input = game.Input;
        _animTimer += dt;

        // Navigate menu
        if (input.IsKeyPressed(SDL.Scancode.Up) || input.IsKeyPressed(SDL.Scancode.W))
            _selectedOption = (_selectedOption - 1 + MenuOptions.Length) % MenuOptions.Length;

        if (input.IsKeyPressed(SDL.Scancode.Down) || input.IsKeyPressed(SDL.Scancode.S))
            _selectedOption = (_selectedOption + 1) % MenuOptions.Length;

        // Confirm selection
        if (input.IsKeyPressed(SDL.Scancode.Return) || input.IsKeyPressed(SDL.Scancode.E))
        {
            LaunchOption(game, _selectedOption);
        }

        // Mouse click on options
        if (input.IsMousePressed(1))
        {
            float menuStartY = GameConfig.WindowHeight / 2f - 40;
            float mouseY = input.MouseY;
            float mouseX = input.MouseX;
            float centerX = GameConfig.WindowWidth / 2f;

            for (int i = 0; i < MenuOptions.Length; i++)
            {
                float optY = menuStartY + i * 50;
                float optW = 400;
                if (mouseX >= centerX - optW / 2 && mouseX <= centerX + optW / 2 &&
                    mouseY >= optY - 5 && mouseY <= optY + 30)
                {
                    _selectedOption = i;
                    LaunchOption(game, i);
                    return;
                }
            }
        }

        // Mouse hover
        if (true)
        {
            float menuStartY = GameConfig.WindowHeight / 2f - 40;
            float mouseY = input.MouseY;
            float mouseX = input.MouseX;
            float centerX = GameConfig.WindowWidth / 2f;

            for (int i = 0; i < MenuOptions.Length; i++)
            {
                float optY = menuStartY + i * 50;
                float optW = 400;
                if (mouseX >= centerX - optW / 2 && mouseX <= centerX + optW / 2 &&
                    mouseY >= optY - 5 && mouseY <= optY + 30)
                {
                    _selectedOption = i;
                    break;
                }
            }
        }
    }

    private void LaunchOption(Game game, int option)
    {
        switch (option)
        {
            case 0: // Galaxy Map
                game.ChangeState(new GalaxyMapState());
                break;

            case 1: // Star System
            {
                var system = PickRandomSystem(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new SolarSystemState(system));
                break;
            }

            case 2: // Planet Surface
            {
                var (system, planet) = PickRandomPlanet(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.ChangeState(new PlanetLandingState(system, planet));
                break;
            }

            case 3: // Space Station
            {
                var (system, station) = PickRandomStation(game);
                game.Player.CurrentStarSystemIndex = system.Index;
                game.Player.SolarSystemReturnContext = PlayerData.ReturnContext.FromStation;
                game.Player.ReturnStationIndex = station.Index;
                game.ChangeState(new SpaceStationState(system, station));
                break;
            }

            case 4: // Settlement
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
                    lx = s.TileX + s.Width / 2;
                    ly = s.TileY + s.Height / 2;
                }
                game.ChangeState(new PlanetSurfaceState(system, planet, lx, ly));
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

    private (StarSystemData System, PlanetData Planet) PickRandomPlanet(Game game)
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
                return (system, landable[rng.NextInt(0, landable.Count)]);
            }
        }

        // Fallback: first system, first planet
        var fallbackSystem = systems[0];
        var fallbackRng = game.Seeds.GetStarSystemRandom(fallbackSystem.Index);
        var (fallbackPlanets, _, _) = SolarSystemGenerator.Generate(fallbackRng, fallbackSystem);
        return (fallbackSystem, fallbackPlanets[0]);
    }

    private (StarSystemData System, SpaceStationData Station) PickRandomStation(Game game)
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
                return (system, stations[rng.NextInt(0, stations.Count)]);
            }
        }

        // Fallback: find any system with a station
        foreach (var system in systems.Where(s => s.HasSpaceStation))
        {
            var sysRng = game.Seeds.GetStarSystemRandom(system.Index);
            var (_, _, stations) = SolarSystemGenerator.Generate(sysRng, system);
            if (stations.Count > 0)
                return (system, stations[0]);
        }

        // Last resort: first system
        var fb = systems[0];
        var fbRng = game.Seeds.GetStarSystemRandom(fb.Index);
        var (_, _, fbStations) = SolarSystemGenerator.Generate(fbRng, fb);
        return (fb, fbStations[0]);
    }

    private (StarSystemData System, PlanetData Planet) PickRandomSettlement(Game game)
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
                return (system, settled[rng.NextInt(0, settled.Count)]);
            }
        }

        // Fallback: any solid planet
        return PickRandomPlanet(game);
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
            renderer.DrawRectScreen(x, y, 2, 2, (byte)blink, (byte)blink, (byte)(blink * 0.9f));
        }

        // Title
        string title = "SPACE EXPLORATION";
        float titleScale = 4f;
        float titleW = renderer.MeasureText(title, titleScale);
        float titleX = GameConfig.WindowWidth / 2f - titleW / 2f;
        float titleY = 140;

        // Title glow effect
        byte glowR = (byte)(120 + 40 * MathF.Sin(_animTimer * 0.8f));
        byte glowG = (byte)(150 + 40 * MathF.Sin(_animTimer * 0.8f + 0.5f));
        byte glowB = (byte)(220 + 35 * MathF.Sin(_animTimer * 0.8f + 1f));
        renderer.DrawTextScreen(titleX, titleY, title, glowR, glowG, glowB, titleScale);

        // Subtitle
        string subtitle = "CHOOSE YOUR STARTING POINT";
        float subtitleScale = 1.8f;
        float subtitleW = renderer.MeasureText(subtitle, subtitleScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth / 2f - subtitleW / 2f, titleY + 50,
            subtitle, 120, 120, 140, subtitleScale);

        // Menu options
        float menuStartY = GameConfig.WindowHeight / 2f - 40;
        float centerX = GameConfig.WindowWidth / 2f;

        for (int i = 0; i < MenuOptions.Length; i++)
        {
            float optY = menuStartY + i * 50;
            bool isSelected = i == _selectedOption;

            // Selection background
            float bgW = 420;
            if (isSelected)
            {
                float pulse = 0.7f + 0.3f * MathF.Sin(_animTimer * 4f);
                byte bgAlpha = (byte)(100 * pulse);
                renderer.DrawRectScreen(centerX - bgW / 2f, optY - 5, bgW, 35, 40, 60, 120, bgAlpha);

                // Selection indicator
                float arrowX = centerX - bgW / 2f - 20;
                renderer.DrawTextScreen(arrowX, optY + 2, ">", 100, 200, 255, 2.5f);
            }

            // Option text
            float optScale = isSelected ? 2.5f : 2f;
            byte optR = isSelected ? (byte)220 : (byte)140;
            byte optG = isSelected ? (byte)240 : (byte)140;
            byte optB = isSelected ? (byte)255 : (byte)160;

            float optW = renderer.MeasureText(MenuOptions[i], optScale);
            renderer.DrawTextScreen(centerX - optW / 2f, optY, MenuOptions[i], optR, optG, optB, optScale);
        }

        // Description for selected option
        float descY = menuStartY + MenuOptions.Length * 50 + 30;
        string desc = MenuDescriptions[_selectedOption];
        float descScale = 1.5f;
        float descW = renderer.MeasureText(desc, descScale);
        renderer.DrawRectScreen(centerX - descW / 2f - 8, descY - 4, descW + 16, 22, 0, 0, 0, 160);
        renderer.DrawTextScreen(centerX - descW / 2f, descY, desc, 160, 160, 180, descScale);

        // Bottom info
        string seedInfo = $"SEED: {game.Seeds.GalaxySeed}";
        float seedScale = 1.3f;
        renderer.DrawTextScreen(10, GameConfig.WindowHeight - 25, seedInfo, 80, 80, 100, seedScale);

        // Controls hint
        string controls = "UP/DOWN: SELECT   ENTER: CONFIRM";
        float ctrlScale = 1.3f;
        float ctrlW = renderer.MeasureText(controls, ctrlScale);
        renderer.DrawTextScreen(GameConfig.WindowWidth - ctrlW - 10, GameConfig.WindowHeight - 25,
            controls, 80, 80, 100, ctrlScale);
    }
}
