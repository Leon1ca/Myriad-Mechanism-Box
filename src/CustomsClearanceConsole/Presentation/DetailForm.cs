using System.Drawing.Drawing2D;
using System.Text;

namespace CustomsClearanceConsole;

/// <summary>Read-only declaration detail dialog (design spec 4.1, 600x480).</summary>
internal sealed class DetailForm : DpiDialog
{
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PopupFrame.ApplyRegion(this, 10, DeviceDpi);
    }

    private readonly DeclarationRecord _record;

    public DetailForm(DeclarationRecord record)
    {
        _record = record;
        Text = $"关单明细 · {record.DeclarationNo}";
        LogicalClientSize = new Size(600, 480);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;

        var frame = new RoundedPanel { Dock = DockStyle.Fill, WindowFrame = true, Radius = 10, BorderColor = Theme.Border, BackColor = Color.White };
        Controls.Add(frame);

        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Color.White };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            var inset = DpiLayout.Scale(20, header.DeviceDpi);
            e.Graphics.DrawLine(pen, inset, header.Height - 1, header.Width - inset, header.Height - 1);
        };
        var title = new Label
        {
            Text = "关单明细",
            Location = new Point(20, 14),
            Size = new Size(110, 26),
            Font = Theme.UiFont(16F, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var number = new Label
        {
            Text = string.IsNullOrWhiteSpace(record.DeclarationNo) ? "未识别" : record.DeclarationNo,
            Location = new Point(126, 14),
            Size = new Size(400, 26),
            Font = Theme.MonoFont(14F, true),
            ForeColor = Theme.Primary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var close = new CircleCloseButton { Location = new Point(544, 12) };
        close.AccessibleName = "关闭关单明细";
        close.Click += (_, _) => Close();
        var subtitle = new Label
        {
            Text = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(record.Consignee) ? "未识别收货人" : record.Consignee,
                string.IsNullOrWhiteSpace(record.ContractNo) ? "合同未识别" : $"合同 {record.ContractNo}",
                record.SourceName
            }),
            Location = new Point(20, 44),
            Size = new Size(560, 20),
            Font = Theme.UiFont(12.5F),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        header.Controls.AddRange([title, number, close, subtitle]);

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1, 0, 1, 0) };
        // The amount column is labelled per row with its own currency; there is no single
        // "table currency" because a declaration may mix currencies (never summed together).
        var columnHeader = new DetailColumnHeader("总价");
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        var lineList = new DetailLineList(record) { Dock = DockStyle.Top };
        // Logical pixels like every other constructor value; DpiDialog scales them once.
        lineList.Height = Math.Max(1, record.LineTotals.Count) * 44 + 4;
        scroll.Controls.Add(lineList);

        var currencyRows = ReliableCurrencyTotals(record);
        var footerHeight = 84 + Math.Max(0, currencyRows.Count - 1) * 22;
        var footer = new DetailFooter(record);
        var footerPanel = new Panel { Dock = DockStyle.Bottom, Height = footerHeight, BackColor = UiTokens.Colors.DialogFooter };
        footerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            e.Graphics.DrawLine(pen, 0, 0, footerPanel.Width, 0);
        };
        footer.Dock = DockStyle.Fill;
        footerPanel.Controls.Add(footer);

        content.Controls.Add(scroll);
        content.Controls.Add(columnHeader);
        frame.Controls.Add(content);
        frame.Controls.Add(header);
        frame.Controls.Add(footerPanel);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDialogDrag(); };
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDialogDrag(); };
    }

    private void BeginDialogDrag()
    {
        Capture = false;
        var drag = Message.Create(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        WndProc(ref drag);
    }

    /// <summary>
    /// Determines whether the reliable line items reconcile. The saved total is
    /// derived from the same reliable lines, so the result never claims an
    /// independent "consistent" verdict: it reports the reliable line sum instead.
    /// An empty line set is never reported as consistent.
    /// </summary>
    internal static (bool Consistent, string Message) EvaluateConsistency(DeclarationRecord record)
    {
        if (record.LineTotals.Count == 0)
            return (false, "未保存分项，请重新识别源文件后再核对。");
        var reliable = DeclarationParser.SumReliableLineTotals(record.LineTotals);
        var currencies = record.Totals.Keys.Union(reliable.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var lines = new List<string>();
        var mismatch = false;
        foreach (var currency in currencies)
        {
            var expected = record.Totals.GetValueOrDefault(currency);
            var actual = reliable.GetValueOrDefault(currency);
            if (Math.Abs(expected - actual) > 0.005m)
            {
                mismatch = true;
                lines.Add($"{currency} 可靠分项合计 {actual:N2} 与已保存金额 {expected:N2} 相差 {Math.Abs(expected - actual):N2}");
            }
        }
        var unreliable = record.LineTotals.Where(x => !x.IsReliable).Select(x => x.ItemNo).Where(x => x.Length > 0).ToList();
        if (unreliable.Count > 0)
        {
            var prefix = $"第 {string.Join("、", unreliable)} 项两引擎金额不一致，未计入确认合计。";
            return (false, lines.Count == 0 ? prefix : prefix + string.Join("；", lines));
        }
        if (mismatch) return (false, string.Join("；", lines));
        if (reliable.Count == 0)
            return (false, "没有可确认的分项金额，未形成可靠分项合计。");
        if (record.Status is "识别失败")
            return (false, "识别失败，未形成可核对的分项合计。");
        return (true, "已识别可靠分项合计；该合计由可靠分项求和，未与独立票面总额核对。");
    }

    /// <summary>Reliable per-currency line sums. Currencies are never added together.</summary>
    internal static IReadOnlyList<(string Currency, decimal Amount)> ReliableCurrencyTotals(DeclarationRecord record)
    {
        if (record.LineTotals.Count == 0) return [];
        return DeclarationParser.SumReliableLineTotals(record.LineTotals)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => (x.Key, x.Value))
            .ToList();
    }

    /// <summary>Full per-currency summary text used for tooltips and regression assertions.</summary>
    internal static string CurrencySummaryText(DeclarationRecord record)
    {
        var rows = ReliableCurrencyTotals(record);
        if (rows.Count == 0) return "没有可确认的可靠分项合计";
        return string.Join(" · ", rows.Select(x => $"{x.Currency} {x.Amount:N2}"));
    }

    private sealed class DetailColumnHeader : Control
    {
        private readonly string _amountHeader;

        public DetailColumnHeader(string amountHeader)
        {
            _amountHeader = amountHeader;
            Dock = DockStyle.Top;
            Height = 34;
            BackColor = Theme.PanelSubtle;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var font = Theme.UiFont(12.5F, FontStyle.Bold);
            foreach (var (rect, text, alignment) in Columns(Width, DeviceDpi, 0, Height, _amountHeader))
            {
                TextRenderer.DrawText(e.Graphics, text, font, rect, Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                    (alignment == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left));
            }
            using var pen = new Pen(Theme.Divider);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    private sealed class DetailLineList : Control
    {
        private readonly DeclarationRecord _record;
        private readonly List<DeclarationLineTotal> _ordered;
        private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };
        private int _hover = -1;

        public DetailLineList(DeclarationRecord record)
        {
            _record = record;
            _ordered = record.LineTotals.OrderBy(x => x.PageNumber).ThenBy(x => x.Sequence).ToList();
            BackColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var size = Math.Max(1, (int)Math.Round(44 * DeviceDpi / 96f));
            var index = e.Y / size;
            var next = index >= 0 && index < _ordered.Count ? index : -1;
            if (next != _hover)
            {
                _hover = next;
                // Full product name, quantities and both engines' values stay readable even
                // when the painted column uses an ellipsis.
                _tip.SetToolTip(this, next >= 0 ? DescribeLine(next) : "");
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

        internal string DescribeLine(int index)
        {
            if (index < 0 || index >= _ordered.Count) return "";
            var line = _ordered[index];
            var text = new StringBuilder();
            text.Append("项号 ").Append(string.IsNullOrWhiteSpace(line.ItemNo) ? "—" : line.ItemNo)
                .Append("（第 ").Append(line.PageNumber).Append(" 页）\n");
            text.Append("商品名称：").Append(line.DisplayProduct).Append('\n');
            // The full tooltip always carries the lossless source value so a small
            // quantity or a long unit price can never read as an unmarked zero.
            text.Append("数量 / 单位：").Append(line.ExactQuantityUnit).Append('\n');
            text.Append("单价：").Append(line.ExactUnitPrice).Append('\n');
            text.Append("总价：").Append(string.IsNullOrWhiteSpace(line.Currency) ? "—" : line.Currency).Append(' ').Append(line.Amount.ToString("N2"));
            if (line.VerificationAmount is not null)
                text.Append("\n另一引擎总价：").Append(line.Currency).Append(' ').Append(line.VerificationAmount.Value.ToString("N2"));
            if (line.VerificationQuantity is not null || !string.IsNullOrWhiteSpace(line.VerificationUnit))
            {
                var quantity = line.VerificationQuantity is null ? "—" : NumberFormats.Exact(line.VerificationQuantity.Value);
                var unit = string.IsNullOrWhiteSpace(line.VerificationUnit) ? "" : " " + line.VerificationUnit;
                text.Append("\n另一引擎数量 / 单位：").Append(quantity).Append(unit);
            }
            if (line.VerificationUnitPrice is not null)
                text.Append("\n另一引擎单价：").Append(NumberFormats.Exact(line.VerificationUnitPrice.Value));
            if (!line.IsReliable) text.Append("\n该行两引擎金额不一致，未计入确认合计。");
            if (!string.IsNullOrWhiteSpace(line.Note)) text.Append("\n说明：").Append(line.Note);
            return text.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var dpi = DeviceDpi;
            int S(int px) => (int)Math.Round(px * dpi / 96f);
            var rowHeight = S(44);
            if (_ordered.Count == 0)
            {
                using var emptyFont = Theme.UiFont(13F);
                TextRenderer.DrawText(graphics, "未保存分项，请重新识别源文件。", emptyFont, ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            using var numberFont = Theme.MonoFont(13F, true);
            using var nameFont = Theme.UiFont(13.5F);
            using var amountFont = Theme.MonoFont(13F, true);
            for (var i = 0; i < _ordered.Count; i++)
            {
                var line = _ordered[i];
                var y = i * rowHeight;
                if (i == _hover)
                {
                    using var hover = new SolidBrush(UiTokens.Status.RowHover);
                    graphics.FillRectangle(hover, 0, y, Width, rowHeight);
                }
                if (!line.IsReliable)
                {
                    using var warn = new SolidBrush(UiTokens.Status.Attention.Row);
                    graphics.FillRectangle(warn, 0, y, Width, rowHeight);
                }
                using (var divider = new Pen(Theme.Divider))
                    graphics.DrawLine(divider, S(20), y + rowHeight - 1, Width - S(20), y + rowHeight - 1);
                var columns = Columns(Width, dpi, y, rowHeight, "总价");
                TextRenderer.DrawText(graphics, line.ItemNo, numberFont, columns[0].Rect, Theme.Ink2, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(graphics, line.DisplayProduct, nameFont, columns[1].Rect, Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(graphics, line.DisplayQuantityUnit, numberFont, columns[2].Rect, Theme.Ink2, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(graphics, line.DisplayUnitPrice, numberFont, columns[3].Rect, Theme.Ink2, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(graphics, string.IsNullOrWhiteSpace(line.Currency) ? "—" : line.Currency, numberFont, columns[4].Rect, Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var amountColor = line.IsReliable ? Theme.Text : Theme.Warning;
                TextRenderer.DrawText(graphics, line.Amount.ToString("N2"), amountFont, columns[5].Rect, amountColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (!line.IsReliable)
                {
                    var underlineY = y + rowHeight / 2 + S(11);
                    using var underline = new Pen(UiTokens.Status.AttentionUnderline, S(1)) { DashStyle = DashStyle.Dash };
                    graphics.DrawLine(underline, columns[5].Rect.Right - S(74), underlineY, columns[5].Rect.Right, underlineY);
                    if (line.VerificationAmount is not null)
                    {
                        using var noteFont = Theme.UiFont(11F);
                        TextRenderer.DrawText(graphics, $"另一引擎 {line.VerificationAmount.Value:N2}", noteFont,
                            new Rectangle(columns[5].Rect.X, y + rowHeight / 2 + S(8), columns[5].Rect.Width, S(16)), Theme.Warning,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class DetailFooter : Control
    {
        private readonly DeclarationRecord _record;
        private readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 250, ReshowDelay = 100, ShowAlways = true };

        public DetailFooter(DeclarationRecord record)
        {
            _record = record;
            BackColor = UiTokens.Colors.DialogFooter;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            var (_, message) = EvaluateConsistency(record);
            _tip.SetToolTip(this, message + "\n" + CurrencySummaryText(record));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var dpi = DeviceDpi;
            int S(int px) => (int)Math.Round(px * dpi / 96f);
            var (consistent, message) = EvaluateConsistency(_record);
            var rows = ReliableCurrencyTotals(_record);
            using (var labelFont = Theme.UiFont(12.5F))
                TextRenderer.DrawText(graphics, $"共 {_record.LineTotals.Count} 项 · 已识别可靠分项合计（按币种，不换算）", labelFont,
                    new Rectangle(S(20), S(4), Width - S(40), S(20)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            var y = S(24);
            if (rows.Count == 0)
            {
                using var emptyFont = Theme.MonoFont(15F, true);
                TextRenderer.DrawText(graphics, "—", emptyFont, new Rectangle(S(20), y, Width - S(40), S(26)), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            foreach (var (currency, amount) in rows)
            {
                using var currencyFont = Theme.UiFont(12F);
                TextRenderer.DrawText(graphics, currency, currencyFont, new Rectangle(Width - S(190), y, S(50), S(26)), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using var amountFont = Theme.MonoFont(16F, true);
                TextRenderer.DrawText(graphics, amount.ToString("N2"), amountFont, new Rectangle(Width - S(180), y, S(160), S(26)),
                    consistent ? Theme.Text : Theme.Warning,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                y += S(22);
            }
            var messageTop = Height - S(34);
            var icon = UiV2Icons.Load(consistent ? Ui2.CheckGreen : Ui2.Alert, 16, dpi);
            if (icon is not null)
            {
                graphics.DrawImage(icon, S(20), messageTop + S(9), icon.Width, icon.Height);
                icon.Dispose();
            }
            using var messageFont = Theme.UiFont(12F);
            TextRenderer.DrawText(graphics, message, messageFont, new Rectangle(S(42), messageTop, Width - S(62), S(34)),
                consistent ? Theme.Success : Theme.Warning,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Six columns: item, product, quantity/unit, unit price, currency and amount.
    /// The currency column keeps mixed-currency rows unambiguous.
    /// </summary>
    internal static List<(Rectangle Rect, string Text, ContentAlignment Alignment)> Columns(int width, int dpi, int y, int height, string amountHeader)
    {
        int S(int px) => (int)Math.Round(px * dpi / 96f);
        var padX = S(20);
        var gap = S(10);
        var available = width - padX * 2 - gap * 5;
        var number = S(34);
        var currency = S(40);
        var quantity = S(120);
        var unitPrice = S(90);
        var amount = S(120);
        var name = Math.Max(S(80), available - number - currency - quantity - unitPrice - amount);
        var widths = new[] { number, name, quantity, unitPrice, currency, amount };
        var x = padX;
        var result = new List<(Rectangle, string, ContentAlignment)>();
        var headers = new[] { "项号", "商品名称", "数量 / 单位", "单价", "币种", amountHeader };
        var alignments = new[] { ContentAlignment.MiddleLeft, ContentAlignment.MiddleLeft, ContentAlignment.MiddleRight, ContentAlignment.MiddleRight, ContentAlignment.MiddleRight, ContentAlignment.MiddleRight };
        for (var i = 0; i < widths.Length; i++)
        {
            result.Add((new Rectangle(x, y, widths[i], height), headers[i], alignments[i]));
            x += widths[i] + gap;
        }
        return result;
    }
}
