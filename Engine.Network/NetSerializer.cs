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

    public static byte[] Write(in C_JoinMessage msg)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_Join);
        w.Write(msg.PlayerName ?? string.Empty);
        WritePlayerInfo(w, msg.PlayerInfo);
        return ms.ToArray();
    }

    public static byte[] Write(in C_PlayerStateMessage msg)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_PlayerState);
        WritePlayerState(w, msg.State);
        return ms.ToArray();
    }

    public static byte[] Write(in C_LocationChangedMessage msg)
    {
        using var ms = new MemoryStream(20);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_LocationChanged);
        WritePlayerLocation(w, msg.NewLocation);
        return ms.ToArray();
    }

    public static byte[] Write(in C_DisconnectMessage _)
    {
        using var ms = new MemoryStream(2);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_Disconnect);
        return ms.ToArray();
    }

    public static byte[] Write(in C_TransitionStartedMessage msg)
    {
        using var ms = new MemoryStream(4);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_TransitionStarted);
        WritePlayerLocation(w, msg.Transition.From);
        WritePlayerLocation(w, msg.Transition.To);
        return ms.ToArray();
    }

    public static byte[] Write(in S_PlayerTransitionStartedMessage msg)
    {
        using var ms = new MemoryStream(4);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerTransitionStarted);
        w.Write(msg.PlayerId);
        WritePlayerLocation(w, msg.Transition.From);
        WritePlayerLocation(w, msg.Transition.To);
        return ms.ToArray();
    }

    public static byte[] Write(in S_WelcomeMessage msg)
    {
        using var ms = new MemoryStream(48);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_Welcome);
        w.Write(msg.PlayerId);
        w.Write(msg.GalaxySeed);
        w.Write(msg.GlobalTime);
        w.Write(msg.PlayerCount);
        WritePlayerLocation(w, msg.PlayerLocation);
        w.Write(msg.PlayerCoordinates.X);
        w.Write(msg.PlayerCoordinates.Y);
        return ms.ToArray();
    }

    public static byte[] Write(in S_PlayerJoinedMessage msg)
    {
        using var ms = new MemoryStream(128);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerJoined);
        w.Write(msg.PlayerId);
        w.Write(msg.Name ?? string.Empty);
        WritePlayerLocation(w, msg.Location);
        WritePlayerInfo(w, msg.Info);
        WritePlayerState(w, msg.State);
        return ms.ToArray();
    }

    public static byte[] Write(in S_PlayerLeftMessage msg)
    {
        using var ms = new MemoryStream(4);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerLeft);
        w.Write(msg.PlayerId);
        return ms.ToArray();
    }

    public static byte[] Write(in S_PlayerLocationChangedMessage msg)
    {
        using var ms = new MemoryStream(32);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_PlayerLocationChanged);
        w.Write(msg.PlayerId);
        WritePlayerLocation(w, msg.Location);
        w.Write(msg.Coordinates.X);
        w.Write(msg.Coordinates.Y);
        return ms.ToArray();
    }

    public static byte[] Write(in S_WorldStateMessage msg)
    {
        // 1 (type) + 1 (count) + 8 (time) + count * (1 + playerState size)
        int estimatedSize = 10 + msg.PlayerCount * 42;
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

    public static C_JoinMessage ReadJoin(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_JoinMessage
        {
            PlayerName = r.ReadString(),
            PlayerInfo = ReadPlayerInfo(r),
        };
    }

    public static C_PlayerStateMessage ReadPlayerState(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_PlayerStateMessage { State = ReadNetPlayerState(r) };
    }

    public static C_LocationChangedMessage ReadLocationChanged(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_LocationChangedMessage
        {
            NewLocation = ReadPlayerLocation(r),
        };
    }

    public static C_TransitionStartedMessage ReadTransitionStarted(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_TransitionStartedMessage
        {
            Transition = new NetPlayerTransition
            {
                From = ReadPlayerLocation(r),
                To = ReadPlayerLocation(r),
            },
        };
    }

    public static S_PlayerTransitionStartedMessage ReadPlayerTransitionStarted(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_PlayerTransitionStartedMessage
        {
            PlayerId = r.ReadByte(),
            Transition = new NetPlayerTransition
            {
                From = ReadPlayerLocation(r),
                To = ReadPlayerLocation(r),
            },
        };
    }

    public static S_WelcomeMessage ReadWelcome(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_WelcomeMessage
        {
            PlayerId = r.ReadByte(),
            GalaxySeed = r.ReadUInt64(),
            GlobalTime = r.ReadDouble(),
            PlayerCount = r.ReadByte(),
            PlayerLocation = ReadPlayerLocation(r),
            PlayerCoordinates = new Vector2(r.ReadSingle(), r.ReadSingle()),
        };
    }

    public static S_PlayerJoinedMessage ReadPlayerJoined(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_PlayerJoinedMessage
        {
            PlayerId = r.ReadByte(),
            Name = r.ReadString(),
            Location = ReadPlayerLocation(r),
            Info = ReadPlayerInfo(r),
            State = ReadNetPlayerState(r),
        };
    }

    public static S_PlayerLeftMessage ReadPlayerLeft(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_PlayerLeftMessage { PlayerId = r.ReadByte() };
    }

    public static S_PlayerLocationChangedMessage ReadPlayerLocationChanged(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_PlayerLocationChangedMessage
        {
            PlayerId = r.ReadByte(),
            Location = ReadPlayerLocation(r),
            Coordinates = new Vector2(r.ReadSingle(), r.ReadSingle()),
        };
    }

    public static S_WorldStateMessage ReadWorldState(ReadOnlySpan<byte> data)
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
        return new S_WorldStateMessage
        {
            PlayerCount = count,
            ServerTime = serverTime,
            Players = players,
        };
    }

    // ────────────────────────────────────────────────────────────
    //  Shared sub-serialization
    // ────────────────────────────────────────────────────────────

    private static void WritePlayerInfo(BinaryWriter w, in NetPlayerInfo info)
    {
        w.Write(info.ShipTypeId ?? string.Empty);
        w.Write(info.MaxHull);
        w.Write(info.MaxShield);
    }

    private static NetPlayerInfo ReadPlayerInfo(BinaryReader r)
    {
        return new NetPlayerInfo
        {
            ShipTypeId = r.ReadString(),
            MaxHull = r.ReadInt32(),
            MaxShield = r.ReadInt32(),
        };
    }

    private static void WritePlayerLocation(BinaryWriter w, in NetPlayerLocation location)
    {
        w.Write(location.SolarSystemIndex);
        w.Write(location.SpaceStationIndex);
        w.Write(location.PlanetIndex);
        w.Write(location.MoonIndex);
        w.Write(location.SettlementIndex);
    }

    private static NetPlayerLocation ReadPlayerLocation(BinaryReader r)
    {
        return new NetPlayerLocation
        {
            SolarSystemIndex = r.ReadInt32(),
            SpaceStationIndex = r.ReadInt32(),
            PlanetIndex = r.ReadInt32(),
            MoonIndex = r.ReadInt32(),
            SettlementIndex = r.ReadInt32(),
        };
    }

    private static void WritePlayerState(BinaryWriter w, in NetPlayerState s)
    {
        w.Write(s.Alive);
        w.Write(s.Position.X);
        w.Write(s.Position.Y);
        w.Write(s.Rotation);
        w.Write(s.Velocity.X);
        w.Write(s.Velocity.Y);
        w.Write(s.Hull);
        w.Write(s.Shield);
        w.Write(s.Shooting);
        w.Write(s.AimDirection.X);
        w.Write(s.AimDirection.Y);
        w.Write(s.AccelerationDirection.X);
        w.Write(s.AccelerationDirection.Y);
        w.Write(s.RotationSpeed);
    }

    private static NetPlayerState ReadNetPlayerState(BinaryReader r)
    {
        return new NetPlayerState
        {
            Alive = r.ReadBoolean(),
            Position = new Vector2(r.ReadSingle(), r.ReadSingle()),
            Rotation = r.ReadSingle(),
            Velocity = new Vector2(r.ReadSingle(), r.ReadSingle()),
            Hull = r.ReadSingle(),
            Shield = r.ReadSingle(),
            Shooting = r.ReadBoolean(),
            AimDirection = new Vector2(r.ReadSingle(), r.ReadSingle()),
            AccelerationDirection = new Vector2(r.ReadSingle(), r.ReadSingle()),
            RotationSpeed = r.ReadSingle(),
        };
    }

    // ────────────────────────────────────────────────────────────
    //  NPC sync write helpers
    // ────────────────────────────────────────────────────────────

    public static byte[] Write(in S_NpcStatesMessage msg)
    {
        int estimatedSize = 5 + msg.NpcCount * 80;
        using var ms = new MemoryStream(estimatedSize);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_NpcStates);
        WritePlayerLocation(w, msg.Location);
        w.Write(msg.NpcCount);
        for (int i = 0; i < msg.NpcCount; i++)
            WriteNpcState(w, msg.Npcs[i]);
        w.Write(msg.NotSentNpcCount);
        for (int i = 0; i < msg.NotSentNpcCount; i++)
            WriteNotSentNpcState(w, msg.NotSentNpcs[i]);
        return ms.ToArray();
    }

    public static byte[] Write(in C_NpcHitMessage msg)
    {
        using var ms = new MemoryStream(32);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_NpcHit);
        w.Write(msg.NpcId);
        w.Write(msg.Damage);
        w.Write(msg.RemainingHull);
        w.Write(msg.RemainingShield);
        w.Write(msg.Killed);
        return ms.ToArray();
    }

    public static byte[] Write(in S_NpcHitMessage msg)
    {
        using var ms = new MemoryStream(32);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_NpcHit);
        w.Write(msg.NpcId);
        w.Write(msg.PlayerId);
        w.Write(msg.Damage);
        w.Write(msg.RemainingHull);
        w.Write(msg.RemainingShield);
        w.Write(msg.Killed);
        return ms.ToArray();
    }

    public static byte[] Write(in S_NpcKillRewardMessage msg)
    {
        using var ms = new MemoryStream(64);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_NpcKillReward);
        w.Write(msg.NpcId);
        w.Write(msg.Credits);
        w.Write(msg.RewardMessage ?? string.Empty);
        return ms.ToArray();
    }

    public static byte[] Write(in C_PlayerKilledByNpcMessage msg)
    {
        using var ms = new MemoryStream(8);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_PlayerKilledByNpc);
        w.Write(msg.NpcId);
        return ms.ToArray();
    }

    // ────────────────────────────────────────────────────────────
    //  NPC sync read helpers
    // ────────────────────────────────────────────────────────────

    public static S_NpcStatesMessage ReadNpcStates(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        var location = ReadPlayerLocation(r);
        int count = r.ReadInt32();
        var npcs = new NetNpcState[count];
        for (int i = 0; i < count; i++)
            npcs[i] = ReadNpcState(r);
        int notSentCount = r.ReadInt32();
        var notSentNpcs = new NetNotSentNpcState[notSentCount];
        for (int i = 0; i < notSentCount; i++)
            notSentNpcs[i] = ReadNotSentNpcState(r);
        return new S_NpcStatesMessage { Location = location, NpcCount = count, Npcs = npcs, NotSentNpcCount = notSentCount, NotSentNpcs = notSentNpcs };
    }

    public static C_NpcHitMessage ReadNpcHit(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_NpcHitMessage
        {
            NpcId = r.ReadInt32(),
            Damage = r.ReadSingle(),
            RemainingHull = r.ReadSingle(),
            RemainingShield = r.ReadSingle(),
            Killed = r.ReadBoolean(),
        };
    }

    public static S_NpcHitMessage ReadServerNpcHit(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_NpcHitMessage
        {
            NpcId = r.ReadInt32(),
            PlayerId = r.ReadByte(),
            Damage = r.ReadSingle(),
            RemainingHull = r.ReadSingle(),
            RemainingShield = r.ReadSingle(),
            Killed = r.ReadBoolean(),
        };
    }

    public static S_NpcKillRewardMessage ReadNpcKillReward(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_NpcKillRewardMessage
        {
            NpcId = r.ReadInt32(),
            Credits = r.ReadInt32(),
            RewardMessage = r.ReadString(),
        };
    }

    public static C_PlayerKilledByNpcMessage ReadPlayerKilledByNpc(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_PlayerKilledByNpcMessage
        {
            NpcId = r.ReadInt32(),
        };
    }

    public static byte[] Write(in C_InteractDerelictMessage msg)
    {
        using var ms = new MemoryStream(10);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_InteractDerelict);
        w.Write(msg.SolarSystemIndex);
        w.Write(msg.DerelictIndex);
        return ms.ToArray();
    }

    public static C_InteractDerelictMessage ReadInteractDerelict(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_InteractDerelictMessage
        {
            SolarSystemIndex = r.ReadInt32(),
            DerelictIndex = r.ReadInt32(),
        };
    }

    public static byte[] Write(in C_InteractDistressMessage msg)
    {
        using var ms = new MemoryStream(10);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.C_InteractDistress);
        w.Write(msg.SolarSystemIndex);
        w.Write(msg.BeaconIndex);
        return ms.ToArray();
    }

    public static C_InteractDistressMessage ReadInteractDistress(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new C_InteractDistressMessage
        {
            SolarSystemIndex = r.ReadInt32(),
            BeaconIndex = r.ReadInt32(),
        };
    }

    public static byte[] Write(in S_InteractDerelictResultMessage msg)
    {
        using var ms = new MemoryStream(14);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_InteractDerelictResult);
        w.Write(msg.Success);
        w.Write(msg.SolarSystemIndex);
        w.Write(msg.DerelictIndex);
        return ms.ToArray();
    }

    public static S_InteractDerelictResultMessage ReadInteractDerelictResult(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_InteractDerelictResultMessage
        {
            Success = r.ReadBoolean(),
            SolarSystemIndex = r.ReadInt32(),
            DerelictIndex = r.ReadInt32(),
        };
    }

    public static byte[] Write(in S_InteractDistressResultMessage msg)
    {
        using var ms = new MemoryStream(14);
        using var w = new BinaryWriter(ms);
        w.Write((byte)MessageType.S_InteractDistressResult);
        w.Write(msg.Success);
        w.Write(msg.SolarSystemIndex);
        w.Write(msg.BeaconIndex);
        w.Write(msg.IsAmbush);
        return ms.ToArray();
    }

    public static S_InteractDistressResultMessage ReadInteractDistressResult(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms);
        r.ReadByte(); // skip type
        return new S_InteractDistressResultMessage
        {
            Success = r.ReadBoolean(),
            SolarSystemIndex = r.ReadInt32(),
            BeaconIndex = r.ReadInt32(),
            IsAmbush = r.ReadBoolean(),
        };
    }

    // ────────────────────────────────────────────────────────────
    //  NPC state sub-serialization
    // ────────────────────────────────────────────────────────────

    private static void WriteNpcState(BinaryWriter w, in NetNpcState s)
    {
        w.Write(s.NpcId);
        w.Write((byte)s.NpcType);
        w.Write(s.Faction);
        w.Write(s.ShipTypeId ?? string.Empty);
        w.Write(s.QualityTier);
        w.Write(s.DangerLevel);
        w.Write(s.Position.X);
        w.Write(s.Position.Y);
        w.Write(s.Rotation);
        w.Write(s.Velocity.X);
        w.Write(s.Velocity.Y);
        w.Write(s.Hull);
        w.Write(s.Shield);
        w.Write(s.Dead);
        w.Write(s.Warping);
        w.Write(s.WarpingIn);
        w.Write(s.WarpProgress);
        w.Write(s.WarpDuration);
        w.Write(s.Shooting);
        w.Write(s.AimDirection.X);
        w.Write(s.AimDirection.Y);
        w.Write(s.LandedAnimProgress);
        w.Write(s.LandedIsLanding);
    }

    private static NetNpcState ReadNpcState(BinaryReader r)
    {
        return new NetNpcState
        {
            NpcId = r.ReadInt32(),
            NpcType = (NetNpcType)r.ReadByte(),
            Faction = r.ReadByte(),
            ShipTypeId = r.ReadString(),
            QualityTier = r.ReadInt32(),
            DangerLevel = r.ReadInt32(),
            Position = new Vector2(r.ReadSingle(), r.ReadSingle()),
            Rotation = r.ReadSingle(),
            Velocity = new Vector2(r.ReadSingle(), r.ReadSingle()),
            Hull = r.ReadSingle(),
            Shield = r.ReadSingle(),
            Dead = r.ReadBoolean(),
            Warping = r.ReadBoolean(),
            WarpingIn = r.ReadBoolean(),
            WarpProgress = r.ReadSingle(),
            WarpDuration = r.ReadSingle(),
            Shooting = r.ReadBoolean(),
            AimDirection = new Vector2(r.ReadSingle(), r.ReadSingle()),
            LandedAnimProgress = r.ReadSingle(),
            LandedIsLanding = r.ReadBoolean(),
        };
    }


    // ────────────────────────────────────────────────────────────
    //  NPC state sub-serialization
    // ────────────────────────────────────────────────────────────

    private static void WriteNotSentNpcState(BinaryWriter w, in NetNotSentNpcState s)
    {
        w.Write(s.NpcId);
    }

    private static NetNotSentNpcState ReadNotSentNpcState(BinaryReader r)
    {
        return new NetNotSentNpcState
        {
            NpcId = r.ReadInt32(),
        };
    }
}
