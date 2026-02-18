using System.Numerics;
using SDL3;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.UI;
using SpaceExplorationGame.UI.Overlays.Menu;

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

    private readonly MainMenuOverlay _menuOverlay = new();

    // Auto-launch: if not None, skip menu and launch this option immediately
    private readonly StartOption _autoLaunchOption;

    private Camera _fakeCamera = new(GameConfig.WindowWidth, GameConfig.WindowHeight)
    {
        Position = new Vector2(GameConfig.WindowWidth / 2f, GameConfig.WindowHeight / 2f),
        Zoom = 1f
    };

    public MainMenuState(StartOption autoLaunchOption = StartOption.None)
    {
        _autoLaunchOption = autoLaunchOption;
    }

    public override void Enter(Game game)
    {
        // Reset player data for a fresh start
        game.Player.Reset();

        // Music
        game.Audio.SetMusicTheme(MusicTheme.MainMenu);

        // Auto-launch if requested (from command line)
        if (_autoLaunchOption != StartOption.None)
        {
            LaunchOption(game, _autoLaunchOption);
            return;
        }

        // Open the menu overlay
        _menuOverlay.Open();

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
        _menuOverlay.UpdateInput(game);

        if (_menuOverlay.SelectedOption is { } option)
        {
            game.Audio.PlaySfx(SfxType.MenuSelect);
            _menuOverlay.SelectedOption = null;
            LaunchOption(game, option);
        }
    }

    public override void Update(Game game, float dt)
    {
        _animTimer += dt;
        _menuOverlay.Update(game, dt);
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
        var systems = game.GalaxyData;
        var rng = new SeededRandom((ulong)(game.GlobalTime * 1000 + 1));
        return systems[rng.NextInt(0, systems.Count)];
    }

    private SystemPlanet PickRandomPlanet(Game game)
    {
        var systems = game.GalaxyData;
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
        var systems = game.GalaxyData;
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
        var systems = game.GalaxyData;
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
        var systems = game.GalaxyData;
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

        // --- Space stations in background ---
        float centerX = GameConfig.WindowWidth / 2f;
        float centerY = GameConfig.WindowHeight / 2f;
        var spaceStation1pos = new Vector2(centerX - 600f, centerY - 180f);
        var spaceStation2pos = new Vector2(centerX + 600f, centerY + 180f);
        game.StationRenderer.RenderStation(
            renderer,
            _fakeCamera,
            spaceStation1pos,
            game.GlobalTime
        );
        game.StationRenderer.RenderStation(
            renderer,
            _fakeCamera,
            spaceStation2pos,
            game.GlobalTime + 0.5f
        );

        // Title
        string title = "SPACE EXPLORATION";
        float titleScale = 4f;
        float titleW = renderer.MeasureText(title, titleScale);
        float titleX = GameConfig.WindowWidth / 2f - titleW / 2f;
        float titleY = _menuOverlay.PanelTop - 80;

        // Title glow effect
        byte glowR = (byte)(120 + 40 * MathF.Sin(_animTimer * 0.8f));
        byte glowG = (byte)(150 + 40 * MathF.Sin(_animTimer * 0.8f + 0.5f));
        byte glowB = (byte)(220 + 35 * MathF.Sin(_animTimer * 0.8f + 1f));
        renderer.DrawTextScreen(titleX, titleY, title, new Color3(glowR, glowG, glowB), titleScale);

        // Menu overlay (renders the panel with options)
        _menuOverlay.Render(game);

        // Bottom info
        string seedInfo = $"SEED: {game.Seeds.GalaxySeed}";
        float seedScale = 1.3f;
        renderer.DrawTextScreen(10, GameConfig.WindowHeight - 25, seedInfo, new Color3(80, 80, 100), seedScale);
    }
}
