using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CustomsClearanceConsole;

/// <summary>
/// Controlled end-to-end harness for the in-page manual long screenshot. It serves
/// a synthetic single-window-like page over loopback, drives the visible card button
/// through real CDP mouse input, and asserts success, mismatch, too-long, double-click,
/// re-injection, tiling, frames, navigation, reconnect and mid-capture result changes.
/// It never touches the real website.
/// </summary>
internal static class BrowserCaptureE2E
{
    public sealed record Scenario(string Name, bool Pass, IReadOnlyList<string> Details);

    /// <summary>
    /// Reads the fixture iframe's inline height/max-height and both priorities. Used to
    /// compare the frame style captured before the capture with the style after restore, so
    /// the assertion is against the real original instead of a hard-coded value.
    /// </summary>
    private const string FrameStyleProbe =
        "JSON.stringify((function(){var f=document.getElementById('inner');if(!f)return {missing:true};" +
        "return {height:f.style.getPropertyValue('height'),heightPriority:f.style.getPropertyPriority('height')," +
        "maxHeight:f.style.getPropertyValue('max-height'),maxHeightPriority:f.style.getPropertyPriority('max-height')};})())";

    public static async Task<int> RunAsync(string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var checks = new List<Scenario>();
        var browser = TestBrowser();
        if (browser is null)
        {
            WriteReport(outputFolder, checks, ["未找到 Edge 或 Chrome，无法运行受控浏览器截图 E2E。"]);
            Console.Error.WriteLine("BROWSER_E2E_FAILED · 未找到 Edge/Chrome");
            return 1;
        }

        using var server = new TestServer();
        server.Start();
        try
        {
            checks.Add(await RunSuccessAsync(outputFolder, browser, server));
            checks.Add(await RunTilingAsync(outputFolder, browser, server));
            checks.Add(await RunMismatchAsync(outputFolder, browser, server));
            checks.Add(await RunTooLongAsync(outputFolder, browser, server));
            checks.Add(await RunDoubleClickAsync(outputFolder, browser, server));
            checks.Add(await RunReloadAsync(outputFolder, browser, server));
            checks.Add(await RunNoQueryAsync(outputFolder, browser, server));
            checks.Add(await RunCaptchaErrorAsync(outputFolder, browser, server));
            checks.Add(await RunWriteRejectedAsync(outputFolder, browser, server));
            checks.Add(await RunRepeatCaptureAsync(outputFolder, browser, server));
            checks.Add(await RunIframeAsync(outputFolder, browser, server));
            checks.Add(await RunNoClickAsync(outputFolder, browser, server));
            checks.Add(await RunMissingNumberAsync(outputFolder, browser, server));
            checks.Add(await RunStaleAsync(outputFolder, browser, server));
            checks.Add(await RunFailureRetryAsync(outputFolder, browser, server));
            checks.Add(await RunPrepareFailureRestoresAsync(outputFolder, browser, server));
            checks.Add(await RunCrossOriginFrameAsync(outputFolder, browser, server));
            checks.Add(await RunCrossSiteOopifFrameAsync(outputFolder, browser, server));
            checks.Add(await RunCrossSiteOopifPreexpandedAsync(outputFolder, browser, server));
            checks.Add(await RunChildSessionNavigationAsync(outputFolder, browser, server));
            checks.Add(await RunOopifViewportReadFailureAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialDomSuccessAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialHashRouteAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialSameSiteFrameAsync(outputFolder, browser, server));
            checks.Add(await RunStalledCaptureAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialDomWrongNumberAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialDomEmptyAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialDomErrorAsync(outputFolder, browser, server));
            checks.Add(await RunOfficialDomResultChangeAsync(outputFolder, browser, server));
            checks.Add(await RunConcurrentSessionsAsync(outputFolder, browser, server));
            checks.Add(await RunNavigationReturnAsync(outputFolder, browser, server));
            checks.Add(await RunResultChangeAsync(outputFolder, browser, server));
            checks.Add(await RunReconnectAsync(outputFolder, browser, server));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            checks.Add(new Scenario("harness", false, [ex.ToString()]));
        }

        var issues = checks.Where(x => !x.Pass).SelectMany(x => x.Details).ToList();
        WriteReport(outputFolder, checks, issues);
        Console.WriteLine($"BROWSER_E2E · {checks.Count(x => x.Pass)}/{checks.Count} scenarios passed");
        foreach (var check in checks)
            Console.WriteLine($"  {(check.Pass ? "PASS" : "FAIL")}: {check.Name} · {string.Join(" | ", check.Details)}");
        return issues.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Edge/Chrome for the controlled E2E. CUSTOMS_CONSOLE_E2E_BROWSER may name another Chromium
    /// executable (the portable harness in tests/BrowserE2E.Portable uses it on Linux/macOS).
    /// </summary>
    internal static string? TestBrowser()
    {
        var configured = Environment.GetEnvironmentVariable("CUSTOMS_CONSOLE_E2E_BROWSER");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        return BrowserValidation.FindBrowser("Edge") ?? BrowserValidation.FindBrowser("Chrome");
    }

    private static BrowserCaptureResult Harness(BrowserValidation session, string message) =>
        new(session.SessionId, session.DeclarationNo, "harness", null, message);

    /// <summary>Starts the session, waits for the card and optionally the query result.</summary>
    private static async Task<string?> StartAndWaitAsync(BrowserValidation session, bool waitForSettled)
    {
        await session.StartAsync(CancellationToken.None);
        if (!await session.WaitForWidgetAsync(TimeSpan.FromSeconds(30), CancellationToken.None)) return "控件未注入。";
        if (waitForSettled)
        {
            var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            if (identity.StartsWith("waiting|", StringComparison.Ordinal) || identity.Length == 0)
            {
                string frames;
                try { frames = await session.DescribeFramesForTestAsync(CancellationToken.None); }
                catch (Exception ex) { frames = "帧诊断失败：" + ex.Message; }
                return $"查询结果未就绪：{identity}；帧诊断：{frames}";
            }
        }
        return null;
    }

    /// <summary>Performs a real mouse click on the visible card button and waits for the production verdict.</summary>
    private static async Task<BrowserCaptureResult> ClickAndAwaitAsync(BrowserValidation session, TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<BrowserCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<BrowserCaptureResult> handler = (_, value) => completion.TrySetResult(value);
        session.CaptureCompleted += handler;
        try
        {
            var before = await WidgetSnapshotAsync(session);
            if (!await session.ClickCaptureButtonAsync(CancellationToken.None))
                return Harness(session, $"未找到可见的卡片按钮，未执行真实点击；点击前卡片={before}；就绪={session.LastClickReadiness}；指针={session.LastClickPointer}；失败={session.LastClickFailure}。");
            try { return await completion.Task.WaitAsync(timeout); }
            catch (TimeoutException)
            {
                var final = await WidgetSnapshotAsync(session);
                return Harness(session, $"测试侧等待超时，未取得生产结论；点击前={before}；超时={final}；命中={session.LastClickHitTarget}；精确命中={session.LastClickExactHit}；就绪={session.LastClickReadiness}；指针={session.LastClickPointer}。");
            }
        }
        finally { session.CaptureCompleted -= handler; }
    }

    /// <summary>
    /// Real-mouse click that also asserts the deterministic retry ordering contract: the
    /// single-owner capture gate must already be released at the moment the production
    /// completion event is published. The event handler runs synchronously on the capture
    /// thread, so a gate released only in an outer finally (the fixed race) is observed as
    /// held and the immediate retry would be swallowed while the card stays capturing.
    /// </summary>
    private static async Task<(BrowserCaptureResult Result, bool GateHeldAtCompletion)> ClickAndAwaitContractAsync(
        BrowserValidation session, TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<BrowserCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateHeld = false;
        EventHandler<BrowserCaptureResult> handler = (_, value) =>
        {
            if (session.CaptureLockHeldForTest) gateHeld = true;
            completion.TrySetResult(value);
        };
        session.CaptureCompleted += handler;
        try
        {
            // Exactly one real click. The click helper waits for a bounded render-readiness
            // condition first, then the closure's DOM-click/binding-call counters are read before
            // and after so a lost click is localised to the harness/hit-test, the page handler or
            // the CDP binding instead of being papered over with an automatic second click.
            var before = await WidgetSnapshotAsync(session);
            if (!await session.ClickCaptureButtonAsync(CancellationToken.None))
                return (Harness(session, $"未找到可见的卡片按钮，未执行真实点击；点击前卡片={before}；就绪={session.LastClickReadiness}；指针={session.LastClickPointer}；失败={session.LastClickFailure}。"), gateHeld);
            var after = await WidgetSnapshotAsync(session);
            if (!await WaitForInputRegistrationAsync(session, before, TimeSpan.FromSeconds(5)))
            {
                var stalled = await WidgetSnapshotAsync(session);
                return (Harness(session, $"真实点击未到达生产后端（未自动补点）；点击前={before}；点击后={after}；5 秒后={stalled}；命中={session.LastClickHitTarget}；精确命中={session.LastClickExactHit}；就绪={session.LastClickReadiness}；指针={session.LastClickPointer}。"), gateHeld);
            }
            try { return (await completion.Task.WaitAsync(timeout), gateHeld); }
            catch (TimeoutException)
            {
                var final = await WidgetSnapshotAsync(session);
                return (Harness(session, $"测试侧等待超时，未取得生产结论；点击前={before}；点击后={after}；超时={final}；命中={session.LastClickHitTarget}；精确命中={session.LastClickExactHit}；就绪={session.LastClickReadiness}；指针={session.LastClickPointer}。"), gateHeld);
            }
        }
        finally { session.CaptureCompleted -= handler; }
    }

    /// <summary>Best-effort card/input-chain snapshot for a real click; never throws.</summary>
    private static async Task<string> WidgetSnapshotAsync(BrowserValidation session)
    {
        try { return await session.DescribeWidgetForTestAsync(CancellationToken.None); }
        catch (Exception ex) { return "卡片诊断失败=" + ex.Message; }
    }

    /// <summary>
    /// True once the page registered the click (binding-call counter advanced past the pre-click
    /// snapshot). A missing DOM click only proves the point never produced a button click (a
    /// paint/compositor loss or a harness miss stays a risk, not a proven cause); a DOM click
    /// without a binding call points at the page handler; a binding call without a backend
    /// accept points at the CDP binding. Never re-clicks: the caller reports the exact counters
    /// when this is false.
    /// </summary>
    private static async Task<bool> WaitForInputRegistrationAsync(BrowserValidation session, string beforeJson, TimeSpan timeout)
    {
        TryReadInputCounters(beforeJson, out _, out var beforeBinding);
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            var snapshot = await WidgetSnapshotAsync(session);
            if (TryReadInputCounters(snapshot, out _, out var binding) && binding > beforeBinding) return true;
            await Task.Delay(120);
        }
        return false;
    }

    /// <summary>Reads the closure's DOM-click/binding-call counters out of a diagnostics snapshot.</summary>
    private static bool TryReadInputCounters(string json, out long domClicks, out long bindingCalls)
    {
        domClicks = 0;
        bindingCalls = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("domClickCount", out var dom) || dom.ValueKind != JsonValueKind.Number) return false;
            if (!document.RootElement.TryGetProperty("bindingCallCount", out var binding) || binding.ValueKind != JsonValueKind.Number) return false;
            domClicks = dom.GetInt64();
            bindingCalls = binding.GetInt64();
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// True when a whenInteractive snapshot explicitly reports ready=true. Used to assert the
    /// completion-publish recovery readiness as a signal independent from the saved verdict.
    /// </summary>
    private static bool IsReadySnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Carries the fixture iframe's inline style read before the capture. A read failure is
    /// recorded here (not swallowed) so the owning scenario fails instead of skipping the
    /// restore comparison.
    /// </summary>
    private sealed class FrameStyleEvidence
    {
        public string? Before { get; set; }

        public Exception? BeforeError { get; set; }
    }

    /// <summary>
    /// Carries the real viewport (innerWidth/innerHeight/DPR) read before and after a capture
    /// that temporarily enlarges it, so the restoration is asserted from runtime evidence
    /// instead of trusting that the finally branch exists in source.
    /// </summary>
    private sealed class ViewportEvidence
    {
        public string? Before { get; set; }
        public string? BeforeError { get; set; }
        public string? After { get; set; }
        public string? AfterError { get; set; }
    }

    private const string ViewportProbe = "JSON.stringify({w:window.innerWidth,h:window.innerHeight,dpr:window.devicePixelRatio})";

    private static async Task<BrowserCaptureResult> DriveAsync(BrowserValidation session, string? evidencePath, bool waitForSettled,
        FrameStyleEvidence? styleEvidence = null, ViewportEvidence? viewportEvidence = null)
    {
        var problem = await StartAndWaitAsync(session, waitForSettled);
        // The page is loaded by now; let the scenario snapshot the original inline style
        // before the capture mutates and restores it. Runs even when the settle wait failed
        // so a failing scenario can still report the real original.
        if (styleEvidence is not null)
        {
            try { styleEvidence.Before = await session.EvaluateRawAsync(FrameStyleProbe, CancellationToken.None); }
            catch (Exception ex) { styleEvidence.BeforeError = ex; }
            // Without a valid original there is no basis to claim a restore, so a pre-capture
            // read failure is immediately reported as this scenario's failure.
            if (styleEvidence.BeforeError is not null)
                return Harness(session, $"捕获前读取 iframe 原样式失败，场景失败：{styleEvidence.BeforeError.Message}");
        }
        if (viewportEvidence is not null)
        {
            try { viewportEvidence.Before = await session.EvaluateRawAsync(ViewportProbe, CancellationToken.None); }
            catch (Exception ex) { viewportEvidence.BeforeError = ex.Message; }
            if (viewportEvidence.BeforeError is not null)
                return Harness(session, $"捕获前读取真实视口失败，场景失败：{viewportEvidence.BeforeError}");
        }
        if (problem is not null) return Harness(session, problem);
        if (!waitForSettled) await Task.Delay(1200);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        if (viewportEvidence is not null)
        {
            try { viewportEvidence.After = await session.EvaluateRawAsync(ViewportProbe, CancellationToken.None); }
            catch (Exception ex) { viewportEvidence.AfterError = ex.Message; }
        }
        if (evidencePath is not null && result.State == "saved" && result.FilePath is not null && File.Exists(result.FilePath))
            File.Copy(result.FilePath, evidencePath, overwrite: true);
        return result;
    }

    /// <summary>The four inline style values the frame fixture restore must give back exactly.</summary>
    private static readonly string[] FrameStyleFields = ["height", "heightPriority", "maxHeight", "maxHeightPriority"];

    /// <summary>Reads the after-capture frame style, turning a protocol/script failure into data.</summary>
    private static async Task<(string? Json, string? Error)> ReadFrameStyleAsync(BrowserValidation session)
    {
        try { return (await session.EvaluateRawAsync(FrameStyleProbe, CancellationToken.None), null); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    /// <summary>
    /// Requires a concrete style reading: parseable JSON, not the probe's missing=true
    /// sentinel, with all four fields present as strings. An empty read or a shape mismatch is
    /// not evidence and must never let the strict restore comparison be skipped.
    /// </summary>
    private static bool TryParseFrameStyle(string? json, out Dictionary<string, string> values, out string error)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) { error = "读取结果为空"; return false; }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = $"根节点不是对象（{root.ValueKind}）"; return false; }
            if (root.TryGetProperty("missing", out var missing) && missing.ValueKind == JsonValueKind.True)
            { error = "探针返回 missing=true，未找到 iframe"; return false; }
            foreach (var field in FrameStyleFields)
            {
                if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
                { error = $"缺少字符串字段 {field}"; return false; }
                values[field] = element.GetString() ?? "";
            }
            error = "";
            return true;
        }
        catch (JsonException ex) { error = $"JSON 解析失败：{ex.Message}"; return false; }
    }

    /// <summary>
    /// Strictly compares the four inline style values read before and after the capture. Both
    /// readings must first validate as concrete evidence; only then are values compared, so an
    /// empty/unparseable/absent reading can never be reported as a restored original.
    /// </summary>
    private static void AssertFrameStyleRestored(string? beforeJson, string? beforeError, string? afterJson, string? afterError,
        string label, List<string> details)
    {
        var beforeValid = TryParseFrameStyle(beforeJson, out var before, out var beforeParseError);
        if (!beforeValid)
            details.Add($"{label}：捕获前样式证据无效，无法断言原样式已复原（{beforeError ?? beforeParseError}）。");
        var afterValid = TryParseFrameStyle(afterJson, out var after, out var afterParseError);
        if (!afterValid)
            details.Add($"{label}：捕获后样式证据无效，无法断言原样式已复原（{afterError ?? afterParseError}）。");
        if (!beforeValid || !afterValid) return;
        foreach (var field in FrameStyleFields)
        {
            if (!string.Equals(before[field], after[field], StringComparison.Ordinal))
                details.Add($"{label}：{field} 未复原，原=\"{before[field]}\" 现=\"{after[field]}\"。");
        }
    }

    private static bool TryParseViewport(string? json, out int width, out int height, out double scale, out string error)
    {
        width = 0;
        height = 0;
        scale = 0;
        if (string.IsNullOrWhiteSpace(json)) { error = "读取结果为空"; return false; }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = $"根节点不是对象（{root.ValueKind}）"; return false; }
            if (!root.TryGetProperty("w", out var w) || w.ValueKind != JsonValueKind.Number) { error = "缺少数值字段 w"; return false; }
            if (!root.TryGetProperty("h", out var h) || h.ValueKind != JsonValueKind.Number) { error = "缺少数值字段 h"; return false; }
            if (!root.TryGetProperty("dpr", out var dpr) || dpr.ValueKind != JsonValueKind.Number) { error = "缺少数值字段 dpr"; return false; }
            width = w.GetInt32();
            height = h.GetInt32();
            scale = dpr.GetDouble();
            if (width <= 0 || height <= 0) { error = $"视口尺寸非正：w={width},h={height}"; return false; }
            error = "";
            return true;
        }
        catch (JsonException ex) { error = $"JSON 解析失败：{ex.Message}"; return false; }
    }

    /// <summary>
    /// Asserts the temporarily enlarged viewport really returned to its original value after the
    /// capture. Both readings must be concrete evidence first; the finally branch existing in
    /// source is not runtime restoration evidence.
    /// </summary>
    private static void AssertViewportRestored(ViewportEvidence evidence, string label, List<string> details)
    {
        var beforeValid = TryParseViewport(evidence.Before, out var beforeWidth, out var beforeHeight, out var beforeScale, out var beforeError);
        var afterValid = TryParseViewport(evidence.After, out var afterWidth, out var afterHeight, out var afterScale, out var afterError);
        if (!beforeValid) details.Add($"{label}：捕获前视口证据无效，无法断言恢复（{evidence.BeforeError ?? beforeError}）。");
        if (!afterValid) details.Add($"{label}：捕获后视口证据无效，无法断言恢复（{evidence.AfterError ?? afterError}）。");
        if (!beforeValid || !afterValid) return;
        if (beforeWidth != afterWidth || beforeHeight != afterHeight || Math.Abs(beforeScale - afterScale) > 0.001)
            details.Add($"{label}：临时放大视口未恢复原值（前 {beforeWidth}x{beforeHeight}@{beforeScale}，后 {afterWidth}x{afterHeight}@{afterScale}）。");
    }

    /// <summary>
    /// Full-frame completeness evidence for an out-of-process frame capture: the frame's top
    /// marker, its internal scroll tail, the query result row between them and the frame's
    /// bottom marker must all be painted. A saved file alone cannot prove the cross-process
    /// frame was repainted below the physical viewport, which is the exact prior regression.
    /// Returns true when a marker is missing or the image is short (truncation).
    /// </summary>
    private static bool AssertOopifFrameComplete(Bitmap image, string label, List<string> details)
    {
        var truncated = false;
        if (image.Height < 700) { details.Add($"{label}：截图高度 {image.Height} 未覆盖整页，可能被截断。"); truncated = true; }
        if (!RegionContainsColor(image, new Rectangle(0, 0, image.Width, Math.Min(500, image.Height)), Color.FromArgb(0x00, 0x88, 0xCC)))
            details.Add($"{label}：frame 顶部标记未入图。");
        var internalTail = FindColorRows(image, Color.FromArgb(0xCC, 0x00, 0xCC));
        if (internalTail.Count == 0)
        { details.Add($"{label}：frame 内部滚动尾标未入图（独立进程内容被截断）。"); truncated = true; }
        var frameBottom = FindColorRows(image, Color.FromArgb(0x77, 0x00, 0xAA));
        if (frameBottom.Count == 0)
        { details.Add($"{label}：frame 底部紫标未入图（独立进程内容被截断）。"); truncated = true; }
        if (internalTail.Count > 0 && frameBottom.Count > 0)
        {
            var top = Math.Min(internalTail[^1] + 1, image.Height);
            var bottom = Math.Min(frameBottom[0], image.Height);
            if (bottom <= top) details.Add($"{label}：frame 内部尾标与底部紫标顺序异常（{top}/{bottom}）。");
            else if (!RegionHasDarkText(image, top, bottom))
                details.Add($"{label}：frame 查询结果行未入图。");
        }
        return truncated;
    }

    private static List<int> FindColorRows(Bitmap image, Color expected)
    {
        var rows = new List<int>();
        for (var y = 0; y < image.Height; y += 2)
        {
            for (var x = 0; x < image.Width; x += 4)
            {
                var pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - expected.R) <= 6 && Math.Abs(pixel.G - expected.G) <= 6 && Math.Abs(pixel.B - expected.B) <= 6)
                { rows.Add(y); break; }
            }
        }
        return rows;
    }

    private static bool RegionHasDarkText(Bitmap image, int top, int bottom)
    {
        for (var y = Math.Max(0, top); y < Math.Min(image.Height, bottom); y++)
            for (var x = 0; x < image.Width; x += 2)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R < 70 && pixel.G < 70 && pixel.B < 80) return true;
            }
        return false;
    }

    private static async Task<Scenario> RunSuccessAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "success");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000001", folder,
            server.Url("/page?content=2400&result=310120260000000001"), browser, headless: true, allowTestTarget: true);
        var result = await DriveAsync(session, Path.Combine(outputFolder, "capture-success.png"), waitForSettled: true);
        var details = new List<string>();
        if (result.State != "saved") details.Add($"状态 {result.State}：{result.Message}");
        if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("未生成截图文件。");
        else
        {
            using var image = new Bitmap(result.FilePath);
            if (image.Height < 2500) details.Add($"截图高度 {image.Height} 未覆盖整页（应 >= 2500）。");
            AssertPixel(image, 40, image.Height - 4, Color.FromArgb(0x12, 0x34, 0x56), "页脚标记", details);
            AssertPixel(image, 40, 40, Color.FromArgb(0x65, 0x43, 0x21), "页首标记", details);
            if (!ColumnContainsColor(image, Color.FromArgb(0xCC, 0x00, 0xCC))) details.Add("内部滚动容器末端未出现在截图中。");
            if (AnywhereContainsColor(image, Color.FromArgb(0x13, 0x35, 0x5E))) details.Add("截图中出现卡片主色，控件未被排除。");
            var restore = await session.EvaluateRawAsync("JSON.stringify(window.__cccLastCapture||null)", CancellationToken.None);
            AssertRestoreEvidence(restore, details);
            var hosts = await session.CountWidgetHostsAsync(CancellationToken.None);
            if (hosts != 1) details.Add($"控件实例数 {hosts}，应为 1。");
            if (!await session.CanCaptureAsync(CancellationToken.None)) details.Add("保存后卡片按钮不可再次点击。");
        }
        if (!session.LastClickHitTarget.Contains("host", StringComparison.OrdinalIgnoreCase))
            details.Add($"真实鼠标点击命中 {session.LastClickHitTarget}，未命中卡片按钮。");
        return new Scenario("success-fullpage", details.Count == 0, details.Count == 0 ? ["真实鼠标点击后整页首尾标记、控件排除与页面恢复均通过"] : details);
    }

    private static void AssertRestoreEvidence(string restore, List<string> details)
    {
        if (!restore.Contains("\"widgetWasHidden\":true", StringComparison.Ordinal) ||
            !restore.Contains("\"widgetHiddenAtPrepare\":true", StringComparison.Ordinal))
            details.Add($"页面未记录控件在准备时隐藏、恢复后可见：{restore}");
        if (!restore.Contains("\"stylesRestored\":true", StringComparison.Ordinal))
            details.Add($"原样式未完整恢复：{restore}");
        if (!restore.Contains("\"scrollRestored\":true", StringComparison.Ordinal))
            details.Add($"原滚动位置未完整恢复：{restore}");
        if (!restore.Contains("\"widgetVisibleAfterRestore\":true", StringComparison.Ordinal))
            details.Add($"恢复后控件未重新可见：{restore}");
    }

    private static async Task<Scenario> RunTilingAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "tiling");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000011", folder,
            server.Url("/page?content=14000&result=310120260000000011&mid=11900"), browser, headless: true, allowTestTarget: true);
        var result = await DriveAsync(session, Path.Combine(outputFolder, "capture-tiling.png"), waitForSettled: true);
        var details = new List<string>();
        if (result.State != "saved") details.Add($"状态 {result.State}：{result.Message}");
        if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("未生成拼接截图。");
        else
        {
            using var image = new Bitmap(result.FilePath);
            if (image.Height < 14000) details.Add($"拼接高度 {image.Height} 不足（应覆盖 14000 以上）。");
            AssertPixel(image, 40, 11950, Color.FromArgb(0x00, 0xAA, 0x55), "接缝上侧", details);
            AssertPixel(image, 40, 12050, Color.FromArgb(0x00, 0xAA, 0x55), "接缝下侧", details);
            AssertPixel(image, 40, 40, Color.FromArgb(0x65, 0x43, 0x21), "页首标记", details);
            AssertPixel(image, 40, image.Height - 4, Color.FromArgb(0x12, 0x34, 0x56), "页尾标记", details);
        }
        return new Scenario("tiling-beyond-viewport", details.Count == 0, details.Count == 0 ? ["跨 12000px 分片拼接且接缝内容完整"] : details);
    }

    private static async Task<Scenario> RunMismatchAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "mismatch");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000002", folder,
            server.Url("/page?content=1200&result=310120260000000099"), browser, headless: true, allowTestTarget: true);
        var result = await DriveAsync(session, null, waitForSettled: true);
        var details = new List<string>();
        if (result.State != "mismatch") details.Add($"错误单号应拒绝保存，实际状态 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("错误单号仍然生成了截图文件。");
        return new Scenario("mismatch-rejected", details.Count == 0, details.Count == 0 ? ["结果单号不一致时未保存文件"] : details);
    }

    private static async Task<Scenario> RunTooLongAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "too-long");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000003", folder,
            server.Url("/page?content=30000&result=310120260000000003"), browser, maxPixels: 300_000, headless: true, allowTestTarget: true);
        var result = await DriveAsync(session, null, waitForSettled: true);
        var details = new List<string>();
        if (result.State != "too-long") details.Add($"超长页面应拒绝，实际状态 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("超长页面被裁切并保存了假成功文件。");
        return new Scenario("too-long-rejected", details.Count == 0, details.Count == 0 ? ["超过像素上限时拒绝且无文件"] : details);
    }

    private static async Task<Scenario> RunDoubleClickAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "double");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000004", folder,
            server.Url("/page?content=1500&result=310120260000000004"), browser, headless: true, allowTestTarget: true);
        var completion = new TaskCompletionSource<BrowserCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.CaptureCompleted += (_, value) => completion.TrySetResult(value);
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) return new Scenario("double-click-single-file", false, [problem]);
        await Task.WhenAll(session.ClickCaptureButtonAsync(CancellationToken.None), session.ClickCaptureButtonAsync(CancellationToken.None));
        BrowserCaptureResult result;
        try { result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(120)); }
        catch (TimeoutException)
        {
            var final = await WidgetSnapshotAsync(session);
            return new Scenario("double-click-single-file", false,
                [$"测试侧等待超时，未取得生产结论；卡片={final}；命中={session.LastClickHitTarget}；就绪={session.LastClickReadiness}；指针={session.LastClickPointer}。"]);
        }
        var files = Directory.EnumerateFiles(folder, "*.png").ToList();
        var details = new List<string>();
        if (result.State != "saved") details.Add($"状态 {result.State}：{result.Message}");
        if (files.Count != 1) details.Add($"同一次会话生成 {files.Count} 个文件，应为 1。");
        return new Scenario("double-click-single-file", details.Count == 0, details.Count == 0 ? ["重复点击只保存一次"] : details);
    }

    private static async Task<Scenario> RunReloadAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "reload");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000005", folder,
            server.Url("/page?content=900&result=310120260000000005"), browser, headless: true, allowTestTarget: true);
        await session.StartAsync(CancellationToken.None);
        var details = new List<string>();
        if (!await session.WaitForWidgetAsync(TimeSpan.FromSeconds(30), CancellationToken.None)) details.Add("初次注入失败。");
        try { await session.EvaluateRawAsync("location.reload()", CancellationToken.None); }
        catch (Exception ex) { AppLog.Write($"E2E 刷新页面时连接重置：{ex.Message}"); }
        await Task.Delay(1500);
        if (!await session.WaitForWidgetAsync(TimeSpan.FromSeconds(30), CancellationToken.None)) details.Add("刷新后未重新注入控件。");
        else
        {
            var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            if (identity.StartsWith("waiting|", StringComparison.Ordinal) || identity.Length == 0)
                details.Add($"刷新后结果未就绪：{identity}");
            else
            {
                var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
                if (result.State != "saved") details.Add($"刷新后真实点击截图失败：{result.State}：{result.Message}");
                else if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("刷新后未生成截图文件。");
            }
        }
        var hosts = await session.CountWidgetHostsAsync(CancellationToken.None);
        if (hosts != 1) details.Add($"刷新后控件实例数 {hosts}，应为 1。");
        return new Scenario("reload-click-capture", details.Count == 0, details.Count == 0 ? ["刷新清理旧上下文后真实点击截图成功"] : details);
    }

    private static async Task<Scenario> RunNoQueryAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "no-query");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000006", folder,
            server.Url("/page?content=900&result=310120260000000006&query=0"), browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("no-query-rejected", false, [problem]);
        // The user may have collapsed the card before clicking: the refusal reason must still show.
        await session.EvaluateRawAsync("window.__cccWidget && window.__cccWidget.setCollapsed(true)", CancellationToken.None);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (result.State != "error") details.Add($"未查询应拒绝保存，实际状态 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("未查询仍然生成了截图。");
        using (var card = JsonDocument.Parse(await session.DescribeWidgetForTestAsync(CancellationToken.None)))
        {
            var message = card.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            if (!message.Contains("尚未确认", StringComparison.Ordinal))
                details.Add($"收起的卡片收到拒绝结论后未展开显示原因：{card.RootElement}");
        }
        return new Scenario("no-query-rejected", details.Count == 0, details.Count == 0 ? ["真实点击后未查询时未保存假成功"] : details);
    }

    private static async Task<Scenario> RunCaptchaErrorAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "captcha-error");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000007", folder,
            server.Url("/page?content=900&result=310120260000000007&error=%E9%AA%8C%E8%AF%81%E7%A0%81%E9%94%99%E8%AF%AF"), browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("captcha-error-rejected", false, [problem]);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (result.State != "error") details.Add($"验证码错误应拒绝保存，实际状态 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("验证码错误仍然生成了截图。");
        return new Scenario("captcha-error-rejected", details.Count == 0, details.Count == 0 ? ["验证码错误时未保存假成功"] : details);
    }

    private static async Task<Scenario> RunWriteRejectedAsync(string outputFolder, string browser, TestServer server)
    {
        var target = Path.Combine(outputFolder, "write-rejected", "not-a-directory");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "blocking file");
        await using var session = new BrowserValidation("310120260000000008", target,
            server.Url("/page?content=900&result=310120260000000008"), browser, headless: true, allowTestTarget: true);
        var result = await DriveAsync(session, null, waitForSettled: true);
        var details = new List<string>();
        if (result.State != "error") details.Add($"不可写目录应报错，实际状态 {result.State}：{result.Message}");
        if (Directory.Exists(target) && Directory.EnumerateFiles(target, "*.png").Any()) details.Add("不可写目录仍然生成了文件。");
        return new Scenario("write-rejected", details.Count == 0, details.Count == 0 ? ["目录不可写时失败且无文件"] : details);
    }

    private static async Task<Scenario> RunRepeatCaptureAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "repeat");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000009", folder,
            server.Url("/page?content=1200&result=310120260000000009"), browser, headless: true, allowTestTarget: true);
        var details = new List<string>();
        var first = await DriveAsync(session, null, waitForSettled: true);
        if (first.State != "saved") details.Add($"首次截图失败：{first.Message}");
        if (!await session.CanCaptureAsync(CancellationToken.None)) details.Add("首次截图后按钮不可用，无法重新截图。");
        var (second, gateHeldAtCompletion) = await ClickAndAwaitContractAsync(session, TimeSpan.FromSeconds(120));
        if (gateHeldAtCompletion) details.Add("重截完成事件发布时截图锁仍被持有，后续快速重试可能被吞掉。");
        if (second.State != "saved") details.Add($"重新截图失败：{second.Message}");
        var files = Directory.EnumerateFiles(folder, "*.png").ToList();
        if (files.Count != 2) details.Add($"重新截图应生成 2 个唯一文件，实际 {files.Count}。");
        return new Scenario("repeat-capture-visible-retry", details.Count == 0, details.Count == 0 ? ["可见按钮真实点击可再次截图且文件名唯一"] : details);
    }

    private static async Task<Scenario> RunIframeAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "iframe");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000010", folder,
            server.Url("/page?content=300&result=310120260000000010&frame=1"), browser, headless: true, allowTestTarget: true);
        var result = await DriveAsync(session, Path.Combine(outputFolder, "capture-iframe.png"), waitForSettled: true);
        var details = new List<string>();
        if (result.State != "saved") details.Add($"同源 frame 截图失败：{result.Message}");
        if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("未生成 frame 场景截图。");
        else
        {
            using var image = new Bitmap(result.FilePath);
            if (image.Height < 900) details.Add($"frame 内容未被完整捕获，高度 {image.Height}。");
            if (!ColumnContainsColor(image, Color.FromArgb(0x77, 0x00, 0xAA))) details.Add("frame 底部标记未出现在截图中。");
        }
        return new Scenario("same-origin-frame", details.Count == 0, details.Count == 0 ? ["同源 frame 内容与嵌套滚动被完整捕获"] : details);
    }

    private static async Task<Scenario> RunNoClickAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "no-click");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000012", folder,
            server.Url("/page?content=1200&result=310120260000000012"), browser, headless: true, allowTestTarget: true);
        var details = new List<string>();
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) details.Add(problem);
        await Task.Delay(1500);
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("未点击网页按钮却生成了截图。");
        if (!await session.CanCaptureAsync(CancellationToken.None)) details.Add("未点击时卡片按钮不可用。");
        return new Scenario("no-click-no-save", details.Count == 0, details.Count == 0 ? ["未点击网页按钮不保存文件"] : details);
    }

    private static async Task<Scenario> RunMissingNumberAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "missing-number");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000013", folder,
            server.Url("/page?content=1200&result=310120260000000013&nonumber=1"), browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("missing-number-rejected", false, [problem]);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (!session.LastClickHitTarget.Contains("host", StringComparison.OrdinalIgnoreCase))
            details.Add($"缺号场景真实鼠标点击命中 {session.LastClickHitTarget}，未命中卡片按钮。");
        if (result.State != "error") details.Add($"结果区域缺少单号时应由生产路径拒绝，实际状态 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("结果区域没有单号时仍生成了文件。");
        return new Scenario("missing-number-rejected", details.Count == 0, details.Count == 0 ? ["真实点击后缺少单号的生产路径拒绝保存"] : details);
    }

    private static async Task<Scenario> RunStaleAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "stale");
        Directory.CreateDirectory(folder);
        // pre=1 renders the matching result before the query, so the query changes nothing
        // and identity must report stale|unchanged instead of a false success.
        await using var session = new BrowserValidation("310120260000000014", folder,
            server.Url("/page?content=1200&result=310120260000000014&pre=1"), browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("stale-result-rejected", false, [problem]);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (result.State != "error") details.Add($"旧结果未变化时应由生产路径拒绝，实际状态 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("旧结果未变化时仍生成了文件。");
        return new Scenario("stale-result-rejected", details.Count == 0, details.Count == 0 ? ["真实点击后旧结果未变化拒绝保存"] : details);
    }

    private static async Task<Scenario> RunFailureRetryAsync(string outputFolder, string browser, TestServer server)
    {
        var parent = Path.Combine(outputFolder, "failure-retry");
        Directory.CreateDirectory(parent);
        var target = Path.Combine(parent, "blocked");
        File.WriteAllText(target, "blocking file");
        await using var session = new BrowserValidation("310120260000000015", target,
            server.Url("/page?content=1200&result=310120260000000015"), browser, headless: true, allowTestTarget: true);
        var details = new List<string>();
        var first = await DriveAsync(session, null, waitForSettled: true);
        if (first.State == "saved") details.Add("目录不可写时首次截图意外成功。");
        File.Delete(target);
        Directory.CreateDirectory(target);
        if (!await session.CanCaptureAsync(CancellationToken.None)) details.Add("失败后卡片按钮不可重试。");
        var second = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        if (second.State != "saved") details.Add($"解除阻塞后重试失败：{second.Message}");
        if (!Directory.EnumerateFiles(target, "*.png").Any()) details.Add("重试成功后没有生成截图。");
        return new Scenario("failure-retry", details.Count == 0, details.Count == 0 ? ["失败状态可重试并成功保存"] : details);
    }

    /// <summary>
    /// Production-path failure injection for R5-2: the controlled page makes the SECOND
    /// scroll container throw on its first style write while the first container was
    /// already expanded. The prepare script must reject the capture (no file, no backfill),
    /// the C# finally must restore the already-modified styles and scroll offset, and the
    /// visible card must stay retryable; the one-shot injection then lets the retry save.
    /// </summary>
    private static async Task<Scenario> RunPrepareFailureRestoresAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "prepare-failure-restore");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000022";
        await using var session = new BrowserValidation(number, folder,
            server.Url($"/page?content=1200&result={number}&prepfail=1"), browser, headless: true, allowTestTarget: true);
        var details = new List<string>();
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) return new Scenario("prepare-failure-restore", false, [problem]);
        // Give the first (successfully expanded) container a non-zero scroll offset so its
        // restoration is observable, and confirm the injected failure is armed.
        var armed = await session.EvaluateRawAsync(
            "(function(){var s=document.querySelector('#content .scroller');if(!s)return 'no-scroller';s.scrollTop=300;return 'armed:'+s.scrollTop+':'+(!!document.getElementById('bad-scroller'))+':'+(!!window.__cccPrepareFailArmed);})()",
            CancellationToken.None);
        if (!armed.Contains("armed:300:true:true", StringComparison.Ordinal)) details.Add($"受控页面未能布置失败注入：{armed}");
        var first = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        if (first.State == "saved") details.Add("准备阶段写样式失败仍保存了截图。");
        else if (first.State != "error") details.Add($"准备失败未返回错误结论：{first.State}：{first.Message}");
        else if (!first.Message.Contains("准备失败", StringComparison.Ordinal)) details.Add($"准备失败的结论不是准备错误：{first.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("准备失败仍生成了截图文件。");
        // The already-expanded container must be back to its original inline styles, scroll
        // offset and no leftover prepare ledger.
        var restored = await session.EvaluateRawAsync(
            "JSON.stringify((function(){var s=document.querySelector('#content .scroller');if(!s)return {missing:true};return {height:s.style.getPropertyValue('height'),maxHeight:s.style.getPropertyValue('max-height'),overflow:s.style.getPropertyValue('overflow-y'),scrollTop:s.scrollTop,ledger:!!window.__cccCaptureState,hidden:!!(document.getElementById('ccc-widget-host')&&document.getElementById('ccc-widget-host').style.getPropertyValue('display')==='none')};})())",
            CancellationToken.None);
        if (!restored.Contains("\"height\":\"\"", StringComparison.Ordinal) ||
            !restored.Contains("\"maxHeight\":\"\"", StringComparison.Ordinal) ||
            !restored.Contains("\"overflow\":\"\"", StringComparison.Ordinal))
            details.Add($"失败后先前修改的样式未恢复：{restored}");
        if (!restored.Contains("\"ledger\":false", StringComparison.Ordinal)) details.Add($"失败后准备账本未清理：{restored}");
        if (!restored.Contains("\"hidden\":false", StringComparison.Ordinal)) details.Add($"失败后卡片未恢复可见：{restored}");
        var scrollMatch = System.Text.RegularExpressions.Regex.Match(restored, "\"scrollTop\":(\\d+)");
        if (!scrollMatch.Success || Math.Abs(int.Parse(scrollMatch.Groups[1].Value) - 300) > 1)
            details.Add($"失败后滚动位置未恢复：{restored}");
        if (!await session.CanCaptureAsync(CancellationToken.None)) details.Add("准备失败后卡片按钮不可重试。");
        else
        {
            var second = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
            if (second.State != "saved") details.Add($"解除注入后重试失败：{second.State}：{second.Message}");
            else if (second.FilePath is null || !File.Exists(second.FilePath)) details.Add("重试成功后没有生成截图。");
        }
        return new Scenario("prepare-failure-restore", details.Count == 0,
            details.Count == 0 ? ["准备阶段局部写样式失败不保存且先前修改/滚动/卡片均恢复，可重试成功"] : details);
    }

    private static async Task<Scenario> RunCrossOriginFrameAsync(string outputFolder, string browser, TestServer server)
    {
        using var other = new TestServer();
        other.Start();
        var folder = Path.Combine(outputFolder, "cross-origin-frame");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000016";
        // The query form and the matching result live ONLY in the cross-origin frame; the
        // top-level shell has neither query nor result, like the official page. The frame is
        // additionally capped by max-height so a height-only write would truncate it (R5-3).
        var frameUrl = other.Url($"/frame?content=800&result={number}&query=1");
        var url = server.Url($"/page?content=300&result={number}&frame=cross&clamp=1&xhost={Uri.EscapeDataString(frameUrl)}");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var styleEvidence = new FrameStyleEvidence();
        var result = await DriveAsync(session, Path.Combine(outputFolder, "capture-cross-frame.png"), waitForSettled: true,
            styleEvidence: styleEvidence);
        var details = new List<string>();
        if (result.State != "saved") details.Add($"跨源 frame 截图失败：{result.Message}");
        if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("未生成跨源 frame 截图。");
        else
        {
            using var image = new Bitmap(result.FilePath);
            if (image.Height < 700) details.Add($"跨源 frame 高度不足，可能被截断：{image.Height}。");
            if (!RegionContainsColor(image, new Rectangle(0, 0, image.Width, Math.Min(500, image.Height)), Color.FromArgb(0x00, 0x88, 0xCC)))
                details.Add("跨源 frame 可见顶部标记未出现在截图中。");
            if (!ColumnContainsColor(image, Color.FromArgb(0x77, 0x00, 0xAA)))
                details.Add("跨源 frame 底部标记未出现在截图中（内容被截断）。");
        }
        // The frame element really is height:400px + max-height:400px !important in the
        // fixture; the restore must give back exactly that. Both readings must first be
        // concrete evidence, then all four values are compared strictly.
        var (afterStyle, afterError) = await ReadFrameStyleAsync(session);
        AssertFrameStyleRestored(styleEvidence.Before, styleEvidence.BeforeError?.Message, afterStyle, afterError,
            "跨源 frame 原高度/max-height 及 !important 优先级", details);
        return new Scenario("cross-origin-frame", details.Count == 0, details.Count == 0 ? ["顶层无查询，max-height 约束的跨源 frame 内查询首尾完整且原样式优先级复原"] : details);
    }

    /// <summary>
    /// A frame served from a different registrable site is put out of process by Chromium.
    /// This proves the flattened Target session path reaches, queries, prepares, expands and
    /// captures an OOPIF query frame whose result the top-level shell never sees.
    /// </summary>
    private static async Task<Scenario> RunCrossSiteOopifFrameAsync(string outputFolder, string browser, TestServer server)
    {
        using var other = new TestServer();
        other.Start();
        const string host = "e2e-frame.test";
        var folder = Path.Combine(outputFolder, "oopif-frame");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000022";
        var frameUrl = $"http://{host}:{other.Port}/frame?content=800&result={number}&query=1";
        var url = server.Url($"/page?content=300&result={number}&frame=cross&clamp=1&xhost={Uri.EscapeDataString(frameUrl)}");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true,
            extraBrowserArgs: ["--no-proxy-server", $"--host-resolver-rules=MAP {host} 127.0.0.1"]);
        var styleEvidence = new FrameStyleEvidence();
        var viewportEvidence = new ViewportEvidence();
        var result = await DriveAsync(session, Path.Combine(outputFolder, "capture-oopif-frame.png"), waitForSettled: true,
            styleEvidence: styleEvidence, viewportEvidence: viewportEvidence);
        var details = new List<string>();
        if (result.State != "saved")
        {
            details.Add($"跨站 OOPIF frame 截图失败：{result.Message}");
            // Report the registered frames, their URLs and whether the monitor reached each
            // one, so a persistent failure names the exact condition instead of guessing.
            try { details.Add("OOPIF 帧诊断：" + await session.DescribeFramesForTestAsync(CancellationToken.None)); }
            catch (Exception ex) { details.Add("OOPIF 帧诊断失败：" + ex.Message); }
        }
        if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("未生成跨站 OOPIF frame 截图。");
        else
        {
            using var image = new Bitmap(result.FilePath);
            if (AssertOopifFrameComplete(image, "跨站 OOPIF frame", details))
            {
                try { details.Add("OOPIF 截断帧诊断：" + await session.DescribeFramesForTestAsync(CancellationToken.None)); }
                catch (Exception ex) { details.Add("OOPIF 截断帧诊断失败：" + ex.Message); }
            }
        }
        var (afterStyle, afterError) = await ReadFrameStyleAsync(session);
        AssertFrameStyleRestored(styleEvidence.Before, styleEvidence.BeforeError?.Message, afterStyle, afterError,
            "OOPIF frame 原高度/max-height 及优先级", details);
        AssertViewportRestored(viewportEvidence, "跨站 OOPIF frame", details);
        return new Scenario("cross-site-oopif-frame", details.Count == 0, details.Count == 0 ? ["独立进程 OOPIF frame 内查询经 CDP session 完整捕获、样式与视口均复原"] : details);
    }

    /// <summary>
    /// Regression for the second viewport gap: an attached child-session frame whose owner is
    /// already tall enough for its own content (so the expand path has nothing to grow) but
    /// still taller than the physical viewport must also get the real viewport enlarged. Tying
    /// the enlargement to "did we grow the owner" left this frame on the known-blank
    /// captureBeyondViewport path, so this fixture keeps the 1600px owner and asserts the same
    /// full tail pixels instead of an equivalent "owner was grown" signal.
    /// </summary>
    private static async Task<Scenario> RunCrossSiteOopifPreexpandedAsync(string outputFolder, string browser, TestServer server)
    {
        using var other = new TestServer();
        other.Start();
        const string host = "e2e-frame.test";
        var folder = Path.Combine(outputFolder, "oopif-preexpanded");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000028";
        var frameUrl = $"http://{host}:{other.Port}/frame?content=800&result={number}&query=1";
        // frameh=1600 is intentionally taller than the frame document (~1454px) and the real
        // viewport, with no max-height clamp: the owner already wraps its content.
        var url = server.Url($"/page?content=300&result={number}&frame=cross&frameh=1600&xhost={Uri.EscapeDataString(frameUrl)}");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true,
            extraBrowserArgs: ["--no-proxy-server", $"--host-resolver-rules=MAP {host} 127.0.0.1"]);
        var styleEvidence = new FrameStyleEvidence();
        var viewportEvidence = new ViewportEvidence();
        var result = await DriveAsync(session, Path.Combine(outputFolder, "capture-oopif-preexpanded.png"), waitForSettled: true,
            styleEvidence: styleEvidence, viewportEvidence: viewportEvidence);
        var details = new List<string>();
        if (result.State != "saved")
        {
            details.Add($"预展开 OOPIF frame 截图失败：{result.Message}");
            try { details.Add("OOPIF 帧诊断：" + await session.DescribeFramesForTestAsync(CancellationToken.None)); }
            catch (Exception ex) { details.Add("OOPIF 帧诊断失败：" + ex.Message); }
        }
        if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("未生成预展开 OOPIF frame 截图。");
        else
        {
            using var image = new Bitmap(result.FilePath);
            if (AssertOopifFrameComplete(image, "预展开 OOPIF frame", details))
            {
                try { details.Add("OOPIF 截断帧诊断：" + await session.DescribeFramesForTestAsync(CancellationToken.None)); }
                catch (Exception ex) { details.Add("OOPIF 截断帧诊断失败：" + ex.Message); }
            }
        }
        var (afterStyle, afterError) = await ReadFrameStyleAsync(session);
        AssertFrameStyleRestored(styleEvidence.Before, styleEvidence.BeforeError?.Message, afterStyle, afterError,
            "预展开 OOPIF frame 原高度/max-height 及优先级", details);
        AssertViewportRestored(viewportEvidence, "预展开 OOPIF frame", details);
        return new Scenario("cross-site-oopif-preexpanded-frame", details.Count == 0,
            details.Count == 0 ? ["原本已包住内容的跨进程 OOPIF 在不改 owner 高度时仍经真实视口完整绘制并复原样式/视口"] : details);
    }

    /// <summary>
    /// Regression for the flattened child-session navigation bug: when a cross-site OOPIF
    /// navigates its own root document, Page.frameNavigated arrives on the child CDP session
    /// with no parentId. That event must never replace the top-level frame id/URL or bump the
    /// capture generation, must keep the child frame's known parent link, and must still leave
    /// both the page and the child frame authorized so the real click saves a full capture.
    /// </summary>
    private static async Task<Scenario> RunChildSessionNavigationAsync(string outputFolder, string browser, TestServer server)
    {
        using var other = new TestServer();
        other.Start();
        const string host = "e2e-frame.test";
        var folder = Path.Combine(outputFolder, "child-session-navigation");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000025";
        var frameUrl = $"http://{host}:{other.Port}/frame?content=800&result={number}&query=1";
        var navigatedUrl = $"http://{host}:{other.Port}/frame?content=800&result={number}&query=1&childnav=1";
        var url = server.Url($"/page?content=300&result={number}&frame=cross&clamp=1&xhost={Uri.EscapeDataString(frameUrl)}");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true,
            extraBrowserArgs: ["--no-proxy-server", $"--host-resolver-rules=MAP {host} 127.0.0.1"]);
        var details = new List<string>();
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) return new Scenario("oopif-child-session-navigation", false, [problem]);
        var token = CancellationToken.None;
        var mainIdBefore = session.MainFrameIdForTest;
        var mainUrlBefore = session.CurrentUrlForTest;
        var generationBefore = session.NavigationGenerationForTest;
        var childFrameId = session.FrameUrlsForTest()
            .Where(x => !x.Key.Equals(mainIdBefore, StringComparison.Ordinal)
                        && x.Value.Contains(host, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(childFrameId))
        {
            details.Add("未登记 OOPIF 子 frame，无法执行子 session 导航回归。");
            try { details.Add("子 session 导航帧诊断：" + await session.DescribeFramesForTestAsync(token)); }
            catch (Exception ex) { details.Add("子 session 导航帧诊断失败：" + ex.Message); }
            return new Scenario("oopif-child-session-navigation", false, details);
        }
        var parentBefore = session.FrameParentForTest(childFrameId);
        if (!await session.NavigateChildFrameForTestAsync(childFrameId, navigatedUrl, token))
            details.Add("无法通过子 CDP session 导航 OOPIF 根文档（未找到子 session）。");
        var childUpdated = false;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            await Task.Delay(250);
            var current = session.FrameUrlsForTest().TryGetValue(childFrameId, out var value) ? value : "";
            if (current.Contains("childnav=1", StringComparison.Ordinal)) { childUpdated = true; break; }
        }
        if (!childUpdated)
            details.Add($"子 frame 根导航后其 URL 未更新为 {navigatedUrl}（当前 {session.FrameUrlsForTest().GetValueOrDefault(childFrameId)}）。");
        if (!string.Equals(mainIdBefore, session.MainFrameIdForTest, StringComparison.Ordinal))
            details.Add($"子 session 根导航覆盖了顶层 frame：前={mainIdBefore} 后={session.MainFrameIdForTest}");
        if (!string.Equals(mainUrlBefore, session.CurrentUrlForTest, StringComparison.Ordinal))
            details.Add($"子 session 根导航覆盖了顶层 URL：前={mainUrlBefore} 后={session.CurrentUrlForTest}");
        if (generationBefore != session.NavigationGenerationForTest)
            details.Add($"子 session 根导航递增了顶层导航代次：前={generationBefore} 后={session.NavigationGenerationForTest}");
        var parentAfter = session.FrameParentForTest(childFrameId);
        if (parentBefore is not null && !string.Equals(parentBefore, parentAfter, StringComparison.Ordinal))
            details.Add($"子 session 无 parentId 导航把已知父 frame 覆盖：前={parentBefore} 后={parentAfter}");
        // Both the top-level page and the navigated child frame must still be authorized, so a
        // real card click must still produce a complete saved capture; a saved file alone does
        // not prove the cross-process frame was repainted, so the same full-tail assertions as
        // the OOPIF scenario plus the restored viewport are required here too.
        if (!await session.WaitForWidgetAsync(TimeSpan.FromSeconds(30), token)) details.Add("子 frame 导航后未重新注入控件。");
        else
        {
            var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), token);
            if (identity.StartsWith("waiting|", StringComparison.Ordinal) || identity.Length == 0)
                details.Add($"子 frame 导航后查询结果未就绪：{identity}");
            else
            {
                var viewportEvidence = new ViewportEvidence();
                try { viewportEvidence.Before = await session.EvaluateRawAsync(ViewportProbe, token); }
                catch (Exception ex) { viewportEvidence.BeforeError = ex.Message; }
                var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
                try { viewportEvidence.After = await session.EvaluateRawAsync(ViewportProbe, token); }
                catch (Exception ex) { viewportEvidence.AfterError = ex.Message; }
                if (result.State != "saved") details.Add($"子 frame 导航后截图失败：{result.State}：{result.Message}");
                else if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("子 frame 导航后未生成截图。");
                else
                {
                    using var image = new Bitmap(result.FilePath);
                    if (AssertOopifFrameComplete(image, "子 session 导航 OOPIF", details))
                    {
                        try { details.Add("子 session 导航 OOPIF 截断诊断：" + await session.DescribeFramesForTestAsync(token)); }
                        catch (Exception ex) { details.Add("子 session 导航 OOPIF 截断诊断失败：" + ex.Message); }
                    }
                }
                AssertViewportRestored(viewportEvidence, "子 session 导航 OOPIF", details);
            }
        }
        return new Scenario("oopif-child-session-navigation", details.Count == 0,
            details.Count == 0 ? ["子 session 无 parentId 根导航未覆盖顶层身份/URL/代次，子 frame URL 更新且整页首尾/查询结果完整、样式与视口复原"] : details);
    }

    /// <summary>
    /// The real viewport read is a hard prerequisite when an attached child-session frame
    /// participates: an unreadable viewport must reject the capture instead of silently falling
    /// back to the blank captureBeyondViewport path. All three read attempts are injected as
    /// failures, then the injection is exhausted and the same session must recover and save a
    /// complete capture.
    /// </summary>
    private static async Task<Scenario> RunOopifViewportReadFailureAsync(string outputFolder, string browser, TestServer server)
    {
        using var other = new TestServer();
        other.Start();
        const string host = "e2e-frame.test";
        var folder = Path.Combine(outputFolder, "oopif-viewport-read-failure");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000029";
        var frameUrl = $"http://{host}:{other.Port}/frame?content=800&result={number}&query=1";
        var url = server.Url($"/page?content=300&result={number}&frame=cross&clamp=1&xhost={Uri.EscapeDataString(frameUrl)}");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true,
            extraBrowserArgs: ["--no-proxy-server", $"--host-resolver-rules=MAP {host} 127.0.0.1"]);
        var details = new List<string>();
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) return new Scenario("oopif-viewport-read-failure", false, [problem]);
        // Three attempts are made per capture; fail all of them for the first click.
        session.FailNextViewportReadsForTest(3);
        // The refusal uses the real mouse path. Its completion event is also the deterministic
        // ordering contract: the single-owner capture gate must already be free when the
        // production completion is published, otherwise an immediate retry's binding can be
        // delivered into the still-held gate and swallowed while the card stays capturing.
        var (refused, gateHeldAtRefusal) = await ClickAndAwaitContractAsync(session, TimeSpan.FromSeconds(120));
        var refusalReadiness = session.LastCompletionReadiness;
        if (gateHeldAtRefusal) details.Add("视口失败完成事件发布时截图锁仍被持有，快速重试可能被忙拒绝吞掉。");
        if (refused.State != "error") details.Add($"视口读取失败时未拒绝保存：{refused.State}：{refused.Message}");
        else if (!refused.Message.Contains("视口", StringComparison.Ordinal)) details.Add($"拒绝结论未说明视口读取失败：{refused.Message}");
        if (refused.FilePath is not null && File.Exists(refused.FilePath)) details.Add("视口读取失败时仍返回了截图文件。");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("视口读取失败时目录内仍出现 PNG。");
        // The refusal is a saved-file verdict; the completion-publish readiness is asserted
        // separately so a card that was not yet interactive is reported honestly without
        // pretending the refusal itself was wrong.
        if (!IsReadySnapshot(refusalReadiness))
            details.Add($"视口失败完成事件发布时卡片未就绪（独立于拒绝结论）：{refusalReadiness}");
        // Injection exhausted: the same session must recover through the real mouse path and
        // save a complete capture. A small immediate-retry loop then re-clicks as soon as each
        // capture completes, which is exactly the ordering the fix must keep safe; a retry that
        // is busy-rejected while the card is already capturing would leave it stuck with no
        // further completion event and fail the wait below.
        if (!await session.CanCaptureAsync(CancellationToken.None)) details.Add("视口读取失败被拒后卡片按钮不可再次点击。");
        else
        {
            var (recovered, gateHeldAtRecovery) = await ClickAndAwaitContractAsync(session, TimeSpan.FromSeconds(120));
            var recoveryReadiness = session.LastCompletionReadiness;
            if (gateHeldAtRecovery) details.Add("注入结束恢复完成事件发布时截图锁仍被持有。");
            if (recovered.State != "saved") details.Add($"注入结束后未恢复保存：{recovered.State}：{recovered.Message}");
            else if (recovered.FilePath is null || !File.Exists(recovered.FilePath)) details.Add("注入结束后未生成截图。");
            else
            {
                // The saved-file fact is asserted above and must never be re-judged by the
                // recovery UX; a card that is not yet interactive at completion-publish time is
                // its own, separately reported signal.
                if (!IsReadySnapshot(recoveryReadiness))
                    details.Add($"恢复保存完成，但完成事件发布时卡片未就绪（独立于保存结论）：{recoveryReadiness}");
                using var image = new Bitmap(recovered.FilePath);
                if (AssertOopifFrameComplete(image, "视口失败恢复 OOPIF", details))
                {
                    try { details.Add("视口失败恢复 OOPIF 截断诊断：" + await session.DescribeFramesForTestAsync(CancellationToken.None)); }
                    catch (Exception ex) { details.Add("视口失败恢复 OOPIF 截断诊断失败：" + ex.Message); }
                }
            }
            for (var retry = 1; retry <= 2 && details.Count == 0; retry++)
            {
                if (!await session.CanCaptureAsync(CancellationToken.None)) { details.Add($"快速重试第 {retry} 次前卡片按钮不可点击。"); break; }
                var (again, gateHeldAtRetry) = await ClickAndAwaitContractAsync(session, TimeSpan.FromSeconds(120));
                var retryReadiness = session.LastCompletionReadiness;
                if (gateHeldAtRetry) { details.Add($"快速重试第 {retry} 次完成事件发布时截图锁仍被持有。"); break; }
                if (again.State != "saved")
                {
                    details.Add($"快速重试第 {retry} 次未保存：{again.State}：{again.Message}");
                    break;
                }
                if (!IsReadySnapshot(retryReadiness))
                    details.Add($"快速重试第 {retry} 次保存完成，但完成事件发布时卡片未就绪（独立于保存结论）：{retryReadiness}");
                if (again.FilePath is null || !File.Exists(again.FilePath)) { details.Add($"快速重试第 {retry} 次未生成截图。"); break; }
            }
        }
        var produced = Directory.EnumerateFiles(folder, "*.png").Count();
        if (details.Count == 0 && produced != 3) details.Add($"注入拒绝后应恰好生成 3 张截图（恢复 + 2 次快速重试），实际 {produced}。");
        return new Scenario("oopif-viewport-read-failure", details.Count == 0,
            details.Count == 0 ? ["OOPIF 参与时视口读取失败会拒绝保存，注入结束后同一会话经真实点击恢复并可立即重试完整截图"] : details);
    }

    /// <summary>
    /// The official query frame DOM: the top-level shell has no query, the form and the
    /// result live in the iframe, and the result is rendered as
    /// #queryDetail .display-content .content-field &gt; .field-order. The query button is
    /// triggered by a real CDP mouse click inside the frame.
    /// </summary>
    private static async Task<Scenario> RunOfficialDomSuccessAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-dom");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000023";
        // Like the official page, the target is routed by fragment (#/publicInquiryDetail),
        // which CDP reports separately from Frame.url.
        var url = server.Url($"/page?result={number}&frame=official&mode=ok&lazy=2500") + "#/publicInquiryDetail?id=pi4";
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        var details = new List<string>();
        if (problem is not null) return new Scenario("official-dom-success", false, [problem]);
        if (!await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None))
            details.Add("未能在 frame 内真实点击查询按钮。");
        var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        if (!identity.StartsWith("ready|", StringComparison.Ordinal)) details.Add($"官方 DOM 查询未就绪：{identity}");
        // Like the official page, the query frame shares the renderer process with the top page,
        // so the per-document script runs inside it too. Exactly one card may exist: the top one.
        var topCards = await session.CountWidgetHostsAsync(CancellationToken.None);
        var frameCards = await session.CountSubframeWidgetHostsAsync(CancellationToken.None);
        if (topCards != 1 || frameCards != 0) details.Add($"截图卡片数量错误：顶层 {topCards}、查询 frame 内 {frameCards}（应为 1 和 0）。");
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        if (result.State != "saved") details.Add($"官方 DOM 截图失败：{result.State}：{result.Message}");
        else if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("官方 DOM 未生成截图。");
        else
        {
            File.Copy(result.FilePath, Path.Combine(outputFolder, "capture-official-dom.png"), overwrite: true);
            using var image = new Bitmap(result.FilePath);
            if (image.Height < 900) details.Add($"官方 DOM 高度不足，内容可能被截断：{image.Height}。");
            if (!RegionContainsColor(image, new Rectangle(0, 0, image.Width, Math.Min(500, image.Height)), Color.FromArgb(0x00, 0x88, 0xCC)))
                details.Add("官方 DOM 顶部标记未入图。");
            if (!ColumnContainsColor(image, Color.FromArgb(0x77, 0x00, 0xAA)))
                details.Add("官方 DOM 底部标记未入图（display-content 内容被截断）。");
        }
        return new Scenario("official-dom-success", details.Count == 0, details.Count == 0 ? ["仿官网 DOM 真实点击查询与卡片后 display-content/content-field 完整捕获"] : details);
    }

    /// <summary>
    /// A single-page app that loads on another hash route and only then routes to the inquiry
    /// page (Page.navigatedWithinDocument): the capture must be accepted once the route is the
    /// inquiry route, exactly as on the official page.
    /// </summary>
    private static async Task<Scenario> RunOfficialHashRouteAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-hash-route");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000031";
        var url = server.Url($"/page?result={number}&frame=official&mode=ok&lazy=1500&spa=1") + "#/home";
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        var details = new List<string>();
        if (problem is not null) return new Scenario("official-hash-route", false, [problem]);
        for (var i = 0; i < 40 && !session.CurrentUrlForTest.Contains("#/publicInquiryDetail", StringComparison.Ordinal); i++) await Task.Delay(100);
        if (!session.CurrentUrlForTest.Contains("#/publicInquiryDetail", StringComparison.Ordinal))
            details.Add($"页面内路由切换后记录的地址仍为 {session.CurrentUrlForTest}。");
        if (!await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None))
            details.Add("未能在 frame 内真实点击查询按钮。");
        var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        if (!identity.StartsWith("ready|", StringComparison.Ordinal)) details.Add($"查询未就绪：{identity}");
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        if (result.State != "saved") details.Add($"哈希路由页面截图失败：{result.State}：{result.Message}");
        return new Scenario("official-hash-route", details.Count == 0, details.Count == 0 ? ["单页应用切换到 #/publicInquiryDetail 后截图被接受并保存"] : details);
    }

    /// <summary>
    /// The official configuration exactly: the page on www.* and the query frame on swapp.* of
    /// the same site (same renderer process, but cross-origin, so the page cannot reach into
    /// the frame), created after load, with the hash route.
    /// </summary>
    private static async Task<Scenario> RunOfficialSameSiteFrameAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-same-site");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000032";
        const string top = "www.e2e-sw.test", query = "swapp.e2e-sw.test";
        var frameOrigin = $"http://{query}:{server.Port}";
        var url = $"http://{top}:{server.Port}/page?result={number}&frame=official&mode=ok&lazy=1500&ohost={Uri.EscapeDataString(frameOrigin)}#/publicInquiryDetail?id=pi4";
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true,
            extraBrowserArgs: ["--no-proxy-server", $"--host-resolver-rules=MAP {top} 127.0.0.1, MAP {query} 127.0.0.1"]);
        var problem = await StartAndWaitAsync(session, false);
        var details = new List<string>();
        if (problem is not null) return new Scenario("official-same-site", false, [problem]);
        if (!await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None))
            details.Add("未能在 frame 内真实点击查询按钮。");
        var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        if (!identity.StartsWith("ready|", StringComparison.Ordinal)) details.Add($"查询未就绪：{identity}");
        var started = DateTime.UtcNow;
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        if (result.State != "saved") details.Add($"同站跨源 frame 截图失败（{seconds:F0}s）：{result.State}：{result.Message}");
        else if (seconds > 30) details.Add($"同站跨源 frame 截图耗时 {seconds:F0}s，过慢。");
        else if (result.FilePath is not null)
        {
            using var image = new Bitmap(result.FilePath);
            if (!ColumnContainsColor(image, Color.FromArgb(0x77, 0x00, 0xAA)))
                details.Add("同站跨源 frame 的查询结果底部未入图。");
        }
        return new Scenario("official-same-site", details.Count == 0, details.Count == 0 ? [$"www/swapp 同站跨源查询框架截图保存（{seconds:F1}s）"] : details);
    }

    /// <summary>
    /// A browser that stops answering in the middle of a capture must not leave the card on
    /// "截取中": the capture ends at its deadline with a timeout reason on the card, the page is
    /// restored and the next click captures normally.
    /// </summary>
    private static async Task<Scenario> RunStalledCaptureAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "stalled-capture");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        const string number = "310120260000000033";
        var url = server.Url($"/page?result={number}&frame=official&mode=ok") + "#/publicInquiryDetail?id=pi4";
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        var details = new List<string>();
        if (problem is not null) return new Scenario("stalled-capture", false, [problem]);
        // The query frame may still be loading right after start; retry the real click until it lands.
        var clicked = false;
        for (var attempt = 0; attempt < 40 && !clicked; attempt++)
        {
            clicked = await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None);
            if (!clicked) await Task.Delay(250);
        }
        if (!clicked) details.Add("未能在 frame 内真实点击查询按钮。");
        var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        if (!identity.StartsWith("ready|", StringComparison.Ordinal)) details.Add($"查询未就绪：{identity}");

        session.CaptureDeadline = TimeSpan.FromSeconds(3);
        session.StallCaptureForTest = TimeSpan.FromSeconds(30);
        var started = DateTime.UtcNow;
        var stalled = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(20));
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        if (stalled.State != "error" || !stalled.Message.Contains("截图超时", StringComparison.Ordinal) || !stalled.Message.Contains("准备页面", StringComparison.Ordinal))
            details.Add($"卡住的截图未按时限结束（{seconds:F0}s）：{stalled.State}：{stalled.Message}");
        var card = await WidgetSnapshotAsync(session);
        if (!card.Contains("截图超时", StringComparison.Ordinal)) details.Add($"卡片未显示超时原因：{card}");
        if (session.CaptureLockHeldForTest) details.Add("超时后截图锁仍被占用。");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("超时的截图仍然生成了文件。");

        session.CaptureDeadline = TimeSpan.FromSeconds(60);
        session.StallCaptureForTest = TimeSpan.Zero;
        var retry = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        if (retry.State != "saved") details.Add($"超时后重试未保存：{retry.State}：{retry.Message}");
        return new Scenario("stalled-capture", details.Count == 0, details.Count == 0 ? [$"卡住的截图 {seconds:F1}s 内以超时结束并显示原因，重试保存成功"] : details);
    }

    private static async Task<Scenario> RunOfficialDomWrongNumberAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-dom-wrong");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000024";
        var url = server.Url($"/page?result={number}&frame=official&mode=wrong");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("official-dom-wrong-number", false, [problem]);
        await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None);
        var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (!identity.StartsWith("mismatch|", StringComparison.Ordinal)) details.Add($"官方 DOM 错号未被识别为 mismatch：{identity}");
        if (result.State != "mismatch") details.Add($"错号结果应拒绝保存，实际 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("错号仍然生成了文件。");
        return new Scenario("official-dom-wrong-number", details.Count == 0, details.Count == 0 ? ["官方 DOM 结果单号不符时拒绝保存"] : details);
    }

    private static async Task<Scenario> RunOfficialDomEmptyAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-dom-empty");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000025";
        var url = server.Url($"/page?result={number}&frame=official&mode=empty");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("official-dom-empty-result", false, [problem]);
        await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None);
        await Task.Delay(1500);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (result.State != "error") details.Add($"空结果应拒绝保存，实际 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("空结果仍然生成了文件。");
        return new Scenario("official-dom-empty-result", details.Count == 0, details.Count == 0 ? ["官方 DOM 空结果不保存假成功"] : details);
    }

    private static async Task<Scenario> RunOfficialDomErrorAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-dom-error");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000026";
        var url = server.Url($"/page?result={number}&frame=official&mode=error");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("official-dom-captcha-error", false, [problem]);
        await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None);
        await Task.Delay(1500);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (result.State != "error") details.Add($"验证码无效应拒绝保存，实际 {result.State}：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("验证码无效仍然生成了文件。");
        return new Scenario("official-dom-captcha-error", details.Count == 0, details.Count == 0 ? ["官方“验证码无效/没有符合条件的数据”文案被识别为失败"] : details);
    }

    private static async Task<Scenario> RunOfficialDomResultChangeAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "official-dom-change");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000027";
        var url = server.Url($"/page?result={number}&frame=official&mode=mutate");
        await using var session = new BrowserValidation(number, folder, url, browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, false);
        if (problem is not null) return new Scenario("official-dom-result-change", false, [problem]);
        await session.ClickElementInFrameAsync("#inner", "#queryBtn", CancellationToken.None);
        var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (!identity.StartsWith("ready|", StringComparison.Ordinal)) details.Add($"官方 DOM 结果变化前未就绪：{identity}");
        if (result.State == "saved") details.Add("官方 DOM 捕获中结果变化仍保存了截图。");
        if (result.State == "harness") details.Add($"测试侧未取得生产结论：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("官方 DOM 结果变化仍生成了文件。");
        return new Scenario("official-dom-result-change", details.Count == 0, details.Count == 0 ? ["官方 DOM 捕获中 field-order 变化被撤销保存"] : details);
    }

    private static async Task<Scenario> RunConcurrentSessionsAsync(string outputFolder, string browser, TestServer server)
    {
        var root = Path.Combine(outputFolder, "concurrent");
        var folderA = Path.Combine(root, "a");
        var folderB = Path.Combine(root, "b");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        await using var a = new BrowserValidation("310120260000000017", folderA,
            server.Url("/page?content=900&result=310120260000000017"), browser, headless: true, allowTestTarget: true);
        await using var b = new BrowserValidation("310120260000000018", folderB,
            server.Url("/page?content=900&result=310120260000000018"), browser, headless: true, allowTestTarget: true);
        var results = await Task.WhenAll(
            DriveAsync(a, null, waitForSettled: true),
            DriveAsync(b, null, waitForSettled: true));
        var details = new List<string>();
        foreach (var (session, folder, result) in new[] { (a, folderA, results[0]), (b, folderB, results[1]) })
        {
            if (result.State != "saved") { details.Add($"{session.DeclarationNo} 截图失败：{result.Message}"); continue; }
            var files = Directory.EnumerateFiles(folder, "*.png").ToList();
            if (files.Count != 1) details.Add($"{session.DeclarationNo} 生成 {files.Count} 个文件。");
            else if (!Path.GetFileName(files[0]).StartsWith(session.DeclarationNo, StringComparison.Ordinal))
                details.Add($"{session.DeclarationNo} 文件名 {Path.GetFileName(files[0])} 串号。");
        }
        if (Directory.EnumerateFiles(folderA, "*.png").Any(f => !Path.GetFileName(f).Contains("017"))) details.Add("会话 A 目录混入其它单号截图。");
        if (Directory.EnumerateFiles(folderB, "*.png").Any(f => !Path.GetFileName(f).Contains("018"))) details.Add("会话 B 目录混入其它单号截图。");
        return new Scenario("concurrent-session-isolation", details.Count == 0, details.Count == 0 ? ["不同会话并发截图彼此隔离"] : details);
    }

    private static async Task<Scenario> RunNavigationReturnAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "navigation");
        Directory.CreateDirectory(folder);
        const string number = "310120260000000019";
        var target = server.Url($"/page?content=900&result={number}");
        await using var session = new BrowserValidation(number, folder, target, browser, headless: true, allowTestTarget: true);
        var details = new List<string>();
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) details.Add(problem);
        await session.NavigateAsync("about:blank", CancellationToken.None);
        await Task.Delay(1200);
        await session.NavigateAsync(target, CancellationToken.None);
        if (!await session.WaitForWidgetAsync(TimeSpan.FromSeconds(30), CancellationToken.None)) details.Add("返回目标页后未重新注入控件。");
        else
        {
            var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            if (identity.StartsWith("waiting|", StringComparison.Ordinal) || identity.Length == 0)
                details.Add($"返回目标页后结果未就绪：{identity}");
            else
            {
                var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
                if (result.State != "saved") details.Add($"导航返回后截图失败：{result.State}：{result.Message}");
                else if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("导航返回后未生成截图。");
            }
        }
        return new Scenario("navigate-away-return", details.Count == 0, details.Count == 0 ? ["离开目标页再返回后重新注入并可截图"] : details);
    }

    private static async Task<Scenario> RunResultChangeAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "result-change");
        Directory.CreateDirectory(folder);
        // The page rewrites the result shortly after the real prepare path runs, inside
        // the capture window; the save-time re-validation must abort it.
        await using var session = new BrowserValidation("310120260000000020", folder,
            server.Url("/page?content=20000&result=310120260000000020&mutate=1"), browser, headless: true, allowTestTarget: true);
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) return new Scenario("result-change-aborts-save", false, [problem]);
        var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
        var details = new List<string>();
        if (result.State == "saved") details.Add("捕获中查询结果变化仍保存了截图。");
        if (result.State == "harness") details.Add($"测试侧未取得生产结论：{result.Message}");
        if (Directory.EnumerateFiles(folder, "*.png").Any()) details.Add("捕获中结果变化仍生成了文件。");
        return new Scenario("result-change-aborts-save", details.Count == 0, details.Count == 0 ? ["捕获中结果变化被撤销且无错误回填"] : details);
    }

    private static async Task<Scenario> RunReconnectAsync(string outputFolder, string browser, TestServer server)
    {
        var folder = Path.Combine(outputFolder, "reconnect");
        Directory.CreateDirectory(folder);
        await using var session = new BrowserValidation("310120260000000021", folder,
            server.Url("/page?content=900&result=310120260000000021"), browser, headless: true, allowTestTarget: true);
        var details = new List<string>();
        var problem = await StartAndWaitAsync(session, true);
        if (problem is not null) details.Add(problem);
        session.SimulateConnectionDropForTest();
        await Task.Delay(500);
        if (!await session.WaitForWidgetAsync(TimeSpan.FromSeconds(45), CancellationToken.None)) details.Add("断连后未恢复控件。");
        else
        {
            var identity = await session.WaitForIdentitySettledAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            if (identity.StartsWith("waiting|", StringComparison.Ordinal) || identity.Length == 0)
                details.Add($"断连恢复后结果未就绪：{identity}");
            else
            {
                var result = await ClickAndAwaitAsync(session, TimeSpan.FromSeconds(120));
                if (result.State != "saved") details.Add($"断连恢复后截图失败：{result.State}：{result.Message}");
                else if (result.FilePath is null || !File.Exists(result.FilePath)) details.Add("断连恢复后未生成截图。");
            }
        }
        return new Scenario("reconnect-recovery", details.Count == 0, details.Count == 0 ? ["连接中断后恢复并可完成截图"] : details);
    }

    private static void AssertPixel(Bitmap image, int x, int y, Color expected, string label, List<string> details)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) { details.Add($"{label} 采样越界。"); return; }
        var actual = image.GetPixel(x, y);
        if (Math.Abs(actual.R - expected.R) > 6 || Math.Abs(actual.G - expected.G) > 6 || Math.Abs(actual.B - expected.B) > 6)
            details.Add($"{label} 颜色不符：{actual}。");
    }

    private static bool ColumnContainsColor(Bitmap image, Color expected)
    {
        var x = image.Width / 2;
        for (var y = 0; y < image.Height; y += 3)
        {
            var pixel = image.GetPixel(x, y);
            if (Math.Abs(pixel.R - expected.R) <= 6 && Math.Abs(pixel.G - expected.G) <= 6 && Math.Abs(pixel.B - expected.B) <= 6) return true;
        }
        return false;
    }

    private static bool AnywhereContainsColor(Bitmap image, Color expected)
    {
        for (var y = 0; y < image.Height; y += 3)
            for (var x = 0; x < image.Width; x += 3)
            {
                var pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - expected.R) <= 6 && Math.Abs(pixel.G - expected.G) <= 6 && Math.Abs(pixel.B - expected.B) <= 6) return true;
            }
        return false;
    }

    private static bool RegionContainsColor(Bitmap image, Rectangle region, Color expected)
    {
        var left = Math.Max(0, region.Left);
        var top = Math.Max(0, region.Top);
        var right = Math.Min(image.Width, region.Right);
        var bottom = Math.Min(image.Height, region.Bottom);
        for (var y = top; y < bottom; y += 2)
            for (var x = left; x < right; x += 2)
            {
                var pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - expected.R) <= 6 && Math.Abs(pixel.G - expected.G) <= 6 && Math.Abs(pixel.B - expected.B) <= 6) return true;
            }
        return false;
    }

    private static void WriteReport(string outputFolder, IReadOnlyList<Scenario> scenarios, IReadOnlyList<string> issues)
    {
        var report = new
        {
            pass = issues.Count == 0,
            scenarios = scenarios.Select(x => new { x.Name, x.Pass, x.Details }),
            issues
        };
        File.WriteAllText(Path.Combine(outputFolder, "browser-e2e.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    /// <summary>Minimal loopback HTTP server (no URL ACL required).</summary>
    private sealed class TestServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = new();
        private int _port;

        public void Start()
        {
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public string Url(string path) => $"http://127.0.0.1:{_port}{path}";

        public int Port => _port;

        private async Task AcceptLoopAsync()
        {
            while (!_lifetime.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_lifetime.Token); }
                catch (Exception) { return; }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private static async Task ServeAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[8192];
                    var read = await stream.ReadAsync(buffer);
                    var request = Encoding.ASCII.GetString(buffer, 0, Math.Max(0, read));
                    var firstLine = request.Split("\r\n").FirstOrDefault() ?? "GET / HTTP/1.1";
                    var parts = firstLine.Split(' ');
                    var path = parts.Length >= 2 ? parts[1] : "/";
                    var html = Page(path);
                    var body = Encoding.UTF8.GetBytes(html);
                    var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                    await stream.WriteAsync(body);
                }
            }
            catch (Exception ex) { AppLog.Write($"E2E 测试页响应失败：{ex.Message}"); }
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            try { _listener.Stop(); } catch { }
            _lifetime.Dispose();
        }

        private static Dictionary<string, string> Parameters(string path)
        {
            var query = path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : "";
            return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2))
                .Where(x => x.Length == 2)
                .ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]), StringComparer.OrdinalIgnoreCase);
        }

        private static string FormHtml(string decl, string result, bool autoQuery, string error, bool pre, bool nonumber, string extraScript)
        {
            var rowText = nonumber
                ? "查询结果已返回 海关状态 已放行 申报日期 2026-09-24 放行日期 2026-09-24"
                : $"报关单号 {result} 申报日期 2026-09-24 放行日期 2026-09-24 海关状态 已放行";
            var resultRow = error.Length > 0 ? "" : $"<tr><td>{rowText}</td></tr>";
            var initial = pre ? resultRow : "";
            // The auto-query clicks only after the injected monitor exists (bounded wait). A
            // real user clicks seconds after injection, but the old fixed 400ms timer could
            // fire before the monitor reached a late-attached OOPIF frame, so the click was
            // never observed and identity stayed "waiting|not-started". The click is still a
            // real click on the real button and the monitor must still observe it, so this
            // removes only the artificial race, not the verification.
            var resultRowJson = JsonSerializer.Serialize(resultRow);
            var errorScript = autoQuery
                ? "(function(){var tries=0;function fire(){var m=window.__customsConsoleMonitor;if(!(m&&m.document===document)&&tries++<800){setTimeout(fire,25);return;}" +
                  "var q=document.getElementById('query');if(q)q.click();" +
                  "var r=document.getElementById('result');if(r)r.innerHTML=" + resultRowJson + ";}" +
                  "fire();})();"
                : "";
            var errorTextScript = error.Length > 0 ? $"document.getElementById('error').textContent = {JsonSerializer.Serialize(error)};" : "";
            return $$"""
              <form onsubmit="return false"><label>报关单号 <input data-customs-declaration value="{{decl}}"></label>
              <button type="button" id="query">查询</button></form>
              <div id="error"></div><div id="loading" class="loading" hidden>加载中</div>
              <div class="scroller"><div class="inner"><div id="scrollband"></div></div></div>
              <table><tbody id="result">{{initial}}</tbody></table>
              <script>
                document.querySelector('input').value = {{JsonSerializer.Serialize(decl)}};
                {{errorScript}}
                {{errorTextScript}}
                {{extraScript}}
              </script>
            """;
        }

        private static string Page(string path)
        {
            if (path.StartsWith("/blank", StringComparison.OrdinalIgnoreCase))
                return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>blank</title></head><body>blank</body></html>";
            var parameters = Parameters(path);
            var content = parameters.TryGetValue("content", out var c) && int.TryParse(c, out var height) ? height : 1200;
            var result = parameters.TryGetValue("result", out var r) ? r : "310120260000000001";
            var decl = parameters.TryGetValue("decl", out var d) ? d : result;
            var autoQuery = !parameters.TryGetValue("query", out var q) || q != "0";
            var error = parameters.TryGetValue("error", out var e) ? e : "";
            var mid = parameters.TryGetValue("mid", out var m) && int.TryParse(m, out var midY) ? midY : 0;
            var frame = parameters.TryGetValue("frame", out var f) ? f : "";
            var xhost = parameters.TryGetValue("xhost", out var xh) ? xh : "";
            var pre = parameters.TryGetValue("pre", out var pv) && pv == "1";
            var nonumber = parameters.TryGetValue("nonumber", out var nv) && nv == "1";
            var mutate = parameters.TryGetValue("mutate", out var mv) && mv == "1";
            // A clamping iframe reproduces the R5-3 case: the element is capped by
            // max-height, so writing only a height would leave content truncated.
            var clamp = parameters.TryGetValue("clamp", out var cv) && cv == "1";
            // frameh lets a fixture start with an owner iframe already tall enough for its
            // document, so the expand path has nothing to grow while the frame still extends
            // past the physical viewport (the pre-expanded OOPIF regression).
            // lazy=<ms>: the page script creates the iframe after this delay, like the official
            // single-page app that renders its query frame after load. The frame's document is
            // then created after the session registered its per-document script.
            var lazyFrame = parameters.TryGetValue("lazy", out var lz) && int.TryParse(lz, out var lzParsed) && lzParsed > 0 ? lzParsed : 0;
            var frameHeight = parameters.TryGetValue("frameh", out var fh) && int.TryParse(fh, out var fhParsed) && fhParsed > 0 ? fhParsed : 400;
            // R5-2 failure injection: the first scroll container expands normally, then the
            // second one throws once on its first style write, exercising the production
            // prepare failure + finally restore + retry path.
            var prepfail = parameters.TryGetValue("prepfail", out var pf) && pf == "1";
            // Parsed before the /official fixture branch, which substitutes it into the
            // official-DOM page script; keeping the declaration later made the file
            // uncompilable (CS0841) and blocked every downstream verification step.
            var mode = parameters.TryGetValue("mode", out var mo) ? mo : "ok";
            var mutateScript = mutate
                ? "setInterval(function () { if (window.__cccPrepareAt && !window.__cccMutated) { window.__cccMutated = true; setTimeout(function () { var t = document.getElementById('result'); if (t) t.innerHTML = '<tr><td>报关单号 310120260000009999 申报日期 2026-09-24 放行日期 2026-09-24 海关状态 已放行</td></tr>'; }, 100); } }, 50);"
                : "";

            if (path.StartsWith("/frame", StringComparison.OrdinalIgnoreCase))
            {
                return $$"""
                <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>frame</title>
                <style>html,body{margin:0;padding:0;background:#fff}#ftop{height:80px;background:#0088CC}
                .scroller{height:300px;overflow-y:auto;border:1px solid #ccc}
                .inner{position:relative;height:1200px;background:linear-gradient(#eef3f8,#dde6f0)}table{width:100%;border-collapse:collapse}
                td{padding:6px;font:13px system-ui;color:#0f1b2d}#fband{height:120px;background:#7700AA}
                #scrollband{position:absolute;left:0;right:0;top:1080px;height:120px;background:#CC00CC}</style></head><body>
                <div id="ftop"></div>
                {{FormHtml(decl, result, autoQuery, error, pre, nonumber, "")}}
                <div id="fband"></div>
                </body></html>
                """;
            }

            // A fixture that mirrors the official query frame DOM: no-placeholder #entryId
            // next to a 报关单号 label, #queryBtn, and a #queryDetail result rendered as
            // .display-content > .content-field > .field-order/.field-time/.field-text
            // (see planning/singlewindow-statistics.js). The top-level shell has no query.
            if (path.StartsWith("/official", StringComparison.OrdinalIgnoreCase))
            {
                return """
                <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>官方查询</title>
                <style>
                html,body{margin:0;padding:0;background:#fff;font:13px system-ui}
                #officialTop{height:80px;background:#0088CC}
                #officialBottom{height:120px;background:#7700AA}
                #queryForm{margin:8px;padding:8px;border:1px solid #eee}
                .content-area{min-height:240px}
                .content-field{border:1px solid #ddd;margin:6px;padding:6px;width:140px;word-break:break-all}
                .field-title{font-weight:600}
                .field-order{font-family:monospace}
                </style></head><body>
                <div id="officialTop"></div>
                <form id="queryForm">
                  <label for="entryId">报关单号</label>
                  <input type="text" id="entryId" name="entryId" maxlength="18">
                  <input type="text" id="randomcode" size="8">
                  <button type="button" id="queryBtn">查询</button>
                </form>
                <div class="content-area" id="queryDetail"></div>
                <div id="officialError"></div>
                <div id="officialBottom"></div>
                <script>
                  var no = "__DECL__";
                  var mode = "__MODE__";
                  document.getElementById('entryId').value = no;
                  function officialResult(number) {
                    var titles = ['放行','查验','报关','转关','转关运抵','舱单','理货','装运','到港','离港','卸货','申报'];
                    var html = '';
                    for (var i = 0; i < titles.length; i++) {
                      html += '<div id="node' + i + '" class="dec-cus"><div class="content-title"><div><span class="content-title-text">' + titles[i] + '</span></div></div>' +
                        '<div class="display-content">' +
                        '<div class="content-field"><div class="field-title">' + titles[i] + '</div><div class="field-order">' + number + '</div>' +
                        '<div class="field-time">2026-09-24 10:00</div><div class="field-text">已' + titles[i] + '</div></div>' +
                        '</div></div>';
                    }
                    return html;
                  }
                  document.getElementById('queryBtn').addEventListener('click', function () {
                    var detail = document.getElementById('queryDetail');
                    if (mode === 'empty') { detail.innerHTML = ''; return; }
                    if (mode === 'error') { document.getElementById('officialError').textContent = '温馨提示：验证码无效，请点击刷新验证码！'; return; }
                    var number = mode === 'wrong' ? '310120260000009999' : no;
                    detail.innerHTML = officialResult(number);
                  });
                  if (mode === 'mutate') {
                    setInterval(function () { if (window.__cccPrepareAt && !window.__cccMutated) { window.__cccMutated = true; setTimeout(function () { document.querySelectorAll('#queryDetail .field-order').forEach(function (e) { e.textContent = '310120260000009999'; }); }, 100); } }, 50);
                  }
                </script></body></html>
                """.Replace("__DECL__", decl).Replace("__MODE__", mode);
            }

            var midDiv = mid > 0 ? $"<div id=\"mid\" style=\"position:absolute;left:0;top:{mid}px;width:100%;height:200px;background:#00AA55\"></div>" : "";
            var isFrame = frame is "1" or "cross" or "official";
            var frameSrc = frame == "cross" && xhost.Length > 0
                ? xhost
                : frame == "official"
                    ? (parameters.TryGetValue("ohost", out var ohost) && ohost.Length > 0 ? ohost : "") + $"/official?result={result}&mode={mode}&decl={decl}"
                    : $"/frame?content=800&result={result}&query={(autoQuery ? "1" : "0")}";
            // A cross-origin frame scenario puts the query form and the matching result ONLY
            // inside the iframe: the top-level shell has neither query nor result, exactly
            // like the official page. Only the frame's own tall auxiliary content is used.
            var body = frame == "cross"
                ? $"""
                  <div id="top"></div>
                  <iframe id="inner" src="{frameSrc}" style="width:100%;height:{frameHeight}px;{(clamp ? "max-height:400px !important;" : "")}border:0"></iframe>
                  <div id="bottom"></div>
                  """
                : isFrame && lazyFrame > 0
                    ? $$"""
                      <div id="top"></div>
                      <div id="frame-slot"></div>
                      <div id="bottom"></div>
                      <script>
                        setTimeout(function () {
                          var frame = document.createElement('iframe');
                          frame.id = 'inner';
                          frame.src = {{JsonSerializer.Serialize(frameSrc)}};
                          frame.style.cssText = 'width:100%;height:{{frameHeight}}px;border:0';
                          document.getElementById('frame-slot').appendChild(frame);
                        }, {{lazyFrame}});
                      </script>
                      """
                : isFrame
                    ? $"""
                      <div id="top"></div>
                      <iframe id="inner" src="{frameSrc}" style="width:100%;height:{frameHeight}px;border:0"></iframe>
                      <div id="bottom"></div>
                      """
                    : $"""
                      <div id="top"></div>
                      <div id="content">{(prepfail ? "<div id=\"bad-scroller\" class=\"badscroller\"><div class=\"badinner\"></div></div>" : "")}{FormHtml(decl, result, autoQuery, error, pre, nonumber, mutateScript)}</div>
                      <div id="bottom"></div>
                      """;
            var contentRule = isFrame ? "height:auto;" : $"height:{content}px;";
            // One-shot injection: the bad container throws on its very first style write and
            // immediately removes the override, so the retry after the restored failure can
            // run the normal prepare path and save.
            var spaScript = parameters.TryGetValue("spa", out var spaValue) && spaValue == "1"
                ? "<script>setTimeout(function () { location.hash = '#/publicInquiryDetail?id=pi4'; }, 800);</script>"
                : "";
            var prepfailScript = prepfail ? """
            <script>
            (function () {
              var bad = document.getElementById('bad-scroller');
              if (!bad) return;
              Object.defineProperty(bad.style, 'setProperty', {
                configurable: true,
                value: function () {
                  delete bad.style.setProperty;
                  throw new Error('injected-prepare-failure');
                }
              });
              window.__cccPrepareFailArmed = bad.style.hasOwnProperty('setProperty');
            })();
            </script>
            """ : "";
            return $$"""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>受控核验页</title>
            <style>
              html,body{margin:0;padding:0;background:#ffffff;position:relative}
              #top{height:200px;background:#654321}
              #content{ {{contentRule}}box-sizing:border-box;padding:16px}
              .scroller{height:400px;overflow-y:auto;border:1px solid #cccccc;margin-top:12px}
              .scroller .inner{position:relative;height:1800px;background:linear-gradient(#eef3f8,#dde6f0)}
              .badscroller{height:400px;overflow-y:auto;border:1px solid #999999;margin-bottom:12px}
              .badscroller .badinner{position:relative;height:1800px;background:linear-gradient(#f8eee0,#f0dde0)}
              #scrollband{position:absolute;left:0;right:0;top:1680px;height:120px;background:#CC00CC}
              #bottom{height:200px;background:#123456}
              table{width:100%;border-collapse:collapse}td{padding:8px;font:14px system-ui;color:#0f1b2d}
              iframe{display:block}
            </style></head><body>
            {{midDiv}}
            {{body}}
            {{prepfailScript}}
            {{spaScript}}
            </body></html>
            """;
        }
    }
}
