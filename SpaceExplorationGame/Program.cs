using SpaceExplorationGame.Core;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Parse optional arguments:
        //   dotnet run [seed] [--start galaxy|system|planet|station|settlement]
        ulong? galaxySeed = null;
        int autoLaunch = -1;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--start" && i + 1 < args.Length)
            {
                autoLaunch = args[i + 1].ToLower() switch
                {
                    "galaxy" or "0" => 0,
                    "system" or "1" => 1,
                    "planet" or "2" => 2,
                    "station" or "3" => 3,
                    "settlement" or "4" => 4,
                    _ => -1
                };
                i++; // skip value
            }
            else if (ulong.TryParse(args[i], out var seed))
            {
                galaxySeed = seed;
            }
        }

        using var game = new Game();
        game.Initialize(galaxySeed);

        Console.WriteLine($"Galaxy Seed: {game.Seeds.GalaxySeed}");
        Console.WriteLine("Starting game...");
        if (autoLaunch >= 0)
            Console.WriteLine($"Auto-start: {(new[] { "galaxy", "system", "planet", "station", "settlement" })[autoLaunch]}");

        game.ChangeState(new MainMenuState(autoLaunch));
        game.Run();
    }
}
