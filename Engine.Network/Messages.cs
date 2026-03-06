using System.Numerics;

namespace Engine.Network;

/// <summary>
/// Compact per-tick state of a player entity, sent by each client and relayed by the server.
/// Contains everything other clients need to render this player.
/// </summary>
public struct NetPlayerState
{
    /// <summary>World position.</summary>
    public Vector2 Position;
    /// <summary>Rotation in degrees.</summary>
    public float Rotation;
    /// <summary>Linear velocity.</summary>
    public Vector2 Velocity;
    /// <summary>Current hull HP.</summary>
    public float Hull;
    /// <summary>Max hull HP.</summary>
    public float MaxHull;
    /// <summary>Current shield HP.</summary>
    public float Shield;
    /// <summary>Max shield HP.</summary>
    public float MaxShield;
    /// <summary>Whether the fire button is held (for visual thruster/weapon effects).</summary>
    public bool Shooting;
    /// <summary>Ship input acceleration direction (for thruster visuals).</summary>
    public Vector2 AccelerationDirection;
}

// ────────────────────────────────────────────────────────────────
//  Client → Server messages
// ────────────────────────────────────────────────────────────────

/// <summary>Client requests to join. Sent once on connection.</summary>
public struct JoinMessage
{
    /// <summary>Display name for this player.</summary>
    public string PlayerName;
    /// <summary>Star system index the player wants to join (-1 = server decides).</summary>
    public int StarSystemIndex;
}

/// <summary>Client sends its own player state each tick.</summary>
public struct PlayerStateMessage
{
    public NetPlayerState State;
}

/// <summary>Client notifies the server of a location change (e.g. star system jump).</summary>
public struct LocationChangedMessage
{
    /// <summary>New star system index.</summary>
    public int StarSystemIndex;
}

// ────────────────────────────────────────────────────────────────
//  Server → Client messages
// ────────────────────────────────────────────────────────────────

/// <summary>Server acknowledges a join request.</summary>
public struct WelcomeMessage
{
    /// <summary>Unique player ID assigned by the server (0-based).</summary>
    public byte PlayerId;
    /// <summary>Galaxy seed for this server session.</summary>
    public ulong GalaxySeed;
    /// <summary>Star system index the server is currently running.</summary>
    public int StarSystemIndex;
    /// <summary>Server's current global simulation time.</summary>
    public double GlobalTime;
    /// <summary>Number of players already connected (including this one).</summary>
    public byte PlayerCount;
}

/// <summary>Notification that a new player has joined.</summary>
public struct PlayerJoinedMessage
{
    public byte PlayerId;
    public string PlayerName;
    /// <summary>Star system the player is in.</summary>
    public int StarSystemIndex;
    public NetPlayerState InitialState;
}

/// <summary>Notification that a player has left.</summary>
public struct PlayerLeftMessage
{
    public byte PlayerId;
}

/// <summary>Notification that a player changed star system.</summary>
public struct PlayerLocationChangedMessage
{
    public byte PlayerId;
    public int StarSystemIndex;
}

/// <summary>
/// Per-tick broadcast of all player states. Each client uses this to update remote players.
/// The sender's own state is included (the client can ignore it or use it for reconciliation).
/// </summary>
public struct WorldStateMessage
{
    /// <summary>Number of player entries.</summary>
    public byte PlayerCount;
    /// <summary>Server's authoritative global time at the moment of this snapshot.</summary>
    public double ServerTime;
    /// <summary>Per-player ID + state pairs.</summary>
    public (byte PlayerId, NetPlayerState State)[] Players;
}
