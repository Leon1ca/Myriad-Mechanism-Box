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

    // ---- capture ----

    private void OnBindingCalled(JsonElement message)
    {
        try
        {
            var parameters = message.GetProperty("params");
            var name = parameters.GetProperty("name").GetString();
            var contextId = parameters.TryGetProperty("executionContextId", out var context) ? context.GetInt32() : -1;
            var sessionId = SessionIdOf(message);
            if (IsAuthorizedContext(contextId, sessionId))
            {
                HandleAuthorizedBinding(name, contextId);
                return;
            }
            // The frame URL can still be unknown right after (re)connecting or attaching: the
            // frame-tree snapshot may predate the commit. Resolve it from CDP once before
            // judging, instead of refusing a legitimate click on the target page.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var refresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    refresh.CancelAfter(TimeSpan.FromSeconds(5));
                    await RefreshUnknownFrameUrlsAsync(refresh.Token);
                }
                catch (Exception ex) { AppLog.Write($"复核网页请求来源失败：{ex.Message}"); }
                if (IsAuthorizedContext(contextId, sessionId))
                {
                    HandleAuthorizedBinding(name, contextId);
                    return;
                }
                AppLog.Write($"已忽略来源页面的网页请求：{name} · context={contextId} · session={sessionId ?? "-"} · {DescribeContextForLog(contextId, sessionId)}");
                await SetWidgetStateAsync("error", "当前页面不是核验目标页，已忽略网页请求。");
            });
        }
        catch (Exception ex)
        {
            AppLog.Write($"处理网页请求失败：{ex.Message}");
        }
    }

    private void HandleAuthorizedBinding(string? name, int contextId)
    {
        try
        {
            if (name == "cccRequestCapture")
            {
                if (Interlocked.CompareExchange(ref _captureGate, 1, 0) != 0)
                {
                    // Another capture already owns the single-owner gate. Reject without
                    // touching the gate: the running capture publishes the next retryable
                    // page state, so a card that switched itself to capturing does not need a
                    // second completion event to recover.
                    AppLog.Write($"忙拒绝网页截图请求：已有截图正在进行（context={contextId}，当前步骤：{_captureStep}）。");
                    // The card switched itself to capturing; tell it the earlier capture still
                    // runs so it does not wait silently for a completion that is not its own.
                    _ = ReportProgressAsync(0, 1, "上一次截图仍在进行，请稍候");
                    return;
                }
                AppLog.Write($"接受网页截图请求并持有截图锁（context={contextId}）。");
                _ = Task.Run(async () =>
                {
                    try { await CaptureAndReportAsync(); }
                    catch (Exception ex) { AppLog.Write($"截图任务异常：{ex.Message}"); }
                });
            }
            else if (name == "cccOpenFolder")
            {
                // The page can only ask to open the already-configured folder.
                OpenTargetFolder();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"处理网页请求失败：{ex.Message}");
        }
    }

    private void OpenTargetFolder()
    {
        try
        {
            if (Directory.Exists(_targetFolder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_targetFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private async Task SetWidgetStateAsync(string state, string message)
    {
        try
        {
            if (_cdp is null || _cdp.IsClosed) return;
            var json = JsonSerializer.Serialize(new { message });
            await EvaluateContextAsync($"window.__cccWidget && window.__cccWidget.setState({JsonSerializer.Serialize(state)}, {json});", null, CancellationToken.None, awaitPromise: false);
        }
        catch (Exception ex) { AppLog.Write($"更新网页卡片状态失败：{ex.Message}"); }
    }

    private async Task ReportAsync(BrowserCaptureResult result)
    {
        try
        {
            if (_cdp is null || _cdp.IsClosed) return;
            var json = JsonSerializer.Serialize(new { file = result.FilePath is null ? "" : Path.GetFileName(result.FilePath), message = result.Message });
            await EvaluateContextAsync($"window.__cccWidget && window.__cccWidget.setState({JsonSerializer.Serialize(result.State)}, {json});", null, _lifetime.Token, awaitPromise: false);
        }
        catch (Exception ex) { AppLog.Write($"更新网页卡片状态失败：{ex.Message}"); }
    }

    private async Task ReportProgressAsync(int done, int total, string? text)
    {
        try
        {
            if (_cdp is null || _cdp.IsClosed) return;
            var label = text is null ? "" : JsonSerializer.Serialize(text);
            await EvaluateContextAsync($"window.__cccWidget && window.__cccWidget.setProgress({done}, {total}, {label});", null, _lifetime.Token, awaitPromise: false);
        }
        catch (Exception ex) { AppLog.Write($"更新网页进度失败：{ex.Message}"); }
    }

    // ---- E2E helpers ----

    public async Task<bool> WaitForWidgetAsync(TimeSpan timeout, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var value = await EvaluateContextAsync("!!(window.__cccWidget && window.__cccWidget.declarationNo === " + JsonSerializer.Serialize(DeclarationNo) + ")", null, token, awaitPromise: false);
                if (value.Contains("true", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception ex) when (IsConnectionFailure(ex)) { try { await ReconnectAsync(token); } catch { } }
            await Task.Delay(150, token);
        }
        return false;
    }

    public Task<string> ProbeIdentityAsync(CancellationToken token) =>
        EvaluateAllContextsAsync(BrowserScript("identity", DeclarationNo), token, false, "ready|", "mismatch|", "error|", "loading|", "stale|");

    public async Task<string> WaitForIdentitySettledAsync(TimeSpan timeout, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        string last = "";
        while (DateTime.UtcNow - started < timeout)
        {
            token.ThrowIfCancellationRequested();
            last = await ProbeIdentityAsync(token);
            if (last.Length > 0 && !last.StartsWith("waiting|", StringComparison.Ordinal)) return last;
            await Task.Delay(150, token);
        }
        return last;
    }

    /// <summary>
    /// The single result-region definition shared by identity/verify/probe. It covers the
    /// official #queryDetail .display-content .content-field renderer (R6) as well as the
    /// table/timeline shapes used by the controlled fixtures.
    /// </summary>
    private const string ResultSelectors =
        "table tbody tr,.timeline li,.el-timeline-item,.ant-timeline-item,.query-result tr,.result-list li," +
        "[class*=result] tbody tr,[class*=inquiry] tbody tr,[class*=detail] tbody tr," +
        "#queryDetail .display-content .content-field,#queryDetail .content-field,[class*=display-content] .content-field," +
        "#queryDetail [class*=field-order],[class*=display-content] [class*=field-order]";
}
