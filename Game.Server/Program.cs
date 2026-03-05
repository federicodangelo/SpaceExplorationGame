using Engine.Platform.Null;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.Simulation.Base;

namespace SpaceExplorationGame;

internal static class Program
{
    private static void Main(string[] args)
    {
        ulong? galaxySeed = null;

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
        }

        using var platform = new NullPlatform("Dedicated Server",
            WindowConfig.DefaultWindowWidth, WindowConfig.DefaultWindowHeight);

        using var game = new Game();
        game.Initialize(platform, galaxySeed);

        Console.WriteLine($"Galaxy Seed: {game.Seeds.GalaxySeed}");
        Console.WriteLine("Starting dedicated server...");

        // Launch an initial solar system simulation so the coordinator has work to do.
        var startSystem = game.GalaxyData[0];
        var sim = game.Coordinator.FindOrCreate<SolarSystemSimulation>(
            s => s.StarSystem.Index == startSystem.Index,
            () => new SolarSystemSimulation(game, startSystem));

        var serverPlayer = sim.AddPlayer(game.Player);
        Console.WriteLine($"Solar system: {startSystem.Name} (index {startSystem.Index})");
        Console.WriteLine("Server ticking. Press Ctrl+C to stop.");

        // Run the headless game loop (fixed timestep, no rendering).
        game.ChangeState(new ServerState());
        game.Run();
    }
}

/// <summary>
/// Minimal no-op game state for the dedicated server.
/// The coordinator ticks all active simulations automatically;
/// this state just keeps the game loop alive.
/// </summary>
internal sealed class ServerState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

    private float _logTimer;
    private const float LogIntervalSeconds = 5f;

    public override void Enter(Game game) { }
    public override void Exit(Game game) { }
    public override void UpdateInput(Game game) { }

    public override void Update(Game game)
    {
        _logTimer += game.DeltaTime;
        if (_logTimer < LogIntervalSeconds) return;
        _logTimer -= LogIntervalSeconds;

        var sims = game.Coordinator.Simulations;
        Console.WriteLine($"[{game.GlobalTime:F1}s] Active simulations: {sims.Count}");
        foreach (var sim in sims)
        {
            int entityCount = sim.EcsWorld.Size;
            int playerCount = sim.HasPlayers ? ((SimulationBase)sim).Players.Count : 0;
            Console.WriteLine($"  {sim.GetType().Name}: {playerCount} player(s), {entityCount} entities");
            if (sim is IDebugInfoProvider simDebugInfo)
            {
                var debugLines = simDebugInfo.GetDebugInfo();
                if (debugLines != null)
                {
                    foreach (var line in debugLines)
                        Console.WriteLine($"    {line}");
                }
                var debugTimings = simDebugInfo.GetDebugTimings();
                if (debugTimings != null)
                {
                    foreach (var entry in debugTimings)
                        Console.WriteLine($"    {entry.Name}: {entry.ElapsedMs:F2} ms");
                }
            }
        }
    }

    public override void RenderGame(Game game) { }
    public override void RenderHud(Game game) { }
}
