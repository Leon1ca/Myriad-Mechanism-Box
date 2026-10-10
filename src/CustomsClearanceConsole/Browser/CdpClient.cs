using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CustomsClearanceConsole;

/// <summary>
/// Minimal DevTools protocol client with a single background receive loop. Commands
/// are serialized and correlated by id; events are dispatched by method name, so a
/// command never consumes another command's response or drops an event.
/// </summary>
internal sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Action<JsonElement>> _events = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _readerLifetime = new();
    private int _nextId;
    private Task? _reader;
    private volatile bool _closed;
    private readonly object _stateLock = new();
    private int _disposed;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(90);

    public event Action<string>? Closed;
    public bool IsClosed => _closed;

    /// <summary>Test hook: aborts the socket so the reconnect path can be exercised.</summary>
    internal void AbortForTest() => _socket.Abort();

    public static async Task<CdpClient> ConnectAsync(string url, CancellationToken token)
    {
        var client = new CdpClient();
        try
        {
            await client._socket.ConnectAsync(new Uri(url), token);
            client._reader = Task.Run(client.ReceiveLoopAsync);
            return client;
        }
        catch { await client.DisposeAsync(); throw; }
    }

    public void On(string method, Action<JsonElement> handler) => _events[method] = handler;

    public async Task<JsonElement> SendAsync(string method, object? parameters, CancellationToken token, string? sessionId = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock)
        {
            if (_closed) throw new IOException("浏览器控制连接已关闭。");
            _pending[id] = completion;
        }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _readerLifetime.Token);
            deadline.CancelAfter(CommandTimeout);
            // The same deadline covers waiting for the send lock, the socket write and response.
            var envelope = sessionId is null
                ? (object)new { id, method, @params = parameters ?? new { } }
                : new { id, method, @params = parameters ?? new { }, sessionId };
            var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
            try
            {
                await _sendLock.WaitAsync(deadline.Token);
                try { await _socket.SendAsync(payload, WebSocketMessageType.Text, true, deadline.Token); }
                finally { _sendLock.Release(); }
                return await completion.Task.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !_closed)
            { throw new TimeoutException($"浏览器命令超时：{method}"); }
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[1 << 16];
        try
        {
            while (!_readerLifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _readerLifetime.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { FailAll(new IOException("浏览器控制连接已关闭。")); return; }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (message.Length == 0) continue;
                JsonDocument document;
                try { document = JsonDocument.Parse(message.ToArray()); }
                catch (JsonException) { continue; }
                using (document)
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("id", out var idProperty) && idProperty.TryGetInt32(out var id))
                    {
                        if (!_pending.TryRemove(id, out var completion)) continue;
                        if (root.TryGetProperty("error", out var error)) completion.TrySetException(new InvalidOperationException(error.ToString()));
                        else completion.TrySetResult(root.Clone());
                    }
                    else if (root.TryGetProperty("method", out var methodProperty))
                    {
                        var method = methodProperty.GetString();
                        if (method is not null && _events.TryGetValue(method, out var handler))
                        {
                            try { handler(root.Clone()); }
                            catch (Exception ex) { AppLog.Write($"CDP 事件处理失败：{method} · {ex.Message}"); }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FailAll(ex);
        }
    }

    private void FailAll(Exception exception, bool notify = true)
    {
        TaskCompletionSource<JsonElement>[] pending;
        lock (_stateLock)
        {
            if (_closed) return;
            _closed = true;
            pending = _pending.Values.ToArray();
            _pending.Clear();
        }
        foreach (var completion in pending) completion.TrySetException(exception);
        if (notify) Closed?.Invoke(exception.Message);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        FailAll(new ObjectDisposedException(nameof(CdpClient)), notify: false);
        try { _readerLifetime.Cancel(); } catch { }
        try { _socket.Abort(); } catch { }
        _socket.Dispose();
        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        _readerLifetime.Dispose();
        // Do not dispose the managed semaphore while cancelled SendAsync continuations
        // may still release it. It has no native wait handle; GC owns its final lifetime.
    }
}
