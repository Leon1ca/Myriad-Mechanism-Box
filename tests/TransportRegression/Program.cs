using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomsClearanceConsole;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var token = deadline.Token;
var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }

await using (var peer = await Peer.ConnectAsync(token))
{
    var a = peer.Client.SendAsync("first", null, token);
    var b = peer.Client.SendAsync("second", null, token, "child-session");
    var one = await peer.ReadAsync(token); var two = await peer.ReadAsync(token);
    await peer.ReplyAsync(two.GetProperty("id").GetInt32(), 2, token);
    await peer.ReplyAsync(one.GetProperty("id").GetInt32(), 1, token);
    Check((await a).GetProperty("result").GetProperty("value").GetInt32() == 1 &&
        (await b).GetProperty("result").GetProperty("value").GetInt32() == 2,
        "并发命令按 ID 匹配逆序响应");
    Check(two.GetProperty("sessionId").GetString() == "child-session",
        "跨进程框架 sessionId 保留在命令信封");

    using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
    var pending = peer.Client.SendAsync("cancelled", null, cancelled.Token);
    await peer.ReadAsync(token);
    cancelled.Cancel();
    var didCancel = false;
    try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
    catch (OperationCanceledException) { didCancel = true; }
    Check(didCancel, "取消会及时结束等待响应的命令");
    var next = peer.Client.SendAsync("after-cancel", null, token);
    var request = await peer.ReadAsync(token);
    await peer.ReplyAsync(request.GetProperty("id").GetInt32(), 3, token);
    Check((await next).GetProperty("result").GetProperty("value").GetInt32() == 3,
        "单条命令取消后连接仍可继续使用");

    var closing = peer.Client.SendAsync("dispose-pending", null, token);
    await peer.ReadAsync(token);
    await peer.Client.DisposeAsync();
    var failed = false;
    try { await closing.WaitAsync(TimeSpan.FromSeconds(2)); }
    catch (Exception ex) when (ex is ObjectDisposedException or IOException or OperationCanceledException) { failed = true; }
    Check(failed, "关闭连接立即结束未完成命令，不等待 90 秒");
    await peer.Client.DisposeAsync();
    Check(true, "重复释放连接安全");
}
await using (var peer = await Peer.ConnectAsync(token))
{
    var notifications = 0;
    peer.Client.Closed += _ => Interlocked.Increment(ref notifications);
    var pending = peer.Client.SendAsync("remote-close", null, token);
    await peer.ReadAsync(token);
    await peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", token);
    var failed = false;
    try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
    catch (Exception ex) when (ex is IOException or WebSocketException or OperationCanceledException) { failed = true; }
    Check(failed, "远端关闭立即终止在途命令");
    Check(notifications == 1, "远端关闭只通知一次");
}
Console.WriteLine($"TRANSPORT_REGRESSION_OK: {count} checks");

internal sealed class Peer(TcpClient tcp, WebSocket socket, CdpClient client) : IAsyncDisposable
{
    public WebSocket Socket => socket;
    public CdpClient Client => client;
    public static async Task<Peer> ConnectAsync(CancellationToken token)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var connect = CdpClient.ConnectAsync($"ws://127.0.0.1:{port}/devtools", token);
            var tcp = await listener.AcceptTcpClientAsync(token);
            var stream = tcp.GetStream();
            var header = new StringBuilder();
            var buffer = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (header.Length > 16384 || await stream.ReadAsync(buffer, token) == 0) throw new IOException("Bad handshake");
                header.Append((char)buffer[0]);
            }
            var key = header.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), token);
            var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromMinutes(1));
            return new Peer(tcp, socket, await connect);
        }
        finally { listener.Stop(); }
    }
    public async Task<JsonElement> ReadAsync(CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult part;
        do { part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token); bytes.Write(buffer, 0, part.Count); }
        while (!part.EndOfMessage);
        using var document = JsonDocument.Parse(bytes.ToArray());
        return document.RootElement.Clone();
    }
    public async Task ReplyAsync(int id, int value, CancellationToken token) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { value } }),
            WebSocketMessageType.Text, true, token);
    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        socket.Dispose();
        tcp.Dispose();
    }
}
