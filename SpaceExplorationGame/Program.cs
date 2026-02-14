using SpaceExplorationGame.Core;
using SpaceExplorationGame.States;

namespace SpaceExplorationGame;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Parse optional galaxy seed from command line
        ulong? galaxySeed = null;
        if (args.Length > 0 && ulong.TryParse(args[0], out var seed))
        {
            galaxySeed = seed;
        }

        using var game = new Game();
        game.Initialize(galaxySeed);

        Console.WriteLine($"Galaxy Seed: {game.Seeds.GalaxySeed}");
        Console.WriteLine("Starting game...");

        // Start at the galaxy map
        game.ChangeState(new GalaxyMapState());
        game.Run();
    }
}
