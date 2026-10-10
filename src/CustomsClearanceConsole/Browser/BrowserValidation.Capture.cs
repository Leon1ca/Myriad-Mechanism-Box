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

    private async Task CaptureAndReportAsync()
    {
        BrowserCaptureResult result;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(CaptureDeadline);
            _captureClock.Restart();
            CaptureStep("开始");
            try
            {
                result = await CaptureWithRetryAsync(deadline.Token);
            }
            catch (Exception ex) when (IsDeadline(ex, deadline))
            {
                // A browser that stops answering (a renderer busy with page scripts, a window
                // that is not painted) must end the capture with a reason, never leave the
                // card on "截取中"; the step names where it stalled for the log.
                AppLog.Write($"截图超时：{CaptureDeadline.TotalSeconds:0} 秒内未完成，停在“{_captureStep}”。");
                result = new BrowserCaptureResult(SessionId, DeclarationNo, "error", null,
                    $"截图超时，未保存（浏览器 {CaptureDeadline.TotalSeconds:0} 秒内无响应，停在：{_captureStep}）。请保持浏览器窗口可见后重试。");
            }
            catch (OperationCanceledException)
            {
                result = new BrowserCaptureResult(SessionId, DeclarationNo, "cancelled", null, "截图已取消。");
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
                result = new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, $"截图未保存：{ex.Message}");
            }
        }
        finally
        {
            // The capture and every page restore are finished, so this request no longer owns
            // the capture resource. Release the single-owner gate here, before the retryable
            // page state and the completion event are published, so a fast retry whose real
            // click lands right after the button is re-enabled can never be delivered into a
            // still-held gate and swallowed. This is the only release: the busy path never
            // touches the gate, and the finally keeps the gate from being held forever on a
            // failure, cancellation or reporting exception.
            _captureStep = "空闲";
            Interlocked.Exchange(ref _captureGate, 0);
            AppLog.Write("网页截图锁已释放。");
        }
        AppLog.Write($"网页截图结论：{result.State}。");
        await ReportAsync(result);
        // Publish the retryable page state only after the card is interactively painted again.
        // The capture hid the card and may have restored an enlarged viewport; a completion
        // published before the card is painted could let a fast retry's real click be lost
        // (a compositor/paint risk to verify, not a proven cause). The wait is bounded and
        // best-effort: its outcome is recorded as a separate recovery signal and must never
        // change the saved-file verdict or suppress the completion event.
        LastCompletionReadiness = "";
        try
        {
            using var ready = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            ready.CancelAfter(TimeSpan.FromSeconds(3));
            LastCompletionReadiness = await WaitForWidgetInteractiveAsync(TimeSpan.FromSeconds(2), _captureBaselineViewport, ready.Token);
            if (!TryParseReadiness(LastCompletionReadiness, out var completionReadiness)
                || !completionReadiness.Ready || !completionReadiness.ExactButtonHit
                || !completionReadiness.Enabled || !completionReadiness.Visible)
                AppLog.Write($"截图完成发布前卡片未恢复可交互（独立于保存结论，不改变 {result.State}）：{Shorten(LastCompletionReadiness)}");
        }
        catch (Exception ex)
        {
            LastCompletionReadiness = "就绪探测失败:" + ex.Message;
            AppLog.Write($"等待卡片恢复可交互失败（独立于保存结论，不改变 {result.State}）：{ex.Message}");
        }
        CaptureCompleted?.Invoke(this, result);
    }

    /// <summary>Upper bound for one capture request, restore excluded.</summary>
    public TimeSpan CaptureDeadline { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>E2E-only: holds the capture after the page was prepared, like a browser that stops answering.</summary>
    public TimeSpan StallCaptureForTest { get; set; }

    private readonly Stopwatch _captureClock = new();
    private volatile string _captureStep = "空闲";

    private void CaptureStep(string step)
    {
        _captureStep = step;
        AppLog.Write($"截图步骤：{step}（{_captureClock.ElapsedMilliseconds} ms）");
    }

    private bool IsDeadline(Exception ex, CancellationTokenSource deadline) =>
        deadline.IsCancellationRequested && !_lifetime.IsCancellationRequested
        && (ex is OperationCanceledException || ex is TimeoutException || ex.InnerException is OperationCanceledException);

    private async Task<BrowserCaptureResult> CaptureWithRetryAsync(CancellationToken token)
    {
        try
        {
            return await CaptureCoreAsync(token);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            await ReconnectAsync(token);
            return await CaptureCoreAsync(token);
        }
    }

    private async Task<BrowserCaptureResult> CaptureCoreAsync(CancellationToken token)
    {
        var cdp = _cdp ?? throw new InvalidOperationException("浏览器控制连接已断开，请重新打开核验。");
        if (!IsTargetUrl(_currentUrl))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "当前页面不是核验目标页，未保存截图。");

        var generation = Volatile.Read(ref _navigationGeneration);
        // The readiness published with this capture's completion must target the viewport
        // observed before this capture enlarged it; a stale value from a previous capture
        // could make the completion wait for the wrong size.
        _captureBaselineViewport = null;

        CaptureStep("确认查询结果");
        var identity = await ProbeIdentityAsync(token);
        if (identity.StartsWith("mismatch|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "mismatch", null, "网页结果单号与本次核验不一致，未保存截图。");
        if (identity.StartsWith("error|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "网页提示验证码或查询条件有误，未保存截图。");
        if (identity.StartsWith("loading|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "网页仍在加载查询结果，未保存截图。");
        if (identity.StartsWith("stale|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "查询结果尚未变化，未保存截图。请重新查询后再试。");
        if (!identity.StartsWith("ready|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "查询结果尚未确认，未保存截图。请先在网页完成查询。");

        // A layout-independent fingerprint of the result region: it is re-checked before
        // the atomic save so a query change, a result re-render or a navigation during
        // the capture aborts the save instead of backfilling a stale image.
        CaptureStep("校验结果稳定");
        var stableBefore = await VerifyStableAsync(token);
        if (stableBefore.StartsWith("mismatch|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "mismatch", null, "网页结果单号与本次核验不一致，未保存截图。");
        if (stableBefore.StartsWith("error|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "网页提示验证码或查询条件有误，未保存截图。");
        if (!stableBefore.StartsWith("stable|", StringComparison.Ordinal))
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "查询结果尚未确认，未保存截图。请先在网页完成查询。");
        await Task.Delay(350, token);
        var stableSecond = await VerifyStableAsync(token);
        if (stableSecond != stableBefore)
            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "查询结果仍在变化，未保存截图。请稍后重试。");

        Directory.CreateDirectory(_targetFolder);
        var preparedTargets = new List<EvalTarget>();
        var expandedFrames = new List<string>();
        var capturedOopif = new List<OopifPaintRange>();
        try
        {
            try
            {
                CaptureStep("准备页面");
                await PrepareContextsAsync(BrowserScript("capture-prepare"), preparedTargets, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, $"网页准备失败，未保存截图：{ex.Message}");
            }
            await Task.Delay(300, token);
            if (StallCaptureForTest > TimeSpan.Zero) await Task.Delay(StallCaptureForTest, token);

            // Cross-origin frames cannot be resized from page JS. The CDP frame owner is
            // grown to the frame document height so the complete frame content is painted;
            // a frame that cannot be expanded rejects the capture instead of truncating it.
            CaptureStep("展开网页框架");
            var failedFrames = await ExpandFramesAsync(expandedFrames, capturedOopif, token);
            if (failedFrames.Count > 0)
                return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null,
                    $"存在无法完整捕获的网页框架（{string.Join("、", failedFrames)}），未保存截图。");

            CaptureStep("读取页面尺寸");
            var metrics = await cdp.SendAsync("Page.getLayoutMetrics", null, token);
            var content = metrics.GetProperty("result").GetProperty("contentSize");
            var width = (int)Math.Ceiling(content.GetProperty("width").GetDouble());
            var height = (int)Math.Ceiling(content.GetProperty("height").GetDouble());
            if (width <= 0 || height <= 0)
                return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "无法读取页面尺寸，未保存截图。");
            // Explicitly reject oversized pages; never shrink the clip to fake success.
            if ((long)width * height > _maxPixels || width > 16_384 || height > 65_535)
                return new BrowserCaptureResult(SessionId, DeclarationNo, "too-long", null, "页面超过 6000 万像素，未保存截图；请使用浏览器分段保存。");

            // captureBeyondViewport does not reliably re-render an out-of-process iframe whose
            // painted box extends past the real viewport: the region below the viewport is
            // captured blank. Every authorized attached child-session frame that participates in
            // the capture is recorded above with its painted extent, whether or not this run had
            // to grow its owner; the real viewport is enlarged whenever the clip or any such
            // frame exceeds the current viewport. Reading the real viewport is therefore a
            // required prerequisite here, not an optional optimization: when a child-session
            // frame participates and the viewport cannot be read reliably, the capture is
            // rejected instead of falling back to the known-blank path. The override, including
            // the original device pixel ratio, is cleared in the finally below.
            var viewport = capturedOopif.Count > 0 ? await TryReadViewportAsync(token) : null;
            if (capturedOopif.Count > 0 && viewport is null)
                return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null,
                    "无法读取浏览器实际视口尺寸，为避免保存空白跨进程框架截图，已拒绝本次截图。");
            _captureBaselineViewport = viewport;
            var viewportWidth = 0;
            var viewportHeight = 0;
            var viewportScale = 1.0;
            if (viewport is { } readViewport)
            {
                viewportWidth = readViewport.Width;
                viewportHeight = readViewport.Height;
                viewportScale = readViewport.Scale;
            }
            var enlargedViewport = viewport is not null
                && (width > viewportWidth || height > viewportHeight
                    || capturedOopif.Any(frame => frame.Bottom > viewportHeight || frame.Right > viewportWidth));
            var paintWidth = enlargedViewport ? Math.Max(viewportWidth, width) : viewportWidth;
            var paintHeight = enlargedViewport ? Math.Max(viewportHeight, height) : viewportHeight;
            try
            {
                if (enlargedViewport)
                {
                    CaptureStep("放大视口");
                    await cdp.SendAsync("Emulation.setDeviceMetricsOverride", new
                    {
                        width = paintWidth,
                        height = paintHeight,
                        deviceScaleFactor = viewportScale,
                        mobile = false
                    }, token);
                    // Never capture on a fixed delay: under load the out-of-process frame has not
                    // yet learned that it is fully visible and its lower part is saved blank. Wait
                    // (bounded) until the page reports the enlarged viewport and every captured
                    // child frame reports itself completely inside it, then for fresh frames.
                    var paintProblem = await WaitForEnlargedPaintAsync(paintWidth, paintHeight, capturedOopif, token);
                    if (paintProblem is not null)
                        return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null,
                            $"跨进程网页框架未能在放大视口后完成重绘（{paintProblem}），为避免保存空白截图，已拒绝本次截图。");
                    // The enlarged viewport can change the content size; re-read and never
                    // shrink below the frame content that was already grown.
                    var expandedMetrics = await cdp.SendAsync("Page.getLayoutMetrics", null, token);
                    var expandedContent = expandedMetrics.GetProperty("result").GetProperty("contentSize");
                    width = Math.Max(width, (int)Math.Ceiling(expandedContent.GetProperty("width").GetDouble()));
                    height = Math.Max(height, (int)Math.Ceiling(expandedContent.GetProperty("height").GetDouble()));
                    if ((long)width * height > _maxPixels || width > 16_384 || height > 65_535)
                        return new BrowserCaptureResult(SessionId, DeclarationNo, "too-long", null, "页面超过 6000 万像素，未保存截图；请使用浏览器分段保存。");
                }

                var tiles = (int)Math.Ceiling(height / (double)TileHeight);
                using var full = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                for (var attempt = 1; ; attempt++)
                {
                    CaptureStep($"截取 {tiles} 段（{width}×{height}）");
                    await ReportProgressAsync(0, tiles, "正在截取整页");
                    using (var graphics = Graphics.FromImage(full))
                    {
                        graphics.Clear(Color.White);
                        for (var index = 0; index < tiles; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            if (Volatile.Read(ref _navigationGeneration) != generation || !IsTargetUrl(_currentUrl))
                                return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "捕获期间页面已导航，未保存截图。");
                            var y = index * TileHeight;
                            var tileHeight = Math.Min(TileHeight, height - y);
                            using var tile = await CaptureClipAsync(cdp, 0, y, width, tileHeight, token);
                            graphics.DrawImageUnscaled(tile, 0, y);
                            await ReportProgressAsync(index + 1, tiles, null);
                        }
                    }
                    // Even after the frame reported itself visible, its tiles can still be rastered
                    // after the capture drew the frame (seen 1/80 under load): the saved band is blank
                    // while a later look shows content. Re-check each out-of-process frame's bottom
                    // band against a fresh clip and capture again instead of saving a blank frame.
                    if (capturedOopif.Count == 0) break;
                    var paint = await CheckOopifPaintAsync(cdp, full, capturedOopif, token);
                    if (paint == OopifPaintCheck.Consistent) break;
                    AppLog.Write($"跨进程框架绘制复核第 {attempt} 次：{paint}。");
                    if (attempt >= 3)
                    {
                        if (paint == OopifPaintCheck.Unpainted)
                            return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null,
                                "跨进程网页框架多次截取仍未完成绘制，为避免保存空白截图，已拒绝本次截图。");
                        // The frame is painted; only its content keeps changing (for example an
                        // animation), which the result fingerprint check below still guards.
                        break;
                    }
                    var repaintProblem = await WaitForEnlargedPaintAsync(paintWidth, paintHeight, capturedOopif, token);
                    if (repaintProblem is not null)
                        return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null,
                            $"跨进程网页框架未能完成重绘（{repaintProblem}），为避免保存空白截图，已拒绝本次截图。");
                }
                // Final identity/generation re-validation before the atomic move.
                if (Volatile.Read(ref _navigationGeneration) != generation || !IsTargetUrl(_currentUrl))
                    return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "捕获期间页面已导航，未保存截图。");
                CaptureStep("复核并保存");
                var stableAfter = await VerifyStableAsync(token);
                if (!stableAfter.StartsWith("stable|", StringComparison.Ordinal) || stableAfter != stableBefore)
                    return new BrowserCaptureResult(SessionId, DeclarationNo, "error", null, "捕获期间查询结果发生变化，未保存截图。");
                var file = SaveAtomically(full);
                return new BrowserCaptureResult(SessionId, DeclarationNo, "saved", file, "长截图已保存。");
            }
            finally
            {
                if (enlargedViewport)
                {
                    try { await cdp.SendAsync("Emulation.clearDeviceMetricsOverride", null, CancellationToken.None); }
                    catch (Exception ex) { AppLog.Write($"恢复视口尺寸失败：{ex.Message}"); }
                }
            }
        }
        finally
        {
            // Restoration must run even when the capture token was cancelled. Frame heights
            // are restored through CDP first (cross-origin included), then the page-level
            // script puts back scroll offsets, overflow and widget visibility.
            using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await RestoreFramesAsync(expandedFrames, restore.Token);
            await RestoreContextsAsync(BrowserScript("capture-restore"), preparedTargets, restore.Token);
        }
    }

    /// <summary>
    /// Reads the real CSS viewport and device pixel ratio the capture must enlarge to paint an
    /// out-of-process frame. Transient CDP/script failures and non-positive readings are retried
    /// because an unreadable viewport must never be mistaken for "no enlargement needed" and
    /// fall back to the blank capture path; the caller rejects when this still returns null.
    /// </summary>
    private async Task<(int Width, int Height, double Scale)?> TryReadViewportAsync(CancellationToken token)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (Volatile.Read(ref _viewportReadFailures) > 0)
                {
                    Interlocked.Decrement(ref _viewportReadFailures);
                    AppLog.Write($"注入视口读取失败（第 {attempt}/3 次）。");
                }
                else
                {
                    using var document = JsonDocument.Parse(await EvaluateContextAsync(
                        "JSON.stringify({w:window.innerWidth||0,h:window.innerHeight||0,dpr:window.devicePixelRatio||1})",
                        null, token, awaitPromise: false));
                    var width = document.RootElement.GetProperty("w").GetInt32();
                    var height = document.RootElement.GetProperty("h").GetInt32();
                    var scale = document.RootElement.TryGetProperty("dpr", out var dpr) && dpr.GetDouble() > 0 ? dpr.GetDouble() : 1;
                    if (width > 0 && height > 0) return (width, height, scale);
                    AppLog.Write($"读取视口尺寸无效（第 {attempt}/3 次）：w={width},h={height}。");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write($"读取视口尺寸失败（第 {attempt}/3 次）：{ex.Message}");
            }
            if (attempt < 3) await Task.Delay(150, token);
        }
        return null;
    }

    private async Task<Bitmap> CaptureClipAsync(CdpClient cdp, int x, int y, int width, int height, CancellationToken token)
    {
        var response = await cdp.SendAsync("Page.captureScreenshot", new
        {
            format = "png",
            fromSurface = true,
            captureBeyondViewport = true,
            clip = new { x, y, width, height, scale = 1 }
        }, token);
        var data = Convert.FromBase64String(response.GetProperty("result").GetProperty("data").GetString()!);
        using var stream = new MemoryStream(data);
        // Bitmap(Stream) keeps reading the stream lazily; copy so the result owns its pixels.
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    private enum OopifPaintCheck { Consistent, Changed, Unpainted }

    /// <summary>
    /// Compares the bottom band of every captured out-of-process frame in the stitched image
    /// with a fresh clip of the same page area. Unpainted: the stitched band is a single flat
    /// colour while the fresh clip is not (the frame was drawn before its tiles existed).
    /// Changed: the bands differ otherwise (live content). Consistent: they match.
    /// </summary>
    private async Task<OopifPaintCheck> CheckOopifPaintAsync(CdpClient cdp, Bitmap full, IReadOnlyList<OopifPaintRange> frames, CancellationToken token)
    {
        var result = OopifPaintCheck.Consistent;
        foreach (var frame in frames)
        {
            var left = Math.Clamp(frame.Left, 0, full.Width - 1);
            var right = Math.Clamp(frame.Right, left + 1, full.Width);
            var bottom = Math.Clamp(frame.Bottom, 1, full.Height);
            var top = Math.Max(0, bottom - 360);
            if (right - left < 4 || bottom - top < 4) continue;
            using var fresh = await CaptureClipAsync(cdp, left, top, right - left, bottom - top, token);
            var width = Math.Min(fresh.Width, right - left);
            var height = Math.Min(fresh.Height, bottom - top);
            var samples = 0;
            var differences = 0;
            var stitchedFlat = true;
            var freshFlat = true;
            var stitchedFirst = full.GetPixel(left, top);
            var freshFirst = fresh.GetPixel(0, 0);
            for (var y = 0; y < height; y += 3)
                for (var x = 0; x < width; x += 3)
                {
                    var a = full.GetPixel(left + x, top + y);
                    var b = fresh.GetPixel(x, y);
                    samples++;
                    if (!Near(a, b)) differences++;
                    if (stitchedFlat && !Near(a, stitchedFirst)) stitchedFlat = false;
                    if (freshFlat && !Near(b, freshFirst)) freshFlat = false;
                }
            if (samples == 0 || differences <= samples / 200) continue;
            if (stitchedFlat && !freshFlat) return OopifPaintCheck.Unpainted;
            result = OopifPaintCheck.Changed;
        }
        return result;

        static bool Near(Color a, Color b) =>
            Math.Abs(a.R - b.R) <= 8 && Math.Abs(a.G - b.G) <= 8 && Math.Abs(a.B - b.B) <= 8;
    }

    private const int PaintWaitMilliseconds = 8000;

    /// <summary>
    /// Resolves once the top-level page reports at least the enlarged viewport and has produced
    /// two further frames. Bounded: a missing frame clock resolves with a reason, never hangs.
    /// </summary>
    private static string MainViewportPaintScript(int width, int height) => FormattableString.Invariant($$"""
        new Promise(function (resolve) {
          var deadline = Date.now() + {{PaintWaitMilliseconds}}, done = false;
          function finish(v) { if (!done) { done = true; resolve(v); } }
          setTimeout(function () { finish('no-frames:' + window.innerWidth + 'x' + window.innerHeight); }, {{PaintWaitMilliseconds + 500}});
          function step() {
            if (done) return;
            if (window.innerWidth >= {{width}} - 1 && window.innerHeight >= {{height}} - 1) {
              requestAnimationFrame(function () { requestAnimationFrame(function () { finish('ready'); }); });
              return;
            }
            if (Date.now() > deadline) { finish('viewport:' + window.innerWidth + 'x' + window.innerHeight); return; }
            requestAnimationFrame(step);
          }
          requestAnimationFrame(step);
        })
        """);

    /// <summary>
    /// Runs inside an out-of-process frame: resolves once that frame's renderer reports its whole
    /// visible document inside the top-level viewport (IntersectionObserver uses the embedder's
    /// real viewport intersection, which is what the compositor paints by) and two further frames
    /// were produced. A fresh observer per frame always delivers the current state.
    /// </summary>
    private static readonly string OopifVisiblePaintScript = FormattableString.Invariant($$"""
        new Promise(function (resolve) {
          var deadline = Date.now() + {{PaintWaitMilliseconds}}, done = false, last = 'none';
          function finish(v) { if (!done) { done = true; resolve(v); } }
          setTimeout(function () { finish('no-frames:' + last); }, {{PaintWaitMilliseconds + 500}});
          var el = document.documentElement;
          if (!el || typeof IntersectionObserver !== 'function') { finish('unsupported'); return; }
          function check() {
            if (done) return;
            if (Date.now() > deadline) { finish('hidden:' + last); return; }
            var io = new IntersectionObserver(function (entries) {
              io.disconnect();
              var e = entries[entries.length - 1];
              var needH = Math.min(window.innerHeight, e.boundingClientRect.height) - 2;
              var needW = Math.min(window.innerWidth, e.boundingClientRect.width) - 2;
              last = Math.round(e.intersectionRect.width) + 'x' + Math.round(e.intersectionRect.height) + '/' + Math.round(needW + 2) + 'x' + Math.round(needH + 2);
              if (e.intersectionRect.height >= needH && e.intersectionRect.width >= needW)
                requestAnimationFrame(function () { requestAnimationFrame(function () { finish('visible'); }); });
              else requestAnimationFrame(check);
            });
            io.observe(el);
          }
          check();
        })
        """);

    /// <summary>
    /// Bounded paint-readiness gate after the real viewport was enlarged for an OOPIF capture.
    /// Returns null when every participant is ready, otherwise a short reason.
    /// </summary>
    private async Task<string?> WaitForEnlargedPaintAsync(int width, int height, IReadOnlyList<OopifPaintRange> frames, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromMilliseconds(PaintWaitMilliseconds + 3000));
        try
        {
            var main = await EvaluateContextAsync(MainViewportPaintScript(width, height), null, bounded.Token, awaitPromise: true);
            if (main != "ready") return $"page={main}";
            foreach (var frame in frames)
            {
                var state = await EvaluateContextAsync(OopifVisiblePaintScript, frame.ContextId, frame.SessionId, bounded.Token, awaitPromise: true);
                if (state != "visible") return $"{frame.FrameId}={state}";
            }
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return "timeout";
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !IsConnectionFailure(ex))
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Grows every authorized sub-frame's owner element to the frame document height using
    /// CDP (so cross-origin and OOPIF frames are included). It is not enough to write a
    /// height: the element's real rendered height, its computed max-height and the frame's
    /// own viewport are read back after a layout tick. A frame that cannot be fully exposed
    /// is reported so the capture fails instead of truncating (R5-3).
    /// </summary>
    private async Task<List<string>> ExpandFramesAsync(List<string> expanded, List<OopifPaintRange> capturedOopif, CancellationToken token)
    {
        var failed = new List<string>();
        if (_cdp is null) return failed;
        JsonElement tree;
        try { tree = await _cdp.SendAsync("Page.getFrameTree", null, token); }
        catch (Exception ex)
        {
            // A missing frame tree means the frame candidates could not be confirmed at all;
            // returning an empty failure list here would let an unverified capture save.
            AppLog.Write($"读取框架树失败：{ex.Message}");
            return [$"(root frame tree 读取失败：{ex.Message})"];
        }
        var frames = new List<(string FrameId, string Url)>();
        CollectFrames(tree.GetProperty("result").GetProperty("frameTree"), frames);
        // Refresh real URLs from the root tree, but never let the root's placeholder for an OOPIF
        // (empty or ":") clobber the child-session URL that attach/child-getFrameTree registered.
        foreach (var (id, url) in frames)
            if (id.Length > 0 && IsKnownUrl(url))
                lock (_frameUrls) _frameUrls[id] = url;
        // A flattened OOPIF is reached through its own child target and can be missing from
        // the root session's Page.getFrameTree. Enumerate the live attached child targets too
        // so the cross-process query frame is expanded and verified rather than silently
        // truncating the capture.
        var seenFrames = new HashSet<string>(frames.Select(x => x.FrameId), StringComparer.Ordinal);
        lock (_frameTargetSessions)
            foreach (var targetId in _frameTargetSessions.Keys)
                if (targetId.Length > 0 && seenFrames.Add(targetId)) frames.Add((targetId, ""));
        var candidateLog = frames.Select(x => x.FrameId + (IsAuthorizedFrameId(x.FrameId) ? "[授权]" : "[跳过]"));
        AppLog.Write("框架扩展候选：" + string.Join("、", candidateLog));
        foreach (var (frameId, _) in frames)
        {
            if (frameId.Equals(_mainFrameId, StringComparison.Ordinal)) continue;
            if (!IsAuthorizedFrameId(frameId)) continue;
            token.ThrowIfCancellationRequested();
            try
            {
                var context = ContextForFrame(frameId);
                var session = context?.SessionId ?? SessionForFrame(frameId);
                var contextId = context?.ContextId;
                if (contextId is null)
                    contextId = await CreateIsolatedWorldAsync(frameId, "customs-console-frame-expand", session, token);
                if (contextId is null) { AppLog.Write($"扩展框架 {frameId} 失败：无执行环境（session={session ?? "-"}）。"); failed.Add(frameId); continue; }

                var measured = await EvaluateContextAsync(
                    "JSON.stringify((function(){var d=document.documentElement,b=document.body;return {content:Math.max(d?d.scrollHeight:0,b?b.scrollHeight:0)};})())",
                    contextId, session, token, awaitPromise: false);
                int frameContent;
                try
                {
                    using var document = JsonDocument.Parse(measured);
                    frameContent = (int)Math.Ceiling(document.RootElement.GetProperty("content").GetDouble());
                }
                catch { AppLog.Write($"扩展框架 {frameId} 失败：内容高度读取异常（{measured}）。"); failed.Add(frameId); continue; }
                if (frameContent <= 0)
                {
                    // An attached child-session (OOPIF) query frame is never legitimately empty:
                    // an unreadable height there must reject the capture, not pass as expanded.
                    if (session is not null) { AppLog.Write($"扩展框架 {frameId} 失败：子 session frame 内容高度为 {frameContent}。"); failed.Add(frameId); }
                    else AppLog.Write($"扩展框架 {frameId} 跳过：内容高度为 {frameContent}。");
                    continue;
                }

                var objectId = await GetFrameOwnerObjectIdAsync(frameId, token);
                if (string.IsNullOrEmpty(objectId)) { AppLog.Write($"扩展框架 {frameId} 失败：找不到 owner 元素（session={session ?? "-"}）。"); failed.Add(frameId); continue; }
                var ownerSession = OwnerSessionForFrame(frameId);
                var beforeJson = await CallFunctionOnAsync(objectId, ReadFrameGeometryFunction, null, token, ownerSession);
                if (!TryReadFrameGeometry(beforeJson, out var beforeVisible, out var beforeMax, out var beforeBottom, out var beforeRight, out var beforeLeft)) { AppLog.Write($"扩展框架 {frameId} 失败：前置几何读取失败（{beforeJson}）。"); failed.Add(frameId); continue; }
                AppLog.Write($"扩展框架 {frameId}：内容高 {frameContent}，前置可视 {beforeVisible}/{beforeMax}，范围右/下 {beforeRight}/{beforeBottom}。");
                // Already fully exposed: nothing to change and nothing to restore. An attached
                // child-session frame still participates in the capture, though, and if its box
                // extends past the real viewport the capture must enlarge the viewport even
                // without any owner mutation (otherwise the OOPIF below the fold stays blank).
                if (beforeVisible >= frameContent - 2 && beforeMax >= frameContent - 2)
                {
                    if (session is not null)
                        capturedOopif.Add(new OopifPaintRange(frameId, beforeLeft, beforeBottom, beforeRight, contextId.Value, session));
                    continue;
                }

                // Register before mutation so restore runs even if the response is lost.
                expanded.Add(frameId);
                await CallFunctionOnAsync(objectId, GrowFrameFunction, new { value = frameContent }, token, ownerSession);
                await Task.Delay(120, token);

                var afterJson = await CallFunctionOnAsync(objectId, ReadFrameGeometryFunction, null, token, ownerSession);
                if (!TryReadFrameGeometry(afterJson, out var afterVisible, out var afterMax, out var afterBottom, out var afterRight, out var afterLeft)) { AppLog.Write($"扩展框架 {frameId} 失败：后置几何读取失败（{afterJson}）。"); failed.Add(frameId); continue; }
                // A grown attached child-session frame is an OOPIF; captureBeyondViewport does
                // not repaint those beyond the real viewport, so the caller enlarges it first.
                if (session is not null)
                    capturedOopif.Add(new OopifPaintRange(frameId, afterLeft, afterBottom, afterRight, contextId.Value, session));
                var viewportJson = await EvaluateContextAsync(
                    "JSON.stringify({content:Math.max(document.documentElement?document.documentElement.scrollHeight:0,document.body?document.body.scrollHeight:0),viewport:window.innerHeight||0})",
                    contextId, session, token, awaitPromise: false);
                int afterContent, afterViewport;
                try
                {
                    using var document = JsonDocument.Parse(viewportJson);
                    afterContent = (int)Math.Ceiling(document.RootElement.GetProperty("content").GetDouble());
                    afterViewport = (int)Math.Ceiling(document.RootElement.GetProperty("viewport").GetDouble());
                }
                catch { AppLog.Write($"扩展框架 {frameId} 失败：后置内容读取失败（{viewportJson}）。"); failed.Add(frameId); continue; }

                var elementExposed = afterVisible >= Math.Max(frameContent, afterContent) - 2 && afterMax >= Math.Max(frameContent, afterContent) - 2;
                var frameExposed = afterViewport >= afterContent - 2;
                if (!elementExposed || !frameExposed) AppLog.Write($"扩展框架 {frameId} 失败：元素 {afterVisible}/{afterMax}，内容 {afterContent}，视口 {afterViewport}。");
                if (!elementExposed || !frameExposed) failed.Add(frameId);
            }
            catch (Exception ex) when (!IsConnectionFailure(ex))
            {
                AppLog.Write($"扩展框架 {frameId} 失败：{ex.Message}");
                failed.Add(frameId);
            }
        }
        return failed;
    }

    private async Task RestoreFramesAsync(IReadOnlyList<string> frameIds, CancellationToken token)
    {
        if (_cdp is null) return;
        foreach (var frameId in frameIds.Reverse())
        {
            if (token.IsCancellationRequested) break;
            try
            {
                var objectId = await GetFrameOwnerObjectIdAsync(frameId, token);
                if (string.IsNullOrEmpty(objectId)) continue;
                await CallFunctionOnAsync(objectId, RestoreFrameFunction, null, token, OwnerSessionForFrame(frameId));
            }
            catch (Exception ex) { AppLog.Write($"恢复框架高度失败：{ex.Message}"); }
        }
    }

    private string SaveAtomically(Bitmap image)
    {
        Directory.CreateDirectory(_targetFolder);
        var finalPath = GetAvailableScreenshotPath(_targetFolder, DeclarationNo);
        var temporary = Path.Combine(_targetFolder, $".{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            image.Save(temporary, ImageFormat.Png);
            File.Move(temporary, finalPath, overwrite: false);
            return finalPath;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static string GetAvailableScreenshotPath(string folder, string declarationNo)
    {
        var path = Path.Combine(folder, declarationNo + ".png");
        if (!File.Exists(path)) return path;
        for (var index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(folder, $"{declarationNo}_{DateTime.Now:yyyyMMdd_HHmmss}_{index:D2}.png");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{declarationNo}_{Guid.NewGuid():N}.png");
    }
}
