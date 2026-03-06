using Engine.Network;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.Simulation.Base;

namespace SpaceExplorationGame;

/// <summary>
/// Server game state: manages the network layer alongside the simulation coordinator.
/// Broadcasts world state to all connected clients every tick and
/// processes inbound client messages. Solar systems are loaded on-demand
/// when players join.
/// </summary>
internal sealed class ServerState : GameState
{
    public override GameStateType Type => GameStateType.SolarSystem;

    private readonly GameServer _server;
    private readonly Game _game;

    /// <summary>Per-player tracking: simulation player + which system they're in.</summary>
    private sealed class NetPlayer
    {
        public SimulationPlayer SimPlayer;
        public SolarSystemSimulation Simulation;
        public string Name;
        public int StarSystemIndex;

        public NetPlayer(SimulationPlayer simPlayer, SolarSystemSimulation simulation, string name, int starSystemIndex)
        {
            SimPlayer = simPlayer;
            Simulation = simulation;
            Name = name;
            StarSystemIndex = starSystemIndex;
        }
    }

    // Map network player ID → server-side player info
    private readonly Dictionary<byte, NetPlayer> _netPlayers = new();

    private float _logTimer;
    private const float LogIntervalSeconds = 5f;

    // Tick counter for world-state broadcast rate (every N ticks)
    private int _tickCounter;
    private const int BroadcastEveryNTicks = 3; // ~20 Hz at 60 tick

    // Reusable buffer for draining network events each tick
    private readonly List<ServerEvent> _pendingEvents = new();

    public ServerState(GameServer server, Game game)
    {
        _server = server;
        _game = game;
    }

    public override void Enter(Game game) { }
    public override void Exit(Game game) { }

    public override void UpdateInput(Game game) { }

    public override void Update(Game game)
    {
        HandleServerEvents();

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

    private void HandleServerEvents()
    {
        // Drain and process all queued network events on the main thread.
        _server.DrainEvents(_pendingEvents);
        foreach (var evt in _pendingEvents)
        {
            switch (evt.Type)
            {
                case ServerEventType.ClientJoined:
                    HandleClientJoined(evt.PlayerId, evt.Join);
                    break;
                case ServerEventType.ClientLeft:
                    HandleClientLeft(evt.PlayerId);
                    break;
                case ServerEventType.PlayerState:
                    HandlePlayerState(evt.PlayerId, evt.PlayerState);
                    break;
                case ServerEventType.LocationChanged:
                    HandleLocationChanged(evt.PlayerId, evt.LocationChanged);
                    break;
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

        // Pick a starting star system: use client's requested index, or default to first system.
        int systemIndex = join.StarSystemIndex >= 0 && join.StarSystemIndex < _game.GalaxyData.Count
            ? join.StarSystemIndex
            : 0;
        var starSystem = _game.GalaxyData[systemIndex];

        // Find or create the solar system simulation on demand.
        var sim = _game.Coordinator.FindOrCreate<SolarSystemSimulation>(
            s => s.StarSystem.Index == starSystem.Index,
            () =>
            {
                Console.WriteLine($"[Server] Creating simulation for {starSystem.Name} (index {starSystem.Index})");
                return new SolarSystemSimulation(_game, starSystem);
            });

        // Create a new PlayerData for this remote player
        var remotePlayerData = new PlayerData { Type = PlayerType.Remote };
        var simPlayer = sim.AddPlayer(remotePlayerData);
        _netPlayers[playerId] = new NetPlayer(simPlayer, sim, join.PlayerName, starSystem.Index);

        Console.WriteLine($"[Server] Player {playerId} ({join.PlayerName}) joined {starSystem.Name}.");

        // Send S_Welcome
        var welcome = new WelcomeMessage
        {
            PlayerId = playerId,
            GalaxySeed = _game.Seeds.GalaxySeed,
            StarSystemIndex = starSystem.Index,
            GlobalTime = _game.GlobalTime,
            PlayerCount = (byte)_server.Clients.Count,
        };
        _server.Send(playerId, NetSerializer.Write(welcome));

        // Send existing players info to the new client
        foreach (var (existingId, existingPlayer) in _netPlayers)
        {
            if (existingId == playerId) continue;
            var existingMsg = new PlayerJoinedMessage
            {
                PlayerId = existingId,
                PlayerName = existingPlayer.Name,
                StarSystemIndex = existingPlayer.StarSystemIndex,
                InitialState = default,
            };
            _server.Send(playerId, NetSerializer.Write(existingMsg));
        }

        // Notify other clients about the new player
        var joinedMsg = new PlayerJoinedMessage
        {
            PlayerId = playerId,
            PlayerName = join.PlayerName,
            StarSystemIndex = starSystem.Index,
            InitialState = default,
        };
        _server.BroadcastExcept(NetSerializer.Write(joinedMsg), playerId);
    }

    private void HandleClientLeft(byte playerId)
    {
        Console.WriteLine($"[Server] Player {playerId} disconnected.");

        if (!_netPlayers.TryGetValue(playerId, out var netPlayer))
            return;
        _netPlayers.Remove(playerId);

        netPlayer.Simulation.RemovePlayer(netPlayer.SimPlayer);

        var leftMsg = new PlayerLeftMessage { PlayerId = playerId };
        _server.Broadcast(NetSerializer.Write(leftMsg));
    }

    private void HandlePlayerState(byte playerId, PlayerStateMessage msg)
    {
        // Update the remote player's ECS entity with the state reported by the client.
        if (!_netPlayers.TryGetValue(playerId, out var netPlayer))
            return;

        var world = netPlayer.Simulation.EcsWorld;
        var entity = netPlayer.SimPlayer.Entity;
        if (!world.IsAlive(entity)) return;

        ref var transform = ref world.Get<Transform>(entity);
        transform.Position = msg.State.Position;
        transform.Rotation = msg.State.Rotation;

        ref var velocity = ref world.Get<Velocity>(entity);
        velocity.Linear = msg.State.Velocity;

        if (world.Has<Health>(entity))
        {
            ref var health = ref world.Get<Health>(entity);
            health.Hull = msg.State.Hull;
            health.Shield = msg.State.Shield;
        }
    }

    private void HandleLocationChanged(byte playerId, LocationChangedMessage msg)
    {
        if (!_netPlayers.TryGetValue(playerId, out var netPlayer))
            return;

        int newIndex = msg.StarSystemIndex;
        if (newIndex < 0 || newIndex >= _game.GalaxyData.Count) return;
        if (newIndex == netPlayer.StarSystemIndex) return;

        Console.WriteLine($"[Server] Player {playerId} ({netPlayer.Name}) moving to system {newIndex}");

        // Remove from old simulation
        netPlayer.Simulation.RemovePlayer(netPlayer.SimPlayer);

        // Find or create the new simulation
        var starSystem = _game.GalaxyData[newIndex];
        var sim = _game.Coordinator.FindOrCreate<SolarSystemSimulation>(
            s => s.StarSystem.Index == starSystem.Index,
            () =>
            {
                Console.WriteLine($"[Server] Creating simulation for {starSystem.Name} (index {starSystem.Index})");
                return new SolarSystemSimulation(_game, starSystem);
            });

        var remotePlayerData = new PlayerData { Type = PlayerType.Remote };
        var simPlayer = sim.AddPlayer(remotePlayerData);
        netPlayer.SimPlayer = simPlayer;
        netPlayer.Simulation = sim;
        netPlayer.StarSystemIndex = newIndex;

        // Notify other clients
        var locMsg = new PlayerLocationChangedMessage
        {
            PlayerId = playerId,
            StarSystemIndex = newIndex,
        };
        _server.BroadcastExcept(NetSerializer.Write(locMsg), playerId);
    }

    // ────────────────────────────────────────────────────────────
    //  World state broadcast
    // ────────────────────────────────────────────────────────────

    private void BroadcastWorldState()
    {
        if (_netPlayers.Count == 0) return;

        var players = new (byte, NetPlayerState)[_netPlayers.Count];
        int i = 0;
        foreach (var (id, netPlayer) in _netPlayers)
        {
            var state = new NetPlayerState();
            var world = netPlayer.Simulation.EcsWorld;
            var entity = netPlayer.SimPlayer.Entity;

            if (world.IsAlive(entity))
            {
                var transform = world.Get<Transform>(entity);
                state.Position = transform.Position;
                state.Rotation = transform.Rotation;

                var velocity = world.Get<Velocity>(entity);
                state.Velocity = velocity.Linear;

                if (world.Has<Health>(entity))
                {
                    var health = world.Get<Health>(entity);
                    state.Hull = health.Hull;
                    state.MaxHull = health.MaxHull;
                    state.Shield = health.Shield;
                    state.MaxShield = health.MaxShield;
                }

                if (world.Has<ShipInputComponent>(entity))
                {
                    var input = world.Get<ShipInputComponent>(entity);
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
