using System.Numerics;
using Engine.Network;
using SpaceExplorationGame.ECS.Components;
using Arch.Core;

namespace SpaceExplorationGame.Core;

/// <summary>
/// Client-side network manager. Connects to a dedicated server, sends local player state,
/// and receives remote player states to update their ECS entities.
/// 
/// Usage: call <see cref="ConnectAsync"/> once, then <see cref="SendLocalState"/> +
/// <see cref="ProcessMessages"/> every frame from the active game state.
/// </summary>
public sealed class NetworkManager : IDisposable
{
    private readonly GameClient _client = new();

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

    /// <summary>Raised when a remote player joins.</summary>
    public event Action<byte, string>? OnRemotePlayerJoined;

    /// <summary>Raised when a remote player leaves.</summary>
    public event Action<byte>? OnRemotePlayerLeft;

    /// <summary>
    /// Connect to the server and send a join request.
    /// Blocks until the connection is established (but the join handshake is async).
    /// </summary>
    public async Task ConnectAsync(string url, string playerName)
    {
        await _client.ConnectAsync(url);
        var joinMsg = new JoinMessage { PlayerName = playerName };
        _client.Send(NetSerializer.Write(joinMsg));
    }

    /// <summary>
    /// Send the local player's current entity state to the server.
    /// Call once per tick from the active game state.
    /// </summary>
    public void SendLocalState(World ecsWorld, Entity playerEntity)
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
        }

        _client.Send(NetSerializer.Write(new PlayerStateMessage { State = state }));
    }

    /// <summary>
    /// Drain all inbound messages from the network receive queue.
    /// Call once per frame (not per tick) from the active game state.
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
            }
        }
    }

    public void Dispose()
    {
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
        RemotePlayers[msg.PlayerId] = new RemotePlayer(msg.PlayerId, msg.PlayerName);
        OnRemotePlayerJoined?.Invoke(msg.PlayerId, msg.PlayerName);
        Console.WriteLine($"[Net] Player {msg.PlayerId} ({msg.PlayerName}) joined");
    }

    private void HandlePlayerLeft(byte[] data)
    {
        var msg = NetSerializer.ReadPlayerLeft(data);
        RemotePlayers.Remove(msg.PlayerId);
        OnRemotePlayerLeft?.Invoke(msg.PlayerId);
        Console.WriteLine($"[Net] Player {msg.PlayerId} left");
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
                var newRemote = new RemotePlayer(id, $"Player {id}")
                {
                    LastState = state,
                    HasReceivedState = true,
                };
                RemotePlayers[id] = newRemote;
                OnRemotePlayerJoined?.Invoke(id, newRemote.Name);
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

    /// <summary>Latest state received from the server.</summary>
    public NetPlayerState LastState;

    /// <summary>True once at least one state update has been received.</summary>
    public bool HasReceivedState;

    /// <summary>ECS entity for rendering this remote player (created by the game state).</summary>
    public Entity Entity;

    /// <summary>Whether the game state has created an ECS entity for this remote player.</summary>
    public bool HasEntity;

    public RemotePlayer(byte playerId, string name)
    {
        PlayerId = playerId;
        Name = name;
    }
}
