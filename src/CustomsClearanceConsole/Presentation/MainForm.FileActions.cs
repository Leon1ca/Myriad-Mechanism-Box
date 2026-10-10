using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{

    // ---- export ----

    private void ShowExportMenu()
    {
        if (_session is not null || _state.Records.Count == 0) return;
        _exportMenu.Show(_exportButton, _exportButton.Height + 6, true);
    }

    private void ExportList(string kind)
    {
        if (_session is not null || _state.Records.Count == 0) return;
        var excel = kind == "excel";
        using var dialog = new SaveFileDialog
        {
            Title = excel ? "导出本批全部关单为 Excel" : "导出本批全部关单为 Markdown",
            Filter = excel ? "Excel 工作簿 (*.xlsx)|*.xlsx" : "Markdown 文档 (*.md)|*.md",
            DefaultExt = excel ? "xlsx" : "md",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"关单列表_{DateTime.Now:yyyyMMdd_HHmm}.{(excel ? "xlsx" : "md")}",
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var snapshot = BatchScanner.SortRecords(_state.Records).ToList();
            var now = DateTime.Now;
            if (excel) ExcelListExporter.Save(dialog.FileName, snapshot, now);
            else MarkdownListExporter.Save(dialog.FileName, snapshot, now);
            ShowFileSavedToast(dialog.FileName, excel ? "关单列表已导出为 Excel" : "关单列表已导出为 Markdown");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            MessageBox.Show($"导出未完成，请检查保存位置权限或文件占用情况。\n{ex.Message}", "列表导出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowFileSavedToast(string filePath, string message)
    {
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        var toast = new RoundedPanel
        {
            Size = new Size(S(392), S(70)),
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = ColorTranslator.FromHtml("#F1FBF5"),
            BorderColor = ColorTranslator.FromHtml("#9ECBB0"),
            Radius = 9,
            AccessibleName = "文件保存成功提示"
        };
        toast.Location = new Point(Math.Max(S(20), ClientSize.Width - toast.Width - S(30)), Math.Max(S(20), ClientSize.Height - toast.Height - S(30)));
        var text = new Label
        {
            Text = message,
            Location = new Point(S(18), 0),
            Size = new Size(S(225), toast.Height),
            Font = Theme.UiFont(14F),
            ForeColor = Theme.Success,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var open = Theme.SecondaryButton("打开位置");
        open.Location = new Point(S(268), S(15));
        open.Size = new Size(S(106), S(40));
        open.Font = Theme.UiFont(14F);
        open.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Write(ex); }
            Controls.Remove(toast);
            toast.Dispose();
        };
        toast.Controls.AddRange([text, open]);
        Controls.Add(toast);
        toast.BringToFront();
        var timer = new System.Windows.Forms.Timer { Interval = 6000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!toast.IsDisposed) { Controls.Remove(toast); toast.Dispose(); }
            timer.Dispose();
        };
        toast.Disposed += (_, _) => { timer.Stop(); timer.Dispose(); };
        timer.Start();
    }

    // ---- cleanup ----

    private void ClearList()
    {
        if (_session is not null) return;
        if (_state.Records.Count == 0) { MessageBox.Show("当前列表已为空。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dialog = new CleanupDialog(CleanupKind.List, 0, _state.Records.Count, _batchFolder);
        if (ModalPresenter.Show(dialog, this) != DialogResult.OK) return;
        InvalidateBrowserSessions();
        _state.Records.Clear();
        _page = 1;
        RefreshAll();
        SaveState();
    }

    private void CleanDeclarationFolder()
    {
        if (_session is not null) return;
        try
        {
            var folder = FileCleanupService.ValidateTargetFolder(_state.LastFolder);
            var files = FileCleanupService.GetDeclarationFiles(folder);
            if (files.Count == 0) { MessageBox.Show("当前关单文件夹没有可清理的 PDF 或图片。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using var dialog = new CleanupDialog(CleanupKind.Declarations, files.Count, 0, folder);
            if (ModalPresenter.Show(dialog, this) != DialogResult.OK) return;
            var result = FileCleanupService.MoveToRecycleBin(files, folder);
            _state.Records.RemoveAll(x => !string.IsNullOrWhiteSpace(x.SourcePath) && result.MovedPaths.Contains(Path.GetFullPath(x.SourcePath)));
            BatchScanner.MarkDuplicates(_state.Records);
            _page = 1;
            SaveState();
            RefreshAll();
            ShowCleanupResult("关单清理", result);
        }
        catch (Exception ex) { AppLog.Write(ex); MessageBox.Show(ex.Message, "关单清理", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void CleanScreenshotFolder()
    {
        if (_session is not null) return;
        try
        {
            var folder = FileCleanupService.ValidateTargetFolder(_state.ScreenshotFolder);
            var files = FileCleanupService.GetScreenshotFiles(folder, _state.Records);
            if (files.Count == 0) { MessageBox.Show("当前截图文件夹没有程序生成的网页截图。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using var dialog = new CleanupDialog(CleanupKind.Screenshots, files.Count, 0, folder);
            if (ModalPresenter.Show(dialog, this) != DialogResult.OK) return;
            var result = FileCleanupService.MoveToRecycleBin(files, folder);
            foreach (var record in _state.Records.Where(x => !string.IsNullOrWhiteSpace(x.ScreenshotPath) && result.MovedPaths.Contains(Path.GetFullPath(x.ScreenshotPath))))
                record.ScreenshotPath = "";
            SaveState();
            RefreshAll();
            ShowCleanupResult("截图清理", result);
        }
        catch (Exception ex) { AppLog.Write(ex); MessageBox.Show(ex.Message, "截图清理", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ShowCleanupResult(string title, CleanupResult result)
    {
        var message = $"已将 {result.Moved} 个文件移入 Windows 回收站。";
        if (result.Failed.Count > 0) message += $"\n\n另有 {result.Failed.Count} 个文件未能处理：{string.Join("、", result.Failed.Take(5))}";
        MessageBox.Show(message, title, MessageBoxButtons.OK, result.Failed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
}
