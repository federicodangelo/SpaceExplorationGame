using System.Buffers;
using System.Numerics;

namespace Engine.Network;

/// <summary>
/// Compact binary serialization for network messages.
/// Wire format: [1-byte MessageType][payload bytes].
/// All multi-byte values are little-endian (BinaryWriter default on .NET).
/// </summary>
public static class NetSerializer
{
    // ────────────────────────────────────────────────────────────
    //  Write helpers
    // ────────────────────────────────────────────────────────────

    public static byte[] Write(in JoinMessage msg)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_Join);
        w.Write(msg.PlayerName ?? string.Empty);
        w.Write(msg.StarSystemIndex);
        return ms.ToArray();
    }

    public static byte[] Write(in PlayerStateMessage msg)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_PlayerState);
        WritePlayerState(w, msg.State);
        return ms.ToArray();
    }

    public static byte[] Write(in LocationChangedMessage msg)
    {
        using var ms = new MemoryStream(8);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_LocationChanged);
        w.Write(msg.StarSystemIndex);
        return ms.ToArray();
    }

    public static byte[] Write(in DisconnectMessage msg)
    {
        using var ms = new MemoryStream(2);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_Disconnect);
        return ms.ToArray();
    }

    public static byte[] Write(in WelcomeMessage msg)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_Welcome);
        w.Write(msg.PlayerId);
        w.Write(msg.GalaxySeed);
        w.Write(msg.StarSystemIndex);
        w.Write(msg.GlobalTime);
        w.Write(msg.PlayerCount);
        return ms.ToArray();
    }

    public static byte[] Write(in PlayerJoinedMessage msg)
    {
        using var ms = new MemoryStream(128);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerJoined);
        w.Write(msg.PlayerId);
        w.Write(msg.PlayerName ?? string.Empty);
        w.Write(msg.StarSystemIndex);
        WritePlayerState(w, msg.InitialState);
        return ms.ToArray();
    }

    public static byte[] Write(in PlayerLeftMessage msg)
    {
        using var ms = new MemoryStream(4);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerLeft);
        w.Write(msg.PlayerId);
        return ms.ToArray();
    }

    public static byte[] Write(in PlayerLocationChangedMessage msg)
    {
        using var ms = new MemoryStream(8);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerLocationChanged);
        w.Write(msg.PlayerId);
        w.Write(msg.StarSystemIndex);
        return ms.ToArray();
    }

    public static byte[] Write(in WorldStateMessage msg)
    {
        // 1 (type) + 1 (count) + 8 (time) + count * (1 + playerState size)
        int estimatedSize = 10 + msg.PlayerCount * 48;
        using var ms = new MemoryStream(estimatedSize);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_WorldState);
        w.Write(msg.PlayerCount);
        w.Write(msg.ServerTime);
        for (int i = 0; i < msg.PlayerCount; i++)
        {
            w.Write(msg.Players[i].PlayerId);
            WritePlayerState(w, msg.Players[i].State);
        }
        return ms.ToArray();
    }

    // ────────────────────────────────────────────────────────────
    //  Read helpers
    // ────────────────────────────────────────────────────────────

    /// <summary>Peek at the message type without consuming the buffer.</summary>
    public static MessageType PeekType(ReadOnlySpan<byte> data) => (MessageType)data[0];

    public static JoinMessage ReadJoin(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new JoinMessage
        {
            PlayerName = r.ReadString(),
            StarSystemIndex = r.ReadInt32(),
        };
    }

    public static PlayerStateMessage ReadPlayerState(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new PlayerStateMessage { State = ReadNetPlayerState(r) };
    }

    public static LocationChangedMessage ReadLocationChanged(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new LocationChangedMessage { StarSystemIndex = r.ReadInt32() };
    }

    public static WelcomeMessage ReadWelcome(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new WelcomeMessage
        {
            PlayerId = r.ReadByte(),
            GalaxySeed = r.ReadUInt64(),
            StarSystemIndex = r.ReadInt32(),
            GlobalTime = r.ReadDouble(),
            PlayerCount = r.ReadByte(),
        };
    }

    public static PlayerJoinedMessage ReadPlayerJoined(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new PlayerJoinedMessage
        {
            PlayerId = r.ReadByte(),
            PlayerName = r.ReadString(),
            StarSystemIndex = r.ReadInt32(),
            InitialState = ReadNetPlayerState(r),
        };
    }

    public static PlayerLeftMessage ReadPlayerLeft(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new PlayerLeftMessage { PlayerId = r.ReadByte() };
    }

    public static PlayerLocationChangedMessage ReadPlayerLocationChanged(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new PlayerLocationChangedMessage
        {
            PlayerId = r.ReadByte(),
            StarSystemIndex = r.ReadInt32(),
        };
    }

    public static WorldStateMessage ReadWorldState(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        byte count = r.ReadByte();
        double serverTime = r.ReadDouble();
        var players = new (byte, NetPlayerState)[count];
        for (int i = 0; i < count; i++)
        {
            byte id = r.ReadByte();
            players[i] = (id, ReadNetPlayerState(r));
        }
        return new WorldStateMessage
        {
            PlayerCount = count,
            ServerTime = serverTime,
            Players = players,
        };
    }

    // ────────────────────────────────────────────────────────────
    //  NetPlayerState sub-serialization
    // ────────────────────────────────────────────────────────────

    private static void WritePlayerState(BinaryWriter w, in NetPlayerState s)
    {
        w.Write(s.Position.X);
        w.Write(s.Position.Y);
        w.Write(s.Rotation);
        w.Write(s.Velocity.X);
        w.Write(s.Velocity.Y);
        w.Write(s.Hull);
        w.Write(s.MaxHull);
        w.Write(s.Shield);
        w.Write(s.MaxShield);
        w.Write(s.Shooting);
        w.Write(s.AccelerationDirection.X);
        w.Write(s.AccelerationDirection.Y);
        w.Write(s.ShipTypeId ?? string.Empty);
    }

    private static NetPlayerState ReadNetPlayerState(BinaryReader r)
    {
        return new NetPlayerState
        {
            Position = new Vector2(r.ReadSingle(), r.ReadSingle()),
            Rotation = r.ReadSingle(),
            Velocity = new Vector2(r.ReadSingle(), r.ReadSingle()),
            Hull = r.ReadSingle(),
            MaxHull = r.ReadSingle(),
            Shield = r.ReadSingle(),
            MaxShield = r.ReadSingle(),
            Shooting = r.ReadBoolean(),
            AccelerationDirection = new Vector2(r.ReadSingle(), r.ReadSingle()),
            ShipTypeId = r.ReadString() is { Length: > 0 } sid ? sid : null,
        };
    }
}
