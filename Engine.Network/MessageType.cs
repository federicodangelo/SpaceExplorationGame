namespace Engine.Network;

/// <summary>
/// Identifies the type of a network message. First byte of every message on the wire.
/// </summary>
public enum MessageType : byte
{
    // ── Client → Server ─────────────────────────────────────────
    /// <summary>Client requests to join the game.</summary>
    C_Join = 1,
    /// <summary>Client sends its player entity state each tick.</summary>
    C_PlayerState = 2,
    /// <summary>Client changed location (e.g. jumped to a different star system).</summary>
    C_LocationChanged = 3,
    /// <summary>Client is disconnecting gracefully.</summary>
    C_Disconnect = 4,
    /// <summary>Client reports hitting an NPC (damage, remaining health, killed).</summary>
    C_NpcHit = 5,
    /// <summary>Client reports the local player was killed by an NPC.</summary>
    C_PlayerKilledByNpc = 6,

    // ── Server → Client ─────────────────────────────────────────
    /// <summary>Server acknowledges join, assigns player ID, sends world info.</summary>
    S_Welcome = 128,
    /// <summary>A new player has connected.</summary>
    S_PlayerJoined = 129,
    /// <summary>A player has disconnected.</summary>
    S_PlayerLeft = 130,
    /// <summary>Snapshot of all remote player states (broadcast every tick).</summary>
    S_WorldState = 131,
    /// <summary>A player changed location (star system).</summary>
    S_PlayerLocationChanged = 132,
    /// <summary>Batch of NPC state snapshots for the client's current location.</summary>
    S_NpcStates = 133,
    /// <summary>An NPC was hit by a player (broadcast to all clients in the location).</summary>
    S_NpcHit = 134,
    /// <summary>An NPC was killed; includes reward info for the killer.</summary>
    S_NpcKillReward = 135,
}
