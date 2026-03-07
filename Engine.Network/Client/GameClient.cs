using System.Collections.Concurrent;
using System.Net.WebSockets;
using Engine.Network.Server;

namespace Engine.Network.Client;

/// <summary>
/// WebSocket client that connects to a <see cref="GameServer"/> and provides
/// non-blocking send/receive via a background thread + thread-safe message queue.
/// Designed for the game loop: call <see cref="Send"/> to enqueue outbound messages
/// and <see cref="TryReceive"/> each frame to drain inbound messages.
/// </summary>
public sealed class GameClient : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    private readonly ConcurrentQueue<byte[]> _inbound = new();

    /// <summary>True once the WebSocket connection is open.</summary>
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    /// <summary>Set to true when the connection drops or is closed.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>
    /// Connect to the server. Returns once the WebSocket handshake completes.
    /// Starts a background receive loop.
    /// </summary>
    public async Task ConnectAsync(string url, CancellationToken ct = default)
    {
        _ws = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _ws.ConnectAsync(new Uri(url), _cts.Token).ConfigureAwait(false);
        _receiveTask = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    /// <summary>Send a serialized message to the server (non-blocking enqueue).</summary>
    public void Send(byte[] data)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        // Fire-and-forget send; small messages finish almost instantly over loopback/LAN.
        _ = _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Try to dequeue the next inbound message. Returns false if the queue is empty.
    /// Call this in the game loop each frame.
    /// </summary>
    public bool TryReceive(out byte[] data)
    {
        return _inbound.TryDequeue(out data!);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (_ws?.State == WebSocketState.Open)
        {
            try
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                    .Wait(TimeSpan.FromSeconds(1));
            }
            catch { /* best-effort */ }
        }
        _receiveTask?.Wait(TimeSpan.FromSeconds(1));
        _ws?.Dispose();
        _cts?.Dispose();
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnected = true;
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                _inbound.Enqueue(ms.ToArray());
            }
        }
        catch (WebSocketException) { /* disconnected */ }
        catch (OperationCanceledException) { /* shutting down */ }
        finally
        {
            Disconnected = true;
        }
    }
}
