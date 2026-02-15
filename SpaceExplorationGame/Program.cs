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
        var autoLaunch = StartOption.None;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--start" && i + 1 < args.Length)
            {
                autoLaunch = args[i + 1].ToLower() switch
                {
                    "galaxy" => StartOption.GalaxyMap,
                    "system" => StartOption.StarSystem,
                    "planet" => StartOption.PlanetSurface,
                    "station" => StartOption.SpaceStation,
                    "settlement-inside" => StartOption.SettlementInside,
                    "settlement" => StartOption.Settlement,
                    "station-inside" => StartOption.SpaceStationInside,
                    _ => throw new ArgumentException($"Invalid start option: {args[i + 1]}")
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
        if (autoLaunch != StartOption.None)
            Console.WriteLine($"Auto-start: {autoLaunch}");

        game.ChangeState(new MainMenuState(autoLaunch));
        game.Run();
    }
}
