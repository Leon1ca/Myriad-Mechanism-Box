using System.Drawing.Drawing2D;

namespace CustomsClearanceConsole;

internal sealed class RoundedButton : Button
{
    /// <summary>Corner radius in 96-DPI logical pixels; drawn at the control's real DPI.</summary>
    public int Radius { get; set; } = 7;
    private float ScaledRadius => Radius * DeviceDpi / 96F;
    public Color BorderColor { get; set; } = Theme.Border;
    public Color HoverBackColor { get; set; } = Color.White;
    public Color PressedBackColor { get; set; } = Color.White;
    public Color DisabledFill { get; set; } = Theme.DisabledFill;
    /// <summary>Trailing dropdown chevron (design: "导出列表 | ⌄" with a separator, "清理 ⌄" without).</summary>
    public DropDownGlyph DropDown { get; set; }
    private bool _hover;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode is Keys.Space or Keys.Enter) { _pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode is Keys.Space or Keys.Enter) { _pressed = false; Invalidate(); } }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var back = _pressed ? PressedBackColor : _hover ? HoverBackColor : BackColor;
        var parentBack = Parent?.BackColor ?? Color.White;
        if (!Enabled) back = DisabledFill;
        var fore = Enabled ? ForeColor : Theme.Blend(ForeColor, parentBack, .58F);
        var border = Enabled ? BorderColor : Theme.Blend(BorderColor, parentBack, .58F);
        var scale = DeviceDpi / 96F;
        // The button never gets a background pass (user paint into a double buffer), so the
        // anti-aliased rounded edge used to blend with whatever the buffer held: a dark partial
        // line on the top edge, faint and uneven borders. Paint the parent colour first, then a
        // whole-pixel border (1px at 100%, 2px at 150%) inset by half its width so the straight
        // edges are crisp and every side has the same weight.
        e.Graphics.Clear(parentBack);
        var penWidth = Math.Max(1F, MathF.Round(scale));
        var inset = penWidth / 2F;
        using var path = Theme.RoundedPath(new RectangleF(inset, inset, Math.Max(1, Width - penWidth), Math.Max(1, Height - penWidth)), ScaledRadius);
        using (var brush = new SolidBrush(back)) e.Graphics.FillPath(brush, path);
        using (var pen = new Pen(border, penWidth)) e.Graphics.DrawPath(pen, path);

        var chevronArea = 0;
        if (DropDown != DropDownGlyph.None)
        {
            // Chevron 10x5 logical, 12 from the right edge; the separated variant adds a 1px
            // line (height 18) 10px before it.
            var right = Width - (int)Math.Round(12 * scale);
            var half = 4.5F * scale;
            var cx = right - half;
            var cy = Height / 2F;
            using (var chevron = new Pen(fore, Math.Max(1F, 1.5F * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                e.Graphics.DrawLines(chevron, [new PointF(cx - half, cy - half / 2), new PointF(cx, cy + half / 2), new PointF(cx + half, cy - half / 2)]);
            chevronArea = Width - (int)(cx - half) + (int)Math.Round(8 * scale);
            if (DropDown == DropDownGlyph.Separated)
            {
                var lineX = (int)(cx - half) - (int)Math.Round(10 * scale);
                var lineHalf = 9 * scale;
                using var separator = new Pen(Enabled ? UiTokens.Colors.BorderControl : border, penWidth);
                e.Graphics.DrawLine(separator, lineX, cy - lineHalf, lineX, cy + lineHalf);
                chevronArea = Width - lineX + (int)Math.Round(4 * scale);
            }
        }
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        var textSize = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, Height), flags);
        var image = Image;
        var gap = image is null || string.IsNullOrEmpty(Text) ? 0 : (int)Math.Round(8 * scale);
        var imageWidth = image?.Width ?? 0;
        var contentWidth = imageWidth + gap + textSize.Width;
        var startX = Math.Max(0, (Width - chevronArea - contentWidth) / 2);
        if (image is not null)
        {
            var imageY = (Height - image.Height) / 2;
            if (Enabled) e.Graphics.DrawImage(image, startX, imageY, image.Width, image.Height);
            else ControlPaint.DrawImageDisabled(e.Graphics, image, startX, imageY, parentBack);
            startX += imageWidth + gap;
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(startX, 0, Math.Max(0, Width - startX), Height), fore, flags | TextFormatFlags.Left);
        if (Focused && ShowFocusCues)
        {
            var focusInset = 3F * scale;
            using var focusPath = Theme.RoundedPath(new RectangleF(focusInset, focusInset, Math.Max(1, Width - focusInset * 2), Math.Max(1, Height - focusInset * 2)), Math.Max(2, ScaledRadius - 2 * scale));
            using var focusPen = new Pen(Theme.Blue, 2F * scale);
            e.Graphics.DrawPath(focusPen, focusPath);
        }
    }

    // No window region: a region rasterises the rounded corners with jagged steps and clipped
    // the anti-aliased border unevenly. The corners are painted in the parent colour instead.
}

internal class RoundedPanel : Panel
{
    /// <summary>Corner radius in 96-DPI logical pixels; drawn at the control's real DPI.</summary>
    public int Radius { get; set; } = 8;
    private float ScaledRadius => Radius * DeviceDpi / 96F;
    public Color BorderColor { get; set; } = Theme.Border;
    public int BorderWidth { get; set; } = 1;
    public Color AccentColor { get; set; } = Color.Transparent;
    public int AccentHeight { get; set; }

    /// <summary>
    /// The panel is the whole face of a borderless dialog: shape and border come from
    /// <see cref="PopupFrame"/>, matching the dialog's own window region exactly.
    /// </summary>
    public bool WindowFrame { get; set; }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        if (Width <= 0 || Height <= 0) return;
        if (WindowFrame)
        {
            PopupFrame.ApplyRegion(this, Radius, DeviceDpi);
            Invalidate();
            return;
        }
        using var path = Theme.RoundedPath(new RectangleF(0, 0, Width, Height), ScaledRadius);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (WindowFrame)
        {
            PopupFrame.PaintBorder(eventArgs.Graphics, Size, Radius, DeviceDpi, BorderColor);
            return;
        }
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Whole-pixel border inset by half its width: crisp and equally heavy on every side.
        var penWidth = Math.Max(1F, MathF.Round(BorderWidth * DeviceDpi / 96F));
        var inset = penWidth / 2F;
        using var borderPath = Theme.RoundedPath(new RectangleF(inset, inset, Width - penWidth, Height - penWidth), ScaledRadius);
        using var borderPen = new Pen(BorderColor, penWidth);
        eventArgs.Graphics.DrawPath(borderPen, borderPath);
        if (AccentHeight > 0 && AccentColor != Color.Transparent)
        {
            using var accentPen = new Pen(AccentColor, AccentHeight);
            eventArgs.Graphics.DrawLine(accentPen, ScaledRadius, AccentHeight / 2F, Width - ScaledRadius, AccentHeight / 2F);
        }
    }
}

internal enum CaptionGlyph { Minimize, Maximize, Close }

internal enum DropDownGlyph { None, Plain, Separated }

internal sealed class WindowCaptionButton : Control
{
    private readonly CaptionGlyph _glyph;
    private bool _hover;
    private bool _pressed;
    internal bool ShowsRestoreGlyph => _glyph == CaptionGlyph.Maximize && FindForm()?.WindowState == FormWindowState.Maximized;

    public WindowCaptionButton(CaptionGlyph glyph)
    {
        _glyph = glyph;
        Size = new Size(46, UiTokens.Metrics.Header);
        Cursor = Cursors.Default;
        TabStop = false;
        AccessibleName = glyph switch { CaptionGlyph.Minimize => "最小化", CaptionGlyph.Maximize => "最大化", _ => "关闭" };
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
    public void RefreshWindowState()
    {
        if (_glyph == CaptionGlyph.Maximize)
            AccessibleName = FindForm()?.WindowState == FormWindowState.Maximized ? "还原窗口" : "最大化";
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }

    /// <summary>
    /// Windows 11 caption button: full title-bar height, 46 logical pixels wide, a 10-pixel
    /// glyph drawn crisply at the real DPI, the title-bar colour at rest, a light overlay on
    /// hover and the system red behind the close glyph.
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var restColor = Parent?.BackColor ?? Theme.HeaderBg;
        var back = !_hover ? restColor
            : _glyph == CaptionGlyph.Close ? (_pressed ? ColorTranslator.FromHtml("#94251A") : ColorTranslator.FromHtml("#C42B1C"))
            : Blend(restColor, Color.White, _pressed ? 0.16F : 0.10F);
        e.Graphics.Clear(back);
        var scale = DeviceDpi / 96F;
        var stroke = Math.Max(1F, MathF.Round(scale));
        var half = MathF.Round(5F * scale);
        var cx = MathF.Round(ClientSize.Width / 2F);
        var cy = MathF.Round(ClientSize.Height / 2F);
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (_glyph == CaptionGlyph.Close)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.White, stroke * 1.1F);
            e.Graphics.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
            e.Graphics.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using var crisp = new Pen(Color.White, stroke) { LineJoin = LineJoin.Miter };
        if (_glyph == CaptionGlyph.Minimize)
        {
            e.Graphics.DrawLine(crisp, cx - half, cy, cx + half, cy);
            return;
        }
        var restore = ShowsRestoreGlyph;
        AccessibleName = restore ? "还原窗口" : "最大化";
        if (!restore)
        {
            e.Graphics.DrawRectangle(crisp, cx - half, cy - half, half * 2, half * 2);
            return;
        }
        // Restore: a front square plus the visible top/right edges of the square behind it.
        var offset = MathF.Round(2F * scale);
        var size = half * 2 - offset;
        var frontLeft = cx - half;
        var frontTop = cy - half + offset;
        e.Graphics.DrawRectangle(crisp, frontLeft, frontTop, size, size);
        var backLeft = frontLeft + offset;
        var backTop = cy - half;
        var backRight = backLeft + size;
        e.Graphics.DrawLine(crisp, backLeft, backTop, backRight, backTop);
        e.Graphics.DrawLine(crisp, backRight, backTop, backRight, backTop + size);
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));
}

internal sealed class SearchField : RoundedPanel
{
    public SearchField(TextBox textBox)
    {
        // Design 2.5: height 34, 1px borderControl, radius 6, 16px search-muted icon.
        Radius = 6;
        BorderColor = Theme.BorderControl;
        BackColor = Color.White;
        // Logical pixels; the window scales them to its DPI. Icon column 0-36, 7px vertical
        // breathing room so the 13px input text is not clipped (R4-2).
        Padding = new Padding(36, 7, 12, 6);
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0);
        textBox.Font = Theme.UiFont(13);
        textBox.GotFocus += (_, _) => { BorderColor = Theme.BlueHover; BorderWidth = 2; Invalidate(); };
        textBox.LostFocus += (_, _) => { BorderColor = Theme.BorderControl; BorderWidth = 1; Invalidate(); };
        Click += (_, _) => textBox.Focus();
        Controls.Add(textBox);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var icon = UiV2Icons.Load(Ui2.Search, 16, DeviceDpi);
        if (icon is null) return;
        var x = (int)Math.Round(12 * DeviceDpi / 96F);
        e.Graphics.DrawImage(icon, x, (Height - icon.Height) / 2, icon.Width, icon.Height);
    }
}

internal sealed class ModernDropDown : RoundedPanel
{
    public List<string> Items { get; } = [];
    public int ItemHeight { get; set; } = 44;
    public bool OpenUpward { get; set; }
    public bool UseMonoValue { get; set; }
    private int _selectedIndex = -1;
    private bool _open;
    private DropDownPopup? _popup;
    private ToolStripControlHost? _host;
    private DropDownOptionsPanel? _options;
    public event EventHandler? SelectedIndexChanged;

    public ModernDropDown()
    {
        Radius = 7;
        BorderColor = Theme.Border;
        BackColor = Color.White;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.FindIndex(x => string.Equals(x, value, StringComparison.Ordinal));
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); ShowOptions(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down) { ShowOptions(); e.Handled = true; }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); BorderColor = Theme.BlueHover; BorderWidth = 2; Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (!_open) { BorderColor = Theme.Border; BorderWidth = 1; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var text = SelectedItem ?? string.Empty;
        var textRect = new Rectangle(ScaleX(10), 0, Math.Max(0, Width - ScaleX(42)), Height);
        using var textFont = UseMonoValue ? Theme.MonoFont(13F, true) : Theme.UiFont(13F, FontStyle.Bold);
        var textColor = Enabled ? Theme.FieldText : Theme.DisabledText;
        TextRenderer.DrawText(e.Graphics, text, textFont, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Enabled ? Theme.Muted : Theme.DisabledText, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var x = Width - ScaleX(15F); var y = Height / 2F;
        if (_open) { e.Graphics.DrawLine(pen, x - 5, y + 2.5F, x, y - 2.5F); e.Graphics.DrawLine(pen, x, y - 2.5F, x + 5, y + 2.5F); }
        else { e.Graphics.DrawLine(pen, x - 5, y - 2.5F, x, y + 2.5F); e.Graphics.DrawLine(pen, x, y + 2.5F, x + 5, y - 2.5F); }
    }

    private int ScaleX(int value) => (int)Math.Round(value * DeviceDpi / 96F);
    private int ScaleX(float value) => (int)Math.Round(value * DeviceDpi / 96F);

    private void ShowOptions()
    {
        if (_open || Items.Count == 0 || !Enabled) return;
        Focus();
        _open = true; BorderColor = Theme.Accent; BorderWidth = 2; Invalidate();
        EnsurePopup();
        if (_popup is null || _options is null || _host is null) return;
        // Exactly one row per option. The popup used to be a Form sized through ClientSize,
        // which reads wrong while its handle is created, so three options showed five rows.
        var size = new Size(Width, Items.Count * ScaleX(ItemHeight) + ScaleX(8));
        _options.Size = size;
        _host.Size = size;
        _popup.Size = size;
        _options.ResetHover();
        var working = Screen.FromControl(this).WorkingArea;
        var desired = PointToScreen(new Point(0, Height + ScaleX(6)));
        var x = OpenUpward ? PointToScreen(new Point(Width - size.Width, 0)).X : desired.X;
        x = Math.Clamp(x, working.Left, Math.Max(working.Left, working.Right - size.Width));
        int y;
        if (OpenUpward)
            y = Math.Max(working.Top, PointToScreen(Point.Empty).Y - size.Height - ScaleX(6));
        else
            y = desired.Y + size.Height <= working.Bottom
                ? desired.Y
                : Math.Max(working.Top, PointToScreen(Point.Empty).Y - size.Height - ScaleX(6));
        // A ToolStripDropDown is a non-activating popup that closes on any outside click, like
        // a native combo box list. The old popup Form activated itself and hid on Deactivate,
        // which could hand activation to another application and put the whole window behind it.
        _popup.Show(new Point(x, y));
    }

    private void EnsurePopup()
    {
        if (_popup is { IsDisposed: false }) return;
        _options = new DropDownOptionsPanel(Items, () => SelectedIndex, index => SelectedIndex = index, () => ScaleX(ItemHeight))
        { Size = new Size(Width, Items.Count * ScaleX(ItemHeight) + ScaleX(8)) };
        _host = new ToolStripControlHost(_options) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty, Size = _options.Size };
        _popup = new DropDownPopup();
        _popup.Items.Add(_host);
        _popup.Closed += (_, _) => SetClosedState();
        _options.OptionChosen += (_, _) => { _popup.Close(ToolStripDropDownCloseReason.ItemClicked); Focus(); };
    }

    private void SetClosedState()
    {
        if (!_open) return;
        _open = false;
        BorderColor = Focused ? Theme.BlueHover : Theme.Border;
        BorderWidth = Focused ? 2 : 1;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_popup is { IsDisposed: false })
            {
                if (_popup.Visible) _popup.Close();
                _popup.Dispose();
            }
            _popup = null;
            _host = null;
            _options = null;
        }
        base.Dispose(disposing);
    }

    /// <summary>Borderless rounded popup; the options panel draws the border and rows.</summary>
    private sealed class DropDownPopup : ToolStripDropDown
    {
        public DropDownPopup()
        {
            AutoSize = false;
            AutoClose = true;
            DropShadowEnabled = false;
            Padding = Padding.Empty;
            BackColor = Color.White;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PopupFrame.ApplyRegion(this, 8, DeviceDpi);
        }
    }

    private sealed class DropDownOptionsPanel : Control
    {
        private readonly IReadOnlyList<string> _items;
        private readonly Func<int> _selected;
        private readonly Action<int> _choose;
        private readonly Func<int> _itemHeight;
        private int _hover = -1;
        public event EventHandler? OptionChosen;

        public DropDownOptionsPanel(IReadOnlyList<string> items, Func<int> selected, Action<int> choose, Func<int> itemHeight)
        {
            _items = items; _selected = selected; _choose = choose; _itemHeight = itemHeight;
            BackColor = Color.White; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void ResetHover() { _hover = -1; Invalidate(); }

        private int IndexAt(int y)
        {
            var pad = (int)Math.Round(4 * DeviceDpi / 96F);
            var height = Math.Max(1, _itemHeight());
            var index = (y - pad) / height;
            return index < 0 || index >= _items.Count ? -1 : index;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var next = IndexAt(e.Y); if (next != _hover) { _hover = next; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e); var index = IndexAt(e.Y);
            if (index < 0) return;
            _choose(index); OptionChosen?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var dpi = DeviceDpi / 96F;
            int S(int px) => (int)Math.Round(px * dpi);
            float Sf(float px) => (float)Math.Round(px * dpi);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.White);
            var pad = S(4);
            var height = Math.Max(1, _itemHeight());
            for (var i = 0; i < _items.Count; i++)
            {
                var row = new Rectangle(pad, pad + i * height, Width - pad * 2, height);
                if (i == _selected() || i == _hover)
                {
                    using var path = Theme.RoundedPath(row, Sf(5));
                    using var brush = new SolidBrush(i == _selected() ? Theme.AccentSoft : Theme.SegmentBg);
                    e.Graphics.FillPath(brush, path);
                }
                using var itemFont = Theme.UiFont(13.5F, i == _selected() ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(e.Graphics, _items[i], itemFont, new Rectangle(row.X + S(20), row.Y, row.Width - S(44), row.Height),
                    i == _selected() ? Theme.Primary : Theme.FieldText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                if (i == _selected())
                {
                    var check = UiV2Icons.Load(Ui2.CheckBlue, 16, DeviceDpi);
                    if (check is not null)
                    {
                        e.Graphics.DrawImage(check, row.X + S(5), row.Y + (row.Height - check.Height) / 2, check.Width, check.Height);
                        check.Dispose();
                    }
                }
            }
            PopupFrame.PaintBorder(e.Graphics, Size, 8, DeviceDpi, Theme.Border);
        }
    }
}

internal sealed class PageBadge : Label
{
    public PageBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        var scale = DeviceDpi / 96F;
        var side = (int)Math.Round(28 * scale);
        var textWidth = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, side), TextFormatFlags.NoPadding).Width;
        var width = Math.Min(ClientSize.Width, Math.Max(side, textWidth + (int)Math.Round(16 * scale)));
        var badge = new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - side) / 2, width, side);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = Theme.RoundedPath(badge, 4 * scale))
        using (var fill = new SolidBrush(Enabled ? Theme.Primary : Theme.DisabledFill))
            e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, badge, Enabled ? Color.White : Theme.DisabledText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>
/// Table layout section that draws a 1px hairline along one edge (toolbar / footer of the
/// records panel), so the bands read as separate from the table without extra controls.
/// </summary>
internal sealed class SectionPanel : TableLayoutPanel
{
    public SectionPanel(DockStyle dividerEdge)
    {
        DividerEdge = dividerEdge;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    public DockStyle DividerEdge { get; }
    public Color DividerColor { get; set; } = Theme.Divider;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var y = DividerEdge switch { DockStyle.Top => 0, DockStyle.Bottom => Height - 1, _ => -1 };
        if (y >= 0) e.Graphics.DrawLine(PaintCache.Pen(DividerColor), 0, y, Width, y);
    }
}

/// <summary>
/// Application logo for the title bar. The brand PNGs come in exact pixel sizes; the control
/// picks the closest size at or above its real pixel size for the current DPI and draws it with
/// high-quality scaling, so the fine mask lines stay crisp at 100%, 150% and 200%.
/// </summary>
internal sealed class AppLogo : Control
{
    private static readonly int[] Sizes = [16, 20, 24, 28, 32, 40, 48, 64, 96, 128, 256];
    private Bitmap? _image;
    private int _imageSize;

    public AppLogo()
    {
        AccessibleName = AppInfo.DisplayName;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    internal static string? PathFor(int pixels)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "assets", "brand");
        foreach (var size in Sizes.Where(x => x >= pixels).Concat(Sizes.Reverse()))
        {
            var path = Path.Combine(folder, $"app-icon-{size}.png");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var pixels = Math.Min(Width, Height);
        if (pixels <= 0) return;
        if (_image is null || _imageSize != pixels)
        {
            _image?.Dispose();
            _image = null;
            _imageSize = pixels;
            var path = PathFor(pixels);
            try
            {
                if (path is not null) { using var source = new Bitmap(path); _image = new Bitmap(source); _ink = InkBounds(_image); }
            }
            catch (Exception ex) { AppLog.Write($"应用图标加载失败：{path} · {ex.Message}"); }
        }
        if (_image is null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        // The larger PNGs keep a slightly uneven transparent margin; centre the visible tile,
        // not the bitmap, so the logo lines up with the title text at every scale.
        var scale = pixels / (float)_image.Width;
        var dx = (_image.Width - _ink.Right - _ink.Left) / 2F * scale;
        var dy = (_image.Height - _ink.Bottom - _ink.Top) / 2F * scale;
        e.Graphics.DrawImage(_image, new RectangleF((Width - pixels) / 2F + dx, (Height - pixels) / 2F + dy, pixels, pixels));
    }

    private Rectangle _ink;

    private static Rectangle InkBounds(Bitmap image)
    {
        int left = image.Width, top = image.Height, right = 0, bottom = 0;
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).A < 32) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
        return right > left ? Rectangle.FromLTRB(left, top, right, bottom) : new Rectangle(0, 0, image.Width, image.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
