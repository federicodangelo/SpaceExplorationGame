using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace Engine.Network.Server;

/// <summary>
/// Event types queued by background receive threads and dispatched on the main thread.
/// </summary>
public enum ServerEventType { ClientJoined, ClientLeft, PlayerState, LocationChanged }

/// <summary>
/// A server-side network event, enqueued from background threads and
/// dispatched on the main thread via <see cref="GameServer.DispatchMessages"/>.
/// </summary>
public readonly struct ServerEvent
{
    public ServerEventType Type { get; init; }
    public byte PlayerId { get; init; }
    public C_JoinMessage Join { get; init; }
    public C_PlayerStateMessage PlayerState { get; init; }
    public C_DisconnectMessage Disconnect { get; init; }
    public C_LocationChangedMessage LocationChanged { get; init; }
}

/// <summary>
/// Lightweight WebSocket server that accepts client connections and provides
/// message-level send/receive. Runs the HTTP listener on a background thread.
/// Network events are queued and dispatched on the main thread.
/// </summary>
public sealed class GameServer : IDisposable
{
    private readonly HttpListener _httpListener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<byte, ConnectedClient> _clients = new();
    private readonly ConcurrentQueue<ServerEvent> _events = new();
    private byte _nextPlayerId;
    private Task? _listenTask;

    public int MaxPlayers { get; }
    public int Port { get; }

    /// <summary>Currently connected clients (thread-safe snapshot).</summary>
    public IReadOnlyDictionary<byte, ConnectedClient> Clients => _clients;

    public GameServer(int port = 9050, int maxPlayers = 8)
    {
        Port = port;
        MaxPlayers = maxPlayers;
        _httpListener.Prefixes.Add($"http://localhost:{port}/");
    }

    /// <summary>Start listening for WebSocket connections.</summary>
    public void Start()
    {
        _httpListener.Start();
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    /// <summary>
    /// Drain all queued network events into the provided list.
    /// Call this once per tick from the main game loop, then iterate the results.
    /// The list is cleared before filling.
    /// </summary>
    public void DrainEvents(List<ServerEvent> dest)
    {
        dest.Clear();
        while (_events.TryDequeue(out var evt))
            dest.Add(evt);
    }

    /// <summary>Broadcast a raw message to all connected clients.</summary>
    public void Broadcast(byte[] data)
    {
        foreach (var kv in _clients)
        {
            _ = kv.Value.SendAsync(data, _cts.Token);
        }
    }

    /// <summary>Broadcast a raw message to all clients except the one with the given ID.</summary>
    public void BroadcastExcept(byte[] data, byte excludePlayerId)
    {
        foreach (var kv in _clients)
        {
            if (kv.Key != excludePlayerId)
                _ = kv.Value.SendAsync(data, _cts.Token);
        }
    }

    /// <summary>Send a raw message to a specific client.</summary>
    public void Send(byte playerId, byte[] data)
    {
        if (_clients.TryGetValue(playerId, out var client))
        {
            _ = client.SendAsync(data, _cts.Token);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var kv in _clients)
            kv.Value.Dispose();
        _clients.Clear();
        _httpListener.Stop();
        _httpListener.Close();
        _listenTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }

    // ────────────────────────────────────────────────────────────
    //  Connection accept loop
    // ────────────────────────────────────────────────────────────

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var httpCtx = await _httpListener.GetContextAsync().ConfigureAwait(false);
                if (!httpCtx.Request.IsWebSocketRequest)
                {
                    httpCtx.Response.StatusCode = 400;
                    httpCtx.Response.Close();
                    continue;
                }

                var wsCtx = await httpCtx.AcceptWebSocketAsync(null).ConfigureAwait(false);
                _ = Task.Run(() => HandleClient(wsCtx.WebSocket, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Accept error: {ex.Message}");
            }
        }
    }

    private byte GetNextPlayerId()
    {
        byte next = _nextPlayerId++;
        // Wrap around, but skip IDs that are currently in use
        int attempts = 0;
        while (_clients.ContainsKey(next) && attempts++ < 255)
        {
            next = _nextPlayerId++;
        }
        return next;
    }

    private async Task HandleClient(WebSocket ws, CancellationToken ct)
    {
        byte playerId = 0;
        bool registered = false;
        try
        {
            // First message must be C_Join
            var data = await ReceiveMessageAsync(ws, ct).ConfigureAwait(false);
            if (data == null || data.Length == 0 || NetSerializer.PeekType(data) != MessageType.C_Join)
            {
                await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Expected C_Join", ct).ConfigureAwait(false);
                return;
            }

            if (_clients.Count >= MaxPlayers)
            {
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Server full", ct).ConfigureAwait(false);
                return;
            }

            var join = NetSerializer.ReadJoin(data);
            playerId = GetNextPlayerId();
            var client = new ConnectedClient(playerId, join.PlayerName, ws);
            _clients[playerId] = client;
            registered = true;

            _events.Enqueue(new ServerEvent
            {
                Type = ServerEventType.ClientJoined,
                PlayerId = playerId,
                Join = join,
            });

            // Receive loop
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var msg = await ReceiveMessageAsync(ws, ct).ConfigureAwait(false);
                if (msg == null) break;

                var type = NetSerializer.PeekType(msg);
                switch (type)
                {
                    case MessageType.C_PlayerState:
                        var state = NetSerializer.ReadPlayerState(msg);
                        client.LastState = state.State;
                        _events.Enqueue(new ServerEvent
                        {
                            Type = ServerEventType.PlayerState,
                            PlayerId = playerId,
                            PlayerState = state,
                        });
                        break;

                    case MessageType.C_LocationChanged:
                        var locChanged = NetSerializer.ReadLocationChanged(msg);
                        _events.Enqueue(new ServerEvent
                        {
                            Type = ServerEventType.LocationChanged,
                            PlayerId = playerId,
                            LocationChanged = locChanged,
                        });
                        break;

                    case MessageType.C_Disconnect:
                        _events.Enqueue(new ServerEvent
                        {
                            Type = ServerEventType.ClientLeft,
                            PlayerId = playerId,
                            Disconnect = new C_DisconnectMessage(),
                        });
                        return;

                    default:
                        throw new InvalidOperationException($"Unknown message type {type}");
                }
            }
        }
        catch (WebSocketException) { /* client disconnected */ }
        catch (OperationCanceledException) { /* server shutting down */ }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Client {playerId} error: {ex.Message}");
        }
        finally
        {
            if (registered)
            {
                _clients.TryRemove(playerId, out _);
                _events.Enqueue(new ServerEvent
                {
                    Type = ServerEventType.ClientLeft,
                    PlayerId = playerId,
                });
            }
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { /* best-effort */ }
            }
            ws.Dispose();
        }
    }

    // ────────────────────────────────────────────────────────────
    //  WebSocket receive helper
    // ────────────────────────────────────────────────────────────

    private static async Task<byte[]?> ReceiveMessageAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return ms.ToArray();
    }
}
