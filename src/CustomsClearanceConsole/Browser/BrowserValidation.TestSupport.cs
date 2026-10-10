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

    /// <summary>
    /// E2E diagnostics: every registered execution context with its frame, CDP session,
    /// authorization and whether the click monitor reached it. Used only to explain an
    /// OOPIF failure; it never relaxes a check.
    /// </summary>
    public async Task<string> DescribeFramesForTestAsync(CancellationToken token)
    {
        List<FrameContext> contexts;
        lock (_frameContexts) contexts = _frameContexts.ToList();
        var parts = new List<string>();
        foreach (var context in contexts)
        {
            string? url;
            lock (_frameUrls) _frameUrls.TryGetValue(context.FrameId, out url);
            string monitor;
            try
            {
                monitor = await SafeEvaluateAsync(
                    "JSON.stringify({monitor:!!window.__customsConsoleMonitor,bound:!!(window.__customsConsoleMonitor&&window.__customsConsoleMonitor.document===document),queryAt:(window.__customsConsoleMonitor&&window.__customsConsoleMonitor.queryAt)||0,href:location.href,ready:document.readyState})",
                    context.ContextId, context.SessionId, token);
            }
            catch (Exception ex) { monitor = "error:" + ex.Message; }
            parts.Add($"frame={context.FrameId};session={context.SessionId ?? "-"};authorized={IsAuthorizedFrameId(context.FrameId)};url={url};monitor={monitor}");
        }
        lock (_frameTargetSessions)
            foreach (var pair in _frameTargetSessions) parts.Add($"target={pair.Key};session={pair.Value}");
        return parts.Count == 0 ? "(未登记任何执行上下文)" : string.Join(" | ", parts);
    }

    /// <summary>Top-level frame id owned by the root CDP session (E2E identity evidence).</summary>
    public string MainFrameIdForTest
    {
        get { lock (_frameUrls) return _mainFrameId; }
    }

    /// <summary>Top-level page URL owned by the root CDP session (E2E identity evidence).</summary>
    public string CurrentUrlForTest
    {
        get { lock (_frameUrls) return _currentUrl; }
    }

    /// <summary>Top-level navigation generation, only advanced by a real main-frame navigation.</summary>
    public int NavigationGenerationForTest => Volatile.Read(ref _navigationGeneration);

    /// <summary>Snapshot of every registered frame id and its last known URL.</summary>
    public IReadOnlyDictionary<string, string> FrameUrlsForTest()
    {
        lock (_frameUrls) return new Dictionary<string, string>(_frameUrls);
    }

    /// <summary>The registered parent link for a frame, or null when no parent is known.</summary>
    public string? FrameParentForTest(string frameId)
    {
        lock (_frameParents) return _frameParents.TryGetValue(frameId, out var parent) ? parent : null;
    }

    /// <summary>
    /// Navigates a flattened child target's own document through its child CDP session. This
    /// is the exact event path where Page.frameNavigated arrives with no parentId and must not
    /// be mistaken for a top-level navigation (E2E regression helper).
    /// </summary>
    public async Task<bool> NavigateChildFrameForTestAsync(string frameId, string url, CancellationToken token)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开。");
        string? session;
        lock (_frameTargetSessions) _frameTargetSessions.TryGetValue(frameId, out session);
        if (string.IsNullOrEmpty(session)) return false;
        await cdp.SendAsync("Page.navigate", new { url }, token, session);
        return true;
    }

    /// <summary>Hit-test result of the last real card-button click (E2E evidence).</summary>
    public string LastClickHitTarget { get; private set; } = "";

    /// <summary>
    /// Clicks the visible web card button with real CDP mouse input (not the internal
    /// requestCapture API), so the E2E exercises the same hit-tested path a user does.
    /// Returns false when the button rectangle cannot be located.
    /// </summary>
    public async Task<bool> ClickCaptureButtonAsync(CancellationToken token)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开。");
        LastClickFailure = "";
        LastClickExactHit = "";
        LastClickPointer = "";
        // The card is hidden and shown around every capture. A CDP click dispatched before the
        // card is painted again can be lost (a compositor/paint risk to verify, not a proven
        // cause), so the single click is gated on a bounded render-readiness condition instead
        // of a blind sleep or an automatic second click. The first ready probe fixes the
        // baseline viewport every later click must see restored.
        try
        {
            LastClickReadiness = await WaitForWidgetInteractiveAsync(TimeSpan.FromSeconds(3), _clickBaselineViewport, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastClickReadiness = "就绪探测失败:" + ex.Message;
        }
        if (!TryParseReadiness(LastClickReadiness, out var readiness))
            return FailClick($"无法解析卡片就绪诊断：{Shorten(LastClickReadiness)}");
        // Dispatch nothing unless the snapshot is a valid ready=true that also proves the button
        // itself (not just the host card) is enabled, visible and exactly under the point. A
        // readiness timeout or a not-ready/exact-miss snapshot returns false with the
        // diagnostic instead of sending a mouse event the page cannot deliver.
        if (!readiness.Ready || !readiness.ExactButtonHit || !readiness.Enabled || !readiness.Visible)
            return FailClick($"卡片未就绪，未发送真实点击：{Shorten(LastClickReadiness)}");
        if (_clickBaselineViewport is null && readiness.Viewport is { } baseline)
            _clickBaselineViewport = baseline;
        var rectJson = await EvaluateContextAsync(
            "JSON.stringify((window.__cccWidget && window.__cccWidget.captureButtonRect && window.__cccWidget.captureButtonRect()) || null)",
            null, token, awaitPromise: false);
        if (string.IsNullOrWhiteSpace(rectJson) || rectJson.TrimStart().StartsWith("null", StringComparison.OrdinalIgnoreCase))
            return FailClick("未找到可见的卡片按钮矩形。");
        double x, y;
        try
        {
            using var document = JsonDocument.Parse(rectJson);
            var root = document.RootElement;
            x = root.GetProperty("x").GetDouble() + root.GetProperty("width").GetDouble() / 2;
            y = root.GetProperty("y").GetDouble() + root.GetProperty("height").GetDouble() / 2;
        }
        catch (Exception ex) { return FailClick($"读取卡片按钮位置失败：{ex.Message}"); }
        var point = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{x},{y}");
        // Re-check the exact point once more after taking the rect and immediately before the
        // trusted mouse input: a readiness snapshot that has gone stale must not authorise a
        // changed/hidden point. The closed-shadow hit is the exact evidence; top host is kept.
        LastClickExactHit = await EvaluateContextAsync(
            $"JSON.stringify((window.__cccWidget && window.__cccWidget.hitTest && window.__cccWidget.hitTest({point})) || null)",
            null, token, awaitPromise: false);
        if (!TryParseExactButtonHit(LastClickExactHit, out var exactButtonHit, out var topIsHost))
            return FailClick($"无法解析发送前精确命中复核：{Shorten(LastClickExactHit)}");
        LastClickHitTarget = topIsHost ? "host" : "none";
        if (!exactButtonHit || !topIsHost)
            return FailClick($"发送前精确命中复核失败（exact={exactButtonHit},host={topIsHost}）：{Shorten(LastClickExactHit)}");
        // The page's own hit test runs on fresh layout, but the browser routes real input by the
        // compositor's hit-test data, which can still describe the frame that was grown during
        // the previous capture. A click sent then lands in that frame and is silently lost. So the
        // real mouse is moved first and only pressed once the button itself has received the
        // move; a move is not a click, and exactly one press/release is sent.
        var routed = await WaitForPointerRoutedAsync(cdp, x, y, token);
        if (routed is null)
            return FailClick($"真实指针移动未到达卡片按钮，未发送按下：{Shorten(LastClickPointer)}");
        var (clickX, clickY) = routed.Value;
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x = clickX, y = clickY, button = "left", clickCount = 1 }, token);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x = clickX, y = clickY, button = "left", clickCount = 1 }, token);
        return true;
    }

    /// <summary>Evidence of the pointer-routing handshake before the last real card click.</summary>
    public string LastClickPointer { get; private set; } = "";

    private async Task<long> ReadPointerMovesAsync(CancellationToken token)
    {
        var value = await EvaluateContextAsync(
            "String((window.__cccWidget && window.__cccWidget.diagnostics) ? window.__cccWidget.diagnostics().pointerMoveCount : -1)",
            null, token, awaitPromise: false);
        return long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : -1;
    }

    /// <summary>
    /// Moves the real mouse inside the button (a small alternating offset so every move is a
    /// genuine position change) until the button reports a routed pointermove, bounded to a
    /// few seconds. Returns the point that was confirmed, or null.
    /// </summary>
    private async Task<(double X, double Y)?> WaitForPointerRoutedAsync(CdpClient cdp, double x, double y, CancellationToken token)
    {
        var baseline = await ReadPointerMovesAsync(token);
        if (baseline < 0) { LastClickPointer = "卡片不支持指针计数"; return null; }
        var started = DateTime.UtcNow;
        for (var attempt = 0; DateTime.UtcNow - started < TimeSpan.FromSeconds(5); attempt++)
        {
            var offset = attempt % 2 == 0 ? -2.0 : 2.0;
            var pointX = x + offset;
            await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x = pointX, y, button = "none", clickCount = 0 }, token);
            var waitStarted = DateTime.UtcNow;
            while (DateTime.UtcNow - waitStarted < TimeSpan.FromMilliseconds(200))
            {
                var moves = await ReadPointerMovesAsync(token);
                if (moves > baseline)
                {
                    LastClickPointer = FormattableString.Invariant($"moves={attempt + 1};routed={moves - baseline}");
                    return (pointX, y);
                }
                await Task.Delay(25, token);
            }
        }
        LastClickPointer = FormattableString.Invariant($"5 秒内指针移动未路由到按钮；基线={baseline}");
        return null;
    }

    private bool FailClick(string reason)
    {
        LastClickFailure = reason;
        AppLog.Write($"真实点击未执行：{reason}");
        return false;
    }

    private static string Shorten(string value) => value.Length > 400 ? value[..400] : value;

    /// <summary>Navigates the page (E2E helper for leave/return and reload flows).</summary>
    public async Task NavigateAsync(string url, CancellationToken token)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开。");
        await cdp.SendAsync("Page.navigate", new { url }, token);
    }

    /// <summary>
    /// Clicks an element inside a direct child iframe with real CDP mouse input. The
    /// element's own rect is found in whichever authorized frame contains it, then offset
    /// by the iframe element's rect in the top page. Used to trigger the official query
    /// button like a user, not through page script.
    /// </summary>
    public async Task<bool> ClickElementInFrameAsync(string frameSelector, string elementSelector, CancellationToken token)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开。");
        var selector = JsonSerializer.Serialize(elementSelector);
        var rectValue = "";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            rectValue = await EvaluateAllContextsAsync(
                $"(() => {{ const el = document.querySelector({selector}); if (!el) return ''; const r = el.getBoundingClientRect(); return 'rect|' + r.left + ',' + r.top + ',' + r.width + ',' + r.height; }})()",
                token, false, "rect|");
            if (rectValue.StartsWith("rect|", StringComparison.Ordinal)) break;
            await Task.Delay(250, token);
        }
        if (!rectValue.StartsWith("rect|", StringComparison.Ordinal)) return false;
        var parts = rectValue[5..].Split(',');
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var left) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var top) ||
            !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height)) return false;
        var frameJson = await EvaluateContextAsync(
            $"JSON.stringify((function(){{ var f = document.querySelector({JsonSerializer.Serialize(frameSelector)}); return f ? f.getBoundingClientRect() : null; }})())",
            null, token, awaitPromise: false);
        double frameLeft = 0, frameTop = 0;
        try
        {
            using var document = JsonDocument.Parse(frameJson);
            frameLeft = document.RootElement.GetProperty("left").GetDouble();
            frameTop = document.RootElement.GetProperty("top").GetDouble();
        }
        catch (Exception ex) { AppLog.Write($"读取 iframe 位置失败：{ex.Message}"); return false; }
        var x = frameLeft + left + width / 2;
        var y = frameTop + top + height / 2;
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y, button = "none", clickCount = 0 }, token);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, token);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, token);
        return true;
    }

    /// <summary>Aborts the control socket so the reconnect production path can be exercised.</summary>
    public void SimulateConnectionDropForTest() => _cdp?.AbortForTest();

    /// <summary>
    /// E2E-only fault injection: makes the next <paramref name="count"/> viewport reads fail so
    /// the required reject-on-unreadable behavior for an OOPIF capture is exercised without a
    /// real browser fault. Normal runs never set this.
    /// </summary>
    public void FailNextViewportReadsForTest(int count) => _viewportReadFailures = Math.Max(0, count);

    public Task<string> CurrentUrlAsync(CancellationToken token) =>
        EvaluateContextAsync("location.href", null, token, awaitPromise: false);

    public Task<bool> CanCaptureAsync(CancellationToken token) =>
        EvaluateBooleanAsync("!!(window.__cccWidget && window.__cccWidget.canCapture())", token);

    /// <summary>
    /// E2E contract hook: whether the single-owner capture gate is currently held. The
    /// production completion event must never be observed while this is true, so a fast retry
    /// can always run once the page shows a retryable button.
    /// </summary>
    public bool CaptureLockHeldForTest => Volatile.Read(ref _captureGate) != 0;

    /// <summary>Render-readiness evidence captured immediately before the last real card click.</summary>
    public string LastClickReadiness { get; private set; } = "";

    /// <summary>Exact closed-shadow button hit evidence for the last real card click.</summary>
    public string LastClickExactHit { get; private set; } = "";

    /// <summary>Why the last real card click was not dispatched, or empty when it was.</summary>
    public string LastClickFailure { get; private set; } = "";

    /// <summary>
    /// Render-readiness observed just before the last production completion event was published.
    /// This is a separate recovery/UX signal: it never changes the saved-file verdict.
    /// </summary>
    public string LastCompletionReadiness { get; private set; } = "";

    /// <summary>
    /// E2E diagnostics for the card's input chain: binding availability, card state, exact
    /// closed-shadow button rect, viewport/DPR and the closure's DOM-click and binding-call
    /// counters. Every real-click/timeout branch records this so a lost click is localised
    /// (harness hit-test, page handler or CDP binding) instead of guessed.
    /// </summary>
    public Task<string> DescribeWidgetForTestAsync(CancellationToken token) =>
        EvaluateRawAsync(
            "JSON.stringify((window.__cccWidget && window.__cccWidget.diagnostics) ? window.__cccWidget.diagnostics() : null)",
            token);

    /// <summary>
    /// Bounded render-readiness gate (never a blind sleep): resolves once the card is enabled
    /// and visible with a rect stable for two animation frames and an exact closed-shadow button
    /// hit; when <paramref name="expectedViewport"/> is given the temporarily enlarged capture
    /// viewport must first be restored. Returns the JSON diagnostics snapshot.
    /// </summary>
    public Task<string> WaitForWidgetInteractiveAsync(TimeSpan timeout, (int Width, int Height, double Scale)? expectedViewport, CancellationToken token)
    {
        var milliseconds = (int)Math.Clamp(timeout.TotalMilliseconds, 0, 30_000);
        var expected = expectedViewport is { } viewport
            ? FormattableString.Invariant($"{{w:{viewport.Width},h:{viewport.Height},dpr:{viewport.Scale}}}")
            : "null";
        return EvaluateContextAsync(
            $"window.__cccWidget && window.__cccWidget.whenInteractive ? window.__cccWidget.whenInteractive({milliseconds}, {expected}).then(function(r){{ return JSON.stringify(r); }}) : Promise.resolve('')",
            null, token, awaitPromise: true);
    }

    /// <summary>
    /// The decision fields parsed out of a whenInteractive diagnostics snapshot. Parsing is
    /// strict: a snapshot that is missing ready/hit/enabled/visible is not accepted as ready.
    /// </summary>
    private sealed record ClickReadinessSnapshot(
        bool Ready, string Reason, bool ExactButtonHit, bool Enabled, bool Visible,
        (int Width, int Height, double Scale)? Viewport);

    private static bool TryParseReadiness(string? json, out ClickReadinessSnapshot snapshot)
    {
        snapshot = new ClickReadinessSnapshot(false, "", false, false, false, null);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var ready = root.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True;
            var reason = root.TryGetProperty("reason", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString() ?? "" : "";
            var enabled = root.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True;
            var visible = root.TryGetProperty("visible", out var vi) && vi.ValueKind == JsonValueKind.True;
            var exact = root.TryGetProperty("hit", out var hit) && hit.ValueKind == JsonValueKind.Object
                && hit.TryGetProperty("exactButtonHit", out var eb) && eb.ValueKind == JsonValueKind.True;
            (int Width, int Height, double Scale)? viewport = null;
            if (root.TryGetProperty("viewport", out var v) && v.ValueKind == JsonValueKind.Object &&
                v.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number &&
                v.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.Number &&
                v.TryGetProperty("dpr", out var dpr) && dpr.ValueKind == JsonValueKind.Number)
            {
                var width = w.GetInt32();
                var height = h.GetInt32();
                var scale = dpr.GetDouble();
                if (width > 0 && height > 0 && scale > 0) viewport = (width, height, scale);
            }
            snapshot = new ClickReadinessSnapshot(ready, reason, exact, enabled, visible, viewport);
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Parses the exact closed-shadow button hit evidence returned by widget.hitTest.</summary>
    private static bool TryParseExactButtonHit(string? json, out bool exactButtonHit, out bool topIsHost)
    {
        exactButtonHit = false;
        topIsHost = false;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            exactButtonHit = root.TryGetProperty("exactButtonHit", out var e) && e.ValueKind == JsonValueKind.True;
            topIsHost = root.TryGetProperty("topIsHost", out var t) && t.ValueKind == JsonValueKind.True;
            return true;
        }
        catch (JsonException) { return false; }
    }

    public Task<int> CountWidgetHostsAsync(CancellationToken token) =>
        EvaluateIntAsync("document.querySelectorAll('#ccc-widget-host').length", token);

    /// <summary>E2E: capture cards mounted in any frame other than the main frame (must stay 0).</summary>
    public async Task<int> CountSubframeWidgetHostsAsync(CancellationToken token)
    {
        var total = 0;
        foreach (var target in await AuthorizedEvalTargetsAsync(token))
        {
            if (target.FrameId.Equals(_mainFrameId, StringComparison.Ordinal)) continue;
            try
            {
                var value = await EvaluateTargetAsync("String(document.querySelectorAll('#ccc-widget-host').length)", target, token, awaitPromise: false);
                if (int.TryParse(value.Trim('"'), out var count)) total += count;
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { }
        }
        return total;
    }
}
