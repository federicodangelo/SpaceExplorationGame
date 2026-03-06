using System.Numerics;
using Engine.Network;
using SpaceExplorationGame.ECS.Components;
using Arch.Core;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Event types produced by <see cref="NetworkManager"/> after processing inbound messages.
/// </summary>
public enum ClientEventType { PlayerJoined, PlayerLeft }

/// <summary>
/// A client-side network event, queued by <see cref="NetworkManager.ProcessMessages"/>
/// and drained on the main thread via <see cref="NetworkManager.DrainEvents"/>.
/// </summary>
public readonly struct ClientEvent
{
    public ClientEventType Type { get; init; }
    public byte PlayerId { get; init; }
    public string PlayerName { get; init; }
    public int StarSystemIndex { get; init; }
}

/// <summary>
/// Client-side network manager. Connects to a dedicated server, sends local player state,
/// and receives remote player states to update their ECS entities.
/// 
/// Usage: call <see cref="ConnectAsync"/> once, then <see cref="SendLocalState"/> +
/// <see cref="ProcessMessages"/> every frame from the game loop.
/// Call <see cref="DrainEvents"/> after ProcessMessages to get join/leave events.
/// </summary>
public sealed class NetworkManager : IDisposable
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

    /// <summary>Star system index the server is running.</summary>
    public int ServerStarSystemIndex { get; private set; }

    /// <summary>Server's global time at the moment of the welcome message.</summary>
    public double ServerGlobalTime { get; private set; }

    /// <summary>Remote player states received from the server, keyed by player ID.</summary>
    public Dictionary<byte, RemotePlayer> RemotePlayers { get; } = new();

    /// <summary>
    /// Connect to the server and send a join request.
    /// Blocks until the connection is established (but the join handshake is async).
    /// </summary>
    public async Task ConnectAsync(string url, string playerName, int starSystemIndex = -1)
    {
        await _client.ConnectAsync(url);
        var joinMsg = new JoinMessage { PlayerName = playerName, StarSystemIndex = starSystemIndex };
        _client.Send(NetSerializer.Write(joinMsg));
    }

    /// <summary>
    /// Send a location change notification to the server (e.g. after a star system jump).
    /// </summary>
    public void SendLocationChanged(int starSystemIndex)
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(new LocationChangedMessage { StarSystemIndex = starSystemIndex }));
    }

    /// <summary>
    /// Send the local player's current entity state to the server.
    /// Call once per tick from the active game state.
    /// </summary>
    public void SendLocalState(World ecsWorld, Entity playerEntity, string? shipTypeId = null)
    {
        if (!IsJoined || !_client.IsConnected) return;
        if (!ecsWorld.IsAlive(playerEntity)) return;

        var transform = ecsWorld.Get<Transform>(playerEntity);
        var velocity = ecsWorld.Get<Velocity>(playerEntity);

        var state = new NetPlayerState
        {
            Position = transform.Position,
            Rotation = transform.Rotation,
            Velocity = velocity.Linear,
            ShipTypeId = shipTypeId,
        };

        if (ecsWorld.Has<Health>(playerEntity))
        {
            var health = ecsWorld.Get<Health>(playerEntity);
            state.Hull = health.Hull;
            state.MaxHull = health.MaxHull;
            state.Shield = health.Shield;
            state.MaxShield = health.MaxShield;
        }

        if (ecsWorld.Has<ShipInputComponent>(playerEntity))
        {
            var input = ecsWorld.Get<ShipInputComponent>(playerEntity);
            state.Shooting = input.Shoot;
            state.AccelerationDirection = input.AccelerationDirection;
            state.RotationSpeed = input.RotationSpeed;
        }

        _client.Send(NetSerializer.Write(new PlayerStateMessage { State = state }));
    }

    /// <summary>
    /// Send a graceful disconnect notification before closing.
    /// </summary>
    public void SendDisconnect()
    {
        if (!IsJoined || !_client.IsConnected) return;
        _client.Send(NetSerializer.Write(new DisconnectMessage()));
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
        ServerStarSystemIndex = msg.StarSystemIndex;
        ServerGlobalTime = msg.GlobalTime;
        IsJoined = true;
        Console.WriteLine($"[Net] Joined server as player {msg.PlayerId} (system {msg.StarSystemIndex}, {msg.PlayerCount} players)");
    }

    private void HandlePlayerJoined(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerJoined(data);
        if (msg.PlayerId == LocalPlayerId) return;
        RemotePlayers[msg.PlayerId] = new RemotePlayer(msg.PlayerId, msg.PlayerName, msg.StarSystemIndex);
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.PlayerJoined,
            PlayerId = msg.PlayerId,
            PlayerName = msg.PlayerName,
            StarSystemIndex = msg.StarSystemIndex,
        });
        Console.WriteLine($"[Net] Player {msg.PlayerId} ({msg.PlayerName}) joined system {msg.StarSystemIndex}");
    }

    private void HandlePlayerLeft(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerLeft(data);
        RemotePlayers.Remove(msg.PlayerId);
        _events.Add(new ClientEvent
        {
            Type = ClientEventType.PlayerLeft,
            PlayerId = msg.PlayerId,
            PlayerName = string.Empty,
        });
        Console.WriteLine($"[Net] Player {msg.PlayerId} left");
    }

    private void HandlePlayerLocationChanged(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerLocationChanged(data);
        if (msg.PlayerId == LocalPlayerId) return;
        if (RemotePlayers.TryGetValue(msg.PlayerId, out var remote))
        {
            remote.StarSystemIndex = msg.StarSystemIndex;
            remote.HasReceivedState = false;
        }
        Console.WriteLine($"[Net] Player {msg.PlayerId} moved to system {msg.StarSystemIndex}");
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
                remote.LastState = state;
                remote.HasReceivedState = true;
            }
            else
            {
                // Player joined before we got an S_PlayerJoined — create on the fly
                var newRemote = new RemotePlayer(id, $"Player {id}", -1)
                {
                    LastState = state,
                    HasReceivedState = true,
                };
                RemotePlayers[id] = newRemote;
                _events.Add(new ClientEvent
                {
                    Type = ClientEventType.PlayerJoined,
                    PlayerId = id,
                    PlayerName = newRemote.Name,
                    StarSystemIndex = -1,
                });
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

    /// <summary>Star system index the remote player is in.</summary>
    public int StarSystemIndex { get; set; }

    /// <summary>Latest state received from the server.</summary>
    public NetPlayerState LastState;

    /// <summary>True once at least one state update has been received.</summary>
    public bool HasReceivedState;

    public RemotePlayer(byte playerId, string name, int starSystemIndex)
    {
        PlayerId = playerId;
        Name = name;
        StarSystemIndex = starSystemIndex;
    }
}
