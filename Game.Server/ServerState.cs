using System.Numerics;
using Engine.Network;
using Engine.Network.Server;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.Simulation.Base;
using SpaceExplorationGame.States;

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
        public byte PlayerId;
        public SimulationPlayer SimPlayer;
        public ISimulation Simulation;
        public string PlayerName;
        public NetPlayerInfo PlayerInfo;
        public NetPlayerState PlayerState;
        public NetPlayerLocation PlayerLocation;

        public NetPlayer(byte id, SimulationPlayer simPlayer, ISimulation simulation, string name, NetPlayerInfo info, NetPlayerState state, NetPlayerLocation location)
        {
            PlayerId = id;
            SimPlayer = simPlayer;
            Simulation = simulation;
            PlayerName = name;
            PlayerInfo = info;
            PlayerState = state;
            PlayerLocation = location;
        }
    }

    // Map network player ID → server-side player info
    private readonly Dictionary<byte, NetPlayer> _netPlayers = new();

    private float _logTimer;
    private const float LogIntervalSeconds = 5f;

    // Tick counter for world-state broadcast rate (every N ticks)
    private int _tickCounter;
    public const int TargetFps = 60;
    private const int BroadcastEveryNTicks = 3; // ~20 Hz at 60 tick
    private const int BroadcastsPerSecond = TargetFps / BroadcastEveryNTicks;

    // There are 20 broadcasts per second ( so )with BroadcastEveryNTicks = 3 and TargetFps = 60)
    private const float CloseDistance = 500f; // Always send NPCs within this distance
    private const float MediumDistance = 1000f; // Send NPCs within this distance with lower frequency

    private const int CloseFrequency = 1; // Send every update (20 times per second)
    private const int MediumFrequency = 2; // Send every 2 updates (10 times per second)
    private const int FarFrequency = 5; // Send every 5 updates (4 times per second)


    // Reusable buffer for draining network events each tick
    private readonly List<ServerEvent> _pendingEvents = new();

    // Starting location assigned to every new player that joins
    private readonly NetPlayerLocation _startingLocation;

    public ServerState(GameServer server, Game game, NetPlayerLocation startingLocation)
    {
        _server = server;
        _game = game;
        _startingLocation = startingLocation;
    }

    public override void Enter(Game game) { }
    public override void Exit(Game game) { }

    public override void UpdateInput(Game game) { }

    public override void Update(Game game)
    {
        _server.UpdateStats();
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
            Console.WriteLine($"[{game.GlobalTime:F1}s] Clients: {clientCount}, Simulations: {sims.Count()}  |  Net TX: {FormatBps(_server.BytesSentPerSecond)} ({FormatBytes(_server.TotalBytesSent)} total)  RX: {FormatBps(_server.BytesReceivedPerSecond)} ({FormatBytes(_server.TotalBytesReceived)} total)");
            foreach (var sim in sims)
            {
                int entityCount = sim.EcsWorld.Size;
                int playerCount = sim.HasPlayers ? ((SimulationBase)sim).Players.Count : 0;
                Console.WriteLine($"  {sim.GetType().Name} [{sim.GetNetPlayerLocation()}]: {playerCount} player(s), {entityCount} entities");
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
                case ServerEventType.NpcHit:
                    HandleNpcHit(evt.PlayerId, evt.NpcHit);
                    break;
                case ServerEventType.PlayerKilledByNpc:
                    HandlePlayerKilledByNpc(evt.PlayerId, evt.PlayerKilledByNpc);
                    break;
            }
        }
    }

    public override void RenderGame(Game game) { }
    public override void RenderHud(Game game) { }

    // ────────────────────────────────────────────────────────────
    //  Network event handlers (called from server's background threads)
    // ────────────────────────────────────────────────────────────

    private void HandleClientJoined(byte playerId, C_JoinMessage join)
    {
        Console.WriteLine($"[Server] Player {playerId} ({join.PlayerName}) joining...");

        var joinedSimulation = GetLocationSimulation(playerId, _startingLocation);

        // Create a new PlayerData for this remote player
        var remotePlayerData = PlayerData.CreateRemote(playerId);
        var simPlayer = joinedSimulation.AddPlayer(remotePlayerData);
        var playerState = joinedSimulation.GetNetPlayerState(simPlayer);
        var playerLocation = joinedSimulation.GetNetPlayerLocation();
        var playerCoordinates = joinedSimulation.GetDefaultSpawnCoordinates();
        _netPlayers[playerId] = new NetPlayer(playerId, simPlayer, joinedSimulation, join.PlayerName, join.PlayerInfo, playerState, playerLocation);

        Console.WriteLine($"[Server] Player {playerId} ({join.PlayerName}) joined {playerLocation}.");

        // Send S_Welcome
        var welcome = new S_WelcomeMessage
        {
            PlayerId = playerId,
            GalaxySeed = _game.Seeds.GalaxySeed,
            GlobalTime = _game.GlobalTime,
            PlayerCount = (byte)_server.Clients.Count,
            PlayerLocation = playerLocation,
            PlayerCoordinates = playerCoordinates
        };
        _server.Send(playerId, NetSerializer.Write(welcome));

        // Send existing players info to the new client
        foreach (var (existingId, existingPlayer) in _netPlayers)
        {
            if (existingId == playerId) continue;
            var existingMsg = new S_PlayerJoinedMessage
            {
                PlayerId = existingId,
                Name = existingPlayer.PlayerName,
                Location = existingPlayer.PlayerLocation,
                State = existingPlayer.PlayerState,
                Info = existingPlayer.PlayerInfo
            };
            _server.Send(playerId, NetSerializer.Write(existingMsg));
        }

        // Notify other clients about the new player
        var joinedMsg = new S_PlayerJoinedMessage
        {
            PlayerId = playerId,
            Name = join.PlayerName,
            Location = playerLocation,
            State = playerState,
            Info = join.PlayerInfo
        };
        _server.BroadcastExcept(NetSerializer.Write(joinedMsg), playerId);
    }

    private ISimulation GetLocationSimulation(byte playerId, NetPlayerLocation location)
    {
        int starSystemIndex = location.SolarSystemIndex >= 0 && location.SolarSystemIndex < _game.GalaxyData.Count
            ? location.SolarSystemIndex
            : 0;

        var starSystem = _game.GalaxyData[starSystemIndex];

        var onSpaceStation = location.SpaceStationIndex >= 0;
        var onPlanetSettlement = location.PlanetIndex >= 0 && location.SettlementIndex >= 0;
        var onMoon = location.PlanetIndex >= 0 && location.MoonIndex >= 0 && !onPlanetSettlement;
        var onPlanet = location.PlanetIndex >= 0 && !onPlanetSettlement && !onMoon;
        var onStarSystem = !onSpaceStation && !onPlanet && !onPlanetSettlement && !onMoon;


        ISimulation joinedSimulation;
        // Find or create the solar system simulation on demand.
        var solarSystemSimulation = _game.Coordinator.FindOrCreate<SolarSystemSimulation>(
            s => s.StarSystem.Index == starSystem.Index,
            () =>
            {
                Console.WriteLine($"[Server] Creating solar system simulation for {location}");
                return new SolarSystemSimulation(_game, starSystem);
            });

        if (onStarSystem)
        {
            joinedSimulation = solarSystemSimulation;
        }
        else if (onSpaceStation)
        {
            var spaceStation = solarSystemSimulation.SpaceStations.ElementAtOrDefault(location.SpaceStationIndex);
            if (spaceStation == null)
            {
                Console.WriteLine($"[Server] Invalid location {location} for player {playerId} in {starSystem.Name}. Joining at star system instead.");
                joinedSimulation = solarSystemSimulation;
            }
            else
            {
                var spaceStationSimulation = _game.Coordinator.FindOrCreate<InteriorSimulation>(
                    s => s.Origin == InteriorOrigin.SpaceStation && s.StarSystem.Index == starSystem.Index && s.SpaceStation?.Index == spaceStation.Index,
                    () =>
                    {
                        Console.WriteLine($"[Server] Creating space station simulation for {location}");
                        return new InteriorSimulation(_game,
                            InteriorOrigin.SpaceStation,
                            starSystem: starSystem,
                            spaceStation: spaceStation,
                            parent: solarSystemSimulation
                        );
                    });
                joinedSimulation = spaceStationSimulation;
            }
        }
        else if (onPlanet || onMoon)
        {
            var planet = solarSystemSimulation.Planets.ElementAtOrDefault(location.PlanetIndex);

            if (onMoon && planet != null)
            {
                var moon = planet.Moons.ElementAtOrDefault(location.MoonIndex);
                if (moon == null)
                {
                    planet = null;
                }
                else
                {
                    planet = moon.ToPlanetData(planet.Index); // Convert moon to planet data for simulation
                }
            }

            if (planet == null)
            {
                Console.WriteLine($"[Server] Invalid location {location} for player {playerId} in {starSystem.Name}. Joining at star system instead.");
                joinedSimulation = solarSystemSimulation;
            }
            else
            {
                var planetSimulation = _game.Coordinator.FindOrCreate<PlanetSurfaceSimulation>(
                    s => s.StarSystem.Index == starSystem.Index && s.Planet.Index == location.PlanetIndex && s.Planet.MoonIndex == location.MoonIndex,
                    () =>
                    {
                        Console.WriteLine($"[Server] Creating planet surface simulation for {location}");
                        return new PlanetSurfaceSimulation(_game,
                            starSystem: starSystem,
                            planet: planet,
                            parent: solarSystemSimulation
                        );
                    });
                joinedSimulation = planetSimulation;
            }
        }
        else if (onPlanetSettlement)
        {
            var planet = solarSystemSimulation.Planets.ElementAtOrDefault(location.PlanetIndex);
            if (planet == null)
            {
                Console.WriteLine($"[Server] Invalid location {location} for player {playerId} in {starSystem.Name}. Joining at star system instead.");
                joinedSimulation = solarSystemSimulation;
            }
            else
            {
                var planetSimulation = _game.Coordinator.FindOrCreate<PlanetSurfaceSimulation>(
                    s => s.StarSystem.Index == starSystem.Index && s.Planet.Index == location.PlanetIndex,
                    () =>
                    {
                        Console.WriteLine($"[Server] Creating planet surface simulation for {location}");
                        return new PlanetSurfaceSimulation(_game,
                            starSystem: starSystem,
                            planet: planet,
                            parent: solarSystemSimulation
                        );
                    });

                var settlement = planetSimulation.Settlements.ElementAtOrDefault(location.SettlementIndex);

                if (settlement == null)
                {
                    Console.WriteLine($"[Server] Invalid location {location} for player {playerId} in {starSystem.Name}. Joining at star system instead.");
                    joinedSimulation = planetSimulation;
                }
                else
                {
                    var settlementSimulation = _game.Coordinator.FindOrCreate<InteriorSimulation>(
                        s => s.Origin == InteriorOrigin.Settlement && s.StarSystem.Index == starSystem.Index && s.Planet?.Index == location.PlanetIndex && s.Settlement?.Index == location.SettlementIndex,
                        () =>
                        {
                            Console.WriteLine($"[Server] Creating settlement simulation for {location}");
                            return new InteriorSimulation(_game,
                                InteriorOrigin.Settlement,
                                starSystem: starSystem,
                                planet: planet,
                                settlement: settlement,
                                parent: planetSimulation
                            );
                        });
                    joinedSimulation = settlementSimulation;
                }
            }
        }
        else
        {
            Console.WriteLine($"[Server] Player {playerId} joining at unknown location in {starSystem.Name}. Joining at star system instead.");
            joinedSimulation = solarSystemSimulation;
        }

        return joinedSimulation;
    }

    private void HandleClientLeft(byte playerId)
    {
        Console.WriteLine($"[Server] Player {playerId} disconnected.");

        if (!_netPlayers.TryGetValue(playerId, out var netPlayer))
            return;
        _netPlayers.Remove(playerId);

        netPlayer.Simulation.RemovePlayer(netPlayer.SimPlayer);

        var leftMsg = new S_PlayerLeftMessage { PlayerId = playerId };
        _server.Broadcast(NetSerializer.Write(leftMsg));
    }

    private void HandlePlayerState(byte playerId, C_PlayerStateMessage msg)
    {
        // Update the remote player's ECS entity with the state reported by the client.
        if (!_netPlayers.TryGetValue(playerId, out var netPlayer))
            return;

        netPlayer.PlayerState = msg.State;
        netPlayer.Simulation.ApplyNetPlayerState(netPlayer.SimPlayer, msg.State);
    }

    private void HandleLocationChanged(byte playerId, C_LocationChangedMessage msg)
    {
        if (!_netPlayers.TryGetValue(playerId, out var netPlayer))
            return;

        var newSimulation = GetLocationSimulation(playerId, msg.NewLocation);

        if (newSimulation == netPlayer.Simulation)
            return; // No change


        var playerData = netPlayer.SimPlayer.Data;
        var newLocation = newSimulation.GetNetPlayerLocation();

        Console.WriteLine($"[Server] Player {playerId} ({netPlayer.PlayerName}) moved to {newLocation}");

        // Remove from old simulation
        netPlayer.Simulation.RemovePlayer(netPlayer.SimPlayer);

        var simPlayer = newSimulation.AddPlayer(playerData);
        netPlayer.SimPlayer = simPlayer;
        netPlayer.Simulation = newSimulation;
        netPlayer.PlayerLocation = newLocation;

        // Notify other clients
        var locMsg = new S_PlayerLocationChangedMessage
        {
            PlayerId = playerId,
            Location = newLocation,
            Coordinates = newSimulation.GetDefaultSpawnCoordinates()
        };
        _server.BroadcastExcept(NetSerializer.Write(locMsg), playerId);

        // Send the new simulation state to the player
        SendOtherPlayersState(netPlayer);
        SendNpcStates(netPlayer, -1);
    }

    private void HandleNpcHit(byte playerId, C_NpcHitMessage msg)
    {
        if (!_netPlayers.TryGetValue(playerId, out var netPlayer)) return;

        // Broadcast the hit to other players in the same location
        var hitMsg = new S_NpcHitMessage
        {
            NpcId = msg.NpcId,
            PlayerId = playerId,
            Damage = msg.Damage,
            RemainingHull = msg.RemainingHull,
            RemainingShield = msg.RemainingShield,
            Killed = msg.Killed
        };
        var hitData = NetSerializer.Write(hitMsg);

        foreach (var other in _netPlayers.Values)
        {
            if (other.PlayerId != playerId && other.PlayerLocation == netPlayer.PlayerLocation)
                _server.Send(other.PlayerId, hitData);
        }
    }

    private void HandlePlayerKilledByNpc(byte playerId, C_PlayerKilledByNpcMessage msg)
    {
        // Log for now; the client handles its own death locally
        Console.WriteLine($"[Server] Player {playerId} killed by NPC {msg.NpcId}");
    }

    // ────────────────────────────────────────────────────────────
    //  World state broadcast
    // ────────────────────────────────────────────────────────────

    private int broadcastCounter = 0;

    private void BroadcastWorldState()
    {
        if (_netPlayers.Count == 0) return;

        // Send each client only the players in the same system
        foreach (var netPlayer in _netPlayers.Values)
        {
            SendOtherPlayersState(netPlayer);
            SendNpcStates(netPlayer, broadcastCounter);
        }

        broadcastCounter++;
    }

    private void SendOtherPlayersState(NetPlayer netPlayer)
    {
        var inSystem = _netPlayers.Values
            .Where(otherNetPlayer => otherNetPlayer.PlayerLocation == netPlayer.PlayerLocation && otherNetPlayer != netPlayer)
            .Select(otherNetPlayer => (otherNetPlayer.PlayerId, otherNetPlayer.Simulation.GetNetPlayerState(otherNetPlayer.SimPlayer)))
            .ToArray();

        var worldMsg = new S_WorldStateMessage
        {
            PlayerCount = (byte)inSystem.Length,
            ServerTime = _game.GlobalTime,
            Players = inSystem,
        };
        _server.Send(netPlayer.PlayerId, NetSerializer.Write(worldMsg));
    }

    private void SendNpcStates(NetPlayer netPlayer, int broadcastCounter)
    {
        if (netPlayer.Simulation is not CombatSimulationBase combatSim) return;

        var npcStates = combatSim.CollectNpcStates();
        var notSentNpcStates = new NetNotSentNpcState[0];

        if (broadcastCounter >= 0)
        {
            // We perform distance based optimizations for NPC updates, so we can skip sending NPC states that are far away from the player for a few seconds without causing noticeable issues. This reduces bandwidth and CPU usage on both server and client, especially in crowded areas with many NPCs.

            var playerPosition = netPlayer.PlayerState.Position;
            var groups = npcStates.GroupBy(npc =>
            {
                float distance = Vector2.Distance(playerPosition, npc.Position);
                if (npc.Dead) return true; // Always send dead NPCs so they disappear immediately
                if (distance < CloseDistance) return broadcastCounter % CloseFrequency == 0;
                if (distance < MediumDistance) return broadcastCounter % MediumFrequency == 0;
                return broadcastCounter % FarFrequency == 0;
            });

            npcStates = groups.Where(g => g.Key).SelectMany(g => g).ToArray();
            notSentNpcStates = groups.Where(g => !g.Key).SelectMany(g => g).Select(npc => new NetNotSentNpcState { NpcId = npc.NpcId }).ToArray();
        }

        if (npcStates.Length == 0) return;

        var msg = new S_NpcStatesMessage
        {
            Location = combatSim.GetNetPlayerLocation(),
            NpcCount = npcStates.Length,
            Npcs = npcStates,
            NotSentNpcCount = notSentNpcStates.Length,
            NotSentNpcs = notSentNpcStates
        };
        _server.Send(netPlayer.PlayerId, NetSerializer.Write(msg));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private static string FormatBps(long bytesPerSecond) => FormatBytes(bytesPerSecond) + "/s";
}
