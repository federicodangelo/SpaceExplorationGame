using System.Numerics;

namespace Engine.Network.Client;

/// <summary>
/// Event types produced by <see cref="ClientNetworkManager"/> after processing inbound messages.
/// </summary>
public enum ClientEventType { PlayerJoined, PlayerLeft, PlayerLocationChanged }

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

    /// <summary>Remote player states received from the server, keyed by player ID.</summary>
    public Dictionary<byte, RemotePlayer> RemotePlayers { get; } = new();

    /// <summary>
    /// Connect to the server and send a join request.
    /// Blocks until the connection is established (but the join handshake is async).
    /// </summary>
    public async Task ConnectAsync(string url, string playerName, NetPlayerInfo info, NetPlayerLocation location)
    {
        await _client.ConnectAsync(url);
        var joinMsg = new C_JoinMessage { PlayerName = playerName, PlayerInfo = info, PlayerLocation = location };
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
    /// Drain all inbound messages from the network receive queue.
    /// Call once per frame from the game loop. After calling, use
    /// <see cref="DrainEvents"/> to get any join/leave events.
    /// </summary>
    public void ProcessMessages()
    {
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
}

/// <summary>
/// Represents a remote player as seen by the client.
/// </summary>
public class RemotePlayer
{
    public byte PlayerId { get; }
    public string Name { get; }

    public NetPlayerLocation Location;
    public NetPlayerState State;
    public NetPlayerInfo Info;

    /// <summary>True once at least one state update has been received.</summary>
    public bool HasReceivedState;

    public RemotePlayer(byte playerId, string name, NetPlayerLocation location, NetPlayerInfo info, NetPlayerState state)
    {
        PlayerId = playerId;
        Name = name;
        Location = location;
        Info = info;
        State = state;
    }
}
