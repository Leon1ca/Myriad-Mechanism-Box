using System.Drawing.Drawing2D;

namespace CustomsClearanceConsole;

/// <summary>
/// Design-spec dropdown menu (export / cleanup). The same renderer paints both the
/// live popup and the native UI self-test snapshots, so screenshots show the real menu.
/// Widths and item heights are logical 96 DPI values scaled by the anchor's DPI.
/// </summary>
internal sealed class DesignMenu : IDisposable
{
    internal sealed record Entry(
        string Key,
        string Title,
        string? Description = null,
        string? IconStem = null,
        string? Trailing = null,
        bool Danger = false,
        bool Enabled = true,
        bool SeparatorBefore = false);

    private readonly IReadOnlyList<Entry> _entries;
    private readonly int _logicalWidth;
    private MenuPopup? _popup;
    private MenuSurface? _surface;
    private int _popupDpi;

    public event EventHandler<string>? ItemSelected;

    public DesignMenu(IEnumerable<Entry> entries, int logicalWidth = 248)
    {
        _entries = entries.ToList();
        _logicalWidth = logicalWidth;
    }

    public bool Visible => _popup is { IsDisposed: false, Visible: true };

    public void Show(Control anchor, int offsetY, bool alignRight)
    {
        if (_entries.Count == 0) return;
        var dpi = anchor.DeviceDpi > 0 ? anchor.DeviceDpi : 96;
        EnsurePopup(dpi);
        if (_popup is null || _surface is null) return;
        var width = UiScale.Px(dpi, _logicalWidth);
        var working = Screen.FromControl(anchor).WorkingArea;
        var origin = anchor.PointToScreen(Point.Empty);
        var x = alignRight ? origin.X + anchor.Width - width : origin.X;
        x = Math.Clamp(x, working.Left, Math.Max(working.Left, working.Right - width));
        var y = origin.Y + UiScale.Px(dpi, offsetY);
        if (y + _surface.Height > working.Bottom) y = Math.Max(working.Top, origin.Y - _surface.Height - UiScale.Px(dpi, 6));
        _popup.Location = new Point(x, y);
        var owner = anchor.FindForm();
        _popup.Opacity = owner?.Opacity ?? 1D;
        if (owner is not null) _popup.Show(owner); else _popup.Show();
        _popup.Activate();
        _surface.ResetHover();
    }

    public void Close() => _popup?.Hide();

    private void EnsurePopup(int dpi)
    {
        if (_popup is { IsDisposed: false } && _popupDpi == dpi) return;
        if (_popup is { IsDisposed: false }) _popup.Dispose();
        _popupDpi = dpi;
        var width = UiScale.Px(dpi, _logicalWidth);
        var height = Measure(_entries, dpi);
        _surface = new MenuSurface(_entries, width, dpi, key =>
        {
            Close();
            ItemSelected?.Invoke(this, key);
        })
        { Size = new Size(width, height) };
        _popup = new MenuPopup(_surface) { ClientSize = new Size(width, height) };
        _popup.Deactivate += (_, _) => Close();
    }

    public void Dispose()
    {
        if (_popup is { IsDisposed: false }) _popup.Dispose();
        _popup = null;
        _surface = null;
    }

    public static int Measure(IReadOnlyList<Entry> entries, int dpi = 96)
    {
        int S(int px) => UiScale.Px(dpi, px);
        var height = S(6);
        foreach (var entry in entries)
        {
            if (entry.SeparatorBefore) height += S(1) + S(8);
            height += S(entry.Description is null ? 40 : 50);
        }
        return height + S(6);
    }

    public static void Render(Graphics graphics, IReadOnlyList<Entry> entries, int width, float dpi, int highlightIndex = -1)
    {
        int S(int px) => UiScale.Px((int)Math.Round(dpi), px);
        float Sf(float px) => px * dpi / 96F;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var menuSize = new Size(width, Measure(entries, (int)Math.Round(dpi)));
        graphics.Clear(Color.White);
        PopupFrame.PaintBorder(graphics, menuSize, 8, (int)Math.Round(dpi), Theme.Border);
        var y = S(6);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.SeparatorBefore)
            {
                using var pen = new Pen(Theme.Divider);
                graphics.DrawLine(pen, S(6), y + S(4), width - S(6), y + S(4));
                y += S(1) + S(8);
            }
            var height = S(entry.Description is null ? 40 : 50);
            var row = new Rectangle(S(6), y, width - S(12), height);
            if (i == highlightIndex)
            {
                using var highlight = Theme.RoundedPath(row, Sf(6));
                using var brush = new SolidBrush(Theme.SegmentBg);
                graphics.FillPath(brush, highlight);
            }
            if (entry.IconStem is not null)
            {
                var block = new Rectangle(row.X + S(4), row.Y + (height - S(26)) / 2, S(26), S(26));
                using (var blockPath = Theme.RoundedPath(block, Sf(5)))
                using (var blockBrush = new SolidBrush(entry.Key == "excel" ? ColorTranslator.FromHtml("#E6F4EE") : Theme.SegmentBg))
                    graphics.FillPath(blockBrush, blockPath);
                var icon = UiV2Icons.Load(entry.IconStem, 16, dpi);
                if (icon is not null)
                {
                    graphics.DrawImage(icon, block.X + (block.Width - icon.Width) / 2, block.Y + (block.Height - icon.Height) / 2, icon.Width, icon.Height);
                    icon.Dispose();
                }
            }
            var textLeft = row.X + (entry.IconStem is null ? S(10) : S(38));
            var trailingWidth = entry.Trailing is null ? 0 : S(46);
            var titleColor = !entry.Enabled ? Theme.DisabledText : entry.Danger ? Theme.Danger : Theme.Text;
            using (var titleFont = Theme.UiFont(13.5F, FontStyle.Regular))
            {
                var titleRect = new Rectangle(textLeft, entry.Description is null ? row.Y : row.Y + S(4), row.Width - (textLeft - row.X) - trailingWidth, entry.Description is null ? row.Height : S(20));
                TextRenderer.DrawText(graphics, entry.Title, titleFont, titleRect, titleColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (entry.Description is not null)
            {
                using var descriptionFont = Theme.UiFont(12F);
                TextRenderer.DrawText(graphics, entry.Description, descriptionFont, new Rectangle(textLeft, row.Y + S(24), row.Width - (textLeft - row.X) - trailingWidth, S(20)), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (entry.Trailing is not null)
            {
                using var trailingFont = Theme.MonoFont(12F);
                TextRenderer.DrawText(graphics, entry.Trailing, trailingFont, new Rectangle(row.Right - S(52), row.Y, S(48), row.Height), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            y += height;
        }
    }

    private sealed class MenuPopup : Form
    {
        public MenuPopup(Control content)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.White;
            Padding = Padding.Empty;
            MinimizeBox = false;
            MaximizeBox = false;
            ControlBox = false;
            content.Dock = DockStyle.Fill;
            Controls.Add(content);
        }


        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PopupFrame.ApplyRegion(this, 8, DeviceDpi);
        }
    }

    private sealed class MenuSurface : Control
    {
        private readonly IReadOnlyList<Entry> _entries;
        private readonly int _dpi;
        private readonly Action<string> _chosen;
        private readonly List<Rectangle> _rows = [];
        private readonly List<int> _rowEntries = [];
        private int _hover = -1;

        public MenuSurface(IReadOnlyList<Entry> entries, int scaledWidth, int dpi, Action<string> chosen)
        {
            _entries = entries;
            _dpi = dpi;
            _chosen = chosen;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BuildRows(scaledWidth);
        }

        private void BuildRows(int width)
        {
            int S(int px) => UiScale.Px(_dpi, px);
            var y = S(6);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.SeparatorBefore) y += S(9);
                var height = S(entry.Description is null ? 40 : 50);
                _rows.Add(new Rectangle(S(6), y, width - S(12), height));
                _rowEntries.Add(i);
                y += height;
            }
        }

        public void ResetHover() { _hover = -1; Invalidate(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var next = -1;
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].Contains(e.Location)) { next = i; break; }
            if (next != _hover) { _hover = next; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_hover < 0 || _hover >= _rowEntries.Count) return;
            var entry = _entries[_rowEntries[_hover]];
            if (!entry.Enabled) return;
            _chosen(entry.Key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var highlight = _hover >= 0 && _hover < _rowEntries.Count ? _rowEntries[_hover] : -1;
            Render(e.Graphics, _entries, Width, _dpi, highlight);
        }
    }
}
