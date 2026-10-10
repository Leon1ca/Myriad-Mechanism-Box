using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{

    // ---- grid painting ----

    /// <summary>Self-test only: reproduces the leaked anti-aliasing to prove the seam check works.</summary>
    internal static bool SkipCellSmoothingResetForTest { get; set; }

    private void PaintRecordCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        // Every cell shares one Graphics. The rounded tags and buttons below switch on
        // anti-aliasing, and that setting used to leak into the next cell's background fill:
        // anti-aliased rectangle edges only half-cover their pixels, and the grid does not
        // clear its double buffer under the cells, so a ~25% black seam appeared along every
        // cell edge on real screens (DrawToBitmap hid it in the alpha channel).
        if (e.Graphics is not null && !SkipCellSmoothingResetForTest)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
        }
        if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            PaintColumnHeader(e);
            return;
        }
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].Tag is not DeclarationRecord record) return;
        var name = _grid.Columns[e.ColumnIndex].Name;
        // Grid borders are off; every cell draws only its own 1px bottom divider (design 2.5).
        // The grid's own SingleHorizontal style still produced column lines on real screens.
        if (name is not ("Index" or "Status" or "No" or "PortDest" or "Amount" or "Detail" or "Verify"))
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border);
            DrawRowDivider(e.Graphics!, e.CellBounds);
            e.Handled = true;
            return;
        }
        e.Paint(e.CellBounds, DataGridViewPaintParts.Background);
        DrawRowDivider(e.Graphics!, e.CellBounds);
        var graphics = e.Graphics!;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        var bounds = e.CellBounds;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        if (selected)
        {
            using var selection = new SolidBrush(UiTokens.Status.CellSelected);
            graphics.FillRectangle(selection, bounds);
        }
        switch (name)
        {
            case "Index":
                using (var font = Theme.MonoFont(12F))
                    TextRenderer.DrawText(graphics, Convert.ToString(e.Value) ?? "", font, bounds, Theme.Placeholder,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            case "Status":
                DrawStatusTag(graphics, bounds, record);
                break;
            case "No":
                DrawNumberCell(graphics, bounds, record, S);
                break;
            case "PortDest":
                using (var portFont = Theme.UiFont(13.5F))
                    TextRenderer.DrawText(graphics, Dash(record.ExitCustoms), portFont, new Rectangle(bounds.X + S(8), bounds.Y + S(6), bounds.Width - S(16), S(20)), Theme.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                using (var destFont = Theme.UiFont(12F))
                    TextRenderer.DrawText(graphics, Dash(record.DestinationCountry), destFont, new Rectangle(bounds.X + S(8), bounds.Y + S(26), bounds.Width - S(16), S(18)), Theme.Muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                break;
            case "Amount":
                DrawAmountCell(graphics, bounds, record, S);
                break;
            case "Detail":
                DrawActionButton(graphics, bounds, "明细", Theme.DetailButtonBg, Theme.Primary, Theme.Border, S, enabled: true);
                break;
            case "Verify":
                if (record.HasScreenshot) DrawKeptTag(graphics, bounds, S);
                else if (!record.HasValidDeclarationNo)
                {
                    using var disabledFont = Theme.UiFont(12.5F);
                    TextRenderer.DrawText(graphics, "不可核验", disabledFont, bounds, Theme.DisabledText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else DrawVerifyButton(graphics, bounds, S);
                break;
        }
        e.Handled = true;
    }

    private static void DrawRowDivider(Graphics graphics, Rectangle bounds)
    {
        using var line = new Pen(Theme.Divider);
        graphics.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    /// <summary>
    /// Design 2.5 table header: panelSubtle background, 12.5px/500 muted text, only a 1px
    /// border line at the bottom (no vertical grid lines).
    /// </summary>
    private void PaintColumnHeader(DataGridViewCellPaintingEventArgs e)
    {
        var graphics = e.Graphics!;
        var bounds = e.CellBounds;
        int S(int px) => (int)Math.Round(px * DeviceDpi / 96.0);
        using (var back = new SolidBrush(Theme.PanelSubtle)) graphics.FillRectangle(back, bounds);
        using (var line = new Pen(Theme.Border)) graphics.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        var column = _grid.Columns[e.ColumnIndex];
        // Short divider between titles (not after the last one): it separates the headings and
        // marks where a column can be resized, without drawing column lines through the rows.
        if (column != _grid.Columns.GetLastColumn(DataGridViewElementStates.Visible, DataGridViewElementStates.None))
        {
            var dividerHeight = Math.Max(S(12), bounds.Height * 4 / 10);
            var dividerTop = bounds.Y + (bounds.Height - 1 - dividerHeight) / 2;
            using var divider = new SolidBrush(Theme.Border);
            graphics.FillRectangle(divider, bounds.Right - Math.Max(1, S(1)), dividerTop, Math.Max(1, S(1)), dividerHeight);
        }
        var inset = column.Name == "Index" ? 0 : S(8);
        var text = new Rectangle(bounds.X + inset, bounds.Y, Math.Max(0, bounds.Width - inset * 2), bounds.Height - 1);
        var align = column.Name switch
        {
            "Index" or "Status" or "Detail" or "Verify" => TextFormatFlags.HorizontalCenter,
            "Amount" => TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        using var font = AppFonts.Ui(12.5F, UiWeight.Medium);
        TextRenderer.DrawText(graphics, column.HeaderText, font, text, Theme.Muted,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        e.Handled = true;
    }

    internal static string StatusLabel(DeclarationRecord record) =>
        record.Status == "识别失败" ? "识别失败"
        : record.IsDuplicate ? "重复"
        : record.NeedsAttention ? "需关注"
        : "正常";

    /// <summary>Full text copied from the number cell: declaration number plus source file.</summary>
    internal static string CopyNumberText(DeclarationRecord record) =>
        (record.DeclarationNo.Length == 0 ? "未识别" : record.DeclarationNo) + "\n源文件：" + record.SourceName;

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

    private void DrawStatusTag(Graphics graphics, Rectangle bounds, DeclarationRecord record)
    {
        var dpi = DeviceDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        var label = StatusLabel(record);
        var palette = label == "识别失败" ? UiTokens.Status.Failed
            : label == "重复" ? UiTokens.Status.Duplicate
            : label == "需关注" ? UiTokens.Status.Attention
            : UiTokens.Status.Ok;
        using var font = Theme.UiFont(12F, FontStyle.Bold);
        var textWidth = TextRenderer.MeasureText(graphics, label, font, new Size(int.MaxValue, S(22)), TextFormatFlags.NoPadding).Width;
        var tag = new Rectangle(bounds.X + S(8), bounds.Y + (bounds.Height - S(22)) / 2, Math.Min(bounds.Width - S(16), textWidth + S(16)), S(22));
        using var path = Theme.RoundedPath(tag, S(4));
        using (var fill = new SolidBrush(palette.Bg)) graphics.FillPath(fill, path);
        TextRenderer.DrawText(graphics, label, font, tag, palette.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawNumberCell(Graphics graphics, Rectangle bounds, DeclarationRecord record, Func<int, int> S)
    {
        var number = record.DeclarationNo.Length == 0 ? "未识别" : record.DeclarationNo;
        using (var numberFont = Theme.MonoFont(13F, true))
            TextRenderer.DrawText(graphics, number, numberFont, new Rectangle(bounds.X + S(10), bounds.Y + S(9), bounds.Width - S(20), S(20)), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var fileX = bounds.X + S(10);
        if (record.IsDuplicate)
        {
            var label = record.IsCanonical ? "合计代表" : "不计入";
            using var microFont = Theme.UiFont(11F);
            var width = TextRenderer.MeasureText(graphics, label, microFont, new Size(int.MaxValue, S(16)), TextFormatFlags.NoPadding).Width + S(10);
            var tag = new Rectangle(fileX, bounds.Y + S(31), width, S(16));
            using var path = Theme.RoundedPath(tag, S(3));
            using var fill = new SolidBrush(record.IsCanonical ? Theme.AccentSoft : Theme.SegmentBg);
            graphics.FillPath(fill, path);
            TextRenderer.DrawText(graphics, label, microFont, tag, record.IsCanonical ? Theme.Accent : ColorTranslator.FromHtml("#4A5566"),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            fileX = tag.Right + S(6);
        }
        using (var fileFont = Theme.UiFont(12F))
            TextRenderer.DrawText(graphics, record.SourceName, fileFont, new Rectangle(fileX, bounds.Y + S(30), Math.Max(0, bounds.Right - fileX - S(10)), S(18)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private void DrawAmountCell(Graphics graphics, Rectangle bounds, DeclarationRecord record, Func<int, int> S)
    {
        var lines = AmountLines(record);
        if (lines.Count == 0)
        {
            using var dashFont = Theme.UiFont(13.5F);
            TextRenderer.DrawText(graphics, "—", dashFont, bounds, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }
        // Every currency gets its own labelled line; overflow shows a count and the full
        // list stays available in the tooltip and the scrolled money summary.
        var visible = lines.Take(2).ToList();
        var lineHeight = visible.Count == 1 ? bounds.Height : S(24);
        var overflow = lines.Count > 2 ? S(14) : 0;
        var startY = bounds.Y + Math.Max(0, (bounds.Height - visible.Count * lineHeight - overflow) / 2);
        using var currencyFont = Theme.UiFont(11.5F);
        using var amountFont = Theme.MonoFont(13.5F, true);
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
                var underlineY = lineY + lineHeight / 2 + S(8);
                using var underline = new Pen(UiTokens.Status.AttentionUnderline, S(1)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                graphics.DrawLine(underline, amountLeft, underlineY, bounds.Right - S(10), underlineY);
            }
        }
        if (overflow > 0)
        {
            using var moreFont = Theme.UiFont(10.5F);
            TextRenderer.DrawText(graphics, $"+{lines.Count - 2} 币种 · 悬停查看全部", moreFont, new Rectangle(bounds.X, bounds.Bottom - overflow, bounds.Width - S(10), overflow), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawActionButton(Graphics graphics, Rectangle bounds, string text, Color back, Color fore, Color border, Func<int, int> S, bool enabled)
    {
        using var font = Theme.UiFont(12.5F);
        var width = Math.Min(bounds.Width - S(12), Math.Max(S(52), TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, S(28)), TextFormatFlags.NoPadding).Width + S(20)));
        var button = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - S(28)) / 2, width, S(28));
        using var path = Theme.RoundedPath(button, S(5));
        using (var fill = new SolidBrush(enabled ? back : Theme.DisabledFill)) graphics.FillPath(fill, path);
        if (enabled)
        {
            using var pen = new Pen(border);
            graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(graphics, text, font, button, enabled ? fore : Theme.DisabledText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawVerifyButton(Graphics graphics, Rectangle bounds, Func<int, int> S)
    {
        using var font = Theme.UiFont(12.5F);
        var textWidth = TextRenderer.MeasureText(graphics, "校验", font, new Size(int.MaxValue, S(28)), TextFormatFlags.NoPadding).Width;
        var width = Math.Min(bounds.Width - S(12), textWidth + S(42));
        var button = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - S(28)) / 2, width, S(28));
        using (var path = Theme.RoundedPath(button, S(5)))
        {
            using var fill = new SolidBrush(Color.White);
            graphics.FillPath(fill, path);
            using var pen = new Pen(UiTokens.Colors.BorderAccentSoft);
            graphics.DrawPath(pen, path);
        }
        var icon = UiV2Icons.Load(Ui2.External, 16, _layoutDpi);
        var iconWidth = icon?.Width ?? 0;
        var contentWidth = textWidth + (iconWidth > 0 ? S(6) + iconWidth : 0);
        var contentX = button.X + (button.Width - contentWidth) / 2;
        TextRenderer.DrawText(graphics, "校验", font, new Rectangle(contentX, button.Y, textWidth + S(2), button.Height), Theme.Primary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (icon is not null)
        {
            graphics.DrawImage(icon, contentX + textWidth + S(6), button.Y + (button.Height - icon.Height) / 2, icon.Width, icon.Height);
            icon.Dispose();
        }
    }

    private void DrawKeptTag(Graphics graphics, Rectangle bounds, Func<int, int> S)
    {
        using var font = Theme.UiFont(12.5F);
        var textWidth = TextRenderer.MeasureText(graphics, "已留存", font, new Size(int.MaxValue, S(26)), TextFormatFlags.NoPadding).Width;
        var width = Math.Min(bounds.Width - S(12), textWidth + S(40));
        var tag = new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - S(26)) / 2, width, S(26));
        using var path = Theme.RoundedPath(tag, S(4));
        using (var fill = new SolidBrush(UiTokens.Status.Kept.Bg)) graphics.FillPath(fill, path);
        var icon = UiV2Icons.Load(Ui2.CameraBlue, 16, _layoutDpi);
        var iconWidth = icon?.Width ?? 0;
        var contentWidth = textWidth + (iconWidth > 0 ? S(5) + iconWidth : 0);
        var contentX = tag.X + (tag.Width - contentWidth) / 2;
        // Design 01-main: the camera icon precedes the "已留存" text.
        var textX = contentX;
        if (icon is not null)
        {
            graphics.DrawImage(icon, contentX, tag.Y + (tag.Height - icon.Height) / 2, icon.Width, icon.Height);
            textX = contentX + iconWidth + S(5);
            icon.Dispose();
        }
        TextRenderer.DrawText(graphics, "已留存", font, new Rectangle(textX, tag.Y, textWidth + S(2), tag.Height), UiTokens.Status.Kept.Fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
