using System.Numerics;

namespace Engine.Network;

/// <summary>
/// Compact per-tick state of a player entity, sent by each client and relayed by the server.
/// Contains everything other clients need to render this player.
/// </summary>
public struct NetPlayerState
{
    /// <summary>Whether the player is alive (i.e. has a valid entity in the world). If false, the rest of the fields may be ignored.</summary>
    public bool Alive;
    /// <summary>World position.</summary>
    public Vector2 Position;
    /// <summary>Rotation in degrees.</summary>
    public float Rotation;
    /// <summary>Linear velocity.</summary>
    public Vector2 Velocity;
    /// <summary>Current hull HP.</summary>
    public float Hull;
    /// <summary>Current shield HP.</summary>
    public float Shield;
    /// <summary>Whether the player is currently shooting their weapon.</summary>
    public bool Shooting;
    /// <summary>Aim direction.</summary>
    public Vector2 AimDirection;
    /// <summary>Acceleration direction.</summary>
    public Vector2 AccelerationDirection;
    /// <summary>Rotation speed.</summary>
    public float RotationSpeed;
}

/// <summary>
/// Additional player info that doesn't change every tick, sent by the client on join and relayed by the server.
/// </summary>
public struct NetPlayerInfo
{
    /// <summary>Ship type identifier (e.g. "scout", "fighter"). Null means unknown/default.</summary>
    public string ShipTypeId;
    public int MaxHull;
    public int MaxShield;
}

/// <summary>
/// Player location info, sent by the server on join and whenever the player changes location.
/// </summary>
public record struct NetPlayerLocation
{
    /// <summary>Solar system index the player is currently in (players are always in a star system).</summary>
    public int SolarSystemIndex;
    public int SpaceStationIndex; // -1 if not docked
    public int PlanetIndex; // -1 if not landed
    public int MoonIndex; // -1 if not landed on a moon
    public int SettlementIndex; // -1 if not landed on a settlement

    public override string ToString()
    {
        if (SpaceStationIndex >= 0)
            return $"space station {SpaceStationIndex} in star system {SolarSystemIndex}";
        else if (PlanetIndex >= 0)
        {
            if (MoonIndex >= 0)
                return $"moon {MoonIndex} of planet {PlanetIndex} in star system {SolarSystemIndex}";
            else if (SettlementIndex >= 0)
                return $"settlement {SettlementIndex} on planet {PlanetIndex} in star system {SolarSystemIndex}";
            else
                return $"planet {PlanetIndex} in star system {SolarSystemIndex}";
        }
        else
            return $"star system {SolarSystemIndex}";
    }

    static public NetPlayerLocation ForSolarSystem(int solarSystemIndex) => new NetPlayerLocation
    {
        SolarSystemIndex = solarSystemIndex,
        SpaceStationIndex = -1,
        PlanetIndex = -1,
        MoonIndex = -1,
        SettlementIndex = -1
    };

    static public NetPlayerLocation ForSpaceStation(int solarSystemIndex, int spaceStationIndex) => new NetPlayerLocation
    {
        SolarSystemIndex = solarSystemIndex,
        SpaceStationIndex = spaceStationIndex,
        PlanetIndex = -1,
        MoonIndex = -1,
        SettlementIndex = -1
    };

    static public NetPlayerLocation ForPlanet(int solarSystemIndex, int planetIndex) => new NetPlayerLocation
    {
        SolarSystemIndex = solarSystemIndex,
        SpaceStationIndex = -1,
        PlanetIndex = planetIndex,
        MoonIndex = -1,
        SettlementIndex = -1
    };

    static public NetPlayerLocation ForMoon(int solarSystemIndex, int planetIndex, int moonIndex) => new NetPlayerLocation
    {
        SolarSystemIndex = solarSystemIndex,
        SpaceStationIndex = -1,
        PlanetIndex = planetIndex,
        MoonIndex = moonIndex,
        SettlementIndex = -1
    };

    static public NetPlayerLocation ForPlanetSettlement(int solarSystemIndex, int planetIndex, int settlementIndex) => new NetPlayerLocation
    {
        SolarSystemIndex = solarSystemIndex,
        SpaceStationIndex = -1,
        PlanetIndex = planetIndex,
        MoonIndex = -1,
        SettlementIndex = settlementIndex
    };

    static public NetPlayerLocation ForUnknown() => new NetPlayerLocation
    {
        SolarSystemIndex = -1,
        SpaceStationIndex = -1,
        PlanetIndex = -1,
        MoonIndex = -1,
        SettlementIndex = -1
    };
}

// ────────────────────────────────────────────────────────────────
//  Client → Server messages
// ────────────────────────────────────────────────────────────────

/// <summary>Client requests to join. Sent once on connection.</summary>
public struct C_JoinMessage
{
    public string PlayerName;
    public NetPlayerInfo PlayerInfo;
}

/// <summary>Client sends its own player state each tick.</summary>
public struct C_PlayerStateMessage
{
    public NetPlayerState State;
}

/// <summary>Client is disconnecting gracefully.</summary>
public struct C_DisconnectMessage
{

}

/// <summary>Client notifies the server of a location change (e.g. star system jump).</summary>
public struct C_LocationChangedMessage
{
    public NetPlayerLocation NewLocation;
}

// ────────────────────────────────────────────────────────────────
//  Server → Client messages
// ────────────────────────────────────────────────────────────────

/// <summary>Server acknowledges a join request.</summary>
public struct S_WelcomeMessage
{
    /// <summary>Unique player ID assigned by the server (0-based).</summary>
    public byte PlayerId;
    /// <summary>Galaxy seed for this server session.</summary>
    public ulong GalaxySeed;
    /// <summary>Server's current global simulation time.</summary>
    public double GlobalTime;
    /// <summary>Number of players already connected (including this one).</summary>
    public byte PlayerCount;
    /// <summary>Starting player location.</summary>
    public NetPlayerLocation PlayerLocation;
    // <summary>Starting player position in the map.</summary>
    public Vector2 PlayerCoordinates;
}

/// <summary>Notification that a new player has joined.</summary>
public struct S_PlayerJoinedMessage
{
    public byte PlayerId;
    public string Name;
    public NetPlayerLocation Location;
    public NetPlayerInfo Info;
    public NetPlayerState State;
}

/// <summary>Notification that a player has left.</summary>
public struct S_PlayerLeftMessage
{
    public byte PlayerId;
}

/// <summary>Notification that a player changed star system.</summary>
public struct S_PlayerLocationChangedMessage
{
    public byte PlayerId;
    public NetPlayerLocation Location;
    public Vector2 Coordinates;
}

/// <summary>
/// Per-tick broadcast of all player states. Each client uses this to update remote players.
/// The sender's own state is included (the client can ignore it or use it for reconciliation).
/// </summary>
public struct S_WorldStateMessage
{
    /// <summary>Number of player entries.</summary>
    public byte PlayerCount;
    /// <summary>Server's authoritative global time at the moment of this snapshot.</summary>
    public double ServerTime;
    /// <summary>Per-player ID + state pairs.</summary>
    public (byte PlayerId, NetPlayerState State)[] Players;
}

// ────────────────────────────────────────────────────────────────
//  NPC synchronization messages
// ────────────────────────────────────────────────────────────────

/// <summary>
/// Describes the type of NPC entity for synchronization.
/// </summary>
public enum NetNpcType : byte
{
    /// <summary>A ship in a solar system (EnemyAI + ShipComponent).</summary>
    Ship = 0,
    /// <summary>A foot NPC on a planet surface (SurfaceAI + AvatarComponent).</summary>
    SurfaceAvatar = 1,
    /// <summary>A landed NPC ship on a planet surface.</summary>
    LandedShip = 2,
}

/// <summary>
/// Per-NPC state snapshot sent from server to clients. Contains everything needed
/// to create, update, or destroy an NPC entity on the client side.
/// </summary>
public struct NetNpcState
{
    /// <summary>Unique NPC identifier (auto-incrementing int assigned by the server).</summary>
    public int NpcId;
    /// <summary>The type of NPC entity.</summary>
    public NetNpcType NpcType;
    /// <summary>Faction affiliation (pirate, trader, patrol).</summary>
    public byte Faction;
    /// <summary>Ship type ID (e.g. "scout", "fighter") for ships; empty for surface NPCs.</summary>
    public string ShipTypeId;
    /// <summary>Quality tier for equipment loadout (used with NpcId seed for deterministic loadout).</summary>
    public int QualityTier;
    /// <summary>Danger level of the NPC.</summary>
    public int DangerLevel;
    /// <summary>World position.</summary>
    public Vector2 Position;
    /// <summary>Rotation in degrees.</summary>
    public float Rotation;
    /// <summary>Linear velocity.</summary>
    public Vector2 Velocity;
    /// <summary>Current hull HP.</summary>
    public float Hull;
    /// <summary>Current shield HP.</summary>
    public float Shield;
    /// <summary>True if the NPC was destroyed (final message).</summary>
    public bool Dead;
    /// <summary>Whether the NPC has an active warp effect.</summary>
    public bool Warping;
    /// <summary>True = warping in, false = warping out (only relevant if Warping is true).</summary>
    public bool WarpingIn;
    /// <summary>Warp animation progress 0→1 (only relevant if Warping is true).</summary>
    public float WarpProgress;
    /// <summary>Warp animation total duration (only relevant if Warping is true).</summary>
    public float WarpDuration;
    /// <summary>For surface avatars: whether the NPC is shooting.</summary>
    public bool Shooting;
    /// <summary>For surface avatars: aim direction.</summary>
    public Vector2 AimDirection;
    /// <summary>For landed ships: landing/takeoff animation progress 0→1.</summary>
    public float LandedAnimProgress;
    /// <summary>For landed ships: true = landing, false = taking off.</summary>
    public bool LandedIsLanding;
}

// Used for NPCs that the server hasn't sent a state update for yet
public struct NetNotSentNpcState
{
    public int NpcId;
}

/// <summary>Server sends batch of NPC states for the client's current location.</summary>
public struct S_NpcStatesMessage
{
    /// <summary>Player location these NPCs belong to (clients can ignore NPCs that don't match their current location).</summary>
    public NetPlayerLocation Location;
    /// <summary>Number of NPC entries.</summary>
    public int NpcCount;
    /// <summary>NPC state snapshots.</summary>
    public NetNpcState[] Npcs;
    /// <summary>Number of not sent NPC entries.</summary>
    public int NotSentNpcCount;
    /// <summary>Ids of the npcs whose information was not sent, to keep them alive in the clients</summary>
    public NetNotSentNpcState[] NotSentNpcs; // NPCs that exist but haven't had their full state sent yet (e.g. just spawned)
}

/// <summary>Client reports hitting an NPC.</summary>
public struct C_NpcHitMessage
{
    /// <summary>The NPC that was hit.</summary>
    public int NpcId;
    /// <summary>Damage dealt to the NPC.</summary>
    public float Damage;
    /// <summary>Remaining hull after the hit.</summary>
    public float RemainingHull;
    /// <summary>Remaining shield after the hit.</summary>
    public float RemainingShield;
    /// <summary>True if the NPC was killed by this hit.</summary>
    public bool Killed;
}

/// <summary>Server broadcasts that an NPC was hit by a player.</summary>
public struct S_NpcHitMessage
{
    /// <summary>The NPC that was hit.</summary>
    public int NpcId;
    /// <summary>The player who hit the NPC.</summary>
    public byte PlayerId;
    /// <summary>Damage dealt.</summary>
    public float Damage;
    /// <summary>Remaining hull after the hit.</summary>
    public float RemainingHull;
    /// <summary>Remaining shield after the hit.</summary>
    public float RemainingShield;
    /// <summary>True if the NPC was killed by this hit.</summary>
    public bool Killed;
}

/// <summary>Server notifies a player about NPC kill rewards.</summary>
public struct S_NpcKillRewardMessage
{
    /// <summary>The NPC that was killed.</summary>
    public int NpcId;
    /// <summary>Credits rewarded to the killer.</summary>
    public int Credits;
    /// <summary>Display message (e.g. "+500 CREDITS +3 IRON").</summary>
    public string RewardMessage;
}

/// <summary>Client reports that the local player was killed by an NPC.</summary>
public struct C_PlayerKilledByNpcMessage
{
    /// <summary>The NPC that killed the player (-1 if unknown).</summary>
    public int NpcId;
}
