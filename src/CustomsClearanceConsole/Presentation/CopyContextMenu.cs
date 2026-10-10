using System.Drawing.Drawing2D;

namespace CustomsClearanceConsole;

internal sealed class CopyContextMenu : ToolStripDropDown
{
    private readonly CopyMenuItemControl _content;
    private readonly ToolStripControlHost _host;

    public CopyContextMenu(Action copy)
    {
        AutoClose = true;
        AutoSize = false;
        BackColor = Color.White;
        DropShadowEnabled = false;
        Padding = new Padding(4);
        Size = new Size(224, 56);
        _content = new CopyMenuItemControl(() => { copy(); Close(); }) { Size = new Size(214, 46) };
        _host = new ToolStripControlHost(_content)
        {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = _content.Size
        };
        Items.Add(_host);
        Opened += (_, _) => _content.Focus();
        UpdateRegion();
    }

    /// <summary>
    /// Sizes the menu for the DPI of the control it opens over and the measured text, so the
    /// label and shortcut are never clipped at 125% / 150% (the menu used to be a fixed 224x56).
    /// </summary>
    public void ShowAt(Control owner, Point screenLocation, bool enabled)
    {
        _content.Enabled = enabled;
        var dpi = owner.DeviceDpi > 0 ? owner.DeviceDpi : 96;
        _content.Dpi = dpi;
        var content = _content.PreferredContentSize();
        var inset = DpiLayout.Scale(4, dpi);
        Padding = new Padding(inset);
        _content.Size = content;
        _host.Size = content;
        Size = new Size(content.Width + inset * 2, content.Height + inset * 2);
        Show(screenLocation);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.White);
        PopupFrame.PaintBorder(e.Graphics, Size, 8, _content?.Dpi ?? DeviceDpi, ColorTranslator.FromHtml("#C2C7CF"));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        PopupFrame.ApplyRegion(this, 8, _content?.Dpi ?? DeviceDpi);
    }

    private sealed class CopyMenuItemControl : Control
    {
        private readonly Action _copy;
        private bool _hover;

        public CopyMenuItemControl(Action copy)
        {
            _copy = copy;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(14F);
            TabStop = true;
            AccessibleRole = AccessibleRole.MenuItem;
            AccessibleName = "复制选中的内容";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) _copy(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                PerformCopy();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void PerformCopy()
        {
            if (Enabled) _copy();
        }

        /// <summary>DPI the item is laid out for (the grid it opens over).</summary>
        public int Dpi { get; set; } = 96;
        private int S(int logical) => DpiLayout.Scale(logical, Dpi);
        // Point-sized fonts already scale with the system DPI; only a monitor whose DPI differs
        // from the system DPI needs the extra ratio.
        private Font LabelFont() => Theme.UiFont(14F * Dpi / DpiLayout.SystemDpi);
        private Font ShortcutFont() => Theme.UiFont(12F * Dpi / DpiLayout.SystemDpi);

        public Size PreferredContentSize()
        {
            using var label = LabelFont();
            using var shortcut = ShortcutFont();
            var labelWidth = TextRenderer.MeasureText("复制选中的内容", label, Size.Empty, TextFormatFlags.NoPadding).Width;
            var shortcutWidth = TextRenderer.MeasureText("Ctrl+C", shortcut, Size.Empty, TextFormatFlags.NoPadding).Width;
            var height = Math.Max(S(40), TextRenderer.MeasureText("复制", label, Size.Empty, TextFormatFlags.NoPadding).Height + S(20));
            return new Size(S(14) + labelWidth + S(32) + shortcutWidth + S(14), height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var surface = _hover || Focused ? ColorTranslator.FromHtml("#F0F4F9") : Color.White;
            if (!Enabled) surface = Color.White;
            e.Graphics.Clear(Color.White);
            using (var path = Theme.RoundedPath(new RectangleF(0, 0, Width - 1F, Height - 1F), S(6)))
            using (var brush = new SolidBrush(surface)) e.Graphics.FillPath(brush, path);

            var foreground = Enabled ? Theme.FieldText : Theme.Placeholder;
            var shortcut = Enabled ? Theme.Placeholder : ColorTranslator.FromHtml("#A8B0BC");
            using var labelFont = LabelFont();
            using var shortcutFont = ShortcutFont();
            var shortcutWidth = TextRenderer.MeasureText(e.Graphics, "Ctrl+C", shortcutFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(e.Graphics, "复制选中的内容", labelFont,
                new Rectangle(S(14), 0, Math.Max(0, Width - S(14) - shortcutWidth - S(28)), Height), foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, "Ctrl+C", shortcutFont,
                new Rectangle(Width - S(14) - shortcutWidth, 0, shortcutWidth, Height), shortcut,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                using var pen = new Pen(Theme.Blue, 1F) { DashStyle = DashStyle.Dot };
                e.Graphics.DrawRectangle(pen, 4, 4, Width - 9, Height - 9);
            }
        }
    }
}
