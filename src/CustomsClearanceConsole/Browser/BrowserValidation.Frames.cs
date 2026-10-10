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

    private string TargetFragment => _allowTestTarget ? _testOrigin?.ToString() ?? "测试目标" : "singlewindow.cn";

    /// <summary>Parses a URL and matches its exact scheme/host/port against the authorized target.</summary>
    private bool MatchesTargetOrigin(string? url, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        uri = parsed;
        if (_allowTestTarget) return IsTestOriginAuthorized(parsed);
        return TargetUrlPolicy.IsSingleWindowOrigin(url);
    }

    /// <summary>Test targets are explicit loopback/.test hosts only; never enabled by default.</summary>
    private bool IsTestOriginAuthorized(Uri parsed)
    {
        if (!_allowTestTarget) return false;
        if (_testOrigin is not null &&
            parsed.Scheme.Equals(_testOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
            parsed.Host.Equals(_testOrigin.Host, StringComparison.OrdinalIgnoreCase) &&
            parsed.Port == _testOrigin.Port)
            return true;
        var host = parsed.Host;
        if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("::1", StringComparison.OrdinalIgnoreCase))
            return parsed.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                   parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        return host.EndsWith(".test", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The main page must be the exact single-window inquiry route; a URL merely containing
    /// "singlewindow" (for example https://example.com/?singlewindow) is not accepted.
    /// </summary>
    private bool IsTargetUrl(string? url) =>
        _allowTestTarget
            ? MatchesTargetOrigin(url, out var parsed) && (!_testRoutesByFragment || TargetUrlPolicy.IsInquiryRoute(parsed!))
            : TargetUrlPolicy.IsSingleWindowInquiry(url);

    /// <summary>
    /// Sub-frames must be either the trusted single-window origin (same page) or the one
    /// verified official query frame; the card lives in the main frame.
    /// </summary>
    private bool IsTargetFrameUrl(string? url, string frameId) =>
        frameId.Equals(_mainFrameId, StringComparison.Ordinal)
            ? IsTargetUrl(url)
            : _allowTestTarget ? MatchesTargetOrigin(url, out _) : TargetUrlPolicy.IsAuthorizedFrame(url);

    /// <summary>
    /// A web request is only honored when its execution context belongs to an allowed frame
    /// of the target origin. An unknown frame or URL is refused instead of falling back to
    /// the top-level URL, so a nested attacker frame cannot borrow the page's authority.
    /// </summary>
    private bool IsAuthorizedContext(int contextId, string? sessionId)
    {
        FrameContext? context;
        lock (_frameContexts)
            context = _frameContexts.FirstOrDefault(x => x.ContextId == contextId && string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
        if (context is null || string.IsNullOrEmpty(context.FrameId)) return false;
        return IsAuthorizedFrameId(context.FrameId);
    }

    /// <summary>Explains an authorization refusal in the log (registered frame, its URL and the main frame).</summary>
    private string DescribeContextForLog(int contextId, string? sessionId)
    {
        FrameContext? context;
        lock (_frameContexts)
            context = _frameContexts.FirstOrDefault(x => x.ContextId == contextId && string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
        string? frameUrl = null;
        string main, current;
        lock (_frameUrls)
        {
            if (context is not null) _frameUrls.TryGetValue(context.FrameId, out frameUrl);
            main = _mainFrameId;
            current = _currentUrl;
        }
        return context is null
            ? $"未登记执行上下文 · main={main} · url={current}"
            : $"frame={context.FrameId} · frameUrl={frameUrl ?? "(未知)"} · main={main} · url={current}";
    }

    private bool IsAuthorizedFrameId(string frameId)
    {
        string? frameUrl;
        lock (_frameUrls) _frameUrls.TryGetValue(frameId, out frameUrl);
        if (string.IsNullOrWhiteSpace(frameUrl) && frameId.Equals(_mainFrameId, StringComparison.Ordinal))
            frameUrl = _currentUrl;
        return !string.IsNullOrWhiteSpace(frameUrl) && IsTargetFrameUrl(frameUrl, frameId);
    }

    /// <summary>Snapshot of the currently authorized default contexts (session-aware).</summary>
    private List<FrameContext> AuthorizedContexts()
    {
        List<FrameContext> snapshot;
        lock (_frameContexts) snapshot = _frameContexts.ToList();
        return snapshot.Where(x => !string.IsNullOrEmpty(x.FrameId) && IsAuthorizedFrameId(x.FrameId)).ToList();
    }

    private static string? SessionIdOf(JsonElement message) =>
        message.TryGetProperty("sessionId", out var session) ? session.GetString() : null;

    private void OnContextCreated(JsonElement message)
    {
        try
        {
            var sessionId = SessionIdOf(message);
            var context = message.GetProperty("params").GetProperty("context");
            if (!context.TryGetProperty("auxData", out var aux)) return;
            if (!aux.TryGetProperty("isDefault", out var isDefault) || !isDefault.GetBoolean()) return;
            var id = context.GetProperty("id").GetInt32();
            var frameId = aux.TryGetProperty("frameId", out var frame) ? frame.GetString() ?? "" : "";
            lock (_frameContexts)
            {
                _frameContexts.RemoveAll(x => x.ContextId == id && string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
                _frameContexts.Add(new FrameContext(id, frameId, sessionId));
            }
            // Install the click monitor as soon as a frame's JS context exists, so a query
            // click that fires shortly after load is observed even in a late-loaded OOPIF.
            if (frameId.Length > 0 && (IsAuthorizedFrameId(frameId) || IsPendingChildFrame(frameId)))
            {
                var monitor = MonitorScript();
                _ = Task.Run(async () =>
                {
                    var cdp = _cdp;
                    if (cdp is null) return;
                    try { await EvaluateContextAsync(monitor, id, sessionId, CancellationToken.None, awaitPromise: true); }
                    catch { }
                });
            }
        }
        catch (Exception ex) { AppLog.Write($"读取执行上下文失败：{ex.Message}"); }
    }

    /// <summary>A navigated-away frame must not leave a stale context id in the active set.</summary>
    private void OnContextDestroyed(JsonElement message)
    {
        try
        {
            var sessionId = SessionIdOf(message);
            var id = message.GetProperty("params").GetProperty("executionContextId").GetInt32();
            lock (_frameContexts)
                _frameContexts.RemoveAll(x => x.ContextId == id && string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
        }
        catch (Exception ex) { AppLog.Write($"清理执行上下文失败：{ex.Message}"); }
    }

    private void OnContextsCleared(JsonElement message)
    {
        var sessionId = SessionIdOf(message);
        lock (_frameContexts)
        {
            if (sessionId is null) _frameContexts.Clear();
            else _frameContexts.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
        }
    }

    /// <summary>A flattened child target (typically an OOPIF query iframe) was attached.</summary>
    private void OnAttachedToTarget(JsonElement message)
    {
        try
        {
            var parameters = message.GetProperty("params");
            var sessionId = parameters.TryGetProperty("sessionId", out var session) ? session.GetString() ?? "" : "";
            var targetInfo = parameters.GetProperty("targetInfo");
            var targetId = targetInfo.TryGetProperty("targetId", out var target) ? target.GetString() ?? "" : "";
            if (targetId.Length == 0 || sessionId.Length == 0) return;
            // For an iframe target the target id is the frame id.
            lock (_frameTargetSessions) _frameTargetSessions[targetId] = sessionId;
            // Register the target URL so the flattened OOPIF frame is authorized by its own
            // URL. Its Page.frameNavigated is delivered on the child session and can fire
            // before that session enabled Page, which previously left the frame URL unknown
            // and the frame permanently unauthorized (identity stayed waiting|not-started).
            var attachUrl = targetInfo.TryGetProperty("url", out var attachUrlProperty) ? attachUrlProperty.GetString() ?? "" : "";
            if (IsKnownUrl(attachUrl) && !attachUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                lock (_frameUrls) _frameUrls[targetId] = attachUrl;
            _ = Task.Run(async () =>
            {
                var cdp = _cdp;
                if (cdp is null) return;
                try { await cdp.SendAsync("Runtime.enable", null, CancellationToken.None, sessionId); } catch { }
                try { await cdp.SendAsync("Page.enable", null, CancellationToken.None, sessionId); } catch { }
                try { await cdp.SendAsync("DOM.enable", null, CancellationToken.None, sessionId); } catch { }
                // The attach-time URL can still be about:blank (or the navigation committed
                // before Page.enable, which drops the frameNavigated event). Ask the child
                // session for its frame tree once so the real URL is registered either way.
                try
                {
                    var tree = await cdp.SendAsync("Page.getFrameTree", null, CancellationToken.None, sessionId);
                    var rootFrame = tree.GetProperty("result").GetProperty("frameTree").GetProperty("frame");
                    var rootFrameId = rootFrame.GetProperty("id").GetString() ?? "";
                    var rootUrl = FrameUrl(rootFrame);
                    if (rootFrameId.Length > 0 && IsKnownUrl(rootUrl))
                        lock (_frameUrls) _frameUrls[rootFrameId] = rootUrl;
                }
                catch { }
                // A child target (typically an OOPIF query frame) needs the click monitor at
                // document start too, otherwise a query that completes before the C# side
                // injects the monitor is never observed and identity stays "waiting". The
                // monitor only; the capture card must stay in the main frame.
                try
                {
                    await cdp.SendAsync("Page.addScriptToEvaluateOnNewDocument",
                        new { source = MonitorScript() }, CancellationToken.None, sessionId);
                }
                catch { }
                try
                {
                    await cdp.SendAsync("Target.setAutoAttach",
                        new { autoAttach = true, waitForDebuggerOnStart = false, flatten = true }, CancellationToken.None, sessionId);
                }
                catch { }
            });
        }
        catch (Exception ex) { AppLog.Write($"处理子目标附加失败：{ex.Message}"); }
    }

    private void OnDetachedFromTarget(JsonElement message)
    {
        try
        {
            var sessionId = message.GetProperty("params").TryGetProperty("sessionId", out var session) ? session.GetString() : null;
            if (string.IsNullOrEmpty(sessionId)) return;
            lock (_frameTargetSessions)
                foreach (var key in _frameTargetSessions.Where(x => x.Value == sessionId).Select(x => x.Key).ToList())
                    _frameTargetSessions.Remove(key);
            lock (_frameContexts) _frameContexts.RemoveAll(x => string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));
        }
        catch (Exception ex) { AppLog.Write($"处理子目标分离失败：{ex.Message}"); }
    }

    private void OnFrameNavigated(JsonElement message)
    {
        try
        {
            // Events carry the flattened child sessionId when they come from an attached
            // OOPIF. Only the page's own (root) session can speak for the top-level frame; a
            // child target reports its own root frame with no parentId, so parentId alone is
            // not enough to identify the page main frame.
            var sessionId = SessionIdOf(message);
            var frame = message.GetProperty("params").GetProperty("frame");
            var frameId = frame.GetProperty("id").GetString() ?? "";
            var url = FrameUrl(frame);
            var parentId = frame.TryGetProperty("parentId", out var parent) ? parent.GetString() ?? "" : "";
            var isMain = sessionId is null && parentId.Length == 0;
            lock (_frameUrls)
            {
                // A placeholder address is not a navigation target; forget the old one instead of
                // recording it, so the frame is resolved again rather than judged by a stale URL.
                if (frameId.Length > 0)
                {
                    if (IsKnownUrl(url)) _frameUrls[frameId] = url;
                    else _frameUrls.Remove(frameId);
                }
                if (isMain) { _mainFrameId = frameId; _currentUrl = url; }
            }
            // A child-session frame without parentId must keep its already known parent link
            // (its owner lives in the parent document); never overwrite a real parent with an
            // empty string. The root main frame legitimately has no parent and is left unset.
            if (frameId.Length > 0 && parentId.Length > 0)
                lock (_frameParents) _frameParents[frameId] = parentId;
            if (isMain)
            {
                Interlocked.Increment(ref _navigationGeneration);
                if (!IsTargetUrl(url) && url.Length > 0)
                    _ = SetWidgetStateAsync("error", "已离开核验目标页面，截图请求将被忽略。");
            }
        }
        catch (Exception ex) { AppLog.Write($"读取页面导航失败：{ex.Message}"); }
    }

    /// <summary>
    /// Fragment / history navigation of a single-page app (the official page routes by hash).
    /// Its full URL, fragment included, replaces the recorded frame address; leaving the
    /// inquiry route counts as a navigation, staying on it does not interrupt a capture.
    /// </summary>
    private void OnNavigatedWithinDocument(JsonElement message)
    {
        try
        {
            var sessionId = SessionIdOf(message);
            var parameters = message.GetProperty("params");
            var frameId = parameters.GetProperty("frameId").GetString() ?? "";
            var url = parameters.TryGetProperty("url", out var value) ? value.GetString() ?? "" : "";
            if (frameId.Length == 0 || !IsKnownUrl(url)) return;
            bool isMain;
            lock (_frameUrls)
            {
                _frameUrls[frameId] = url;
                isMain = sessionId is null && frameId.Equals(_mainFrameId, StringComparison.Ordinal);
                if (isMain) _currentUrl = url;
            }
            if (isMain && !IsTargetUrl(url))
            {
                Interlocked.Increment(ref _navigationGeneration);
                _ = SetWidgetStateAsync("error", "已离开核验目标页面，截图请求将被忽略。");
            }
        }
        catch (Exception ex) { AppLog.Write($"读取页面内导航失败：{ex.Message}"); }
    }

    /// <summary>
    /// Only a parseable absolute URL is a real frame address. For a frame that moved to another
    /// process the root session reports the placeholder ":" — which is neither empty nor about:,
    /// so it used to be taken as a known (and unauthorized) URL: the frame got no click monitor
    /// and the unknown-URL refresh skipped it, leaving the query unobserved.
    /// </summary>
    private static bool IsKnownUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out _);

    private bool IsUnknownFrameUrl(string frameId)
    {
        string? url;
        lock (_frameUrls)
        {
            if (!_frameUrls.TryGetValue(frameId, out url) && frameId.Equals(_mainFrameId, StringComparison.Ordinal)) url = _currentUrl;
        }
        if (!IsKnownUrl(url)) return true;
        // An in-process about:blank frame is a real, settled state (its navigations arrive on
        // the root session). A child target still at about:blank has not reported its commit.
        return url!.StartsWith("about:", StringComparison.OrdinalIgnoreCase) && SessionForFrame(frameId) is not null;
    }

    /// <summary>
    /// Resolves frames whose URL is still unknown (or about:blank) from the authoritative CDP
    /// frame trees: the root session for in-process frames and each attached child session for
    /// OOPIFs. An OOPIF usually attaches with an empty URL and its commit can land before the
    /// child session enabled Page, so the one-shot lookup at attach time is not enough; without
    /// this the frame stays unauthorized and its query result is never seen. Only unknown
    /// entries are filled, so a URL recorded by a (newer) navigation event is never replaced.
    /// </summary>
    private async Task RefreshUnknownFrameUrlsAsync(CancellationToken token)
    {
        var cdp = _cdp;
        if (cdp is null || cdp.IsClosed) return;
        List<string> known;
        lock (_frameContexts) known = _frameContexts.Select(x => x.FrameId).Where(x => x.Length > 0).Distinct().ToList();
        lock (_frameTargetSessions) known.AddRange(_frameTargetSessions.Keys);
        if (_mainFrameId.Length > 0) known.Add(_mainFrameId);
        var unknown = known.Distinct(StringComparer.Ordinal).Where(IsUnknownFrameUrl).ToList();
        if (unknown.Count == 0) return;

        void Fill(string frameId, string url)
        {
            // A still-blank document is not an answer; leave it unknown so a later call retries.
            if (frameId.Length == 0 || !IsKnownUrl(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            // Only a child target may replace a recorded about:blank (its commit event can be
            // lost); an in-process or main frame at about:blank is a settled newer state.
            var childTarget = SessionForFrame(frameId) is not null;
            lock (_frameUrls)
            {
                if (_frameUrls.TryGetValue(frameId, out var existing) && IsKnownUrl(existing) &&
                    !(childTarget && existing.StartsWith("about:", StringComparison.OrdinalIgnoreCase))) return;
                _frameUrls[frameId] = url;
                if (frameId.Equals(_mainFrameId, StringComparison.Ordinal) && _currentUrl.Length == 0) _currentUrl = url;
            }
        }

        try
        {
            var tree = await cdp.SendAsync("Page.getFrameTree", null, token);
            var frames = new List<(string FrameId, string Url)>();
            CollectFrames(tree.GetProperty("result").GetProperty("frameTree"), frames);
            foreach (var (id, url) in frames) Fill(id, url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write($"刷新框架地址失败：{ex.Message}"); }

        foreach (var frameId in unknown.Where(IsUnknownFrameUrl))
        {
            var session = SessionForFrame(frameId);
            if (session is null) continue;
            try
            {
                var tree = await cdp.SendAsync("Page.getFrameTree", null, token, session);
                var frame = tree.GetProperty("result").GetProperty("frameTree").GetProperty("frame");
                Fill(frame.GetProperty("id").GetString() ?? "", FrameUrl(frame));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write($"刷新子框架地址失败 {frameId}：{ex.Message}"); }
        }
    }

    private bool IsPendingChildFrame(string frameId)
    {
        string? url;
        lock (_frameUrls) _frameUrls.TryGetValue(frameId, out url);
        if (IsKnownUrl(url) && !url!.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return false;
        string? parent;
        lock (_frameParents) _frameParents.TryGetValue(frameId, out parent);
        return string.IsNullOrEmpty(parent) || parent.Equals(_mainFrameId, StringComparison.Ordinal);
    }

    private string MonitorScript() => BrowserScript("monitor", DeclarationNo);

    private string ShellScript()
    {
        var monitor = MonitorScript();
        var widget = BrowserScript("widget", DeclarationNo).Replace("__SAVE_DIR__", JsonSerializer.Serialize(_targetFolder));
        return monitor + ";\n" + widget + ";";
    }

    private async Task<bool> AutofillAsync(CancellationToken token)
    {
        var filled = false;
        for (var i = 0; i < 30 && !filled; i++)
        {
            await Task.Delay(500, token);
            // A query frame (including a cross-site OOPIF) can finish loading after the
            // initial injection; keep installing the click monitor so the query click is
            // observed wherever the form actually lives.
            try { await InjectMonitorAsync(token); }
            catch (Exception ex) when (!IsConnectionFailure(ex)) { AppLog.Write($"向子框架安装监视器失败：{ex.Message}"); }
            string result;
            try { result = await EvaluateInAllFramesAsync(BrowserScript("autofill", DeclarationNo), token, "filled"); }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                try { await ReconnectAsync(token); } catch { return false; }
                result = "";
            }
            filled = result.Contains("filled", StringComparison.OrdinalIgnoreCase);
        }
        return filled;
    }

    /// <summary>
    /// Layout-independent result fingerprint used to detect in-flight changes. Only the
    /// top-level shell and the frames that actually hold the query result are consulted:
    /// "waiting" from the outer shell is not a verdict and must not short-circuit a ready
    /// query frame (R5-1).
    /// </summary>
    private Task<string> VerifyStableAsync(CancellationToken token) =>
        EvaluateAllContextsAsync(BrowserScript("verify", DeclarationNo), token, false,
            "stable|", "mismatch|", "error|", "stale|");

    private static bool IsPreparedMarker(string value) =>
        value.Contains("prepared:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("already-prepared", StringComparison.OrdinalIgnoreCase);

    /// <summary>A concrete place a capture script may run: a frame plus its CDP session/context.</summary>
    private sealed record EvalTarget(string FrameId, int? ContextId, string? SessionId);

    private string? SessionForFrame(string frameId)
    {
        lock (_frameTargetSessions) return _frameTargetSessions.TryGetValue(frameId, out var session) ? session : null;
    }

    private FrameContext? ContextForFrame(string frameId)
    {
        List<FrameContext> snapshot;
        lock (_frameContexts) snapshot = _frameContexts.ToList();
        return snapshot
            .Where(x => x.FrameId.Equals(frameId, StringComparison.Ordinal))
            .OrderBy(x => x.SessionId is null ? 0 : 1)
            .FirstOrDefault();
    }

    private async Task<int?> CreateIsolatedWorldAsync(string frameId, string worldName, string? session, CancellationToken token)
    {
        if (_cdp is null) return null;
        try
        {
            var world = session is null
                ? await _cdp.SendAsync("Page.createIsolatedWorld", new { frameId, worldName, grantUniveralAccess = true }, token)
                : await _cdp.SendAsync("Page.createIsolatedWorld", new { frameId, worldName, grantUniveralAccess = true }, token, session);
            return world.GetProperty("result").GetProperty("executionContextId").GetInt32();
        }
        catch (Exception ex)
        {
            AppLog.Write($"无法建立框架执行环境 {frameId}：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Authorized evaluation targets: the main frame plus every authorized child frame. A
    /// frame that has not reported a default context (pre-existing frame or OOPIF) gets an
    /// isolated world on its owning CDP session so its DOM is still reachable.
    /// </summary>
    private async Task<List<EvalTarget>> AuthorizedEvalTargetsAsync(CancellationToken token)
    {
        await RefreshUnknownFrameUrlsAsync(token);
        var targets = new List<EvalTarget> { new(_mainFrameId, null, null) };
        List<string> frameIds;
        lock (_frameUrls) frameIds = _frameUrls.Keys.ToList();
        foreach (var frameId in frameIds)
        {
            if (frameId.Equals(_mainFrameId, StringComparison.Ordinal)) continue;
            if (!IsAuthorizedFrameId(frameId)) continue;
            token.ThrowIfCancellationRequested();
            var context = ContextForFrame(frameId);
            if (context is not null)
            {
                targets.Add(new EvalTarget(frameId, context.ContextId, context.SessionId));
                continue;
            }
            var session = SessionForFrame(frameId);
            var contextId = await CreateIsolatedWorldAsync(frameId, "customs-console-frame", session, token);
            targets.Add(new EvalTarget(frameId, contextId, session));
        }
        return targets;
    }

    private Task<string> EvaluateTargetAsync(string expression, EvalTarget target, CancellationToken token, bool awaitPromise) =>
        EvaluateContextAsync(expression, target.ContextId, target.SessionId, token, awaitPromise);

    /// <summary>
    /// Runs the prepare expression target by target. Every target is registered in the
    /// restore ledger BEFORE the command is sent, so a lost/failed/cancelled response still
    /// restores it; a missing prepared marker or a page script exception rejects the capture
    /// instead of silently saving a half-prepared page (R5-2).
    /// </summary>
    private async Task PrepareContextsAsync(string expression, List<EvalTarget> prepared, CancellationToken token)
    {
        if (_cdp is null) return;
        var targets = await AuthorizedEvalTargetsAsync(token);
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            prepared.Add(target);
            string value;
            try
            {
                value = await EvaluateTargetAsync(expression, target, token, awaitPromise: true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (!IsConnectionFailure(ex))
            {
                throw new InvalidOperationException($"网页框架状态准备失败（{target.FrameId}）：{ex.Message}", ex);
            }
            if (!IsPreparedMarker(value))
                throw new InvalidOperationException($"网页框架状态准备未确认（{target.FrameId}）：{value}");
        }
    }

    private async Task RestoreContextsAsync(string expression, IReadOnlyList<EvalTarget> prepared, CancellationToken token)
    {
        if (_cdp is null) return;
        // Restore in reverse order so the main frame's window scroll is put back last.
        foreach (var target in prepared.Reverse())
        {
            if (token.IsCancellationRequested) break;
            try { await EvaluateTargetAsync(expression, target, token, awaitPromise: true); }
            catch (Exception ex) { AppLog.Write($"网页状态恢复失败：{ex.Message}"); }
        }
    }

    /// <summary>
    /// A CDP Frame's address. <c>Frame.url</c> excludes the fragment, which CDP reports
    /// separately as <c>urlFragment</c>; the official inquiry page routes by fragment
    /// (<c>https://www.singlewindow.cn/#/publicInquiryDetail?id=pi4</c>), so without it the
    /// main page was recorded as <c>https://www.singlewindow.cn/</c> and every capture request
    /// was refused as "当前页面不是核验目标页".
    /// </summary>
    internal static string FrameUrl(JsonElement frame)
    {
        var url = frame.TryGetProperty("url", out var urlValue) ? urlValue.GetString() ?? "" : "";
        if (url.Length > 0 && !url.Contains('#') &&
            frame.TryGetProperty("urlFragment", out var fragmentValue) && fragmentValue.GetString() is { Length: > 1 } fragment)
            url += fragment.StartsWith('#') ? fragment : "#" + fragment;
        return url;
    }

    private static void CollectFrames(JsonElement tree, List<(string FrameId, string Url)> frames)
    {
        if (!tree.TryGetProperty("frame", out var frame)) return;
        var id = frame.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
        var url = FrameUrl(frame);
        if (id.Length > 0) frames.Add((id, url));
        if (tree.TryGetProperty("childFrames", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) CollectFrames(child, frames);
    }

    private static void CollectFrameParents(JsonElement tree, Dictionary<string, string> parents)
    {
        if (!tree.TryGetProperty("frame", out var frame)) return;
        var id = frame.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
        var parent = frame.TryGetProperty("parentId", out var parentValue) ? parentValue.GetString() ?? "" : "";
        if (id.Length > 0 && parent.Length > 0) parents[id] = parent;
        if (tree.TryGetProperty("childFrames", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) CollectFrameParents(child, parents);
    }

    /// <summary>The CDP session that owns a frame's parent document (where its owner element lives).</summary>
    private string? OwnerSessionForFrame(string frameId)
    {
        string? parent;
        lock (_frameParents) _frameParents.TryGetValue(frameId, out parent);
        if (string.IsNullOrEmpty(parent) || parent.Equals(_mainFrameId, StringComparison.Ordinal)) return null;
        return SessionForFrame(parent);
    }

    private async Task<string?> GetFrameOwnerObjectIdAsync(string frameId, CancellationToken token)
    {
        if (_cdp is null) return null;
        var session = OwnerSessionForFrame(frameId);
        var owner = session is null
            ? await _cdp.SendAsync("DOM.getFrameOwner", new { frameId }, token)
            : await _cdp.SendAsync("DOM.getFrameOwner", new { frameId }, token, session);
        if (!owner.GetProperty("result").TryGetProperty("backendNodeId", out var backend) || backend.GetInt32() <= 0) return null;
        var resolved = session is null
            ? await _cdp.SendAsync("DOM.resolveNode", new { backendNodeId = backend.GetInt32() }, token)
            : await _cdp.SendAsync("DOM.resolveNode", new { backendNodeId = backend.GetInt32() }, token, session);
        if (!resolved.GetProperty("result").TryGetProperty("object", out var remote)) return null;
        return remote.TryGetProperty("objectId", out var objectId) ? objectId.GetString() : null;
    }

    private async Task<string> CallFunctionOnAsync(string objectId, string function, object? argument, CancellationToken token, string? sessionId = null)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开。");
        object parameters = argument is null
            ? (object)new { objectId, functionDeclaration = function, returnByValue = true, awaitPromise = false }
            : new { objectId, functionDeclaration = function, arguments = new[] { argument }, returnByValue = true, awaitPromise = false };
        var response = await cdp.SendAsync("Runtime.callFunctionOn", parameters, token, sessionId);
        try
        {
            var result = response.GetProperty("result").GetProperty("result");
            return result.TryGetProperty("value", out var value) ? value.ToString() : "";
        }
        catch { return ""; }
    }

    private static readonly string ReadFrameGeometryFunction =
        "function(){ if(!this||this.tagName!=='IFRAME') return JSON.stringify({ok:false,reason:'not-iframe'});" +
        " var r=this.getBoundingClientRect(); var mh=getComputedStyle(this).maxHeight||'';" +
        " return JSON.stringify({ok:true,client:this.clientHeight||0,rect:Math.round(r.height),maxHeight:mh," +
        " bottom:Math.round(r.bottom+(window.scrollY||0)),right:Math.round(r.right+(window.scrollX||0)),left:Math.round(r.left+(window.scrollX||0))}); }";

    private static readonly string GrowFrameFunction =
        "function(h){ if(!this||this.tagName!=='IFRAME') return '0';" +
        " if(this.__cccPrevHeight===undefined){ this.__cccPrevHeight=this.style.getPropertyValue('height'); this.__cccPrevHeightPriority=this.style.getPropertyPriority('height'); }" +
        " if(this.__cccPrevMaxHeight===undefined){ this.__cccPrevMaxHeight=this.style.getPropertyValue('max-height'); this.__cccPrevMaxHeightPriority=this.style.getPropertyPriority('max-height'); }" +
        " this.style.setProperty('height', h+'px', 'important'); this.style.setProperty('max-height', 'none', 'important');" +
        " return String(h); }";

    private static readonly string RestoreFrameFunction =
        "function(){ if(!this||this.tagName!=='IFRAME') return 'ok';" +
        " if(this.__cccPrevHeight!==undefined){ if(this.__cccPrevHeight) this.style.setProperty('height', this.__cccPrevHeight, this.__cccPrevHeightPriority||''); else this.style.removeProperty('height'); delete this.__cccPrevHeight; delete this.__cccPrevHeightPriority; }" +
        " if(this.__cccPrevMaxHeight!==undefined){ if(this.__cccPrevMaxHeight) this.style.setProperty('max-height', this.__cccPrevMaxHeight, this.__cccPrevMaxHeightPriority||''); else this.style.removeProperty('max-height'); delete this.__cccPrevMaxHeight; delete this.__cccPrevMaxHeightPriority; }" +
        " return 'ok'; }";

    private static bool TryReadFrameGeometry(string json, out int visibleHeight, out double maxHeight, out int bottom, out int right, out int left)
    {
        left = 0;
        visibleHeight = 0;
        maxHeight = double.PositiveInfinity;
        bottom = 0;
        right = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return false;
            visibleHeight = root.TryGetProperty("client", out var client) ? client.GetInt32() : 0;
            bottom = root.TryGetProperty("bottom", out var bottomElement) ? bottomElement.GetInt32() : 0;
            right = root.TryGetProperty("right", out var rightElement) ? rightElement.GetInt32() : 0;
            left = root.TryGetProperty("left", out var leftElement) ? leftElement.GetInt32() : 0;
            if (root.TryGetProperty("maxHeight", out var max))
            {
                var text = max.GetString() ?? "";
                maxHeight = text.Equals("none", StringComparison.OrdinalIgnoreCase) ? double.PositiveInfinity :
                    double.TryParse(text.Replace("px", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.PositiveInfinity;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// An attached child-session (out-of-process) frame that participates in the capture, with
    /// its owner box's page-space bottom/right. It is recorded whether or not the owner had to
    /// be grown, because the real viewport must be enlarged whenever such a frame is painted
    /// beyond it; tying that decision to "did we change the owner" misses a frame that was
    /// already tall enough for its content but still taller than the physical viewport.
    /// </summary>
    private sealed record OopifPaintRange(string FrameId, int Left, int Bottom, int Right, int ContextId, string? SessionId);

    public Task<string> EvaluateRawAsync(string expression, CancellationToken token) =>
        EvaluateContextAsync(expression, null, token, awaitPromise: false);

    private async Task<bool> EvaluateBooleanAsync(string expression, CancellationToken token)
    {
        var value = await EvaluateContextAsync(expression, null, token, awaitPromise: false);
        return value.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> EvaluateIntAsync(string expression, CancellationToken token)
    {
        var value = await EvaluateContextAsync(expression, null, token, awaitPromise: false);
        return int.TryParse(value.Trim(), out var result) ? result : -1;
    }

    // ---- CDP evaluation helpers ----

    private Task<string> EvaluateContextAsync(string expression, int? contextId, CancellationToken token, bool awaitPromise) =>
        EvaluateContextAsync(expression, contextId, null, token, awaitPromise);

    private async Task<string> EvaluateContextAsync(string expression, int? contextId, string? sessionId, CancellationToken token, bool awaitPromise)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开。");
        var parameters = contextId is null
            ? (object)new { expression, returnByValue = true, awaitPromise }
            : new { expression, contextId = contextId.Value, returnByValue = true, awaitPromise };
        var response = await cdp.SendAsync("Runtime.evaluate", parameters, token, sessionId);
        // A page script exception is a normal response payload, not a protocol error; it must
        // be surfaced so a failed prepare/verify is never mistaken for an empty success.
        if (response.TryGetProperty("result", out var envelope) && envelope.TryGetProperty("exceptionDetails", out var exception))
            throw new InvalidOperationException($"网页脚本异常：{exception}");
        try
        {
            var result = response.GetProperty("result").GetProperty("result");
            return result.TryGetProperty("value", out var value) ? value.ToString() : "";
        }
        catch { return ""; }
    }

    private async Task<string> SafeEvaluateAsync(string expression, int? contextId, string? sessionId, CancellationToken token)
    {
        try { return await EvaluateContextAsync(expression, contextId, sessionId, token, awaitPromise: true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { return ""; }
    }

    /// <summary>Ranking used only when no context produced a terminal marker.</summary>
    private static int MarkerPriority(string value)
    {
        if (value.Contains("stable|", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("ready|", StringComparison.OrdinalIgnoreCase)) return 6;
        if (value.Contains("mismatch|", StringComparison.OrdinalIgnoreCase)) return 5;
        if (value.Contains("error|", StringComparison.OrdinalIgnoreCase)) return 4;
        if (value.Contains("stale|", StringComparison.OrdinalIgnoreCase)) return 3;
        if (value.Contains("loading|", StringComparison.OrdinalIgnoreCase)) return 2;
        if (value.Contains("waiting|", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    /// <summary>
    /// Evaluates an expression in the top-level shell and in every authorized frame context.
    /// A terminal marker in ANY frame wins; "waiting" from the outer shell does not stop the
    /// search for a ready iframe. When nothing is terminal the strongest pending value is
    /// returned so the caller can still refuse correctly.
    /// </summary>
    private async Task<string> EvaluateAllContextsAsync(string expression, CancellationToken token, bool childrenFirst, params string[] acceptedMarkers)
    {
        if (_cdp is null) return "";
        if (acceptedMarkers.Length == 0) acceptedMarkers = ["ready|", "mismatch|", "error|", "loading|", "stale|", "prepared:", "restored:", "nothing-to-restore"];
        static bool Accepted(string value, IReadOnlyCollection<string> markers) =>
            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

        var results = new List<string>();
        var main = await SafeEvaluateAsync(expression, null, null, token);
        results.Add(main);
        if (Accepted(main, acceptedMarkers)) return main;

        await RefreshUnknownFrameUrlsAsync(token);
        var contexts = AuthorizedContexts();
        var ordered = childrenFirst
            ? contexts.OrderBy(x => x.FrameId.Equals(_mainFrameId, StringComparison.Ordinal) ? 1 : 0).ToList()
            : contexts.OrderBy(x => x.FrameId.Equals(_mainFrameId, StringComparison.Ordinal) ? 0 : 1).ToList();
        foreach (var context in ordered)
        {
            token.ThrowIfCancellationRequested();
            var value = await SafeEvaluateAsync(expression, context.ContextId, context.SessionId, token);
            if (Accepted(value, acceptedMarkers)) return value;
            results.Add(value);
        }
        return results.OrderByDescending(MarkerPriority).ThenByDescending(x => x.Length).FirstOrDefault() ?? main;
    }

    private async Task EvaluateAllContextsAsync(string expression, CancellationToken token, bool childrenFirst, bool bestEffort)
    {
        if (_cdp is null) return;
        await RefreshUnknownFrameUrlsAsync(token);
        var contexts = AuthorizedContexts();
        var ordered = childrenFirst
            ? contexts.OrderBy(x => x.FrameId.Equals(_mainFrameId, StringComparison.Ordinal) ? 1 : 0).ToList()
            : contexts.OrderBy(x => x.FrameId.Equals(_mainFrameId, StringComparison.Ordinal) ? 0 : 1).ToList();
        foreach (var context in ordered)
        {
            if (token.IsCancellationRequested) break;
            try { await EvaluateContextAsync(expression, context.ContextId, context.SessionId, token, awaitPromise: true); }
            catch (Exception ex) { if (!bestEffort) throw; AppLog.Write($"网页状态准备/恢复失败：{ex.Message}"); }
        }
        try { await EvaluateContextAsync(expression, null, null, token, awaitPromise: true); }
        catch (Exception ex) { if (!bestEffort) throw; AppLog.Write($"网页状态准备/恢复失败：{ex.Message}"); }
    }

    private async Task<string> EvaluateInAllFramesAsync(string expression, CancellationToken token, params string[] acceptedMarkers)
    {
        if (_cdp is null) return "";
        if (acceptedMarkers.Length == 0) acceptedMarkers = ["filled"];
        static bool Accepted(string value, IReadOnlyCollection<string> markers) =>
            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

        var main = await SafeEvaluateAsync(expression, null, null, token);
        if (Accepted(main, acceptedMarkers)) return main;

        var targets = await AuthorizedEvalTargetsAsync(token);
        foreach (var target in targets)
        {
            if (target.FrameId.Equals(_mainFrameId, StringComparison.Ordinal) && target.ContextId is null) continue;
            try
            {
                var value = await EvaluateTargetAsync(expression, target, token, awaitPromise: true);
                if (Accepted(value, acceptedMarkers)) return value;
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { }
        }
        return main;
    }

    private static IEnumerable<string> EnumerateFrameIds(JsonElement tree)
    {
        if (tree.TryGetProperty("frame", out var frame) && frame.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } frameId)
            yield return frameId;
        if (!tree.TryGetProperty("childFrames", out var children) || children.ValueKind != JsonValueKind.Array) yield break;
        foreach (var child in children.EnumerateArray())
            foreach (var childId in EnumerateFrameIds(child)) yield return childId;
    }
}
