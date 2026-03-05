using Engine.Network;
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

        // Launch an initial solar system simulation.
        var startSystem = game.GalaxyData[0];
        var sim = game.Coordinator.FindOrCreate<SolarSystemSimulation>(
            s => s.StarSystem.Index == startSystem.Index,
            () => new SolarSystemSimulation(game, startSystem));

        Console.WriteLine($"Solar system: {startSystem.Name} (index {startSystem.Index})");

        // Start WebSocket server.
        using var server = new GameServer(port, maxPlayers);
        var serverState = new ServerState(server, game, sim);
        server.Start();

        Console.WriteLine($"Listening on ws://localhost:{port}/ (max {maxPlayers} players)");
        Console.WriteLine("Server ticking. Press Ctrl+C to stop.");

        game.ChangeState(serverState);
        game.Run();
    }
}

/// <summary>
/// Server game state: manages the network layer alongside the simulation.
/// Broadcasts world state to all connected clients every tick and
/// processes inbound client messages.
/// </summary>
internal sealed class ServerState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

    private readonly GameServer _server;
    private readonly Game _game;
    private readonly SolarSystemSimulation _sim;

    // Map network player ID → simulation player
    private readonly Dictionary<byte, SimulationPlayer> _netPlayers = new();

    private float _logTimer;
    private const float LogIntervalSeconds = 5f;

    // Tick counter for world-state broadcast rate (every N ticks)
    private int _tickCounter;
    private const int BroadcastEveryNTicks = 3; // ~20 Hz at 60 tick

    public ServerState(GameServer server, Game game, SolarSystemSimulation sim)
    {
        _server = server;
        _game = game;
        _sim = sim;

        _server.OnClientJoined += HandleClientJoined;
        _server.OnClientLeft += HandleClientLeft;
        _server.OnPlayerStateReceived += HandlePlayerState;
    }

    public override void Enter(Game game) { }
    public override void Exit(Game game)
    {
        _server.OnClientJoined -= HandleClientJoined;
        _server.OnClientLeft -= HandleClientLeft;
        _server.OnPlayerStateReceived -= HandlePlayerState;
    }

    public override void UpdateInput(Game game) { }

    public override void Update(Game game)
    {
        _tickCounter++;

        // Broadcast world state at reduced rate
        if (_tickCounter % BroadcastEveryNTicks == 0)
            BroadcastWorldState();

        // Periodic debug log
        _logTimer += game.DeltaTime;
        if (_logTimer >= LogIntervalSeconds)
        {
            _logTimer -= LogIntervalSeconds;
            var sims = game.Coordinator.Simulations;
            int clientCount = _server.Clients.Count;
            Console.WriteLine($"[{game.GlobalTime:F1}s] Clients: {clientCount}, Simulations: {sims.Count}");
            foreach (var sim in sims)
            {
                int entityCount = sim.EcsWorld.Size;
                int playerCount = sim.HasPlayers ? ((SimulationBase)sim).Players.Count : 0;
                Console.WriteLine($"  {sim.GetType().Name}: {playerCount} player(s), {entityCount} entities");
            }
        }
    }

    public override void RenderGame(Game game) { }
    public override void RenderHud(Game game) { }

    // ────────────────────────────────────────────────────────────
    //  Network event handlers (called from server's background threads)
    // ────────────────────────────────────────────────────────────

    private void HandleClientJoined(byte playerId, JoinMessage join)
    {
        Console.WriteLine($"[Server] Player {playerId} ({join.PlayerName}) joining...");

        // Create a new PlayerData for this remote player
        var remotePlayerData = new PlayerData { Type = PlayerType.Remote };
        var simPlayer = _sim.AddPlayer(remotePlayerData);
        lock (_netPlayers) { _netPlayers[playerId] = simPlayer; }

        Console.WriteLine($"[Server] Player {playerId} ({join.PlayerName}) joined simulation.");

        // Send S_Welcome
        var welcome = new WelcomeMessage
        {
            PlayerId = playerId,
            GalaxySeed = _game.Seeds.GalaxySeed,
            StarSystemIndex = _sim.StarSystem.Index,
            GlobalTime = _game.GlobalTime,
            PlayerCount = (byte)_server.Clients.Count,
        };
        _server.Send(playerId, NetSerializer.Write(welcome));

        // Notify other clients
        var joinedMsg = new PlayerJoinedMessage
        {
            PlayerId = playerId,
            PlayerName = join.PlayerName,
            InitialState = default,
        };
        _server.BroadcastExcept(NetSerializer.Write(joinedMsg), playerId);
    }

    private void HandleClientLeft(byte playerId)
    {
        Console.WriteLine($"[Server] Player {playerId} disconnected.");

        SimulationPlayer? simPlayer;
        lock (_netPlayers)
        {
            if (!_netPlayers.TryGetValue(playerId, out simPlayer))
                return;
            _netPlayers.Remove(playerId);
        }

        _sim.RemovePlayer(simPlayer);

        var leftMsg = new PlayerLeftMessage { PlayerId = playerId };
        _server.Broadcast(NetSerializer.Write(leftMsg));
    }

    private void HandlePlayerState(byte playerId, PlayerStateMessage msg)
    {
        // Update the remote player's ECS entity with the state reported by the client.
        SimulationPlayer? simPlayer;
        lock (_netPlayers)
        {
            if (!_netPlayers.TryGetValue(playerId, out simPlayer))
                return;
        }

        if (!_sim.EcsWorld.IsAlive(simPlayer.Entity)) return;

        ref var transform = ref _sim.EcsWorld.Get<ECS.Components.Transform>(simPlayer.Entity);
        transform.Position = msg.State.Position;
        transform.Rotation = msg.State.Rotation;

        ref var velocity = ref _sim.EcsWorld.Get<ECS.Components.Velocity>(simPlayer.Entity);
        velocity.Linear = msg.State.Velocity;

        if (_sim.EcsWorld.Has<ECS.Components.Health>(simPlayer.Entity))
        {
            ref var health = ref _sim.EcsWorld.Get<ECS.Components.Health>(simPlayer.Entity);
            health.Hull = msg.State.Hull;
            health.Shield = msg.State.Shield;
        }
    }

    // ────────────────────────────────────────────────────────────
    //  World state broadcast
    // ────────────────────────────────────────────────────────────

    private void BroadcastWorldState()
    {
        Dictionary<byte, SimulationPlayer> snapshot;
        lock (_netPlayers) { snapshot = new(_netPlayers); }

        if (snapshot.Count == 0) return;

        var players = new (byte, NetPlayerState)[snapshot.Count];
        int i = 0;
        foreach (var (id, simPlayer) in snapshot)
        {
            var state = new NetPlayerState();
            if (_sim.EcsWorld.IsAlive(simPlayer.Entity))
            {
                var transform = _sim.EcsWorld.Get<ECS.Components.Transform>(simPlayer.Entity);
                state.Position = transform.Position;
                state.Rotation = transform.Rotation;

                var velocity = _sim.EcsWorld.Get<ECS.Components.Velocity>(simPlayer.Entity);
                state.Velocity = velocity.Linear;

                if (_sim.EcsWorld.Has<ECS.Components.Health>(simPlayer.Entity))
                {
                    var health = _sim.EcsWorld.Get<ECS.Components.Health>(simPlayer.Entity);
                    state.Hull = health.Hull;
                    state.MaxHull = health.MaxHull;
                    state.Shield = health.Shield;
                    state.MaxShield = health.MaxShield;
                }

                if (_sim.EcsWorld.Has<ECS.Components.ShipInputComponent>(simPlayer.Entity))
                {
                    var input = _sim.EcsWorld.Get<ECS.Components.ShipInputComponent>(simPlayer.Entity);
                    state.Shooting = input.Shoot;
                    state.AccelerationDirection = input.AccelerationDirection;
                }
            }
            players[i++] = (id, state);
        }

        var worldMsg = new WorldStateMessage
        {
            PlayerCount = (byte)players.Length,
            ServerTime = _game.GlobalTime,
            Players = players,
        };
        _server.Broadcast(NetSerializer.Write(worldMsg));
    }
}
