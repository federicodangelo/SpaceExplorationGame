using Engine.Network;
using Engine.Network.Server;
using Engine.Platform.Null;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.Generation;

namespace SpaceExplorationGame;

internal static class Program
{
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
        if (args.Any(arg => arg is "--help" or "-h"))
        {
            PrintHelp();
            return;
        }

        ulong? galaxySeed = null;
        int port = 9050;
        int maxPlayers = 8;
        int latencyMs = 0;
        int jitterMs = 0;
        string? location = null;
        string? subLocation = null;
        int dangerLevel = 1;

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
            else if (arg is "--port" or "-p")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out port) || port < 1 || port > 65535)
                    throw new ArgumentException("Invalid or missing value for --port. Example: --port 9050");
                i++;
            }
            else if (arg is "--max-players" or "-m")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out maxPlayers) || maxPlayers < 1 || maxPlayers > 255)
                    throw new ArgumentException("Invalid or missing value for --max-players. Example: --max-players 8");
                i++;
            }
            else if (arg is "--latency" or "--lat")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out latencyMs) || latencyMs < 0)
                    throw new ArgumentException("Invalid or missing value for --latency. Example: --latency 100");
                i++;
            }
            else if (arg is "--jitter" or "--jit")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out jitterMs) || jitterMs < 0)
                    throw new ArgumentException("Invalid or missing value for --jitter. Example: --jitter 20");
                i++;
            }
            else if (arg is "--location" or "-l")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --location. Example: --location system");
                location = args[i + 1];
                i++;
            }
            else if (arg is "--sublocation" or "-sl")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("Missing value for --sublocation. Example: --sublocation inside");
                subLocation = args[i + 1];
                i++;
            }
            else if (arg is "--danger-level" or "-d")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out dangerLevel) || dangerLevel < 0)
                    throw new ArgumentException("Invalid or missing value for --danger-level. Example: --danger-level 2");
                i++;
            }
            else
            {
                throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        using var platform = new NullPlatform("Dedicated Server",
            WindowConfig.DefaultWindowWidth, WindowConfig.DefaultWindowHeight);

        using var game = new Game();
        game.Initialize(platform, galaxySeed);

        Console.WriteLine($"Galaxy Seed: {game.Seeds.GalaxySeed}");

        // Resolve starting spawn location (default: first danger-1 system in solar system space)
        var startingLocation = ResolveStartingLocation(location, subLocation, dangerLevel, game);
        Console.WriteLine($"Player spawn location: {startingLocation}");

        // Start WebSocket server.
        using var server = new GameServer(port, maxPlayers)
        {
            SimulatedLatencyMs = latencyMs,
            SimulatedJitterMs = jitterMs,
        };
        var serverState = new ServerState(server, game, startingLocation);
        server.Start();

        Console.WriteLine($"Listening on ws://localhost:{port}/ (max {maxPlayers} players)");
        if (latencyMs > 0 || jitterMs > 0)
            Console.WriteLine($"Simulated latency: {latencyMs} ms  jitter: ±{jitterMs} ms");
        Console.WriteLine("Server ticking. Press Ctrl+C to stop.");

        game.ChangeState(serverState);
        game.Run();
    }

    private static NetPlayerLocation ResolveStartingLocation(string? location, string? subLocation, int dangerLevel, Game game)
    {
        // Default starting star system: first danger-1 system, or system 0 as fallback
        var starSystems = game.GalaxyData.FindAll(s => s.DangerLevel == dangerLevel).ToList();

        if (starSystems.Count == 0)
        {
            Console.WriteLine($"Warning: No star systems found with danger level {dangerLevel}. Defaulting to all star systems.");
            starSystems = game.GalaxyData.ToList();
        }

        string loc = Normalize(location ?? "system");
        string? sub = string.IsNullOrWhiteSpace(subLocation) ? null : Normalize(subLocation);

        return loc switch
        {
            "system" or "solar-system" => ResolveSolarSystem(sub, starSystems),
            "station" or "space-station" => ResolveStation(sub, starSystems, game.UniverseGenerator),
            "planet" => ResolvePlanet(sub, starSystems, game.UniverseGenerator),
            "settlement" => ResolveSettlement(sub, starSystems, game.UniverseGenerator),
            _ => throw new ArgumentException($"Invalid --location '{location}'. Valid values: system, station, planet, settlement")
        };
    }

    private static NetPlayerLocation ResolveSolarSystem(string? sub, List<StarSystemData> starSystems)
    {
        if (starSystems.Count == 0)
            throw new ArgumentException("No star systems found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        if (sub is null or "-" or "none") return NetPlayerLocation.ForSolarSystem(starSystems[0].Index);
        throw new ArgumentException("Invalid --sublocation for system. Valid values: none (or omit)");
    }

    private static NetPlayerLocation ResolveStation(string? sub, List<StarSystemData> starSystems, IUniverseGenerator universeGenerator)
    {
        if (starSystems.Count == 0)
            throw new ArgumentException("No star systems found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        var starSystemWithStation = starSystems.FirstOrDefault(s => s.HasSpaceStation);

        if (starSystemWithStation == null)
            throw new ArgumentException("No star systems with space stations found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        var solarSystem = universeGenerator.GenerateSolarSystem(starSystemWithStation);

        if (solarSystem.SpaceStations.Count == 0)
            throw new ArgumentException("No space stations found in the selected star system to spawn at. Adjust the galaxy generation settings.");

        var spaceStation = solarSystem.SpaceStations[0];

        return sub switch
        {
            null or "orbit" or "inside" => NetPlayerLocation.ForSpaceStation(starSystemWithStation.Index, spaceStation.Index),
            _ => throw new ArgumentException("Invalid --sublocation for station. Valid values: orbit | inside")
        };
    }

    private static NetPlayerLocation ResolvePlanet(string? sub, List<StarSystemData> starSystems, IUniverseGenerator universeGenerator)
    {
        if (starSystems.Count == 0)
            throw new ArgumentException("No star systems found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        // Find first star system with at least one planet with solid surface (can't spawn on gas giants or empty systems)
        var starSystemWithPlanet = starSystems.FirstOrDefault(s => universeGenerator.GenerateSolarSystem(s).Planets.Any(p => p.HasSolidSurface));

        if (starSystemWithPlanet == null)
            throw new ArgumentException("No star systems with suitable planets found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        var planet = universeGenerator.GenerateSolarSystem(starSystemWithPlanet).Planets.FirstOrDefault(p => p.HasSolidSurface);

        if (planet == null)
            throw new ArgumentException("No suitable planets with solid surface in the starting star system to spawn at. Choose a different --location or adjust the galaxy generation settings.");

        return sub switch
        {
            null or "orbit" or "landed" or "on-foot" or "foot" or "on-vehicle" or "vehicle"
                => NetPlayerLocation.ForPlanet(starSystemWithPlanet.Index, planet.Index),
            _ => throw new ArgumentException("Invalid --sublocation for planet. Valid values: orbit | landed | on-foot | on-vehicle")
        };
    }

    private static NetPlayerLocation ResolveSettlement(string? sub, List<StarSystemData> starSystems, IUniverseGenerator universeGenerator)
    {
        if (starSystems.Count == 0)
            throw new ArgumentException("No star systems found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        // Find first star system with at least one planet with solid surface and at least one settlement (can't spawn on gas giants, empty systems, or planets without settlements)
        var starSystem = starSystems.FirstOrDefault(s => universeGenerator.GenerateSolarSystem(s).Planets.Any(p => p.HasSolidSurface && p.HasSettlement));

        if (starSystem == null)
            throw new ArgumentException("No star systems with suitable planets and settlements found in the galaxy to spawn at. Adjust the galaxy generation settings.");

        var solarSystem = universeGenerator.GenerateSolarSystem(starSystem);

        var planet = solarSystem.Planets.FirstOrDefault(p => p.HasSolidSurface && p.HasSettlement);

        if (planet == null)
            throw new ArgumentException("No suitable planets with settlements in the starting star system to spawn at. Choose a different --location or adjust the galaxy generation settings.");

        var planetSurface = universeGenerator.GeneratePlanetSurface(starSystem, planet); // Ensure settlements are generated

        if (planetSurface.Settlements.Count == 0)
            throw new ArgumentException("No settlements found on the selected planet to spawn at. Choose a different --location or adjust the galaxy generation settings.");

        var settlement = planetSurface.Settlements[0];

        return sub switch
        {
            null or "above" or "inside" or "on-foot" or "foot" or "on-vehicle" or "vehicle"
                => NetPlayerLocation.ForPlanetSettlement(starSystem.Index, planet.Index, settlement.Index),
            _ => throw new ArgumentException("Invalid --sublocation for settlement. Valid values: above | inside | on-foot | on-vehicle")
        };
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static void PrintHelp()
    {
        Console.WriteLine("Space Exploration Game - Dedicated Server");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Game.Server [options]");
        Console.WriteLine();
        Console.WriteLine("Dev (run from source):");
        Console.WriteLine("  dotnet run -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --help, -h                     Show this help message and exit");
        Console.WriteLine("  --seed, -s <seed>              Explicit galaxy seed (ulong)");
        Console.WriteLine("  --port, -p <port>              WebSocket port to listen on (default: 9050)");
        Console.WriteLine("  --max-players, -m <count>      Maximum connected players (default: 8)");
        Console.WriteLine("  --latency, --lat <ms>          Simulated one-way send latency in ms (default: 0)");
        Console.WriteLine("  --jitter, --jit <ms>           Simulated jitter ± ms added to latency (default: 0)");
        Console.WriteLine("  --location, -l <location>      Starting location for new players:");
        Console.WriteLine("                                   system | station | planet | settlement");
        Console.WriteLine("                                   (default: system, first danger-1 star system)");
        Console.WriteLine("  --sublocation, -sl <subloc>    Sublocation within --location:");
        Console.WriteLine("                                   system:     none (or omit)");
        Console.WriteLine("                                   station:    orbit | menu | inside");
        Console.WriteLine("                                   planet:     orbit | landed | on-foot | on-vehicle");
        Console.WriteLine("                                   settlement: above | inside | on-foot | on-vehicle");
        Console.WriteLine("  --danger-level, -d <level>     (Optional) Minimum danger level for the default starting star system when --location is 'system'. Default: 1");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  Game.Server --seed 12345");
        Console.WriteLine("  Game.Server --port 9100 --max-players 4");
        Console.WriteLine("  Game.Server --location station --sublocation inside");
        Console.WriteLine("  Game.Server --location planet --sublocation landed");
        Console.WriteLine("  Game.Server --latency 100 --jitter 20");
    }
}
