using System.Numerics;

namespace Engine.Network.Client;

/// <summary>
/// Event types produced by <see cref="ClientNetworkManager"/> after processing inbound messages.
/// </summary>
public enum ClientEventType { PlayerJoined, PlayerLeft, PlayerLocationChanged, NpcStates, NpcHit, NpcKillReward }

/// <summary>
/// A client-side network event, queued by <see cref="ClientNetworkManager.ProcessMessages"/>
/// and drained on the main thread via <see cref="ClientNetworkManager.DrainEvents"/>.
/// </summary>
public readonly struct ClientEvent
{
    public ClientEventType Type { get; init; }
    public S_PlayerJoinedMessage PlayerJoined { get; init; }
    public S_PlayerLeftMessage PlayerLeft { get; init; }
    public S_PlayerLocationChangedMessage PlayerLocationChanged { get; init; }
    public S_NpcStatesMessage NpcStates { get; init; }
    public S_NpcHitMessage NpcHit { get; init; }
    public S_NpcKillRewardMessage NpcKillReward { get; init; }
}

/// <summary>
/// Client-side network manager. Connects to a dedicated server, sends local player state,
/// and receives remote player states to update their ECS entities.
/// 
/// Usage: call <see cref="ConnectAsync"/> once, then <see cref="SendLocalState"/> +
/// <see cref="ProcessMessages"/> every frame from the game loop.
/// Call <see cref="DrainEvents"/> after ProcessMessages to get join/leave events.
/// </summary>
public sealed class ClientNetworkManager : IDisposable
{
    private readonly GameClient _client = new();
    private readonly List<ClientEvent> _events = new();

    /// <summary>Assigned by the server in the welcome message.</summary>
    public byte LocalPlayerId { get; private set; }

    /// <summary>True after a successful welcome handshake.</summary>
    public bool IsJoined { get; private set; }

    /// <summary>True if connected to a server (WebSocket is open).</summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>Server's galaxy seed (received in welcome).</summary>
    public ulong ServerGalaxySeed { get; private set; }

    /// <summary>Player starting location in the server</summary>
    public NetPlayerLocation PlayerStartingLocation { get; private set; }
    public Vector2 PlayerStartingCoordinates { get; private set; }

    /// <summary>Server's global time at the moment of the welcome message.</summary>
    public double ServerGlobalTime { get; private set; }

    /// <summary>Total bytes sent to the server since connect.</summary>
    public long TotalBytesSent => _client.TotalBytesSent;
    /// <summary>Total bytes received from the server since connect.</summary>
    public long TotalBytesReceived => _client.TotalBytesReceived;
    /// <summary>Bytes sent in the most recently completed 1-second window.</summary>
    public long BytesSentPerSecond => _client.BytesSentLastSecond;
    /// <summary>Bytes received in the most recently completed 1-second window.</summary>
    public long BytesReceivedPerSecond => _client.BytesReceivedLastSecond;

    /// <summary>Remote player states received from the server, keyed by player ID.</summary>
    public Dictionary<byte, RemotePlayer> RemotePlayers { get; } = new();

    /// <summary>Latest NPC state snapshot received from the server. Null until first broadcast.</summary>
    public NetPlayerLocation LatestNpcStateLocation { get; private set; } = NetPlayerLocation.ForUnknown();
    public NetNpcState[]? LatestNpcStates { get; private set; }
    public NetNotSentNpcState[]? LatestNotSentNpcStates { get; private set; }

    // Pending interaction results (set by ProcessMessages, consumed once by the simulation).
    private readonly List<S_InteractDerelictResultMessage> _pendingDerelictResults = new();
    private readonly List<S_InteractDistressResultMessage> _pendingDistressResults = new();

    /// <summary>
    /// Take the first pending derelict interaction result for the given solar system (if any) and remove it.
    /// Returns null if no matching result is waiting.
    /// </summary>
    public S_InteractDerelictResultMessage? TakeDerelictResult(int solarSystemIndex)
    {
        for (int i = 0; i < _pendingDerelictResults.Count; i++)
        {
            if (_pendingDerelictResults[i].SolarSystemIndex == solarSystemIndex)
            {
                var r = _pendingDerelictResults[i];
                _pendingDerelictResults.RemoveAt(i);
                return r;
            }
        }
        return null;
    }

    /// <summary>
    /// Take the first pending distress signal interaction result for the given solar system (if any) and remove it.
    /// Returns null if no matching result is waiting.
    /// </summary>
    public S_InteractDistressResultMessage? TakeDistressResult(int solarSystemIndex)
    {
        for (int i = 0; i < _pendingDistressResults.Count; i++)
        {
            if (_pendingDistressResults[i].SolarSystemIndex == solarSystemIndex)
            {
                var r = _pendingDistressResults[i];
                _pendingDistressResults.RemoveAt(i);
                return r;
            }
        }
        return null;
    }

    /// <summary>
    /// Connect to the server and send a join request.
    /// Blocks until the connection is established (but the join handshake is async).
    /// </summary>
    public async Task ConnectAsync(string url, string playerName, NetPlayerInfo info)
    {
        await _client.ConnectAsync(url);
        var joinMsg = new C_JoinMessage { PlayerName = playerName, PlayerInfo = info };
        _client.Send(NetSerializer.Write(joinMsg));
    }

    /// <summary>
    /// Send a location change notification to the server (e.g. after a star system jump).
    /// </summary>
    public void SendLocationChanged(NetPlayerLocation location)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(new C_LocationChangedMessage { NewLocation = location }));
    }

    /// <summary>
    /// Send the local player's current entity state to the server.
    /// Call once per tick from the active game state.
    /// </summary>
    public void SendLocalState(NetPlayerState state)
    {
        if (!IsJoined || !_client.IsConnected) return;

        _client.Send(NetSerializer.Write(new C_PlayerStateMessage { State = state }));
    }

    /// <summary>
    /// Send a graceful disconnect notification before closing.
    /// </summary>
    public void SendDisconnect()
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(new C_DisconnectMessage()));
    }

    /// <summary>
    /// Send an NPC hit report to the server.
    /// </summary>
    public void SendNpcHit(C_NpcHitMessage msg)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(msg));
    }

    /// <summary>
    /// Send a player-killed-by-NPC notification to the server.
    /// </summary>
    public void SendPlayerKilledByNpc(C_PlayerKilledByNpcMessage msg)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(msg));
    }

    /// <summary>
    /// Send a derelict ship salvage request to the server.
    /// </summary>
    public void SendInteractDerelict(C_InteractDerelictMessage msg)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(msg));
    }

    /// <summary>
    /// Send a distress signal trigger request to the server.
    /// </summary>
    public void SendInteractDistress(C_InteractDistressMessage msg)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(msg));
    }

    /// <summary>
    /// Announce a transition to the server so other clients can play departure effects.
    /// </summary>
    public void SendTransitionStarted(NetPlayerTransition transition)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(new C_TransitionStartedMessage { Transition = transition }));
    }

    /// <summary>
    /// Drain all inbound messages from the network receive queue.
    /// Call once per frame from the game loop. After calling, use
    /// <see cref="DrainEvents"/> to get any join/leave events.
    /// </summary>
    public void ProcessMessages()
    {
        _client.UpdateStats();

        while (_client.TryReceive(out var data))
        {
            var type = NetSerializer.PeekType(data);
            switch (type)
            {
                case MessageType.S_Welcome:
                    HandleWelcome(data);
                    break;
                case MessageType.S_PlayerJoined:
                    HandlePlayerJoined(data);
                    break;
                case MessageType.S_PlayerLeft:
                    HandlePlayerLeft(data);
                    break;
                case MessageType.S_WorldState:
                    HandleWorldState(data);
                    break;
                case MessageType.S_PlayerLocationChanged:
                    HandlePlayerLocationChanged(data);
                    break;
                case MessageType.S_NpcStates:
                    HandleNpcStates(data);
                    break;
                case MessageType.S_NpcHit:
                    HandleNpcHit(data);
                    break;
                case MessageType.S_NpcKillReward:
                    HandleNpcKillReward(data);
                    break;
                case MessageType.S_InteractDerelictResult:
                    HandleInteractDerelictResult(data);
                    break;
                case MessageType.S_InteractDistressResult:
                    HandleInteractDistressResult(data);
                    break;
                case MessageType.S_PlayerTransitionStarted:
                    HandlePlayerTransitionStarted(data);
                    break;
            }
        }
    }

    /// <summary>
    /// Drain all queued client events into the provided list.
    /// Call this after <see cref="ProcessMessages"/> to iterate join/leave events.
    /// The list is cleared before filling.
    /// </summary>
    public void DrainEvents(List<ClientEvent> dest)
    {
        dest.Clear();
        dest.AddRange(_events);
        _events.Clear();
    }

    public void Dispose()
    {
        SendDisconnect();
        _client.Dispose();
    }

    // ────────────────────────────────────────────────────────────
    //  Message handlers
    // ────────────────────────────────────────────────────────────

    private void HandleWelcome(byte[] data)
    {
        var msg = NetSerializer.ReadWelcome(data);
        LocalPlayerId = msg.PlayerId;
        ServerGalaxySeed = msg.GalaxySeed;
        ServerGlobalTime = msg.GlobalTime;
        PlayerStartingLocation = msg.PlayerLocation;
        PlayerStartingCoordinates = msg.PlayerCoordinates;
        IsJoined = true;
        Console.WriteLine($"[Net] Joined server as player {msg.PlayerId} (system {msg.PlayerLocation}, {msg.PlayerCount} players)");
    }

    private void HandlePlayerJoined(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerJoined(data);
        if (msg.PlayerId == LocalPlayerId) return;
        RemotePlayers[msg.PlayerId] = new RemotePlayer(msg.PlayerId, msg.Name, msg.Location, msg.Info, msg.State);
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.PlayerJoined,
            PlayerJoined = msg,
        });
        Console.WriteLine($"[Net] Player {msg.PlayerId} ({msg.Name}) joined {msg.Location}");
    }

    private void HandlePlayerLeft(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerLeft(data);
        RemotePlayers.Remove(msg.PlayerId);
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.PlayerLeft,
            PlayerLeft = msg,
        });
        Console.WriteLine($"[Net] Player {msg.PlayerId} left");
    }

    private void HandlePlayerLocationChanged(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerLocationChanged(data);
        if (msg.PlayerId == LocalPlayerId) return;
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.PlayerLocationChanged,
            PlayerLocationChanged = msg,
        });
        if (RemotePlayers.TryGetValue(msg.PlayerId, out var remote))
        {
            remote.Location = msg.Location;
            remote.HasReceivedState = false;
        }
        Console.WriteLine($"[Net] Player {msg.PlayerId} moved to {msg.Location}");
    }

    private void HandleWorldState(byte[] data)
    {
        var msg = NetSerializer.ReadWorldState(data);
        for (int i = 0; i < msg.PlayerCount; i++)
        {
            var (id, state) = msg.Players[i];
            if (id == LocalPlayerId) continue; // skip self

            if (RemotePlayers.TryGetValue(id, out var remote))
            {
                remote.State = state;
                remote.HasReceivedState = true;
            }
            else
            {
                // This should NEVER happen
                Console.WriteLine($"[Net] Warning: received state for unknown player {id}");
            }
        }
    }

    private void HandleNpcStates(byte[] data)
    {
        var msg = NetSerializer.ReadNpcStates(data);
        LatestNpcStateLocation = msg.Location;
        LatestNpcStates = msg.Npcs;
        LatestNotSentNpcStates = msg.NotSentNpcs;
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.NpcStates,
            NpcStates = msg,
        });
    }

    private void HandleNpcHit(byte[] data)
    {
        var msg = NetSerializer.ReadServerNpcHit(data);
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.NpcHit,
            NpcHit = msg,
        });
    }

    private void HandleNpcKillReward(byte[] data)
    {
        var msg = NetSerializer.ReadNpcKillReward(data);
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.NpcKillReward,
            NpcKillReward = msg,
        });
    }

    private void HandleInteractDerelictResult(byte[] data)
    {
        _pendingDerelictResults.Add(NetSerializer.ReadInteractDerelictResult(data));
    }

    private void HandleInteractDistressResult(byte[] data)
    {
        _pendingDistressResults.Add(NetSerializer.ReadInteractDistressResult(data));
    }

    private void HandlePlayerTransitionStarted(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerTransitionStarted(data);
        if (msg.PlayerId == LocalPlayerId) return;
        if (RemotePlayers.TryGetValue(msg.PlayerId, out var remote))
        {
            remote.PendingTransition = msg.Transition;
        }
    }
}
