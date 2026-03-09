using System.Net.WebSockets;
using System.Threading.Channels;

namespace Engine.Network.Server;

/// <summary>
/// Represents a connected player on the server side.
/// Messages are sent through a single-consumer ordered channel so that simulated
/// latency/jitter never causes out-of-order delivery.
/// </summary>
public sealed class ConnectedClient : IDisposable
{
    public byte PlayerId { get; }
    public string PlayerName { get; }
    public NetPlayerState LastState;

    private readonly WebSocket _ws;
    private readonly Channel<(byte[] data, long deliverAtMs)> _sendQueue;
    private readonly Task _senderTask;

    public ConnectedClient(byte playerId, string playerName, WebSocket ws, CancellationToken ct)
    {
        PlayerId = playerId;
        PlayerName = playerName;
        _ws = ws;
        _sendQueue = Channel.CreateUnbounded<(byte[], long)>(new UnboundedChannelOptions { SingleReader = true });
        _senderTask = Task.Run(() => SenderLoop(ct), ct);
    }

    /// <summary>
    /// Enqueue a message to be sent after an optional simulated delay (ms).
    /// Order is always preserved regardless of the delay value.
    /// </summary>
    public void EnqueueSend(byte[] data, int delayMs = 0)
    {
        long deliverAt = delayMs > 0 ? Environment.TickCount64 + delayMs : 0;
        _sendQueue.Writer.TryWrite((data, deliverAt));
    }

    private async Task SenderLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var (data, deliverAt) in _sendQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (deliverAt > 0)
                {
                    int waitMs = (int)Math.Max(0, deliverAt - Environment.TickCount64);
                    if (waitMs > 0)
                        await Task.Delay(waitMs, ct).ConfigureAwait(false);
                }

                if (_ws.State != WebSocketState.Open) continue;
                try
                {
                    await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, ct)
                        .ConfigureAwait(false);
                }
                catch (WebSocketException) { break; }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public void Dispose()
    {
        _sendQueue.Writer.TryComplete();
        _ws.Dispose();
    }
}
