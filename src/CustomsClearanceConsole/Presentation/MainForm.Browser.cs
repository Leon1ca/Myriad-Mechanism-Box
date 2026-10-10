using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{

    // ---- browser verification ----

    private async Task StartVerificationAsync(DeclarationRecord record)
    {
        if (!Directory.Exists(_state.ScreenshotFolder))
        {
            ShowDirectorySettings();
            if (!Directory.Exists(_state.ScreenshotFolder))
            {
                MessageBox.Show("请先在设置中选择有效的截图保存目录。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        BrowserValidation session;
        try { session = new BrowserValidation(record.DeclarationNo, _state.ScreenshotFolder); }
        catch (Exception ex) { AppLog.Write(ex); MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        _browserSessions[session.SessionId] = session;
        session.CaptureCompleted += OnBrowserCaptureCompleted;
        session.StatusChanged += OnBrowserStatusChanged;
        session.Ended += OnBrowserSessionEnded;
        ShowToast($"正在打开核验页面：{record.DeclarationNo}");
        try
        {
            var message = await session.StartAsync(CancellationToken.None);
            if (!IsDisposed && !Disposing && _browserSessions.ContainsKey(session.SessionId)) ShowToast(message);
        }
        catch (Exception ex)
        {
            var invalidated = IsDisposed || Disposing || !_browserSessions.ContainsKey(session.SessionId);
            AppLog.Write(ex);
            DetachBrowserSession(session);
            _browserSessions.Remove(session.SessionId);
            await session.DisposeAsync();
            if (invalidated) return;
            MessageBox.Show($"无法启动核验浏览器：{ex.Message}\n\n请确认已安装 Microsoft Edge 或 Google Chrome。", "网页核验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DetachBrowserSession(BrowserValidation session)
    {
        session.CaptureCompleted -= OnBrowserCaptureCompleted;
        session.StatusChanged -= OnBrowserStatusChanged;
        session.Ended -= OnBrowserSessionEnded;
    }

    /// <summary>
    /// The user closed the verification browser: release the session (control socket, process
    /// handle and its throw-away profile) instead of keeping it until the batch changes.
    /// </summary>
    private void OnBrowserSessionEnded(object? sender, EventArgs e)
    {
        if (!OnUiThread(() => OnBrowserSessionEnded(sender, e))) return;
        if (sender is not BrowserValidation session ||
            !_browserSessions.TryGetValue(session.SessionId, out var active) || !ReferenceEquals(active, session)) return;
        _browserSessions.Remove(session.SessionId);
        DetachBrowserSession(session);
        _ = session.DisposeAsync();
    }

    private void OnBrowserStatusChanged(object? sender, string message)
    {
        if (!OnUiThread(() => OnBrowserStatusChanged(sender, message))) return;
        if (sender is BrowserValidation session && _browserSessions.TryGetValue(session.SessionId, out var active) &&
            ReferenceEquals(active, session)) ShowToast(message);
    }

    private void OnBrowserCaptureCompleted(object? sender, BrowserCaptureResult result)
    {
        if (!OnUiThread(() => OnBrowserCaptureCompleted(sender, result))) return;
        // A queued completion from an invalidated session (batch switch, list clear or
        // session disposal) must never touch the current batch, even if the declaration
        // number matches a record in the new batch.
        if (sender is not BrowserValidation session ||
            !_browserSessions.TryGetValue(session.SessionId, out var active) ||
            !ReferenceEquals(active, session) ||
            !string.Equals(session.DeclarationNo, result.DeclarationNo, StringComparison.Ordinal))
        {
            AppLog.Write($"已忽略已失效核验会话的回填：{result.DeclarationNo} · {result.State}");
            return;
        }
        switch (result.State)
        {
            case "saved" when result.FilePath is not null && BrowserCapturePolicy.CanBackfill(session.DeclarationNo, result.State, result.DeclarationNo):
                foreach (var matching in _state.Records.Where(x => x.DeclarationNo == result.DeclarationNo))
                    matching.ScreenshotPath = result.FilePath;
                SaveState();
                RefreshGrid();
                ShowFileSavedToast(result.FilePath, "核验长截图已保存，并回填本批同单号记录");
                break;
            case "mismatch":
                ShowToast("网页结果单号与本次核验不一致，未保存截图。");
                break;
            case "too-long":
                ShowToast("页面超过 6000 万像素，未保存截图；请使用浏览器分段保存。");
                break;
            case "cancelled":
                break;
            default:
                ShowToast(result.Message);
                break;
        }
    }

    private bool OnUiThread(Action retry)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return false;
        if (!InvokeRequired) return true;
        try { BeginInvoke(retry); }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
        return false;
    }

    private void InvalidateBrowserSessions()
    {
        if (_browserSessions.Count == 0) return;
        foreach (var session in _browserSessions.Values.ToList())
        {
            DetachBrowserSession(session);
            _ = session.DisposeAsync();
        }
        _browserSessions.Clear();
    }
}
