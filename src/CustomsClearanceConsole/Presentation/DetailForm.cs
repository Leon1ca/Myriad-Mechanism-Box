using System.Drawing.Drawing2D;
using System.Text;

namespace CustomsClearanceConsole;

/// <summary>
/// Read-only declaration detail dialog. The item table keeps only what an operator checks
/// against the paper declaration: product name, quantity, unit price and total price.
/// </summary>
internal sealed class DetailForm : DpiDialog
{
    /// <summary>Logical client size of the dialog.</summary>
    internal static readonly Size DialogSize = new(680, 500);
    private const int PadX = 24;
    private const int RowHeight = 48;
    private const int ColumnHeaderHeight = 36;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PopupFrame.ApplyRegion(this, 10, DeviceDpi);
    }

    public DetailForm(DeclarationRecord record)
    {
        Text = $"关单明细 · {record.DeclarationNo}";
        LogicalClientSize = DialogSize;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;

        // The 1px padding keeps the docked sections inside the frame border; without it the
        // white header and the grey footer covered the border along their edges.
        var frame = new RoundedPanel { Dock = DockStyle.Fill, WindowFrame = true, Radius = 10, BorderColor = Theme.Border, BackColor = Color.White, Padding = new Padding(1) };
        Controls.Add(frame);

        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White };
        var title = new Label
        {
            Text = "关单明细",
            Location = new Point(PadX, 16),
            Font = Theme.UiFont(16F, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };
        // Sized to its text (measured at system DPI, converted to logical pixels) so the number
        // follows the title at a fixed gap instead of floating after a fixed-width label.
        title.Size = new Size(DpiLayout.MeasuredToLogical(TextRenderer.MeasureText(title.Text, title.Font).Width) + 2, 26);
        var number = new Label
        {
            Text = string.IsNullOrWhiteSpace(record.DeclarationNo) ? "未识别" : record.DeclarationNo,
            Location = new Point(title.Right + 12, 16),
            Size = new Size(DialogSize.Width - title.Right - 12 - 72, 26),
            Font = Theme.MonoFont(14F, true),
            ForeColor = Theme.Primary,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        var close = new CircleCloseButton { Location = new Point(DialogSize.Width - 2 - 16 - 40, 12), Size = new Size(40, 40) };
        close.AccessibleName = "关闭关单明细";
        close.Click += (_, _) => Close();
        var subtitle = new Label
        {
            Text = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(record.Consignee) ? "未识别收货人" : record.Consignee,
                string.IsNullOrWhiteSpace(record.ContractNo) ? "合同未识别" : $"合同 {record.ContractNo}"
            }),
            Location = new Point(PadX, 44),
            Size = new Size(DialogSize.Width - PadX * 2 - 2, 20),
            Font = Theme.UiFont(12.5F),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        header.Controls.AddRange([title, number, close, subtitle]);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
        var lineList = new DetailLineList(record) { Dock = DockStyle.Top };
        // Header and footer columns follow the list's width, so they stay aligned with the
        // rows when the list shows a vertical scroll bar.
        var columnHeader = new DetailColumnHeader(lineList) { Height = ColumnHeaderHeight };
        // Logical pixels like every other constructor value; DpiDialog scales them once.
        lineList.Height = Math.Max(1, record.LineTotals.Count) * RowHeight;
        scroll.Controls.Add(lineList);

        var footer = new DetailFooter(record, lineList) { Dock = DockStyle.Bottom, Height = DetailFooter.LogicalHeight(record) };

        frame.Controls.Add(scroll);
        frame.Controls.Add(columnHeader);
        frame.Controls.Add(header);
        frame.Controls.Add(footer);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        foreach (var target in new Control[] { header, title, number, subtitle })
            target.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDialogDrag(); };
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
            var prefix = $"第 {string.Join("、", unreliable)} 项金额未通过校验，未计入确认合计。";
            return (false, lines.Count == 0 ? prefix : prefix + string.Join("；", lines));
        }
        if (mismatch) return (false, string.Join("；", lines));
        if (reliable.Count == 0)
            return (false, "没有可确认的分项金额，未形成可靠分项合计。");
        if (record.Status is "识别失败")
            return (false, "识别失败，未形成可核对的分项合计。");
        return (true, "合计由可靠分项求和，未与独立票面总额核对。");
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

    /// <summary>
    /// Four columns: product name, quantity, unit price and total price. The name column takes
    /// the remaining width; numbers are right aligned so digits line up. Mixed currencies stay
    /// unambiguous because every total is painted with its own currency code.
    /// </summary>
    internal static List<(Rectangle Rect, string Text, ContentAlignment Alignment)> Columns(int width, int dpi, int y, int height)
    {
        int S(int px) => (int)Math.Round(px * dpi / 96f);
        var padX = S(PadX);
        var gap = S(16);
        var quantity = S(116);
        var unitPrice = S(104);
        var amount = S(150);
        var name = Math.Max(S(120), width - padX * 2 - gap * 3 - quantity - unitPrice - amount);
        var widths = new[] { name, quantity, unitPrice, amount };
        var headers = new[] { "商品名称", "数量", "单价", "总价" };
        var x = padX;
        var result = new List<(Rectangle, string, ContentAlignment)>();
        for (var i = 0; i < widths.Length; i++)
        {
            result.Add((new Rectangle(x, y, widths[i], height), headers[i], i == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight));
            x += widths[i] + gap;
        }
        return result;
    }

    /// <summary>Width the item rows are laid out in (narrower than the dialog while a scroll bar shows).</summary>
    private static int ContentWidth(Control source, Control fallback) => source.Width > 0 ? Math.Min(source.Width, fallback.Width) : fallback.Width;

    private sealed class DetailColumnHeader : Control
    {
        private readonly Control _widthSource;

        public DetailColumnHeader(Control widthSource)
        {
            _widthSource = widthSource;
            widthSource.SizeChanged += (_, _) => Invalidate();
            Dock = DockStyle.Top;
            BackColor = Theme.PanelSubtle;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var font = PaintCache.Ui(12.5F, UiWeight.Medium);
            foreach (var (rect, text, alignment) in Columns(ContentWidth(_widthSource, this), DeviceDpi, 0, Height))
                TextRenderer.DrawText(e.Graphics, text, font, rect, Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                    (alignment == ContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left));
            e.Graphics.DrawLine(PaintCache.Pen(Theme.Divider), 0, 0, Width, 0);
            e.Graphics.DrawLine(PaintCache.Pen(Theme.Border), 0, Height - 1, Width, Height - 1);
        }
    }

    private sealed class DetailLineList : Control
    {
        private readonly List<DeclarationLineTotal> _ordered;
        private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
        private int _hover = -1;

        public DetailLineList(DeclarationRecord record)
        {
            _ordered = record.LineTotals.OrderBy(x => x.PageNumber).ThenBy(x => x.Sequence).ToList();
            BackColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private int ScaledRowHeight => Math.Max(1, (int)Math.Round(RowHeight * DeviceDpi / 96f));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = e.Y / ScaledRowHeight;
            var next = index >= 0 && index < _ordered.Count ? index : -1;
            if (next == _hover) return;
            var previous = _hover;
            _hover = next;
            // The full product name and exact numbers stay readable even when the painted
            // column uses an ellipsis.
            _tip.SetToolTip(this, next >= 0 ? DescribeLine(next) : "");
            InvalidateRow(previous);
            InvalidateRow(next);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            var previous = _hover;
            _hover = -1;
            InvalidateRow(previous);
        }

        private void InvalidateRow(int index)
        {
            if (index >= 0) Invalidate(new Rectangle(0, index * ScaledRowHeight, Width, ScaledRowHeight));
        }

        internal string DescribeLine(int index)
        {
            if (index < 0 || index >= _ordered.Count) return "";
            var line = _ordered[index];
            var text = new StringBuilder();
            text.Append("商品名称：").Append(line.DisplayProduct).Append('\n');
            // The tooltip carries the lossless source value so a small quantity or a long
            // unit price never reads as an unmarked zero.
            text.Append("数量：").Append(line.ExactQuantityUnit).Append('\n');
            text.Append("单价：").Append(line.ExactUnitPrice).Append('\n');
            text.Append("总价：").Append(string.IsNullOrWhiteSpace(line.Currency) ? "—" : line.Currency).Append(' ').Append(line.Amount.ToString("N2"));
            if (!line.IsReliable) text.Append("\n该项金额未通过校验，未计入确认合计。");
            return text.ToString();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            var dpi = DeviceDpi;
            int S(int px) => (int)Math.Round(px * dpi / 96f);
            var rowHeight = ScaledRowHeight;
            if (_ordered.Count == 0)
            {
                TextRenderer.DrawText(graphics, "未保存分项，请重新识别源文件。", PaintCache.Ui(13F), ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            var nameFont = PaintCache.Ui(13.5F);
            var numberFont = PaintCache.Mono(13F);
            var amountFont = PaintCache.Mono(13F, true);
            var unitFont = PaintCache.Ui(12F);
            var currencyFont = PaintCache.Ui(11.5F);
            var first = Math.Max(0, e.ClipRectangle.Top / rowHeight);
            var last = Math.Min(_ordered.Count - 1, e.ClipRectangle.Bottom / rowHeight);
            for (var i = first; i <= last; i++)
            {
                var line = _ordered[i];
                var y = i * rowHeight;
                var rowRect = new Rectangle(0, y, Width, rowHeight);
                if (!line.IsReliable) graphics.FillRectangle(PaintCache.Brush(UiTokens.Status.Attention.Row), rowRect);
                if (i == _hover) graphics.FillRectangle(PaintCache.Brush(UiTokens.Status.RowHover), rowRect);
                graphics.DrawLine(PaintCache.Pen(Theme.Divider), 0, y + rowHeight - 1, Width, y + rowHeight - 1);
                var columns = Columns(Width, dpi, y, rowHeight - 1);

                // Product name: up to two lines, vertically centred, ellipsis on overflow.
                var nameRect = columns[0].Rect;
                const TextFormatFlags nameFlags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;
                var lineHeight = TextRenderer.MeasureText(graphics, "国", nameFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
                var measured = TextRenderer.MeasureText(graphics, line.DisplayProduct, nameFont, new Size(nameRect.Width, int.MaxValue), nameFlags).Height;
                var nameHeight = Math.Min(measured, lineHeight * 2);
                TextRenderer.DrawText(graphics, line.DisplayProduct, nameFont,
                    new Rectangle(nameRect.X, y + (rowHeight - nameHeight) / 2, nameRect.Width, nameHeight), Theme.Text, nameFlags);

                DrawQuantity(graphics, columns[1].Rect, line, numberFont, unitFont, S);
                TextRenderer.DrawText(graphics, line.DisplayUnitPrice, numberFont, columns[2].Rect, line.UnitPrice is null ? Theme.Muted : Theme.Ink2,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                DrawAmount(graphics, columns[3].Rect, line, amountFont, currencyFont, S);
            }
        }

        /// <summary>Quantity in the mono face with its unit as a lighter suffix, right aligned.</summary>
        private static void DrawQuantity(Graphics graphics, Rectangle rect, DeclarationLineTotal line, Font numberFont, Font unitFont, Func<int, int> S)
        {
            const TextFormatFlags flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (line.Quantity is null)
            {
                TextRenderer.DrawText(graphics, "—", numberFont, rect, Theme.Muted, flags);
                return;
            }
            var unit = string.IsNullOrWhiteSpace(line.Unit) ? "" : line.Unit;
            var unitWidth = unit.Length == 0 ? 0 : TextRenderer.MeasureText(graphics, unit, unitFont, new Size(int.MaxValue, rect.Height), TextFormatFlags.NoPadding).Width;
            unitWidth = Math.Min(unitWidth, rect.Width / 2);
            if (unitWidth > 0)
                TextRenderer.DrawText(graphics, unit, unitFont, new Rectangle(rect.Right - unitWidth, rect.Y, unitWidth, rect.Height), Theme.Muted, flags | TextFormatFlags.EndEllipsis);
            var numberRight = rect.Right - (unitWidth > 0 ? unitWidth + S(4) : 0);
            TextRenderer.DrawText(graphics, line.DisplayQuantity, numberFont, new Rectangle(rect.X, rect.Y, Math.Max(0, numberRight - rect.X), rect.Height), Theme.Ink2,
                flags | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Total with its own currency code; an unconfirmed amount is amber and underlined.</summary>
        private static void DrawAmount(Graphics graphics, Rectangle rect, DeclarationLineTotal line, Font amountFont, Font currencyFont, Func<int, int> S)
        {
            const TextFormatFlags flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var text = line.Amount.ToString("N2");
            var currency = string.IsNullOrWhiteSpace(line.Currency) ? "—" : line.Currency;
            var amountWidth = TextRenderer.MeasureText(graphics, text, amountFont, new Size(int.MaxValue, rect.Height), TextFormatFlags.NoPadding).Width;
            var currencyWidth = TextRenderer.MeasureText(graphics, currency, currencyFont, new Size(int.MaxValue, rect.Height), TextFormatFlags.NoPadding).Width;
            var color = line.IsReliable ? Theme.Text : Theme.Warning;
            var amountLeft = rect.Right - amountWidth;
            TextRenderer.DrawText(graphics, text, amountFont, new Rectangle(amountLeft, rect.Y, amountWidth, rect.Height), color, flags);
            if (amountLeft - S(6) - currencyWidth >= rect.X)
                TextRenderer.DrawText(graphics, currency, currencyFont, new Rectangle(amountLeft - S(6) - currencyWidth, rect.Y, currencyWidth, rect.Height), Theme.Muted, flags);
            if (!line.IsReliable)
            {
                var underlineY = rect.Y + rect.Height / 2 + S(10);
                using var underline = new Pen(UiTokens.Status.AttentionUnderline, Math.Max(1, S(1))) { DashStyle = DashStyle.Dash };
                graphics.DrawLine(underline, amountLeft, underlineY, rect.Right, underlineY);
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
        private const int PadTop = 12, TotalRow = 28, MessageRow = 36;
        private readonly DeclarationRecord _record;
        private readonly Control _widthSource;
        private readonly ToolTip _tip = new() { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };

        public DetailFooter(DeclarationRecord record, Control widthSource)
        {
            _record = record;
            _widthSource = widthSource;
            widthSource.SizeChanged += (_, _) => Invalidate();
            BackColor = UiTokens.Colors.DialogFooter;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            var (_, message) = EvaluateConsistency(record);
            _tip.SetToolTip(this, message + "\n" + CurrencySummaryText(record));
        }

        /// <summary>Logical height: one line per currency total plus the verdict line.</summary>
        public static int LogicalHeight(DeclarationRecord record) =>
            PadTop + Math.Max(1, ReliableCurrencyTotals(record).Count) * TotalRow + MessageRow;

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(BackColor);
            var dpi = DeviceDpi;
            int S(int px) => (int)Math.Round(px * dpi / 96f);
            graphics.DrawLine(PaintCache.Pen(Theme.Border), 0, 0, Width, 0);
            var (consistent, message) = EvaluateConsistency(_record);
            var rows = ReliableCurrencyTotals(_record);
            var columns = Columns(ContentWidth(_widthSource, this), dpi, 0, 0);
            var amountColumn = columns[3].Rect;
            var padX = columns[0].Rect.X;

            TextRenderer.DrawText(graphics, $"共 {_record.LineTotals.Count} 项", PaintCache.Ui(13F, UiWeight.Medium),
                new Rectangle(padX, S(PadTop), S(120), S(TotalRow)), Theme.Ink2,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var labelRight = amountColumn.X - S(16);
            TextRenderer.DrawText(graphics, "合计（按币种，不换算）", PaintCache.Ui(12.5F),
                new Rectangle(padX + S(120), S(PadTop), Math.Max(0, labelRight - padX - S(120)), S(TotalRow)), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            var y = S(PadTop);
            var amountFont = PaintCache.Mono(15F, true);
            var currencyFont = PaintCache.Ui(12F);
            if (rows.Count == 0)
                TextRenderer.DrawText(graphics, "—", amountFont, new Rectangle(amountColumn.X, y, amountColumn.Width, S(TotalRow)), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            foreach (var (currency, amount) in rows)
            {
                var text = amount.ToString("N2");
                var amountWidth = TextRenderer.MeasureText(graphics, text, amountFont, new Size(int.MaxValue, S(TotalRow)), TextFormatFlags.NoPadding).Width;
                var amountLeft = amountColumn.Right - amountWidth;
                TextRenderer.DrawText(graphics, text, amountFont, new Rectangle(amountLeft, y, amountWidth, S(TotalRow)), consistent ? Theme.Text : Theme.Warning,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(graphics, currency, currencyFont, new Rectangle(amountLeft - S(70), y, S(64), S(TotalRow)), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                y += S(TotalRow);
            }

            var messageTop = Height - S(MessageRow);
            graphics.DrawLine(PaintCache.Pen(Theme.Divider), padX, messageTop, Width - padX, messageTop);
            var icon = PaintCache.Icon(consistent ? Ui2.CheckGreen : Ui2.Alert, 16, dpi);
            var textX = padX;
            if (icon is not null)
            {
                graphics.DrawImage(icon, padX, messageTop + (S(MessageRow) - icon.Height) / 2, icon.Width, icon.Height);
                textX += icon.Width + S(8);
            }
            TextRenderer.DrawText(graphics, message, PaintCache.Ui(12F), new Rectangle(textX, messageTop, Width - textX - padX, S(MessageRow)),
                consistent ? Theme.Muted : Theme.Warning,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
