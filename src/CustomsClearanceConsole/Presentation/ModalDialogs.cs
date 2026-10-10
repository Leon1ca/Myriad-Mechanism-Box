using System.Drawing.Drawing2D;

namespace CustomsClearanceConsole;

internal static class ModalPresenter
{
    public static DialogResult Show(Form dialog, Form owner)
    {
        using var backdrop = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Bounds = owner.Bounds,
            BackColor = ColorTranslator.FromHtml("#0E1C32"),
            Opacity = .46,
            AutoScaleMode = AutoScaleMode.None
        };
        backdrop.Show(owner);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(
            backdrop.Left + Math.Max(0, (backdrop.Width - dialog.Width) / 2),
            backdrop.Top + Math.Max(0, (backdrop.Height - dialog.Height) / 2));
        try { return dialog.ShowDialog(backdrop); }
        finally { backdrop.Close(); }
    }
}

internal sealed class CircleCloseButton : Control
{
    private bool _hover;

    public CircleCloseButton()
    {
        Size = new Size(44, 44);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = "关闭目录设置";
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Design 12-settings: a plain muted "×" whose hover state is a light circle.
        var scale = DeviceDpi / 96F;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        if (_hover)
        {
            using var brush = new SolidBrush(ColorTranslator.FromHtml("#EEF2F7"));
            e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }
        using var pen = new Pen(_hover ? Theme.Text : Theme.Muted, 1.6F * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = Width / 2F; var cy = Height / 2F; var arm = 5F * scale;
        e.Graphics.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
        e.Graphics.DrawLine(pen, cx + arm, cy - arm, cx - arm, cy + arm);
        if (Focused && ShowFocusCues)
        {
            using var focusPen = new Pen(Theme.Blue, 2F * scale);
            e.Graphics.DrawEllipse(focusPen, 2 * scale, 2 * scale, Width - 1 - 4 * scale, Height - 1 - 4 * scale);
        }
    }
}

internal enum CleanupKind { List, Declarations, Screenshots }

/// <summary>
/// Two-click cleanup confirmation (design spec 4.3). The final destructive button
/// is never the AcceptButton, so Enter cannot confirm it accidentally.
/// </summary>
internal sealed class CleanupDialog : DpiDialog
{
    private readonly CleanupKind _kind;
    private readonly int _fileCount;
    private readonly int _listCount;
    private readonly string _folder;
    private readonly Label _title;
    private readonly Label _body;
    private readonly TextBox _folderBox;
    private readonly Button _cancel;
    private readonly Button _primary;
    private readonly Label _step;
    private int _stepIndex = 1;

    public CleanupDialog(CleanupKind kind, int fileCount, int listCount, string folder)
    {
        _kind = kind;
        _fileCount = fileCount;
        _listCount = listCount;
        _folder = folder;
        LogicalClientSize = new Size(440, 260);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Color.White;

        var frame = new RoundedPanel { Dock = DockStyle.Fill, WindowFrame = true, Radius = 10, BorderColor = Theme.Border, BackColor = Color.White };
        _step = new Label
        {
            Text = string.Empty,
            Location = new Point(90, 22),
            Size = new Size(120, 18),
            Font = Theme.UiFont(11.5F),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var icon = new CleanupIcon(kind) { Location = new Point(28, 24), Size = new Size(44, 44) };
        _title = new Label
        {
            Text = string.Empty,
            Location = new Point(88, 38),
            Size = new Size(320, 28),
            Font = Theme.UiFont(16F, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _body = new Label
        {
            Text = string.Empty,
            Location = new Point(28, 92),
            Size = new Size(384, 52),
            Font = Theme.UiFont(13F),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.TopLeft
        };
        _folderBox = new TextBox
        {
            Location = new Point(28, 150),
            Size = new Size(384, 30),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Theme.Ink2,
            Font = Theme.MonoFont(12F),
            AccessibleName = "清理范围目录"
        };

        var footer = new Panel { Location = new Point(1, 190), Size = new Size(438, 69), BackColor = UiTokens.Colors.DialogFooter };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        _cancel = Theme.SecondaryButton("取消");
        _cancel.Location = new Point(192, 16);
        _cancel.Size = new Size(110, 36);
        _cancel.Click += (_, _) => HandleCancel();
        _primary = new Button();
        _primary.Location = new Point(312, 16);
        _primary.Size = new Size(110, 36);
        _primary.Click += (_, _) => HandlePrimary();
        frame.Controls.AddRange([_step, icon, _title, _body, _folderBox, footer]);
        footer.Controls.AddRange([_cancel, _primary]);
        Controls.Add(frame);
        AcceptButton = _cancel;
        CancelButton = _cancel;
        RenderStep();
    }

    private void RenderStep()
    {
        var unit = _kind == CleanupKind.Screenshots ? "张核验截图" : "个关单文件";
        if (_kind == CleanupKind.List)
        {
            _step.Text = string.Empty;
            _title.Text = "清空当前列表？";
            _body.Text = $"只清除本批 {_listCount} 条识别记录，不删除任何关单或截图文件。";
            _folderBox.Visible = false;
            _primary.Text = "清空列表";
            _primary.BackColor = Theme.Primary;
            _primary.ForeColor = Color.White;
            _primary.FlatStyle = FlatStyle.Flat;
            _primary.FlatAppearance.BorderSize = 0;
            _primary.Font = Theme.UiFont(14F);
            _cancel.Text = "取消";
            return;
        }

        _folderBox.Visible = true;
        _folderBox.Text = _folder + "（仅当前层）";
        if (_stepIndex == 1)
        {
            _step.Text = "确认 1 / 2";
            _title.Text = _kind == CleanupKind.Declarations ? "清理关单文件" : "清理核验截图";
            _body.Text = $"将 {_fileCount} {unit}移入 Windows 回收站。";
            _primary.Text = "继续";
            StyleDangerOutline(_primary);
            _cancel.Text = "取消";
        }
        else
        {
            _step.Text = "确认 2 / 2";
            _title.Text = "再次确认";
            _body.Text = $"确定将 {_fileCount} {unit}移入回收站吗？之后可从回收站恢复。";
            _primary.Text = "移入回收站";
            StyleDangerSolid(_primary);
            _cancel.Text = "返回";
        }
    }

    private void HandlePrimary()
    {
        if (_kind != CleanupKind.List && _stepIndex == 1)
        {
            _stepIndex = 2;
            RenderStep();
            return;
        }
        Confirm();
    }

    private void HandleCancel()
    {
        if (_kind != CleanupKind.List && _stepIndex == 2)
        {
            _stepIndex = 1;
            RenderStep();
            return;
        }
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void Confirm()
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void StyleDangerOutline(Button button)
    {
        button.BackColor = Color.White;
        button.ForeColor = Theme.Danger;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#E4A6A0");
        button.Font = Theme.UiFont(14F);
    }

    private static void StyleDangerSolid(Button button)
    {
        button.BackColor = Theme.Danger;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = Theme.UiFont(14F);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        PopupFrame.ApplyRegion(this, 10, DeviceDpi);
    }


    private sealed class CleanupIcon : Control
    {
        private readonly CleanupKind _kind;

        public CleanupIcon(CleanupKind kind)
        {
            _kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var back = new SolidBrush(_kind == CleanupKind.List ? Theme.SegmentBg : ColorTranslator.FromHtml("#FDECEA")))
                graphics.FillEllipse(back, 0, 0, Width - 1, Height - 1);
            var stem = _kind == CleanupKind.List ? Ui2.TrashInk : Ui2.TrashRed;
            var icon = UiV2Icons.Load(stem, 24, DeviceDpi);
            if (icon is not null)
            {
                graphics.DrawImage(icon, (Width - icon.Width) / 2, (Height - icon.Height) / 2, icon.Width, icon.Height);
                icon.Dispose();
            }
        }
    }
}
