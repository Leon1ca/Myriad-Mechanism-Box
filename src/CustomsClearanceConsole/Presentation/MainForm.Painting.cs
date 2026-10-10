namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{
    // ---- grid painting ----

    /// <summary>Self-test only: reproduces the leaked anti-aliasing to prove the seam check works.</summary>
    internal static bool SkipCellSmoothingResetForTest { get; set; }

    private void PaintRecordCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.Graphics is not { } graphics) return;
        // Every cell shares one Graphics. The rounded tags and buttons below switch on
        // anti-aliasing, and that setting used to leak into the next cell's background fill:
        // anti-aliased rectangle edges only half-cover their pixels, and the grid does not
        // clear its double buffer under the cells, so a ~25% black seam appeared along every
        // cell edge on real screens (DrawToBitmap hid it in the alpha channel).
        if (!SkipCellSmoothingResetForTest)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
        }
        if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            PaintColumnHeader(e);
            return;
        }
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].Tag is not DeclarationRecord record) return;
        var name = _grid.Columns[e.ColumnIndex].Name;
        var bounds = e.CellBounds;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        // Grid borders are off; every cell draws only its own 1px bottom divider (design 2.5).
        // The grid's own SingleHorizontal style still produced column lines on real screens.
        if (name is not ("Index" or "Status" or "No" or "PortDest" or "Amount" or "Detail" or "Verify"))
        {
            e.Paint(bounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border);
            DrawRowDivider(graphics, bounds);
            e.Handled = true;
            return;
        }
        e.Paint(bounds, DataGridViewPaintParts.Background);
        if (selected) graphics.FillRectangle(PaintCache.Brush(UiTokens.Status.CellSelected), bounds);
        // The divider goes on top of the selection fill so a selected cell keeps the row line.
        DrawRowDivider(graphics, bounds);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        switch (name)
        {
            case "Index":
                TextRenderer.DrawText(graphics, Convert.ToString(e.Value) ?? "", PaintCache.Mono(12F), bounds, Theme.Placeholder,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            case "Status":
                DrawStatusTag(graphics, bounds, record, S);
                break;
            case "No":
                DrawNumberCell(graphics, bounds, record, S);
                break;
            case "PortDest":
                TextRenderer.DrawText(graphics, Dash(record.ExitCustoms), PaintCache.Ui(13.5F), new Rectangle(bounds.X + S(8), bounds.Y + S(6), bounds.Width - S(16), S(20)), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(graphics, Dash(record.DestinationCountry), PaintCache.Ui(12F), new Rectangle(bounds.X + S(8), bounds.Y + S(26), bounds.Width - S(16), S(18)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                break;
            case "Amount":
                DrawAmountCell(graphics, bounds, record, S);
                break;
            case "Detail":
                DrawActionButton(graphics, bounds, "明细", S);
                break;
            case "Verify":
                if (record.HasScreenshot) DrawKeptTag(graphics, bounds, S);
                else if (!record.HasValidDeclarationNo)
                    TextRenderer.DrawText(graphics, "不可核验", PaintCache.Ui(12.5F), bounds, Theme.DisabledText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                else DrawVerifyButton(graphics, bounds, S);
                break;
        }
        e.Handled = true;
    }

    private static void DrawRowDivider(Graphics graphics, Rectangle bounds) =>
        graphics.DrawLine(PaintCache.Pen(Theme.Divider), bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);

    /// <summary>
    /// Design 2.5 table header: panelSubtle background, 12.5px/500 muted text, only a 1px
    /// border line at the bottom (no vertical grid lines).
    /// </summary>
    private void PaintColumnHeader(DataGridViewCellPaintingEventArgs e)
    {
        var graphics = e.Graphics!;
        var bounds = e.CellBounds;
        int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);
        graphics.FillRectangle(PaintCache.Brush(Theme.PanelSubtle), bounds);
        graphics.DrawLine(PaintCache.Pen(Theme.Border), bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        var column = _grid.Columns[e.ColumnIndex];
        // Short divider between titles (not after the last one): it separates the headings and
        // marks where a column can be resized, without drawing column lines through the rows.
        if (column != _grid.Columns.GetLastColumn(DataGridViewElementStates.Visible, DataGridViewElementStates.None))
        {
            var dividerHeight = Math.Max(S(12), bounds.Height * 4 / 10);
            var dividerTop = bounds.Y + (bounds.Height - 1 - dividerHeight) / 2;
            graphics.FillRectangle(PaintCache.Brush(Theme.Border), bounds.Right - Math.Max(1, S(1)), dividerTop, Math.Max(1, S(1)), dividerHeight);
        }
        var inset = column.Name == "Index" ? 0 : S(8);
        var text = new Rectangle(bounds.X + inset, bounds.Y, Math.Max(0, bounds.Width - inset * 2), bounds.Height - 1);
        var align = column.Name switch
        {
            "Index" or "Status" or "Detail" or "Verify" => TextFormatFlags.HorizontalCenter,
            "Amount" => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        TextRenderer.DrawText(graphics, column.HeaderText, PaintCache.Ui(12.5F, UiWeight.Medium), text, Theme.Muted,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        e.Handled = true;
    }

    internal static string StatusLabel(DeclarationRecord record) =>
        record.Status == "识别失败" ? "识别失败"
        : record.IsDuplicate ? "重复"
        : record.NeedsAttention ? "需关注"
        : "正常";

    /// <summary>Text copied from the number cell (the declaration number only).</summary>
    internal static string CopyNumberText(DeclarationRecord record) =>
        record.DeclarationNo.Length == 0 ? "未识别" : record.DeclarationNo;

    /// <summary>
    /// Per-currency amounts for one record. Reliable and unconfirmed sums are kept on
    /// separate lines and never combined across currencies.
    /// </summary>
    internal static IReadOnlyList<(string Currency, decimal Amount, bool Reliable)> AmountLines(DeclarationRecord record)
    {
        var lines = new List<(string, decimal, bool)>();
        foreach (var total in record.Totals.OrderBy(x => x.Key, StringComparer.Ordinal))
            lines.Add((total.Key, total.Value, true));
        foreach (var group in record.LineTotals.Where(x => !x.IsReliable)
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.Currency) ? "—" : x.Currency, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
            lines.Add((group.Key, group.Sum(x => x.Amount), false));
        return lines;
    }

    /// <summary>Complete multi-currency text used for copying and tooltips.</summary>
    internal static string CopyAmountText(DeclarationRecord record)
    {
        var lines = AmountLines(record);
        if (lines.Count == 0) return "—";
        return string.Join("\n", lines.Select(x => x.Reliable
            ? $"{x.Currency} {x.Amount:N2}"
            : $"{x.Currency} {x.Amount:N2}（未确认，未计入确认合计）"));
    }

    private static void DrawStatusTag(Graphics graphics, Rectangle bounds, DeclarationRecord record, Func<int, int> S)
    {
        var label = StatusLabel(record);
        var palette = label == "识别失败" ? UiTokens.Status.Failed
            : label == "重复" ? UiTokens.Status.Duplicate
            : label == "需关注" ? UiTokens.Status.Attention
            : UiTokens.Status.Ok;
        var font = PaintCache.Ui(12F, UiWeight.Bold);
        var textWidth = TextRenderer.MeasureText(graphics, label, font, new Size(int.MaxValue, S(22)), TextFormatFlags.NoPadding).Width;
        var tag = new Rectangle(bounds.X + S(8), bounds.Y + (bounds.Height - S(22)) / 2, Math.Min(bounds.Width - S(16), textWidth + S(16)), S(22));
        using (var path = Theme.RoundedPath(tag, S(4))) graphics.FillPath(PaintCache.Brush(palette.Bg), path);
        TextRenderer.DrawText(graphics, label, font, tag, palette.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>
    /// Declaration number, vertically centred. A duplicate also gets its small "合计代表 /
    /// 不计入" tag on a second line, which explains which copy the deduplicated sum counts.
    /// </summary>
    private static void DrawNumberCell(Graphics graphics, Rectangle bounds, DeclarationRecord record, Func<int, int> S)
    {
        var number = CopyNumberText(record);
        var numberFont = PaintCache.Mono(13F, true);
        var numberColor = record.DeclarationNo.Length == 0 ? Theme.Muted : Theme.Text;
        var textBox = new Rectangle(bounds.X + S(10), bounds.Y, Math.Max(0, bounds.Width - S(20)), bounds.Height);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        if (!record.IsDuplicate)
        {
            TextRenderer.DrawText(graphics, number, numberFont, textBox, numberColor, flags);
            return;
        }
        var lineHeight = S(20);
        var tagHeight = S(16);
        var gap = S(3);
        var top = bounds.Y + (bounds.Height - lineHeight - gap - tagHeight) / 2;
        TextRenderer.DrawText(graphics, number, numberFont, textBox with { Y = top, Height = lineHeight }, numberColor, flags);
        var label = record.IsCanonical ? "合计代表" : "不计入";
        var microFont = PaintCache.Ui(11F);
        var width = TextRenderer.MeasureText(graphics, label, microFont, new Size(int.MaxValue, tagHeight), TextFormatFlags.NoPadding).Width + S(10);
        var tag = new Rectangle(textBox.X, top + lineHeight + gap, Math.Min(width, textBox.Width), tagHeight);
        using (var path = Theme.RoundedPath(tag, S(3)))
            graphics.FillPath(PaintCache.Brush(record.IsCanonical ? Theme.AccentSoft : Theme.SegmentBg), path);
        TextRenderer.DrawText(graphics, label, microFont, tag, record.IsCanonical ? Theme.Accent : UiTokens.Status.Failed.Fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static void DrawAmountCell(Graphics graphics, Rectangle bounds, DeclarationRecord record, Func<int, int> S)
    {
        var lines = AmountLines(record);
        if (lines.Count == 0)
        {
            TextRenderer.DrawText(graphics, "—", PaintCache.Ui(13.5F), bounds with { Width = bounds.Width - S(10) }, Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }
        // Every currency gets its own labelled line; overflow shows a count and the full
        // list stays available in the tooltip and the scrolled money summary.
        var visible = lines.Take(2).ToList();
        var lineHeight = visible.Count == 1 ? bounds.Height : S(22);
        var overflow = lines.Count > 2 ? S(14) : 0;
        var startY = bounds.Y + Math.Max(0, (bounds.Height - visible.Count * lineHeight - overflow) / 2);
        var currencyFont = PaintCache.Ui(11.5F);
        var amountFont = PaintCache.Mono(13.5F, true);
        for (var i = 0; i < visible.Count; i++)
        {
            // Design 2.5: currency 11.5px muted, 6px gap, amount 500 weight; right aligned.
            var (currency, amount, reliable) = visible[i];
            var lineY = startY + i * lineHeight;
            var text = amount.ToString("N2");
            var amountWidth = TextRenderer.MeasureText(graphics, text, amountFont, new Size(int.MaxValue, S(20)), TextFormatFlags.NoPadding).Width;
            var currencyWidth = TextRenderer.MeasureText(graphics, currency, currencyFont, new Size(int.MaxValue, S(20)), TextFormatFlags.NoPadding).Width;
            var available = bounds.Width - S(8) - S(10);
            var amountLeft = bounds.Right - S(10) - amountWidth;
            if (currencyWidth + S(6) + amountWidth <= available)
            {
                TextRenderer.DrawText(graphics, text, amountFont, new Rectangle(amountLeft, lineY, amountWidth, lineHeight),
                    reliable ? Theme.Text : Theme.Warning, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(graphics, currency, currencyFont, new Rectangle(amountLeft - S(6) - currencyWidth, lineY, currencyWidth, lineHeight), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                // Too narrow for both parts: one string cut with an ellipsis like every other
                // column, instead of the currency being drawn over the digits.
                var box = new Rectangle(bounds.X + S(8), lineY, Math.Max(0, available), lineHeight);
                TextRenderer.DrawText(graphics, $"{currency} {text}", amountFont, box, reliable ? Theme.Text : Theme.Warning,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                amountLeft = box.X;
            }
            if (!reliable)
            {
                var underlineY = lineY + lineHeight / 2 + S(9);
                using var underline = new Pen(UiTokens.Status.AttentionUnderline, S(1)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                graphics.DrawLine(underline, amountLeft, underlineY, bounds.Right - S(10), underlineY);
            }
        }
        if (overflow > 0)
            TextRenderer.DrawText(graphics, $"+{lines.Count - 2} 币种 · 悬停查看全部", PaintCache.Ui(10.5F), new Rectangle(bounds.X, bounds.Bottom - overflow - S(2), bounds.Width - S(10), overflow), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private static Rectangle ActionRect(Rectangle bounds, int width, int height) =>
        new(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);

    private static void DrawActionButton(Graphics graphics, Rectangle bounds, string text, Func<int, int> S)
    {
        var font = PaintCache.Ui(12.5F);
        var width = Math.Min(bounds.Width - S(12), Math.Max(S(52), TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, S(28)), TextFormatFlags.NoPadding).Width + S(20)));
        var button = ActionRect(bounds, width, S(28));
        using (var path = Theme.RoundedPath(new RectangleF(button.X + .5F, button.Y + .5F, button.Width - 1, button.Height - 1), S(5)))
        {
            graphics.FillPath(PaintCache.Brush(Theme.DetailButtonBg), path);
            graphics.DrawPath(PaintCache.Pen(Theme.Border), path);
        }
        TextRenderer.DrawText(graphics, text, font, button, Theme.Primary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawVerifyButton(Graphics graphics, Rectangle bounds, Func<int, int> S)
    {
        var font = PaintCache.Ui(12.5F);
        var textWidth = TextRenderer.MeasureText(graphics, "校验", font, new Size(int.MaxValue, S(28)), TextFormatFlags.NoPadding).Width;
        var button = ActionRect(bounds, Math.Min(bounds.Width - S(12), textWidth + S(42)), S(28));
        using (var path = Theme.RoundedPath(new RectangleF(button.X + .5F, button.Y + .5F, button.Width - 1, button.Height - 1), S(5)))
        {
            graphics.FillPath(PaintCache.Brush(Color.White), path);
            graphics.DrawPath(PaintCache.Pen(UiTokens.Colors.BorderAccentSoft), path);
        }
        var icon = PaintCache.Icon(Ui2.External, 16, _layoutDpi);
        var iconWidth = icon?.Width ?? 0;
        var contentWidth = textWidth + (iconWidth > 0 ? S(6) + iconWidth : 0);
        var contentX = button.X + (button.Width - contentWidth) / 2;
        TextRenderer.DrawText(graphics, "校验", font, new Rectangle(contentX, button.Y, textWidth + S(2), button.Height), Theme.Primary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (icon is not null)
            graphics.DrawImage(icon, contentX + textWidth + S(6), button.Y + (button.Height - icon.Height) / 2, icon.Width, icon.Height);
    }

    private void DrawKeptTag(Graphics graphics, Rectangle bounds, Func<int, int> S)
    {
        var font = PaintCache.Ui(12.5F);
        var textWidth = TextRenderer.MeasureText(graphics, "已留存", font, new Size(int.MaxValue, S(26)), TextFormatFlags.NoPadding).Width;
        var tag = ActionRect(bounds, Math.Min(bounds.Width - S(12), textWidth + S(40)), S(26));
        using (var path = Theme.RoundedPath(tag, S(4))) graphics.FillPath(PaintCache.Brush(UiTokens.Status.Kept.Bg), path);
        var icon = PaintCache.Icon(Ui2.CameraBlue, 16, _layoutDpi);
        var iconWidth = icon?.Width ?? 0;
        var contentWidth = textWidth + (iconWidth > 0 ? S(5) + iconWidth : 0);
        var contentX = tag.X + (tag.Width - contentWidth) / 2;
        // Design 01-main: the camera icon precedes the "已留存" text.
        var textX = contentX;
        if (icon is not null)
        {
            graphics.DrawImage(icon, contentX, tag.Y + (tag.Height - icon.Height) / 2, icon.Width, icon.Height);
            textX = contentX + iconWidth + S(5);
        }
        TextRenderer.DrawText(graphics, "已留存", font, new Rectangle(textX, tag.Y, textWidth + S(2), tag.Height), UiTokens.Status.Kept.Fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
