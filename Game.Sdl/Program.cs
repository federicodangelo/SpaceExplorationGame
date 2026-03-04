using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using Engine.Platform.Sdl;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu;
using SpaceExplorationGame.Core.Config;

namespace SpaceExplorationGame;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Run(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine("Use --help for usage information.");
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        // Parse optional arguments:
        //   dotnet run -- [--seed|-s <number>] [--location|-l <location> [--sublocation|-sl <sublocation>]] [--showcase|-sc <name> [--star-type <type>]]
        if (args.Any(arg => arg is "--help" or "-h"))
        {
            PrintHelp();
            return;
        }

        ulong? galaxySeed = null;
        var autoLaunch = StartOption.None;
        var autoDebugLaunch = DebugLaunchRequest.None;
        var autoDebugStarType = StarClass.G;
        string? location = null;
        string? subLocation = null;
        string? showcase = null;
        string? starTypeArg = null;
        bool debugMode = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--seed" or "-s")
            {
                if (i + 1 >= args.Length || !ulong.TryParse(args[i + 1], out var explicitSeed))
                    throw new ArgumentException("Invalid or missing value for --seed. Example: --seed 12345");
                galaxySeed = explicitSeed;
                i++;
            }
            else if (arg is "--location" or "-l")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --location. Example: --location planet");
                location = args[i + 1];
                i++;
            }
            else if (arg is "--sublocation" or "-sl")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --sublocation. Example: --sublocation on-foot");
                subLocation = args[i + 1];
                i++;
            }
            else if (arg is "--showcase" or "-sc")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --showcase. Example: --showcase star-type");
                showcase = args[i + 1];
                i++;
            }
            else if (arg == "--star-type")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --star-type. Example: --star-type g");
                starTypeArg = args[i + 1];
                i++;
            }
            else if (arg == "--debug")
            {
                debugMode = true;
            }
            else
            {
                throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (showcase != null && (location != null || subLocation != null))
            throw new ArgumentException("--showcase cannot be combined with --location/--sublocation.");

        if (showcase != null)
        {
            autoDebugLaunch = ResolveDebugShowcase(showcase);
            if (autoDebugLaunch == DebugLaunchRequest.StarTypeShowcase)
            {
                autoDebugStarType = ParseStarType(starTypeArg);
            }
            else if (starTypeArg != null)
            {
                throw new ArgumentException("--star-type can only be used with --showcase star-type.");
            }
        }
        else if (starTypeArg != null)
        {
            throw new ArgumentException("--star-type requires --showcase star-type.");
        }

        if (location != null || subLocation != null)
        {
            autoLaunch = ResolveStartFromLocation(location, subLocation);
        }

        WindowConfig.Debug = debugMode;

        // Create platform
        var musicProvider = new GameMusicProvider(SdlAudioManager.SampleRate);
        var sfxProvider = new GameSfxProvider(SdlAudioManager.SampleRate);
        using var platform = new SdlPlatform(
            WindowConfig.WindowTitle,
            WindowConfig.DefaultWindowWidth, WindowConfig.DefaultWindowHeight,
            musicProvider, sfxProvider,
            AudioConfig.AudioMasterVolume, AudioConfig.AudioMusicVolume, AudioConfig.AudioSfxVolume);

        using var game = new Game();
        game.Initialize(platform, galaxySeed);

        Console.WriteLine($"Galaxy Seed: {game.Seeds.GalaxySeed}");
        Console.WriteLine("Starting game...");
        if (autoLaunch != StartOption.None)
            Console.WriteLine($"Auto-start: {autoLaunch}");

        game.ChangeState(new MainMenuState(autoLaunch, autoDebugLaunch, autoDebugStarType));
        game.Run();
    }

    private static DebugLaunchRequest ResolveDebugShowcase(string showcase)
    {
        return Normalize(showcase) switch
        {
            "star-type" or "star" => DebugLaunchRequest.StarTypeShowcase,
            "planet-type" or "planet" => DebugLaunchRequest.PlanetTypeShowcase,
            "asteroid" or "asteroid-mining" => DebugLaunchRequest.AsteroidShowcase,
            "surface-mining" or "surface" => DebugLaunchRequest.SurfaceMiningShowcase,
            _ => throw new ArgumentException("Invalid --showcase. Valid values: star-type, planet-type, asteroid, surface-mining")
        };
    }

    private static StarClass ParseStarType(string? starTypeArg)
    {
        if (string.IsNullOrWhiteSpace(starTypeArg))
            return StarClass.G;

        if (Enum.TryParse<StarClass>(starTypeArg.Trim(), ignoreCase: true, out var starType))
            return starType;

        throw new ArgumentException("Invalid --star-type. Valid values: O, B, A, F, G, K, M, WhiteDwarf, Neutron, RedGiant");
    }

    private static StartOption ResolveStartFromLocation(string? location, string? subLocation)
    {
        if (string.IsNullOrWhiteSpace(location))
            throw new ArgumentException("--location is required when using --sublocation.");

        string loc = Normalize(location);
        string? sub = string.IsNullOrWhiteSpace(subLocation) ? null : Normalize(subLocation);

        return loc switch
        {
            "solar-system" or "system" => ResolveSolarSystem(sub),
            "space-station" or "station" => ResolveStation(sub),
            "planet" => ResolvePlanet(sub),
            "settlement" => ResolveSettlement(sub),
            _ => throw new ArgumentException($"Invalid --location: {location}. Valid values: system, station, planet, settlement")
        };
    }

    private static StartOption ResolveSolarSystem(string? sub)
    {
        if (sub is null or "-" or "none") return StartOption.StarSystem;
        throw new ArgumentException("Invalid --sublocation for system. Valid values: none (or omit)");
    }

    private static StartOption ResolveStation(string? sub)
    {
        return sub switch
        {
            null or "orbit" => StartOption.SpaceStation,
            "menu" => StartOption.SpaceStationMenu,
            "inside" => StartOption.SpaceStationInside,
            _ => throw new ArgumentException("Invalid --sublocation for station. Valid values: orbit, docked, inside")
        };
    }

    private static StartOption ResolvePlanet(string? sub)
    {
        return sub switch
        {
            null or "orbit" => StartOption.Planet,
            "landed" => StartOption.PlanetSurface,
            "on-foot" or "foot" => StartOption.PlanetSurfaceOnFoot,
            "on-vehicle" or "vehicle" => StartOption.PlanetSurfaceOnVehicle,
            _ => throw new ArgumentException("Invalid --sublocation for planet. Valid values: orbit, landed, on-foot, on-vehicle")
        };
    }

    private static StartOption ResolveSettlement(string? sub)
    {
        return sub switch
        {
            null or "above" => StartOption.Settlement,
            "inside" => StartOption.SettlementInside,
            "on-foot" or "foot" => StartOption.SettlementOnFoot,
            "on-vehicle" or "vehicle" => StartOption.SettlementOnVehicle,
            _ => throw new ArgumentException("Invalid --sublocation for settlement. Valid values: above, inside, on-foot, on-vehicle")
        };
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Space Exploration Game CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  SpaceExplorationGame [--seed|-s <seed>] [--location|-l <location> [--sublocation|-sl <sublocation>]]");
        Console.WriteLine("  SpaceExplorationGame [--seed|-s <seed>] --showcase|-sc <showcase> [--star-type <type>]");
        Console.WriteLine("  SpaceExplorationGame --help");
        Console.WriteLine();
        Console.WriteLine("Dev (run from source):");
        Console.WriteLine("  dotnet run -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --help, -h, /?                 Show this help message and exit");
        Console.WriteLine("  --seed, -s <seed>              Explicit galaxy seed (ulong)");
        Console.WriteLine("  --location, -l <location>      system | station | planet | settlement");
        Console.WriteLine("  --sublocation, -sl <subloc>    Depends on --location:");
        Console.WriteLine("                              system: none (or omit)");
        Console.WriteLine("                              station: orbit | docked | inside");
        Console.WriteLine("                              planet: orbit | landed | on-foot | on-vehicle");
        Console.WriteLine("                              settlement: above | inside | on-foot | on-vehicle");
        Console.WriteLine("  --showcase, -sc <showcase>     debug showcase: star-type | planet-type | asteroid | surface-mining");
        Console.WriteLine("  --star-type <type>             optional for star-type showcase (default: G)");
        Console.WriteLine("  --debug                        enable the DEBUG menu in the main menu");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  SpaceExplorationGame --seed 12345");
        Console.WriteLine("  SpaceExplorationGame --location system");
        Console.WriteLine("  SpaceExplorationGame --location station --sublocation docked");
        Console.WriteLine("  SpaceExplorationGame --seed 42 --location planet --sublocation on-foot");
        Console.WriteLine("  SpaceExplorationGame --showcase planet-type");
        Console.WriteLine("  SpaceExplorationGame --showcase star-type --star-type K");
    }
}
