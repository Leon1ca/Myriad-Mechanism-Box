using System.Drawing.Drawing2D;

namespace CustomsClearanceConsole;

internal enum KpiTone { Normal, Danger, Warning, Placeholder, Accent }

internal sealed record KpiCell(string Label, string Value, string Unit, string Note, KpiTone Tone);

/// <summary>2x2 KPI panel from the design spec (section 2.4).</summary>
internal sealed class KpiPanel : RoundedPanel
{
    private KpiCell[] _cells =
    [
        new("本批文件", "—", "", "尚未载入", KpiTone.Placeholder),
        new("重复单号", "—", "", "识别后统计", KpiTone.Placeholder),
        new("需关注", "—", "", "识别后统计", KpiTone.Placeholder),
        new("截图留存", "—", "", "识别后统计", KpiTone.Placeholder)
    ];

    public KpiPanel()
    {
        BackColor = Theme.Surface;
        BorderColor = Theme.Border;
        Radius = 8;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(IReadOnlyList<KpiCell> cells)
    {
        if (cells.Count == 4) _cells = cells.ToArray();
        Invalidate();
    }

    /// <summary>
    /// Minimum 96-DPI logical height for the 2x2 grid such that each cell's label, value and
    /// note all fit without overlapping. The old code decided the height from the currency
    /// row count alone, which collapsed the empty/ready/processing states to 126px and made
    /// the note paint over the value (R4-3).
    /// </summary>
    internal static int RequiredHeight(int dpi, bool compact)
    {
        int S(int px) => (int)Math.Round(px * dpi / 96F);
        var padY = compact ? S(10) : S(14);
        var content = compact ? S(60) : S(72);
        return 2 * (content + padY * 2) + S(2);
    }

    private bool CompactMetrics() => Height < RequiredHeight(DeviceDpi, false);

    private (int PadY, int LabelH, int ValueH, int NoteH) Metrics()
    {
        int S(int px) => (int)Math.Round(px * DeviceDpi / 96F);
        // Non-compact metrics need the full RequiredHeight(false); anything shorter uses the
        // compact metrics so label/value/note can never overlap (R4-3).
        var compact = CompactMetrics();
        return compact ? (S(10), S(16), S(28), S(16)) : (S(14), S(20), S(34), S(18));
    }

    /// <summary>Per-cell label/value/note rectangles exactly as painted; used by the geometry regression.</summary>
    internal (Rectangle Label, Rectangle Value, Rectangle Note)[] CellTextRectsForTest()
    {
        var (padY, labelH, valueH, noteH) = Metrics();
        var padX = CompactMetrics() ? S16() : S18();
        var columnWidth = (Width - S2()) / 2;
        var rowHeight = (Height - S2()) / 2;
        var result = new (Rectangle, Rectangle, Rectangle)[4];
        for (var i = 0; i < 4; i++)
        {
            var column = i % 2;
            var row = i / 2;
            var cell = new Rectangle(column * columnWidth + padX, row * rowHeight + padY, columnWidth - padX * 2, rowHeight - padY * 2);
            result[i] = (
                new Rectangle(cell.X, cell.Y, cell.Width, labelH),
                new Rectangle(cell.X, cell.Y + labelH, cell.Width, valueH),
                new Rectangle(cell.X, cell.Y + labelH + valueH, cell.Width, noteH));
        }
        return result;
    }

    private int S2() => (int)Math.Round(2 * DeviceDpi / 96F);
    private int S16() => (int)Math.Round(16 * DeviceDpi / 96F);
    private int S18() => (int)Math.Round(18 * DeviceDpi / 96F);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var (padY, labelH, valueH, noteH) = Metrics();
        int S(int px) => (int)Math.Round(px * DeviceDpi / 96F);
        var compact = CompactMetrics();
        var padX = compact ? S(16) : S(18);
        var columnWidth = (Width - S(2)) / 2;
        var rowHeight = (Height - S(2)) / 2;
        using var divider = new Pen(Theme.Divider);
        graphics.DrawLine(divider, columnWidth, S(10), columnWidth, Height - S(10));
        graphics.DrawLine(divider, S(10), rowHeight, Width - S(10), rowHeight);
        for (var i = 0; i < 4; i++)
        {
            var column = i % 2;
            var row = i / 2;
            var cell = new Rectangle(column * columnWidth + padX, row * rowHeight + padY, columnWidth - padX * 2, rowHeight - padY * 2);
            var item = _cells[i];
            using var labelFont = Theme.UiFont(compact ? 12.5F : 13F);
            TextRenderer.DrawText(graphics, item.Label, labelFont, new Rectangle(cell.X, cell.Y, cell.Width, labelH), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var valueColor = item.Tone switch
            {
                KpiTone.Danger => Theme.Danger,
                KpiTone.Warning => Theme.Warning,
                KpiTone.Placeholder => UiTokens.Colors.KpiPlaceholder,
                KpiTone.Accent => Theme.Accent,
                _ => Theme.Text
            };
            using var valueFont = Theme.UiFont(compact ? 22F : 26F, FontStyle.Bold);
            var valueWidth = TextRenderer.MeasureText(graphics, item.Value, valueFont, new Size(int.MaxValue, valueH), TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(graphics, item.Value, valueFont, new Rectangle(cell.X, cell.Y + labelH, Math.Max(S(20), valueWidth + S(2)), valueH), valueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(item.Unit))
            {
                using var unitFont = Theme.UiFont(14F);
                TextRenderer.DrawText(graphics, item.Unit, unitFont, new Rectangle(cell.X + valueWidth + S(6), cell.Y + labelH, Math.Max(0, cell.Width - valueWidth - S(6)), valueH), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (!string.IsNullOrEmpty(item.Note))
            {
                using var noteFont = Theme.UiFont(12F);
                TextRenderer.DrawText(graphics, item.Note, noteFont, new Rectangle(cell.X, cell.Y + labelH + valueH, cell.Width, noteH), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }
    }
}

internal sealed record MoneySummaryRow(string Currency, int BeforeCount, int AfterCount, decimal Gross, decimal Net);

/// <summary>Unconfirmed total for one currency. Amounts are never summed across currencies.</summary>
internal sealed record UnconfirmedRow(string Currency, decimal Amount, int Count, string File);

internal sealed record MoneySummarySnapshot(
    IReadOnlyList<MoneySummaryRow> Rows,
    IReadOnlyList<UnconfirmedRow> Unconfirmed);

/// <summary>Currency summary with per-currency counts, gross/net/deduction and per-currency unconfirmed notice.</summary>
internal sealed class MoneySummaryPanel : RoundedPanel
{
    private MoneySummarySnapshot _snapshot = new([], []);
    private bool _updatingScroll;

    public MoneySummaryPanel()
    {
        BackColor = Theme.Surface;
        BorderColor = Theme.Border;
        Radius = 8;
        AutoScroll = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public int RowCount => _snapshot.Rows.Count;
    public int UnconfirmedRowCount => _snapshot.Unconfirmed.Count;

    public void Set(MoneySummarySnapshot snapshot)
    {
        _snapshot = snapshot;
        UpdateScrollExtent();
        Invalidate();
    }

    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96F);

    /// <summary>Single source of truth for the panel metrics, shared with the scroll test hook.</summary>
    private (int HeaderHeight, int ColumnHeaderHeight, int RowHeight, int PadX, int BodyTop) Metrics()
    {
        var compact = Height < S(120);
        var headerHeight = compact ? S(36) : S(40);
        var columnHeaderHeight = compact ? S(28) : S(30);
        var rowHeight = compact ? S(28) : S(32);
        return (headerHeight, columnHeaderHeight, rowHeight, S(16), headerHeight + columnHeaderHeight);
    }

    internal int BodyTopForTest => Metrics().BodyTop;

    /// <summary>
    /// Row rectangle exactly as OnPaint positions it at the current scroll offset. Used by
    /// the native regression to prove GDI text actually followed the scrollbar.
    /// </summary>
    internal Rectangle RowRectForTest(int index)
    {
        var (_, _, rowHeight, padX, bodyTop) = Metrics();
        return new Rectangle(padX, bodyTop + AutoScrollPosition.Y + index * rowHeight, S(80), rowHeight);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollExtent();
    }

    /// <summary>Content taller than the panel becomes scrollable so no currency is silently dropped.</summary>
    private void UpdateScrollExtent()
    {
        if (_updatingScroll) return;
        _updatingScroll = true;
        try
        {
            var (headerHeight, columnHeaderHeight, rowHeight, _, _) = Metrics();
            var rows = Math.Max(1, _snapshot.Rows.Count);
            var unconfirmed = _snapshot.Unconfirmed.Count > 0 ? S(26) + _snapshot.Unconfirmed.Count * S(26) : 0;
            AutoScrollMinSize = new Size(0, headerHeight + columnHeaderHeight + rows * rowHeight + unconfirmed + S(8));
        }
        finally { _updatingScroll = false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var (headerHeight, columnHeaderHeight, rowHeight, padX, bodyTop) = Metrics();

        // Scrollable body: currency rows plus the per-currency unconfirmed block.
        // Overflow becomes a real scrollbar instead of silently dropping currencies.
        if (_snapshot.Rows.Count > 0 || _snapshot.Unconfirmed.Count > 0)
        {
            var state = graphics.Save();
            graphics.SetClip(new Rectangle(0, bodyTop, Width, Math.Max(0, Height - bodyTop)));
            // TextRenderer paints through GDI and ignores a GDI+ TranslateTransform, so
            // the scroll offset is folded into every rectangle instead of the graphics
            // transform. Text, background strips and icons therefore scroll together all
            // the way down to the last currency.
            var y = bodyTop + AutoScrollPosition.Y;
            using (var rowFont = Theme.MonoFont(13.5F, true))
            {
                foreach (var row in _snapshot.Rows)
                {
                    var rowColumns = ColumnRects(S(80), S(110), y, rowHeight);
                    TextRenderer.DrawText(graphics, row.Currency, rowFont, rowColumns[0], Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(graphics, $"{row.BeforeCount} → {row.AfterCount}", rowFont, rowColumns[1], Theme.Ink2, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(graphics, row.Gross.ToString("N2"), rowFont, rowColumns[2], Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(graphics, row.Net.ToString("N2"), rowFont, rowColumns[3], Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    var deduction = row.Net - row.Gross;
                    if (deduction == 0)
                        TextRenderer.DrawText(graphics, "—", rowFont, rowColumns[4], Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    else
                        TextRenderer.DrawText(graphics, deduction.ToString("N2"), rowFont, rowColumns[4], Theme.Danger, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    y += rowHeight;
                }
            }
            if (_snapshot.Unconfirmed.Count > 0)
            {
                y += S(4);
                var header = new Rectangle(1, y, Width - 2, S(26));
                using (var brush = new SolidBrush(UiTokens.Status.AttentionNotice)) graphics.FillRectangle(brush, header);
                var icon = UiV2Icons.Load(Ui2.Alert, 16, DeviceDpi);
                if (icon is not null)
                {
                    graphics.DrawImage(icon, padX, header.Y + (header.Height - icon.Height) / 2, icon.Width, icon.Height);
                    icon.Dispose();
                }
                using (var noticeFont = Theme.UiFont(12F))
                    TextRenderer.DrawText(graphics, "未确认金额（按币种分列，不计入确认合计）", noticeFont, new Rectangle(padX + S(22), header.Y, Width - padX * 2 - S(22), header.Height), Theme.Warning,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += S(26);
                using var noteFont = Theme.UiFont(12F);
                foreach (var item in _snapshot.Unconfirmed)
                {
                    var text = $"{item.Currency} {item.Amount:N2} · {item.Count} 项未确认" +
                               (string.IsNullOrWhiteSpace(item.File) ? "" : $" · {item.File}");
                    TextRenderer.DrawText(graphics, text, noteFont, new Rectangle(padX + S(22), y, Width - padX * 2 - S(22), S(24)), Theme.Warning,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    y += S(26);
                }
            }
            graphics.Restore(state);
        }
        else
        {
            DrawEmptyState(graphics, S, bodyTop);
        }

        // Fixed header (title, unit, column headers) is never scrolled away.
        using (var background = new SolidBrush(Theme.Surface))
            graphics.FillRectangle(background, 1, 0, Width - 2, bodyTop);
        using (var titleFont = Theme.UiFont(14F, FontStyle.Bold))
            TextRenderer.DrawText(graphics, "金额汇总", titleFont, new Rectangle(padX, 0, S(90), headerHeight), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using (var noteFont = Theme.UiFont(12F))
            TextRenderer.DrawText(graphics, "按币种分别合计，不做汇率换算", noteFont, new Rectangle(padX + S(86), 0, S(260), headerHeight), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using (var unitFont = Theme.UiFont(12F))
            TextRenderer.DrawText(graphics, "单位：原币", unitFont, new Rectangle(Width - S(100), 0, S(100) - padX, headerHeight), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var columns = ColumnRects(S(80), S(110), headerHeight, columnHeaderHeight);
        using (var background = new SolidBrush(Theme.PanelSubtle))
            graphics.FillRectangle(background, 1, headerHeight, Width - 2, columnHeaderHeight);
        using (var headerFont = Theme.UiFont(12.5F, FontStyle.Bold))
        {
            DrawHeader(graphics, headerFont, columns[0], "币种", ContentAlignment.MiddleLeft);
            DrawHeader(graphics, headerFont, columns[1], "份数 前→后", ContentAlignment.MiddleLeft);
            DrawHeader(graphics, headerFont, columns[2], "去重前", ContentAlignment.MiddleRight);
            DrawHeader(graphics, headerFont, columns[3], "去重后（计入）", ContentAlignment.MiddleRight);
            DrawHeader(graphics, headerFont, columns[4], "重复扣减", ContentAlignment.MiddleRight);
        }
    }

    private Rectangle[] ColumnRects(int currencyWidth, int countWidth, int y, int height)
    {
        var padX = (int)Math.Round(16 * DeviceDpi / 96F);
        var gap = (int)Math.Round(12 * DeviceDpi / 96F);
        var available = Width - padX * 2 - currencyWidth - countWidth - gap * 4;
        var third = Math.Max(60, available / 3);
        var widths = new[] { currencyWidth, countWidth, third, third, available - third * 2 };
        var rects = new Rectangle[5];
        var x = padX;
        for (var i = 0; i < 5; i++)
        {
            rects[i] = new Rectangle(x, y, widths[i], height);
            x += widths[i] + gap;
        }
        return rects;
    }

    private static void DrawHeader(Graphics graphics, Font font, Rectangle column, string text, ContentAlignment alignment) =>
        TextRenderer.DrawText(graphics, text, font, column, Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            (alignment == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left));

    private void DrawEmptyState(Graphics graphics, Func<int, int> S, int top)
    {
        // Plain white body: no placeholder stripes behind the empty message.
        var icon = UiV2Icons.Load(Ui2.AmountEmpty, 24, DeviceDpi);
        using var titleFont = Theme.UiFont(13F, FontStyle.Bold);
        using var noteFont = Theme.UiFont(12F);
        const string title = "暂无金额汇总";
        const string note = "识别完成后按币种分别合计";
        var titleWidth = TextRenderer.MeasureText(graphics, title, titleFont, new Size(int.MaxValue, 40), TextFormatFlags.NoPadding).Width;
        var noteWidth = TextRenderer.MeasureText(graphics, note, noteFont, new Size(int.MaxValue, 40), TextFormatFlags.NoPadding).Width;
        var textWidth = Math.Max(titleWidth, noteWidth);
        var blockWidth = textWidth + S(66);
        var blockHeight = S(56);
        var centerY = top + (Height - top) / 2;
        var block = new Rectangle((Width - blockWidth) / 2, centerY - blockHeight / 2, blockWidth, blockHeight);
        using (var white = new SolidBrush(Color.White)) graphics.FillRectangle(white, block);
        if (icon is not null)
        {
            // Design icon block: 40x40, #F2F4F7 fill, 1px #E3E7ED border, rounded.
            var iconBlock = new Rectangle(block.X, block.Y + S(6), S(40), S(40));
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedPath(iconBlock, S(8)))
            using (var iconBack = new SolidBrush(ColorTranslator.FromHtml("#F2F4F7")))
            using (var iconBorder = new Pen(ColorTranslator.FromHtml("#E3E7ED")))
            {
                graphics.FillPath(iconBack, path);
                graphics.DrawPath(iconBorder, path);
            }
            graphics.SmoothingMode = smoothing;
            graphics.DrawImage(icon, block.X + S(8), block.Y + S(14), icon.Width, icon.Height);
            icon.Dispose();
        }
        TextRenderer.DrawText(graphics, title, titleFont, new Rectangle(block.X + S(50), block.Y + S(6), textWidth, S(22)), Theme.Ink2,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, note, noteFont, new Rectangle(block.X + S(50), block.Y + S(28), textWidth, S(20)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed record FilterSegment(string Name, string AccessibleName, int Count, bool Enabled, bool Selected);

/// <summary>Segmented filter control (全部 / 正常 / 重复 / 需关注 with counts).</summary>
internal sealed class FilterSegmented : Control
{
    private IReadOnlyList<FilterSegment> _segments = [];
    private readonly List<Rectangle> _bounds = [];
    private int _hover = -1;

    public event EventHandler<int>? SegmentSelected;

    public FilterSegmented()
    {
        Cursor = Cursors.Hand;
        TabStop = false;
        AccessibleName = "记录筛选";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(IReadOnlyList<FilterSegment> segments)
    {
        _segments = segments;
        Invalidate();
    }

    /// <summary>
    /// Width the segments need at the control's DPI (design: padding 3, items spaced 2). The
    /// toolbar column is sized to this so the last segment ("需关注") no longer absorbs all the
    /// leftover width and looks larger than the others.
    /// </summary>
    public int ContentWidth(int dpi)
    {
        int S(int px) => (int)Math.Round(px * dpi / 96F);
        var scale = dpi / DpiLayout.SystemDpi;
        using var labelFont = Theme.UiFont(13F * scale, FontStyle.Bold);
        using var countFont = Theme.MonoFont(12F * scale);
        var total = S(3) * 2;
        for (var i = 0; i < _segments.Count; i++)
        {
            var labelWidth = TextRenderer.MeasureText(_segments[i].Name, labelFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
            var countWidth = TextRenderer.MeasureText(_segments[i].Count.ToString(), countFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
            total += labelWidth + countWidth + S(30) + (i > 0 ? S(2) : 0);
        }
        return total;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var next = HitTest(e.Location);
        if (next != _hover) { _hover = next; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var index = HitTest(e.Location);
        if (index >= 0 && _segments[index].Enabled) SegmentSelected?.Invoke(this, index);
    }

    private int HitTest(Point point)
    {
        for (var i = 0; i < _bounds.Count; i++) if (_bounds[i].Contains(point)) return i;
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var dpi = DeviceDpi / 96F;
        int S(int px) => (int)Math.Round(px * dpi);
        graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        _bounds.Clear();
        var pad = S(3);
        var widths = new int[_segments.Count];
        using var labelFont = Theme.UiFont(13F, FontStyle.Bold);
        using var countFont = Theme.MonoFont(12F);
        var total = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var labelWidth = TextRenderer.MeasureText(graphics, segment.Name, labelFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
            var countWidth = TextRenderer.MeasureText(graphics, segment.Count.ToString(), countFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
            widths[i] = labelWidth + countWidth + S(30);
            total += widths[i];
        }
        // The track wraps the segments exactly; every segment keeps its own width.
        var trackWidth = Math.Min(Width, pad * 2 + total + S(2) * Math.Max(0, _segments.Count - 1));
        using (var background = Theme.RoundedPath(new RectangleF(0, 0, trackWidth - 1, Height - 1), S(7)))
        using (var brush = new SolidBrush(Theme.SegmentBg)) graphics.FillPath(brush, background);
        var x = pad;
        using var selectedFont = Theme.UiFont(13F, FontStyle.Bold);
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            var width = widths[i];
            var rect = new Rectangle(x, pad, width, Height - pad * 2);
            _bounds.Add(rect);
            if (segment.Selected || i == _hover)
            {
                using var path = Theme.RoundedPath(new RectangleF(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), S(6));
                using var fill = new SolidBrush(segment.Selected ? Color.White : Color.FromArgb(90, Color.White));
                graphics.FillPath(fill, path);
            }
            var labelColor = !segment.Enabled ? Theme.DisabledText : segment.Selected ? Theme.Primary : Theme.Ink2;
            var labelWidth = TextRenderer.MeasureText(graphics, segment.Name, labelFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(graphics, segment.Name, selectedFont, new Rectangle(rect.X + S(12), rect.Y, labelWidth + S(4), rect.Height), labelColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var countColor = !segment.Enabled ? Theme.DisabledText
                : segment.Name == "重复" ? Theme.Danger
                : segment.Name == "需关注" ? Theme.Warning
                : Theme.Muted;
            TextRenderer.DrawText(graphics, segment.Count.ToString(), countFont, new Rectangle(rect.X + S(12) + labelWidth + S(5), rect.Y, rect.Width - labelWidth - S(20), rect.Height), countColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += width + S(2);
        }
    }
}

internal sealed record StateMessage(string Title, string Subtitle, string? IconStem, string? PrimaryAction, string? Secondary);

/// <summary>Empty / unloaded / ready / no-result / processing body inside the records panel.</summary>
internal sealed class RecordStatePanel : Control
{
    private StateMessage _message = new("尚未载入关单", "在设置中选择关单目录，或将 PDF / 图片拖入此处", Ui2.FolderUpload, "选择关单目录", null);
    private bool _dropZone;
    private readonly List<(Rectangle Bounds, string Action)> _buttons = [];
    private int _hover = -1;

    public event EventHandler<string>? ActionInvoked;

    public RecordStatePanel()
    {
        BackColor = Theme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(StateMessage message, bool dropZone)
    {
        _message = message;
        _dropZone = dropZone;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var next = -1;
        for (var i = 0; i < _buttons.Count; i++) if (_buttons[i].Bounds.Contains(e.Location)) { next = i; break; }
        if (next != _hover) { _hover = next; Cursor = next >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Cursor = Cursors.Default; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        for (var i = 0; i < _buttons.Count; i++)
            if (_buttons[i].Bounds.Contains(e.Location)) { ActionInvoked?.Invoke(this, _buttons[i].Action); return; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var dpi = DeviceDpi / 96F;
        int S(int px) => (int)Math.Round(px * dpi);
        _buttons.Clear();
        if (_dropZone)
        {
            var zone = new Rectangle(S(16), S(16), Width - S(32), Height - S(32));
            using var zonePath = Theme.RoundedPath(zone, S(8));
            using var zoneFill = new SolidBrush(UiTokens.Colors.DropZoneBg);
            graphics.FillPath(zoneFill, zonePath);
            using var dash = new Pen(UiTokens.Colors.DropZoneBorder, 1.5F * DeviceDpi / 96F) { DashStyle = DashStyle.Dash };
            graphics.DrawPath(dash, zonePath);
        }

        var iconSize = S(56);
        using var titleFont = Theme.UiFont(16F, FontStyle.Bold);
        using var subtitleFont = Theme.UiFont(13F);
        var titleWidth = TextRenderer.MeasureText(graphics, _message.Title, titleFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
        var subtitleWidth = TextRenderer.MeasureText(graphics, _message.Subtitle, subtitleFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
        var actionHeight = _message.PrimaryAction is null ? 0 : S(38);
        var blockHeight = iconSize + S(14) + S(26) + S(8) + S(22) + (actionHeight > 0 ? S(16) + actionHeight : 0);
        var top = Math.Max(S(24), (Height - blockHeight) / 2);
        var centerX = Width / 2;
        if (_message.IconStem is not null)
        {
            var iconBlock = new Rectangle(centerX - iconSize / 2, top, iconSize, iconSize);
            using (var path = Theme.RoundedPath(iconBlock, S(8)))
            using (var brush = new SolidBrush(Theme.AccentSoft)) graphics.FillPath(brush, path);
            var icon = UiV2Icons.Load(_message.IconStem, 28, DeviceDpi);
            if (icon is not null)
            {
                graphics.DrawImage(icon, iconBlock.X + (iconSize - icon.Width) / 2, iconBlock.Y + (iconSize - icon.Height) / 2, icon.Width, icon.Height);
                icon.Dispose();
            }
        }
        TextRenderer.DrawText(graphics, _message.Title, titleFont, new Rectangle(0, top + iconSize + S(14), Width, S(26)), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, _message.Subtitle, subtitleFont, new Rectangle(0, top + iconSize + S(42), Width, S(22)), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (_message.PrimaryAction is not null)
        {
            int buttonTextWidth;
            using (var measureFont = Theme.UiFont(14F, FontStyle.Bold))
                buttonTextWidth = TextRenderer.MeasureText(graphics, _message.PrimaryAction, measureFont, new Size(int.MaxValue, actionHeight), TextFormatFlags.NoPadding).Width;
            var buttonWidth = Math.Max(S(150), buttonTextWidth + S(54));
            var button = new Rectangle(centerX - buttonWidth / 2, top + iconSize + S(78), buttonWidth, actionHeight);
            var hovered = _hover == 0;
            using (var path = Theme.RoundedPath(button, S(6)))
            using (var fill = new SolidBrush(hovered ? Theme.HeaderBg : Theme.Primary)) graphics.FillPath(fill, path);
            var icon = UiV2Icons.Load(Ui2.Folder, 16, DeviceDpi);
            var iconWidth = icon?.Width ?? 0;
            using var actionFont = Theme.UiFont(14F);
            var actionWidth = TextRenderer.MeasureText(graphics, _message.PrimaryAction, actionFont, new Size(int.MaxValue, actionHeight), TextFormatFlags.NoPadding).Width;
            var gap = iconWidth > 0 ? S(8) : 0;
            var contentWidth = iconWidth + gap + actionWidth;
            var contentX = button.X + (button.Width - contentWidth) / 2;
            if (icon is not null)
            {
                graphics.DrawImage(icon, contentX, button.Y + (button.Height - icon.Height) / 2, icon.Width, icon.Height);
                icon.Dispose();
            }
            TextRenderer.DrawText(graphics, _message.PrimaryAction, actionFont, new Rectangle(contentX + iconWidth + gap, button.Y, actionWidth + S(2), button.Height), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            _buttons.Add((button, _message.PrimaryAction));
        }
    }
}

/// <summary>Bottom-centre toast used for copy and export feedback.</summary>
internal sealed class ToastControl : Control
{
    private string _message = "";
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2600 };

    public ToastControl()
    {
        Visible = false;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _timer.Tick += (_, _) => { _timer.Stop(); Visible = false; };
    }

    public void Show(string message)
    {
        _message = message;
        var dpi = DeviceDpi / 96F;
        int textWidth;
        using (var measureFont = Theme.UiFont(12.5F)) textWidth = TextRenderer.MeasureText(message, measureFont).Width;
        var width = textWidth + (int)(70 * dpi);
        Size = new Size(Math.Min(width, (Parent?.Width ?? 800) - 40), (int)(34 * dpi));
        Visible = true;
        BringToFront();
        _timer.Stop();
        _timer.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var dpi = DeviceDpi / 96F;
        int S(int px) => (int)Math.Round(px * dpi);
        using (var path = Theme.RoundedPath(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F))
        using (var fill = new SolidBrush(UiTokens.Colors.ToastBg)) graphics.FillPath(fill, path);
        using var checkPen = new Pen(ColorTranslator.FromHtml("#8FD1B4"), Math.Max(1.6F, S(2)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var cx = S(20);
        var cy = Height / 2F;
        graphics.DrawLines(checkPen, [new PointF(cx - S(5), cy), new PointF(cx - S(1), cy + S(4)), new PointF(cx + S(6), cy - S(5))]);
        using var font = Theme.UiFont(12.5F);
        TextRenderer.DrawText(graphics, _message, font, new Rectangle(S(36), 0, Width - S(46), Height), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Title row block: page title + business status tag + secondary line.</summary>
internal sealed class TitleBlock : Control
{
    private string _title = "本批关单";
    private string _status = "";
    private UiTokens.StatusPalette? _palette;
    private string _subline = "";
    private bool _sublineMono;

    public TitleBlock()
    {
        BackColor = Theme.Canvas;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Set(string title, string status, UiTokens.StatusPalette? palette, string subline, bool sublineMono)
    {
        _title = title;
        _status = status;
        _palette = palette;
        _subline = subline;
        _sublineMono = sublineMono;
        Invalidate();
    }

    /// <summary>Real painted values, used by the geometry/state evidence regression.</summary>
    internal string StatusForTest => _status;
    internal string SublineForTest => _subline;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96f);
        using var titleFont = Theme.UiFont(22F, FontStyle.Bold);
        TextRenderer.DrawText(graphics, _title, titleFont, new Rectangle(0, S(2), Width, S(30)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var titleWidth = TextRenderer.MeasureText(graphics, _title, titleFont, new Size(int.MaxValue, S(30)), TextFormatFlags.NoPadding).Width;
        if (_palette is { } palette && _status.Length > 0)
        {
            using var tagFont = Theme.UiFont(12F, FontStyle.Bold);
            var textWidth = TextRenderer.MeasureText(graphics, _status, tagFont, new Size(int.MaxValue, S(22)), TextFormatFlags.NoPadding).Width;
            var tag = new Rectangle(titleWidth + S(12), S(6), textWidth + S(26), S(22));
            using (var path = Theme.RoundedPath(tag, S(4)))
            using (var fill = new SolidBrush(palette.Bg)) graphics.FillPath(fill, path);
            var icon = UiV2Icons.Load(_status.Contains("识别中") ? Ui2.Info : _status.Contains("未载入") ? Ui2.Minus : _status.Contains("待识别") ? Ui2.Info : Ui2.CheckGreen, 16, dpi);
            var iconWidth = icon?.Width ?? 0;
            if (icon is not null)
            {
                graphics.DrawImage(icon, tag.X + S(6), tag.Y + (tag.Height - icon.Height) / 2, icon.Width, icon.Height);
                icon.Dispose();
            }
            TextRenderer.DrawText(graphics, _status, tagFont, new Rectangle(tag.X + S(6) + iconWidth + (iconWidth > 0 ? S(4) : 0), tag.Y, tag.Width - S(10) - iconWidth, tag.Height), palette.Fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using var subFont = _sublineMono ? Theme.MonoFont(12.5F) : Theme.UiFont(13F);
        TextRenderer.DrawText(graphics, _subline, subFont, new Rectangle(0, S(34), Width, S(20)), Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Recognition progress strip (design 08-processing).</summary>
internal sealed class ProgressStrip : RoundedPanel
{
    public ProgressStrip()
    {
        BackColor = Theme.Surface;
        BorderColor = Theme.Border;
        Radius = 8;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public int Done { get; set; }
    public int Total { get; set; }
    public string FileName { get; set; } = "";
    public int Completed { get; set; }
    public int Attention { get; set; }
    public int Failed { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96f);
        using (var bigFont = Theme.MonoFont(28F, true))
            TextRenderer.DrawText(graphics, $"{Done} / {Total}", bigFont, new Rectangle(S(20), S(8), S(180), S(46)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using (var fileFont = Theme.UiFont(14F))
            TextRenderer.DrawText(graphics, $"当前文件 {FileName}", fileFont, new Rectangle(S(200), S(8), Width - S(220), S(46)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var countsFont = Theme.UiFont(13F))
            TextRenderer.DrawText(graphics, $"完成 {Completed} · 需关注 {Attention} · 失败 {Failed}", countsFont, new Rectangle(Width - S(330), S(8), S(310), S(46)), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var bar = new Rectangle(S(20), Height - S(22), Width - S(40), S(8));
        using (var path = Theme.RoundedPath(bar, S(4)))
        using (var background = new SolidBrush(ColorTranslator.FromHtml("#DCE6F3"))) graphics.FillPath(background, path);
        var ratio = Total <= 0 ? 0F : Math.Clamp(Done / (float)Total, 0F, 1F);
        var fillWidth = Math.Max(S(8), (int)(bar.Width * ratio));
        using (var path = Theme.RoundedPath(new Rectangle(bar.X, bar.Y, fillWidth, bar.Height), S(4)))
        using (var fill = new SolidBrush(Theme.Accent)) graphics.FillPath(fill, path);
    }
}

/// <summary>Lock banner shown while recognition is running (design 08-processing).</summary>
internal sealed class LockBar : RoundedPanel
{
    public LockBar()
    {
        BackColor = ColorTranslator.FromHtml("#E9EDF3");
        BorderColor = ColorTranslator.FromHtml("#E9EDF3");
        Radius = 8;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96f);
        var icon = UiV2Icons.Load(Ui2.LockInk, 16, dpi);
        var iconWidth = icon?.Width ?? 0;
        if (icon is not null)
        {
            graphics.DrawImage(icon, S(14), (Height - icon.Height) / 2, icon.Width, icon.Height);
            icon.Dispose();
        }
        using (var font = Theme.UiFont(13F))
            TextRenderer.DrawText(graphics, "识别期间已锁定：目录修改 · 拖入新批次 · 导出 · 三类清理 · 网页核验", font,
                new Rectangle(S(14) + iconWidth + S(8), 0, Width - S(300) - iconWidth, Height), Theme.Ink2,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var noteFont = Theme.UiFont(12.5F))
            TextRenderer.DrawText(graphics, "取消后保留已完成结果；正在进行的 ONNX 推理需等待当前调用返回", noteFont,
                new Rectangle(Width - S(430), 0, S(416) - S(14), Height), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
