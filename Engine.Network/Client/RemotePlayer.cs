using System.Numerics;

namespace Engine.Network.Client;

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

    /// <summary>Set when a transition-started message is received; consumed by the rendering layer to start departure effects.</summary>
    public NetPlayerTransition PendingTransition = new NetPlayerTransition
    {
        From = NetPlayerLocation.ForUnknown(),
        To = NetPlayerLocation.ForUnknown()
    };
    public double PendingTransitionReceivedServerTime;

    public RemotePlayer(byte playerId, string name, NetPlayerLocation location, NetPlayerInfo info, NetPlayerState state)
    {
        PlayerId = playerId;
        Name = name;
        Location = location;
        Info = info;
        State = state;
    }
}
