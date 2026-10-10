namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{
    private BatchState CurrentState => _session is not null || _previewProcessing ? BatchState.Processing
        : _state.Records.Count > 0 ? BatchState.Complete
        : _batchFolder.Length > 0 ? BatchState.Ready
        : BatchState.Empty;

    /// <summary>Native UI self-test hook: render the recognition-in-progress state without running OCR.</summary>
    internal void PreviewProcessing(int done, int total, string file, int completed, int attention, int failed)
    {
        _previewProcessing = true;
        _scanStartedAt ??= DateTime.Now;
        _scanCompletedAt = null;
        _progressStrip.Done = done;
        _progressStrip.Total = total;
        _progressStrip.FileName = file;
        _progressStrip.Completed = completed;
        _progressStrip.Attention = attention;
        _progressStrip.Failed = failed;
        RefreshAll();
    }

    /// <summary>Native UI self-test hook: apply a search query for the "no results" state.</summary>
    internal void ApplyPreviewSearch(string text)
    {
        _search.Text = text;
        _page = 1;
        RefreshGrid();
    }

    /// <summary>Native UI self-test hook: the layout applied for the current client size.</summary>
    internal Responsive.Layout AppliedLayout => _layout;

    // ---- settings / batch loading ----

    private void ShowDirectorySettings()
    {
        if (_session is not null) return;
        using var dialog = new DirectorySettingsForm(_state.LastFolder, _state.ScreenshotFolder);
        if (ModalPresenter.Show(dialog, this) != DialogResult.OK) return;
        var newFolder = dialog.DeclarationFolder;
        _state.ScreenshotFolder = dialog.ScreenshotFolder;
        if (!string.Equals(newFolder, _state.LastFolder, StringComparison.OrdinalIgnoreCase))
        {
            // A different valid folder is a new batch: pre-check, then replace the batch.
            if (!Directory.Exists(newFolder))
            {
                MessageBox.Show("新的关单目录不可访问，已保留当前批次。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            InvalidateBrowserSessions();
            _state.LastFolder = newFolder;
            _state.Records.Clear();
            _page = 1;
            LoadBatchFileSummary(newFolder);
        }
        SaveState();
        RefreshAll();
    }

    private void LoadBatchFileSummary(string folder)
    {
        _batchFolder = folder;
        try
        {
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Where(ScanPlan.IsSupported).ToList();
            _batchFileCount = files.Count;
            var pdf = files.Count(x => Path.GetExtension(x).Equals(".pdf", StringComparison.OrdinalIgnoreCase));
            var images = files.Count - pdf;
            _batchFileSummary = $"{folder} · {files.Count} 个文件" + (pdf > 0 || images > 0 ? $"（PDF {pdf} · 图片 {images}）" : "");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            _batchFileCount = 0;
            _batchFileSummary = folder;
        }
    }

    private async Task ScanAsync() => await PrepareAndScanAsync(() => ScanPlan.FromFolder(_state.LastFolder));

    private async Task ScanDroppedFilesAsync(IReadOnlyList<string> files) =>
        await PrepareAndScanAsync(() => ScanPlan.FromFiles(files));

    private async Task PrepareAndScanAsync(Func<ScanPlan> prepare)
    {
        if (_session is not null) return;
        ScanPlan plan;
        try { plan = prepare(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "批次未开始", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        InvalidateBrowserSessions();
        using var session = new ScanSession();
        _session = session;
        _scanStartedAt = DateTime.Now;
        _scanCompletedAt = null;
        _state.Records = session.Completed;
        _page = 1;
        _batchFolder = plan.Files.Count > 0 ? Path.GetDirectoryName(plan.Files[0]) ?? _batchFolder : _batchFolder;
        _progressStrip.Total = plan.Files.Count;
        _progressStrip.Done = 0;
        _progressStrip.Completed = 0;
        _progressStrip.Attention = 0;
        _progressStrip.Failed = 0;
        _progressStrip.FileName = "准备识别";
        RefreshAll();
        var progress = new InlineProgress<(int Done, int Total, string File)>(x =>
        {
            _progressStrip.Done = x.Done;
            _progressStrip.Total = x.Total;
            _progressStrip.FileName = x.File;
            _progressStrip.Invalidate();
        });
        var items = new InlineProgress<DeclarationRecord>(record =>
        {
            session.Completed.Add(record);
            BatchScanner.MarkDuplicates(session.Completed);
            _progressStrip.Completed = session.Completed.Count(x => !x.NeedsAttention && !x.IsDuplicate);
            _progressStrip.Attention = session.Completed.Count(x => x.NeedsAttention);
            _progressStrip.Failed = session.Completed.Count(x => x.Status == "识别失败");
            RefreshGrid();
        });
        try { await using var scanner = new BatchScanner(); await scanner.ScanAsync(plan, progress, session.Token, items); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex); MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally
        {
            var cancelled = session.Token.IsCancellationRequested;
            BatchScanner.MarkDuplicates(session.Completed);
            _state.Records = BatchScanner.SortRecords(session.Completed).ToList();
            _session = null;
            _scanCompletedAt = DateTime.Now;
            LoadBatchFileSummary(_batchFolder.Length > 0 ? _batchFolder : _state.LastFolder);
            SaveState();
            RefreshAll();
            if (cancelled && _state.Records.Count > 0)
                ShowToast($"已取消，保留 {_state.Records.Count} 条已完成结果");
        }
    }

    // ---- drag & drop pre-check ----

    private static string[] DroppedSupportedFiles(DragEventArgs e)
    {
        if (!e.Data!.GetDataPresent(DataFormats.FileDrop)) return [];
        var extensions = new HashSet<string>(BatchScanner.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        return ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? [])
            .Where(File.Exists).Where(x => extensions.Contains(Path.GetExtension(x))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // DragOver fires continuously while the mouse moves; the dropped set cannot change during one
    // drag, so the file-system checks run once on enter instead of on every mouse move.
    private string[]? _dragFiles;

    private void GridDragEnter(object? sender, DragEventArgs e)
    {
        _dragFiles = DroppedSupportedFiles(e);
        e.Effect = _dragFiles.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void GridDragOver(object? sender, DragEventArgs e)
    {
        if (_session is not null) { e.Effect = DragDropEffects.None; return; }
        var files = _dragFiles ??= DroppedSupportedFiles(e);
        var message = files.Length == 0 ? "未找到可识别的关单文件"
            : files.Length > BatchScanner.MaximumFiles ? $"本批 {files.Length} 个文件，超过每批 {BatchScanner.MaximumFiles} 个上限，整批停止"
            : $"可载入 {files.Length} 个文件，松开后作为新批次识别";
        ShowDropFeedback(message, files.Length > 0 && files.Length <= BatchScanner.MaximumFiles);
        e.Effect = files.Length > 0 && files.Length <= BatchScanner.MaximumFiles ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void GridDragDrop(object? sender, DragEventArgs e)
    {
        HideDropFeedback();
        _dragFiles = null;
        var files = DroppedSupportedFiles(e);
        if (files.Length == 0) return;
        if (files.Length > BatchScanner.MaximumFiles)
        {
            MessageBox.Show($"一次最多拖入 {BatchScanner.MaximumFiles} 个关单文件。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await ScanDroppedFilesAsync(files);
    }

    private void ShowDropFeedback(string message, bool allowed)
    {
        if (_dropBanner.Visible && _dropBanner.Text == message) return;
        _dropBanner.Text = message;
        _dropBanner.ForeColor = allowed ? Theme.Primary : Theme.Warning;
        _dropBanner.BackColor = allowed ? Color.FromArgb(240, 246, 253) : UiTokens.Status.AttentionNotice;
        _dropBanner.Visible = true;
        _dropBanner.BringToFront();
    }

    private void HideDropFeedback()
    {
        _dragFiles = null;
        _dropBanner.Visible = false;
    }
}
