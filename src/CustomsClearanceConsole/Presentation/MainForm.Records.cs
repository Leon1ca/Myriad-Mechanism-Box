using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{

    // ---- filtering / paging ----

    private IEnumerable<DeclarationRecord> FilteredRecords()
    {
        var query = _state.Records.AsEnumerable();
        var search = _search.Text.Trim();
        if (search.Length > 0)
            query = query.Where(x => new[] { x.DeclarationNo, x.SourceName, x.Consignee, x.ContractNo }
                .Any(value => value.Contains(search, StringComparison.CurrentCultureIgnoreCase)));
        query = _filterIndex switch
        {
            1 => query.Where(x => !x.IsDuplicate && !x.NeedsAttention),
            2 => query.Where(x => x.IsDuplicate),
            3 => query.Where(x => x.NeedsAttention),
            _ => query
        };
        return BatchScanner.SortRecords(query);
    }

    private int CurrentPageSize()
    {
        var text = _pageSize.SelectedItem?.ToString() ?? "50";
        return int.TryParse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], out var size) && size is 50 or 100 or 200 ? size : 50;
    }

    private int PageCount() => Math.Max(1, (int)Math.Ceiling(_visible.Count / (double)CurrentPageSize()));

    // ---- refresh ----

    private void RefreshAll()
    {
        var state = CurrentState;
        var processing = state == BatchState.Processing;
        _body.RowStyles[2].Height = processing ? DpiLayout.Scale(_layout.CompactHeight ? 92 : 104, _layoutDpi) : 0;
        _body.RowStyles[3].Height = processing ? DpiLayout.Scale(_layout.CompactHeight ? 34 : 38, _layoutDpi) : 0;
        _exportButton.Enabled = _state.Records.Count > 0 && !processing;
        _exportButton.Image?.Dispose();
        _exportButton.Image = UiV2Icons.Load(_exportButton.Enabled ? Ui2.Export : Ui2.ExportDisabled, 16, _layoutDpi);
        _settings.Enabled = !processing;
        _cleanupButton.Enabled = !processing;
        _filterSegmented.Enabled = _state.Records.Count > 0 && !processing;
        _searchHost.Enabled = _state.Records.Count > 0 && !processing;
        if (processing)
        {
            _scan.Text = "取消识别";
            _scan.Image?.Dispose();
            _scan.Image = UiV2Icons.Load(Ui2.Stop, 16, _layoutDpi);
            _scan.BackColor = Color.White;
            _scan.ForeColor = Theme.Danger;
            _scan.AccessibleName = "取消识别";
            if (_scan is RoundedButton cancelRounded)
            {
                cancelRounded.BorderColor = ColorTranslator.FromHtml("#E4A6A0");
                cancelRounded.HoverBackColor = UiTokens.Status.AttentionNotice;
                cancelRounded.Invalidate();
            }
        }
        else
        {
            _scan.Text = "开始识别";
            _scan.Image?.Dispose();
            _scan.Image = UiV2Icons.Load(Ui2.Play, 16, _layoutDpi);
            var primaryFill = state == BatchState.Empty ? UiTokens.Colors.DisabledPrimaryFill : Theme.Primary;
            _scan.BackColor = primaryFill;
            _scan.ForeColor = Color.White;
            _scan.Enabled = state is BatchState.Ready or BatchState.Complete || _state.Records.Count > 0;
            _scan.AccessibleName = "开始识别";
            if (_scan is RoundedButton startRounded)
            {
                startRounded.BorderColor = primaryFill;
                startRounded.HoverBackColor = Theme.HeaderBg;
                startRounded.Invalidate();
            }
        }
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        // A leaked debounce timer from a disposed snapshot window can fire during a later
        // DoEvents(); never let it touch a torn-down grid ("No row can be added ... does not
        // have columns"). Dispose stops the timer, this guard covers an already-queued tick.
        if (IsDisposed || Disposing || _grid is null || _grid.IsDisposed || _grid.Columns.Count == 0) return;
        _visible = FilteredRecords().ToList();
        _page = Math.Clamp(_page, 1, PageCount());
        var size = CurrentPageSize();
        var items = _visible.Skip((_page - 1) * size).Take(size).ToList();
        _grid.Rows.Clear();
        var rowHeight = DpiLayout.Scale(_layout.TableRow, _layoutDpi);
        for (var i = 0; i < items.Count; i++)
        {
            var record = items[i];
            var index = _grid.Rows.Add();
            var row = _grid.Rows[index];
            row.Height = rowHeight;
            row.Tag = record;
            row.Cells["Index"].Value = ((_page - 1) * size + i + 1).ToString("D2");
            row.Cells["Status"].Value = StatusLabel(record);
            row.Cells["No"].Value = CopyNumberText(record);
            row.Cells["Consignee"].Value = Dash(record.Consignee);
            row.Cells["Contract"].Value = Dash(record.ContractNo);
            row.Cells["Port"].Value = Dash(record.ExitCustoms);
            row.Cells["Dest"].Value = Dash(record.DestinationCountry);
            row.Cells["PortDest"].Value = Dash(record.ExitCustoms) + "\n" + Dash(record.DestinationCountry);
            row.Cells["Amount"].Value = CopyAmountText(record);
            row.Cells["Detail"].Value = "明细";
            row.Cells["Verify"].Value = record.HasScreenshot ? "已留存" : record.HasValidDeclarationNo ? "校验" : "不可核验";
            // One shared style per row state instead of a private style object on every cell:
            // the row style outranks the alternating/column styles, so rendering is unchanged.
            row.DefaultCellStyle = record.IsDuplicate ? DuplicateRowStyle : record.NeedsAttention ? AttentionRowStyle : NormalRowStyle;
            foreach (DataGridViewCell cell in row.Cells)
                cell.ToolTipText = Convert.ToString(cell.FormattedValue) ?? "";
            row.Cells["Index"].ToolTipText = "行号（本页内序号）";
            row.Cells["Status"].ToolTipText = StatusLabel(record) + (record.AllWarnings.Length > 0 ? "\n" + record.AllWarnings : "");
            row.Cells["No"].ToolTipText = CopyNumberText(record);
            row.Cells["Amount"].ToolTipText = CopyAmountText(record);
            row.Cells["Consignee"].ToolTipText = Dash(record.Consignee);
            row.Cells["Contract"].ToolTipText = Dash(record.ContractNo);
            row.Cells["Port"].ToolTipText = Dash(record.ExitCustoms);
            row.Cells["Dest"].ToolTipText = Dash(record.DestinationCountry);
            row.Cells["PortDest"].ToolTipText = Dash(record.ExitCustoms) + "\n" + Dash(record.DestinationCountry);
            row.Cells["Detail"].ToolTipText = "打开只读关单明细";
            row.Cells["Verify"].ToolTipText = !record.HasValidDeclarationNo ? "未识别出有效报关单号，无法在线核验"
                : record.HasScreenshot ? "已留存；点击可重新核验并保存新截图"
                : "打开单一窗口核验页面；在网页悬浮按钮上手动长截图后回填";
        }
        UpdateSummary(items.Count);
    }

    // Per form, not static: a cell style remembers every grid that uses it, so a static one would
    // keep closed windows (for example the snapshot self-test forms) reachable.
    private readonly DataGridViewCellStyle NormalRowStyle = RowStyle(Theme.Surface);
    private readonly DataGridViewCellStyle DuplicateRowStyle = RowStyle(UiTokens.Status.Duplicate.Row);
    private readonly DataGridViewCellStyle AttentionRowStyle = RowStyle(UiTokens.Status.Attention.Row);

    private static DataGridViewCellStyle RowStyle(Color back) => new()
    {
        BackColor = back,
        SelectionBackColor = UiTokens.Status.CellSelected,
        SelectionForeColor = Theme.Text
    };

    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    /// <summary>Files in the current batch, independent of how many rows were recognized.</summary>
    private int BatchTotalForDisplay()
    {
        var state = CurrentState;
        return state switch
        {
            BatchState.Empty => 0,
            BatchState.Ready => _batchFileCount,
            BatchState.Processing => _progressStrip.Total > 0 ? _progressStrip.Total : _batchFileCount,
            _ => Math.Max(_batchFileCount, _state.Records.Count)
        };
    }

    /// <summary>The filter column wraps the segments plus the design's 16px gap to the search box.</summary>
    private void SizeFilterColumn()
    {
        if (_toolbar is null || _filterSegmented is null) return;
        var width = _filterSegmented.ContentWidth(_layoutDpi) + DpiLayout.Scale(16, _layoutDpi);
        _toolbar.ColumnStyles[0].Width = Math.Min(DpiLayout.Scale(372, _layoutDpi), width);
    }

    /// <summary>Stats row height that fits both the KPI grid and the currency summary.</summary>
    private int StatsRowHeight()
    {
        var dpi = _layoutDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        var kpi = KpiPanel.RequiredHeight(dpi, _layout.CompactHeight);
        var money = S(96 + _moneySummary.RowCount * (_layout.AmountRow + 4) +
            (_moneySummary.UnconfirmedRowCount > 0 ? 26 + _moneySummary.UnconfirmedRowCount * 26 : 0));
        // Design section gap (16, compact 12) between the statistics row and the records panel;
        // without it the two white panels touched and read as one block.
        return Math.Max(kpi, money) + S(_layout.SectionGap);
    }

    /// <summary>Pushes the real batch state, folder and timing into the title block (R4-4).</summary>
    private void UpdateTitleBlock()
    {
        var state = CurrentState;
        var records = _state.Records;
        var (status, palette) = state switch
        {
            BatchState.Empty => ("未载入", new UiTokens.StatusPalette("未载入", Theme.Muted, Theme.SegmentBg, Color.White)),
            BatchState.Ready => ("待识别", new UiTokens.StatusPalette("待识别", Theme.Primary, Theme.AccentSoft, Color.White)),
            BatchState.Processing => ("识别中", new UiTokens.StatusPalette("识别中", Theme.Accent, UiTokens.Status.AttentionNotice, Color.White)),
            _ => ("识别完成", UiTokens.Status.Ok)
        };
        var folder = _batchFolder.Length > 0 ? _batchFolder : _state.LastFolder;
        var subline = state switch
        {
            BatchState.Empty => "请选择关单目录",
            BatchState.Ready => _batchFileSummary,
            BatchState.Processing => (_scanStartedAt is { } started ? $"开始于 {started:HH:mm:ss} · " : "") +
                (_progressStrip.Total > 0 ? $"共 {_progressStrip.Total} 个文件，已识别 {records.Count}" : "正在识别"),
            _ => CompleteSubline(folder, records)
        };
        _titleBlock.Set("本批关单", status, palette, subline, false);
    }

    private string CompleteSubline(string folder, IReadOnlyList<DeclarationRecord> records)
    {
        var parts = new List<string>();
        if (_scanCompletedAt is { } done)
            parts.Add(_scanStartedAt is { } started && done >= started
                ? $"{done:HH:mm} 完成 · 用时 {FormatDuration(done - started)}"
                : $"{done:HH:mm} 完成");
        if (!string.IsNullOrWhiteSpace(folder)) parts.Add(folder);
        if (records.Count > 0) parts.Add($"共 {records.Count} 条记录");
        return string.Join(" · ", parts);
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} 分 {span.Seconds} 秒" : $"{Math.Max(0, span.Seconds)} 秒";

    private void UpdateSummary(int visibleCount)
    {
        var state = CurrentState;
        var records = _state.Records;
        var duplicateGroups = records.Where(x => x.IsDuplicate).GroupBy(x => x.DeclarationNo).Count();
        var duplicateFiles = records.Count(x => x.IsDuplicate);
        var attention = records.Count(x => x.NeedsAttention);
        var savedNumbers = records.Where(x => x.HasScreenshot).Select(x => x.DeclarationNo).Where(x => x.Length > 0).Distinct().Count();
        var validNumbers = records.Where(x => x.HasValidDeclarationNo).Select(x => x.DeclarationNo).Distinct().Count();
        var placeholder = state == BatchState.Empty;
        var batchTotal = BatchTotalForDisplay();
        var completedRecords = records.Count(x => x.Status is "识别完成" or "OCR 识别完成" or "双引擎校验通过" /* records saved by v1.5.2 and earlier */);

        _kpi.Set(
        [
            // The value is the number of files in the current batch, not the number of
            // recognized rows: the ready batch showed 10 loaded files but 0 here because it
            // always printed records.Count (R4-3).
            new KpiCell("本批文件", state == BatchState.Empty ? "—" : batchTotal.ToString(), "份",
                state switch
                {
                    BatchState.Empty => "尚未载入",
                    BatchState.Ready => "待识别",
                    BatchState.Processing => $"已识别 {records.Count} / {batchTotal}",
                    _ => $"识别完成 {completedRecords} / {batchTotal}"
                },
                state == BatchState.Empty ? KpiTone.Placeholder : KpiTone.Normal),
            new KpiCell("重复单号", duplicateGroups.ToString(), "组",
                placeholder ? "尚未载入" : records.Count == 0 ? "识别后统计" : $"{duplicateFiles} 份文件，合计只计 1 份",
                duplicateGroups > 0 ? KpiTone.Danger : placeholder ? KpiTone.Placeholder : KpiTone.Normal),
            new KpiCell("需关注", attention.ToString(), "",
                placeholder ? "尚未载入" : records.Count == 0 ? "识别后统计" : records.Any(x => x.Totals.Count == 0) || records.Any(x => x.LineTotals.Any(l => !l.IsReliable)) ? "金额未通过校验" : "无异常",
                attention > 0 ? KpiTone.Warning : placeholder ? KpiTone.Placeholder : KpiTone.Normal),
            new KpiCell("截图留存", $"{savedNumbers}", validNumbers > 0 ? $"/ {validNumbers} 单号" : "单号",
                placeholder ? "尚未载入" : records.Count == 0 ? "识别后统计" : "网页悬浮按钮截图后自动回填",
                savedNumbers > 0 ? KpiTone.Accent : placeholder ? KpiTone.Placeholder : KpiTone.Normal)
        ]);

        var gross = BatchScanner.GrossTotals(records);
        var net = BatchScanner.DeduplicatedTotals(records);
        var rows = new List<MoneySummaryRow>();
        foreach (var currency in gross.Keys.Union(net.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal))
        {
            var before = records.Count(x => x.Totals.ContainsKey(currency));
            var after = records.Count(x => x.IsCanonical && x.Totals.ContainsKey(currency));
            rows.Add(new MoneySummaryRow(currency, before, after, gross.GetValueOrDefault(currency), net.GetValueOrDefault(currency)));
        }
        // Any record with an unreliable line has unconfirmed money, not only records whose
        // whole Totals map is empty. Amounts stay grouped by currency and are never summed
        // across currencies under a single label.
        var unconfirmedRows = records
            .SelectMany(record => record.LineTotals.Where(line => !line.IsReliable)
                .GroupBy(line => string.IsNullOrWhiteSpace(line.Currency) ? "—" : line.Currency, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Record: record, Currency: group.Key, Amount: group.Sum(line => line.Amount), Count: group.Count())))
            .GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new UnconfirmedRow(group.Key, group.Sum(x => x.Amount), group.Sum(x => x.Count), group.First().Record.SourceName))
            .ToList();
        _moneySummary.Set(new MoneySummarySnapshot(rows, unconfirmedRows));
        // The stats row must fit both the KPI 2x2 grid and the currency summary. Deriving it
        // from the currency count alone shrank the empty/ready/processing KPI to 126px and
        // overlapped label/value/note (R4-3).
        _body.RowStyles[1].Height = StatsRowHeight();

        var counts = new[] { records.Count, records.Count(x => !x.IsDuplicate && !x.NeedsAttention), records.Count(x => x.IsDuplicate), attention };
        _filterSegmented.Set(
        [
            new FilterSegment("全部", "全部记录", counts[0], records.Count > 0, _filterIndex == 0),
            new FilterSegment("正常", "正常记录", counts[1], records.Count > 0, _filterIndex == 1),
            new FilterSegment("重复", "重复记录", counts[2], records.Count > 0, _filterIndex == 2),
            new FilterSegment("需关注", "需关注记录", counts[3], records.Count > 0, _filterIndex == 3)
        ]);
        SizeFilterColumn();

        _dropBanner ??= new Label { Visible = false, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.UiFont(13F), Height = 34 };
        var statePanelVisible = visibleCount == 0;
        if (state == BatchState.Processing && visibleCount == 0)
            _recordState.Set(new StateMessage("正在识别，请稍候…", "结果会逐条写入列表；取消后保留已完成结果", null, null, null), false);
        else if (state == BatchState.Empty)
            _recordState.Set(new StateMessage("尚未载入关单", "在设置中选择关单目录，或将 PDF / 图片拖入此处", Ui2.FolderUpload, "选择关单目录", null), true);
        else if (state == BatchState.Ready && records.Count == 0)
            _recordState.Set(new StateMessage($"已载入 {_batchFileCount} 个文件", "点击“开始识别”后在此列出识别结果", null, null, _batchFileSummary), false);
        else if (records.Count > 0 && visibleCount == 0)
            _recordState.Set(new StateMessage("没有匹配的记录", "调整筛选或搜索条件；汇总仍按本批全部记录计算", null, null, null), false);
        _recordState.Visible = statePanelVisible;
        _recordStateWanted = statePanelVisible;
        if (statePanelVisible)
        {
            // Use the computed business state, never Control.Visible's effective getter:
            // while the form is still unshown the getter is false for every control and the
            // old guard skipped LayoutRecordState/BringToFront, leaving the panel behind the
            // grid (real-screen blank centre in the initial states, P1).
            LayoutRecordState();
            _recordState.BringToFront();
        }

        _footer.Text = records.Count == 0 ? "共 0 条"
            : _visible.Count != records.Count ? $"显示 {_visible.Count} 条，共 {records.Count} 条 · 重复记录置顶"
            : $"显示 {_visible.Count} 条，共 {records.Count} 条 · 重复记录置顶";
        _pageLabel.Text = $"{_page}";
        // Design: with no data the page number uses the disabled style too.
        _pageLabel.Enabled = records.Count > 0;
        _pageLabel.Invalidate();
        _previous.Enabled = _page > 1;
        _next.Enabled = _page < PageCount();
        if (_pageSize.SelectedIndex < 0) _pageSize.SelectedItem = "50 条";
        UpdateTitleBlock();
    }

    private void LayoutRecordState()
    {
        if (_gridHost is null || _recordState is null || _grid is null) return;
        // Correct z-order even before the host has a real size: LayoutRecordState also runs
        // from the constructor when ClientSize is still 0, and the panel must already be on
        // top for the first shown frame (P1).
        if (_recordStateWanted) _recordState.BringToFront();
        if (_gridHost.ClientSize.Width <= 0) return;
        var header = _grid.ColumnHeadersHeight;
        _recordState.Bounds = new Rectangle(0, header, _gridHost.ClientSize.Width, Math.Max(0, _gridHost.ClientSize.Height - header));
        if (_dropBanner is null) return;
        var bannerWidth = DpiLayout.Scale(420, _layoutDpi);
        _dropBanner.Bounds = new Rectangle(Math.Max(0, (_gridHost.ClientSize.Width - bannerWidth) / 2), DpiLayout.Scale(6, _layoutDpi),
            Math.Min(bannerWidth, _gridHost.ClientSize.Width), DpiLayout.Scale(34, _layoutDpi));
        if (_dropBanner.Parent is null)
        {
            _gridHost.Controls.Add(_dropBanner);
            _dropBanner.BringToFront();
        }
    }

    // ---- grid interactions ----

    private void SelectCellForContextMenu(DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var cell = _grid[e.ColumnIndex, e.RowIndex];
        if (cell.Selected) return;
        _grid.ClearSelection();
        cell.Selected = true;
        _grid.CurrentCell = cell;
    }

    /// <summary>
    /// Extracts the current selection as tab/newline separated text. Reads the real
    /// cell values (no CellFormatting blanks) so No/Amount/Status copy with full text.
    /// </summary>
    internal (int Count, string Text) ExtractCopySelection()
    {
        var selected = _grid.SelectedCells.Cast<DataGridViewCell>()
            .Where(x => x.RowIndex >= 0 && x.ColumnIndex >= 0 && x.Visible)
            .Select(x => (x.RowIndex, x.ColumnIndex, Convert.ToString(x.Value) ?? ""))
            .ToList();
        return (selected.Count, selected.Count == 0 ? "" : FormatCellSelection(selected));
    }

    private void CopySelectedCells()
    {
        var (count, text) = ExtractCopySelection();
        if (count == 0) return;
        try
        {
            Clipboard.SetText(text);
            ShowToast($"已复制 {count} 个单元格 · 制表符分隔，可直接粘贴到 Excel");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            MessageBox.Show("复制失败，请稍后重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static string FormatCellSelection(IEnumerable<(int Row, int Column, string Value)> selected) =>
        string.Join(Environment.NewLine, selected.OrderBy(x => x.Row).ThenBy(x => x.Column)
            .GroupBy(x => x.Row)
            .Select(row => string.Join('\t', row.Select(cell => cell.Value.Replace("\r", " ").Replace("\n", " ")))));

    private void GridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C) { CopySelectedCells(); e.Handled = true; e.SuppressKeyPress = true; }
        else if ((e.Shift && e.KeyCode == Keys.F10) || e.KeyCode == Keys.Apps)
        {
            var cell = _grid.CurrentCell;
            var point = cell is null ? new Point(20, 20) : new Point(
                _grid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, true).Left + 16,
                _grid.GetCellDisplayRectangle(cell.ColumnIndex, cell.RowIndex, true).Bottom - 4);
            _copyMenu.ShowAt(_grid, _grid.PointToScreen(point), _grid.SelectedCells.Count > 0);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private async void GridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].Tag is not DeclarationRecord record) return;
        var column = _grid.Columns[e.ColumnIndex].Name;
        if (column == "Detail") { ShowDetail(record); return; }
        if (column != "Verify" || _session is not null) return;
        if (!record.HasValidDeclarationNo) { ShowToast("未识别出有效报关单号，无法在线核验"); return; }
        await StartVerificationAsync(record);
    }

    private void ShowDetail(DeclarationRecord record)
    {
        using var dialog = new DetailForm(record);
        ModalPresenter.Show(dialog, this);
    }

    private void GridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var column = _grid.Columns[e.ColumnIndex].Name;
        if (column is "Detail" or "Verify") return;
        if (_grid.Rows[e.RowIndex].Tag is not DeclarationRecord record || !File.Exists(record.SourcePath)) return;
        try { Process.Start(new ProcessStartInfo(record.SourcePath) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Write(ex); }
    }
}
