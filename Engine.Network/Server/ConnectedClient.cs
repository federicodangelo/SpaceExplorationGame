using System.Net.WebSockets;

namespace Engine.Network.Server;

/// <summary>
/// Represents a connected player on the server side.
/// </summary>
public sealed class ConnectedClient : IDisposable
{
    public byte PlayerId { get; }
    public string PlayerName { get; }
    public NetPlayerState LastState;

    private readonly WebSocket _ws;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ConnectedClient(byte playerId, string playerName, WebSocket ws)
    {
        PlayerId = playerId;
        PlayerName = playerName;
        _ws = ws;
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        _ws.Dispose();
    }
}
