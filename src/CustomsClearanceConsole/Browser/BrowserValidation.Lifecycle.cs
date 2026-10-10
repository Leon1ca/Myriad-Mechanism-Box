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

internal sealed partial class BrowserValidation : IAsyncDisposable
{

    internal sealed record BrowserChoice(string Path, string DisplayName, bool UsedFallback);

    public static string? FindBrowser(string preference)
    {
        var edge = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        var chrome = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        var preferred = preference.Equals("Chrome", StringComparison.OrdinalIgnoreCase) ? chrome : edge;
        var fallback = preference.Equals("Chrome", StringComparison.OrdinalIgnoreCase) ? edge : chrome;
        return preferred.Concat(fallback).FirstOrDefault(File.Exists);
    }

    public static BrowserChoice ResolveBrowser()
    {
        var defaultPath = GetDefaultBrowserExecutable();
        if (!string.IsNullOrWhiteSpace(defaultPath) && File.Exists(defaultPath))
        {
            var fileName = Path.GetFileName(defaultPath);
            if (fileName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
                return new BrowserChoice(defaultPath, "系统默认浏览器（Edge）", false);
            if (fileName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
                return new BrowserChoice(defaultPath, "系统默认浏览器（Chrome）", false);
        }
        var fallback = FindBrowser("Edge") ?? throw new FileNotFoundException("系统默认浏览器不支持自动填写，且未找到 Microsoft Edge 或 Google Chrome。请至少安装其中一种浏览器。");
        var name = Path.GetFileName(fallback).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ? "Microsoft Edge" : "Google Chrome";
        return new BrowserChoice(fallback, name, true);
    }

    private static string? GetDefaultBrowserExecutable()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var progId = choice?.GetValue("ProgId") as string;
            if (string.IsNullOrWhiteSpace(progId)) return null;
            using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var command = commandKey?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = Environment.ExpandEnvironmentVariables(command.Trim());
            if (command.StartsWith('"'))
            {
                var end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : null;
            }
            var exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exeEnd >= 0 ? command[..(exeEnd + 4)].Trim() : null;
        }
        catch (Exception ex)
        {
            AppLog.Write($"读取系统默认浏览器失败：{ex.Message}");
            return null;
        }
    }

    public async Task<string> StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        cancellationToken = startup.Token;
        cancellationToken.ThrowIfCancellationRequested();
        BrowserChoice choice;
        if (!string.IsNullOrWhiteSpace(_browserPathOverride) && File.Exists(_browserPathOverride))
            choice = new BrowserChoice(_browserPathOverride, Path.GetFileNameWithoutExtension(_browserPathOverride), true);
        else
            choice = ResolveBrowser();

        var url = string.IsNullOrWhiteSpace(_urlOverride) ? PrimaryUrl : _urlOverride;
        _debugPort = GetFreePort();
        var profileRoot = Path.Combine(AppLog.Folder, "BrowserProfiles");
        // Every session uses its own throw-away profile (Edge / Chrome refuse remote debugging
        // on the everyday profile). A persistent shared profile was tried during v0.2.0; on real
        // machines auto-fill and the capture card then did not appear, so sessions stay isolated
        // exactly as in v0.1.0. Profiles
        // that earlier runs could not delete are swept so they do not accumulate on disk.
        _ = Task.Run(() => PurgeStaleProfiles(profileRoot, TimeSpan.FromHours(12)));
        var profile = Path.Combine(profileRoot, SessionId);
        _profileFolder = profile;
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo { FileName = choice.Path, UseShellExecute = false };
        start.ArgumentList.Add($"--remote-debugging-port={_debugPort}");
        start.ArgumentList.Add("--remote-allow-origins=*");
        start.ArgumentList.Add($"--user-data-dir={profile}");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--no-default-browser-check");
        start.ArgumentList.Add("--disable-features=Translate");
        // The capture drives the page through timers, animation frames and screenshots. A
        // window that is partly covered by the console, or loses focus while the user switches
        // back to it, is otherwise throttled or not painted at all, which stalls the capture.
        start.ArgumentList.Add("--disable-background-timer-throttling");
        start.ArgumentList.Add("--disable-renderer-backgrounding");
        start.ArgumentList.Add("--disable-backgrounding-occluded-windows");
        if (_headless)
        {
            start.ArgumentList.Add("--headless=new");
            start.ArgumentList.Add("--disable-gpu");
            start.ArgumentList.Add("--window-size=1280,900");
        }
        else
        {
            start.ArgumentList.Add("--start-maximized");
        }
        start.ArgumentList.Add("--new-window");
        if (_extraBrowserArgs is not null)
            foreach (var argument in _extraBrowserArgs) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(url);
        _process = Process.Start(start) ?? throw new InvalidOperationException("无法启动浏览器。");
        _process.EnableRaisingEvents = true;
        _process.Exited += OnBrowserExited;

        var websocket = await WaitForPageAsync(_debugPort, cancellationToken);
        await ConnectAsync(websocket, cancellationToken);
        await InjectShellAsync(cancellationToken);

        var filled = await AutofillAsync(cancellationToken);
        var browserNote = choice.UsedFallback
            ? $"默认浏览器不支持自动填写，已使用 {choice.DisplayName}。"
            : $"已使用{choice.DisplayName}。";
        return filled
            ? $"{browserNote} 已填入报关单号。请在网页输入验证码并点击查询，然后在网页右下角点击“长截图”。"
            : $"{browserNote} 核验页面已打开，请手动输入报关单号；结果出现后在网页右下角点击“长截图”。";
    }

    private async Task ConnectAsync(string websocket, CancellationToken token)
    {
        if (_cdp is not null) await _cdp.DisposeAsync();
        lock (_frameUrls) { _frameUrls.Clear(); _currentUrl = ""; _mainFrameId = ""; }
        lock (_frameParents) _frameParents.Clear();
        lock (_frameContexts) _frameContexts.Clear();
        lock (_frameTargetSessions) _frameTargetSessions.Clear();
        Interlocked.Exchange(ref _navigationGeneration, 0);
        _shellScriptIdentifier = null;
        var connected = await CdpClient.ConnectAsync(websocket, token);
        lock (_connectionSync)
        {
            if (!_disposed) _cdp = connected;
        }
        if (_disposed)
        {
            await connected.DisposeAsync();
            throw new OperationCanceledException("核验会话已关闭。", token);
        }
        connected.Closed += OnConnectionClosed;
        connected.On("Runtime.executionContextCreated", OnContextCreated);
        connected.On("Runtime.executionContextDestroyed", OnContextDestroyed);
        connected.On("Runtime.executionContextsCleared", OnContextsCleared);
        connected.On("Runtime.bindingCalled", OnBindingCalled);
        connected.On("Page.frameNavigated", OnFrameNavigated);
        connected.On("Page.navigatedWithinDocument", OnNavigatedWithinDocument);
        connected.On("Target.attachedToTarget", OnAttachedToTarget);
        connected.On("Target.detachedFromTarget", OnDetachedFromTarget);
        await connected.SendAsync("Page.enable", null, token);
        await connected.SendAsync("Runtime.enable", null, token);
        await connected.SendAsync("DOM.enable", null, token);
        // Flattened auto-attach so a cross-site (out-of-process) query iframe is reachable
        // through its own CDP session instead of being invisible to page-session evaluation.
        try
        {
            await connected.SendAsync("Target.setAutoAttach",
                new { autoAttach = true, waitForDebuggerOnStart = false, flatten = true }, token);
        }
        catch (Exception ex) { AppLog.Write($"启用子目标自动附加失败：{ex.Message}"); }
        // Seed the current URL/frame so binding validation works before the first
        // navigation event arrives. The complete frame tree is registered, not just the
        // root, so an already-loaded sub-frame is authorized (or refused) by its own URL.
        // The snapshot can predate a navigation that commits while it is in flight (the
        // initial document then reports an empty URL), and the frameNavigated event for that
        // commit may already have been applied. The seed therefore only fills values that are
        // still unknown and never replaces what a newer event recorded.
        try
        {
            var tree = await connected.SendAsync("Page.getFrameTree", null, token);
            var frameTree = tree.GetProperty("result").GetProperty("frameTree");
            var frames = new List<(string FrameId, string Url)>();
            CollectFrames(frameTree, frames);
            var parents = new Dictionary<string, string>(StringComparer.Ordinal);
            CollectFrameParents(frameTree, parents);
            var root = frameTree.GetProperty("frame");
            var rootId = root.GetProperty("id").GetString() ?? "";
            var rootUrl = FrameUrl(root);
            lock (_frameUrls)
            {
                foreach (var (id, url) in frames)
                    if (id.Length > 0 && IsKnownUrl(url)) _frameUrls.TryAdd(id, url);
                if (_mainFrameId.Length == 0) _mainFrameId = rootId;
                if (_currentUrl.Length == 0 && rootId.Equals(_mainFrameId, StringComparison.Ordinal)) _currentUrl = rootUrl;
            }
            lock (_frameParents)
                foreach (var pair in parents) _frameParents.TryAdd(pair.Key, pair.Value);
        }
        catch (Exception ex) { AppLog.Write($"读取初始页面地址失败：{ex.Message}"); }
        await RegisterShellScriptAsync(token);
    }

    private async Task RegisterShellScriptAsync(CancellationToken token)
    {
        if (_cdp is null) return;
        // Never accumulate duplicate per-document scripts across reconnects.
        if (_shellScriptIdentifier is not null)
        {
            try { await _cdp.SendAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = _shellScriptIdentifier }, token); }
            catch (Exception ex) { AppLog.Write($"移除旧网页脚本失败：{ex.Message}"); }
            _shellScriptIdentifier = null;
        }
        try
        {
            var response = await _cdp.SendAsync("Page.addScriptToEvaluateOnNewDocument",
                new { source = ShellScript() }, token);
            _shellScriptIdentifier = response.GetProperty("result").TryGetProperty("identifier", out var id) ? id.GetString() : null;
        }
        catch (Exception ex) { AppLog.Write($"注册网页脚本失败：{ex.Message}"); }
    }

    private bool BrowserExited
    {
        get
        {
            try { return _process is { HasExited: true }; }
            catch (InvalidOperationException) { return true; }
        }
    }

    private void OnBrowserExited(object? sender, EventArgs e)
    {
        if (_disposed || Interlocked.Exchange(ref _endedRaised, 1) != 0) return;
        AppLog.Write($"核验浏览器已退出：{DeclarationNo}");
        Ended?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectionClosed(string reason)
    {
        // A closed browser window is the normal end of a session, not a connection fault.
        if (_disposed || BrowserExited) return;
        StatusChanged?.Invoke(this, "浏览器连接中断，正在尝试恢复……");
        // Keep an explicit recovery task running so the status message is truthful.
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 8 && !_disposed && !BrowserExited; attempt++)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    await ReconnectAsync(timeout.Token);
                    StatusChanged?.Invoke(this, "浏览器连接已恢复。");
                    return;
                }
                catch (Exception ex) { AppLog.Write($"浏览器重连第 {attempt + 1} 次失败：{ex.Message}"); }
                try { await Task.Delay(1000); } catch { return; }
            }
        });
    }

    private async Task ReconnectAsync(CancellationToken token)
    {
        // Serialize concurrent recovery attempts (socket close handler and E2E helper).
        await _reconnectGate.WaitAsync(token);
        try
        {
            var websocket = await WaitForPageAsync(_debugPort, token);
            await ConnectAsync(websocket, token);
            await InjectShellAsync(token);
        }
        finally { _reconnectGate.Release(); }
    }

    private async Task InjectShellAsync(CancellationToken token)
    {
        if (_cdp is null) return;
        await _cdp.SendAsync("Runtime.addBinding", new { name = "cccRequestCapture" }, token);
        await _cdp.SendAsync("Runtime.addBinding", new { name = "cccOpenFolder" }, token);
        // Frames that finished loading before this connect never ran the new-document
        // script, so the monitor is installed into the main frame and every frame that is
        // authorized or still pending (its URL is not known yet). Result/read/execute
        // authorization stays strict elsewhere, so a pending frame that later turns out to
        // be foreign can never contribute a verdict. The card belongs to the main frame.
        try { await InjectMonitorAsync(token); }
        catch (Exception ex) when (!IsConnectionFailure(ex)) { AppLog.Write($"向子框架安装监视器失败：{ex.Message}"); }
        try { await EvaluateContextAsync(ShellScript(), null, token, awaitPromise: true); }
        catch (Exception ex) when (IsConnectionFailure(ex)) { }
    }

    /// <summary>
    /// Installs the click monitor in the main frame and every frame that is authorized, plus
    /// direct children whose URL has not been reported yet. A frame already known to be
    /// unauthorized is left untouched.
    /// </summary>
    private async Task InjectMonitorAsync(CancellationToken token)
    {
        if (_cdp is null) return;
        var monitor = MonitorScript();
        List<FrameContext> contexts;
        lock (_frameContexts) contexts = _frameContexts.ToList();
        foreach (var context in contexts)
        {
            if (token.IsCancellationRequested) break;
            if (string.IsNullOrEmpty(context.FrameId)) continue;
            if (!IsAuthorizedFrameId(context.FrameId) && !IsPendingChildFrame(context.FrameId)) continue;
            try { await EvaluateContextAsync(monitor, context.ContextId, context.SessionId, token, awaitPromise: true); }
            catch (Exception ex) when (!IsConnectionFailure(ex)) { AppLog.Write($"向子框架安装监视器失败：{ex.Message}"); }
        }
        try { await EvaluateContextAsync(monitor, null, null, token, awaitPromise: true); }
        catch (Exception ex) when (!IsConnectionFailure(ex)) { AppLog.Write($"向主框架安装监视器失败：{ex.Message}"); }
    }

    private async Task<string> WaitForPageAsync(int port, CancellationToken cancellationToken)
    {
        Exception? last = null;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var i = 0; i < 80; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json", cancellationToken);
                using var doc = JsonDocument.Parse(json);
                // Only a page of the target site may be attached; never fall back to an
                // unrelated tab such as a startup page. The origin is enough here: this
                // throw-away profile only opened our page, and a single-page app can still be
                // on another hash route while loading. Capture requests stay restricted to the
                // inquiry route (IsTargetUrl) regardless.
                var target = doc.RootElement.EnumerateArray().FirstOrDefault(x =>
                    x.TryGetProperty("type", out var type) && type.GetString() == "page" &&
                    x.TryGetProperty("url", out var url) && MatchesTargetOrigin(url.GetString(), out _) &&
                    x.TryGetProperty("webSocketDebuggerUrl", out _));
                if (target.ValueKind != JsonValueKind.Undefined)
                    return target.GetProperty("webSocketDebuggerUrl").GetString()!;
            }
            catch (Exception ex) { last = ex; }
            await Task.Delay(250, cancellationToken);
        }
        throw new InvalidOperationException($"浏览器已打开，但未找到目标页面（{TargetFragment}）的调试连接。", last);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static bool IsConnectionFailure(Exception exception)
    {
        if (exception is WebSocketException or SocketException or IOException or HttpRequestException or ObjectDisposedException)
            return true;
        return exception.InnerException is not null && IsConnectionFailure(exception.InnerException);
    }

    private static string BrowserScript(string name, string? declarationNo = null)
    {
        using var stream = typeof(BrowserValidation).Assembly.GetManifestResourceStream($"CustomsClearanceConsole.BrowserScripts.{name}.js")
            ?? throw new InvalidOperationException($"缺少网页脚本：{name}.js");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd()
            .Replace("__DECLARATION_NO__", JsonSerializer.Serialize(declarationNo))
            .Replace("__RESULT_SELECTORS__", JsonSerializer.Serialize(ResultSelectors));
    }

    public async ValueTask DisposeAsync()
    {
        CdpClient? cdp;
        lock (_connectionSync)
        {
            if (_disposed) return;
            _disposed = true;
            cdp = _cdp;
            _cdp = null;
        }
        try { _lifetime.Cancel(); } catch { }
        if (cdp is not null)
        {
            try { await cdp.SendAsync("Browser.close", null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
            await cdp.DisposeAsync();
        }
        if (_process is not null)
        {
            _process.Exited -= OnBrowserExited;
            try
            {
                // Give Browser.close a moment to finish; only a browser that does not exit
                // is terminated.
                if (!_process.HasExited)
                {
                    try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (TimeoutException) { }
                }
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { }
            _process.Dispose();
        }
        _lifetime.Dispose();
        if (_profileFolder is not null) await DeleteProfileAsync(_profileFolder);
    }

    /// <summary>Best-effort removal of a session profile; browser helpers can hold files briefly after exit.</summary>
    private static async Task DeleteProfileAsync(string folder)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4) { AppLog.Write($"浏览器临时配置暂未删除（下次启动时清理）：{ex.Message}"); return; }
            }
            await Task.Delay(400);
        }
    }

    /// <summary>Deletes session profiles older than <paramref name="age"/>; a profile still in use fails to delete and is kept.</summary>
    internal static void PurgeStaleProfiles(string root, TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var cutoff = DateTime.UtcNow - age;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(folder) < cutoff) Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) { AppLog.Write($"清理浏览器临时配置失败：{ex.Message}"); }
    }
}
