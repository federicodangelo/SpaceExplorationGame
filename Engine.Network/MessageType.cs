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

    // ── Server → Client ─────────────────────────────────────────
    /// <summary>Server acknowledges join, assigns player ID, sends world info.</summary>
    S_Welcome = 128,
    /// <summary>A new player has connected.</summary>
    S_PlayerJoined = 129,
    /// <summary>A player has disconnected.</summary>
    S_PlayerLeft = 130,
    /// <summary>Snapshot of all remote player states (broadcast every tick).</summary>
    S_WorldState = 131,
}
