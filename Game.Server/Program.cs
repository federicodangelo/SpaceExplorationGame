using Engine.Network;
using Engine.Platform.Null;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;

namespace SpaceExplorationGame;

internal static class Program
{
    private static void Main(string[] args)
    {
        ulong? galaxySeed = null;
        int port = 9050;
        int maxPlayers = 8;

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
        }

        using var platform = new NullPlatform("Dedicated Server",
            WindowConfig.DefaultWindowWidth, WindowConfig.DefaultWindowHeight);

        using var game = new Game();
        game.Initialize(platform, galaxySeed);

        Console.WriteLine($"Galaxy Seed: {game.Seeds.GalaxySeed}");

        // Start WebSocket server.
        using var server = new GameServer(port, maxPlayers);
        var serverState = new ServerState(server, game);
        server.Start();

        Console.WriteLine($"Listening on ws://localhost:{port}/ (max {maxPlayers} players)");
        Console.WriteLine("Server ticking. Press Ctrl+C to stop.");

        game.ChangeState(serverState);
        game.Run();
    }
}
