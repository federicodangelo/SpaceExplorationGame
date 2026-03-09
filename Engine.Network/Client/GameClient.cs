using System.Collections.Concurrent;
using System.Diagnostics;
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

    // Bandwidth tracking (thread-safe via Interlocked)
    private long _totalBytesSent;
    private long _totalBytesReceived;
    private long _sentAccum;
    private long _recvAccum;

    // Bandwidth update timer
    private Stopwatch _bandwithUpdateWatch = new Stopwatch();

    /// <summary>Total bytes sent since connect.</summary>
    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
    /// <summary>Total bytes received since connect.</summary>
    public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);
    /// <summary>Bytes sent in the most recently completed 1-second window. Updated by <see cref="UpdateBandwidthWindow"/>.</summary>
    public long BytesSentLastSecond { get; private set; }
    /// <summary>Bytes received in the most recently completed 1-second window. Updated by <see cref="UpdateBandwidthWindow"/>.</summary>
    public long BytesReceivedLastSecond { get; private set; }

    /// <summary>
    /// Update per-second bandwidth stats.
    /// </summary>
    public void UpdateStats()
    {
        if (!_bandwithUpdateWatch.IsRunning)
        {
            _bandwithUpdateWatch.Start();
        }
        else if (_bandwithUpdateWatch.Elapsed.TotalSeconds >= 1.0)
        {
            BytesSentLastSecond = Interlocked.Exchange(ref _sentAccum, 0);
            BytesReceivedLastSecond = Interlocked.Exchange(ref _recvAccum, 0);
            _bandwithUpdateWatch.Restart();
        }
    }

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
        Interlocked.Add(ref _totalBytesSent, data.Length);
        Interlocked.Add(ref _sentAccum, data.Length);
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

        _cts = null;
        _ws = null;
        _receiveTask = null;
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

                var msg = ms.ToArray();
                Interlocked.Add(ref _totalBytesReceived, msg.Length);
                Interlocked.Add(ref _recvAccum, msg.Length);
                _inbound.Enqueue(msg);
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
