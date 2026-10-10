namespace CustomsClearanceConsole;

/// <summary>Directory settings dialog (design spec 4.2, 520x400).</summary>
internal sealed class DirectorySettingsForm : DpiDialog
{
    private readonly TextBox _declarationFolder;
    private readonly TextBox _screenshotFolder;
    private readonly Label _helper;

    public string DeclarationFolder => _declarationFolder.Text.Trim();
    public string ScreenshotFolder => _screenshotFolder.Text.Trim();

    public DirectorySettingsForm(string declarationFolder, string screenshotFolder)
    {
        Text = "设置";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        LogicalClientSize = new Size(520, 400);
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;

        var frame = new RoundedPanel { Dock = DockStyle.Fill, WindowFrame = true, Radius = 10, BorderColor = Theme.Border, BackColor = Color.White };
        var header = new Panel { Location = new Point(1, 1), Size = new Size(518, 56), BackColor = Color.White };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            var inset = DpiLayout.Scale(20, header.DeviceDpi);
            e.Graphics.DrawLine(pen, inset, header.Height - 1, header.Width - inset, header.Height - 1);
        };
        header.Controls.Add(new Label
        {
            Text = "设置",
            Location = new Point(20, 0),
            // One pixel short of the header so the full-width divider below stays visible.
            Size = new Size(200, 55),
            Font = Theme.UiFont(16F, FontStyle.Bold),
            ForeColor = Theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        });
        var close = new CircleCloseButton { Location = new Point(462, 8) };
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        header.Controls.Add(close);

        var declarationLabel = FieldLabel("关单目录", 20, 74);
        var declarationField = PathDisplay(declarationFolder, "选择关单读取目录", out _declarationFolder);
        declarationField.Location = new Point(20, 100);
        declarationField.Size = new Size(392, 36);
        var browseDeclaration = BrowseButton();
        browseDeclaration.Location = new Point(420, 100);
        browseDeclaration.Click += (_, _) => ChooseFolder(_declarationFolder, "选择关单目录");
        var declarationNote = Note("只读取当前层；更换后作为新批次载入，识别中不可修改", 20, 142);

        var screenshotLabel = FieldLabel("截图目录", 20, 182);
        var screenshotField = PathDisplay(screenshotFolder, "选择核验截图保存目录", out _screenshotFolder);
        screenshotField.Location = new Point(20, 208);
        screenshotField.Size = new Size(392, 36);
        var browseScreenshot = BrowseButton();
        browseScreenshot.Location = new Point(420, 208);
        browseScreenshot.Click += (_, _) => ChooseFolder(_screenshotFolder, "选择截图目录");
        var screenshotNote = Note("网页长截图以 18 位报关单号命名保存到此处", 20, 250);

        _helper = new Label
        {
            Text = string.Empty,
            Location = new Point(20, 286),
            Size = new Size(480, 20),
            Font = Theme.UiFont(12.5F),
            ForeColor = Theme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        var footer = new Panel { Location = new Point(1, 319), Size = new Size(518, 80), BackColor = UiTokens.Colors.DialogFooter };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Divider);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var cancel = Theme.SecondaryButton("取消");
        cancel.Location = new Point(272, 22);
        cancel.Size = new Size(110, 36);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var save = Theme.PrimaryButton("保存");
        save.Location = new Point(392, 22);
        save.Size = new Size(110, 36);
        save.Click += (_, _) => SaveAndClose();
        footer.Controls.AddRange([cancel, save]);

        frame.Controls.AddRange([header, declarationLabel, declarationField, browseDeclaration, declarationNote,
            screenshotLabel, screenshotField, browseScreenshot, screenshotNote, _helper, footer]);
        Controls.Add(frame);
        AcceptButton = save;
        CancelButton = cancel;
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginDialogDrag(); };
    }

    private static Label FieldLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(300, 20),
        Font = Theme.UiFont(13F, FontStyle.Bold),
        ForeColor = Theme.FieldText,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Label Note(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(480, 18),
        Font = Theme.UiFont(12F),
        ForeColor = Theme.Muted,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Button BrowseButton()
    {
        var button = Theme.QuietButton("浏览…");
        button.Size = new Size(78, 36);
        return button;
    }

    private static RoundedPanel PathDisplay(string value, string placeholder, out TextBox textBox)
    {
        var panel = new RoundedPanel
        {
            Radius = 6,
            BorderColor = Theme.BorderControl,
            BackColor = Color.White,
            Padding = new Padding(12, 7, 10, 6)
        };
        textBox = new TextBox
        {
            Text = value,
            PlaceholderText = placeholder,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Theme.FieldText,
            Font = Theme.MonoFont(12.5F),
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        panel.Controls.Add(textBox);
        return panel;
    }

    private void ChooseFolder(TextBox target, string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(target.Text) ? target.Text : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        target.Text = dialog.SelectedPath;
        ShowHelper(string.Empty, false);
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(DeclarationFolder) || !Directory.Exists(DeclarationFolder))
        {
            ShowHelper(string.IsNullOrWhiteSpace(DeclarationFolder)
                ? "请选择关单读取目录。"
                : $"关单目录不存在或无法访问，请点“浏览…”重新选择：{DeclarationFolder}", true);
            _declarationFolder.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(ScreenshotFolder) || !Directory.Exists(ScreenshotFolder))
        {
            ShowHelper(string.IsNullOrWhiteSpace(ScreenshotFolder)
                ? "请选择截图保存目录。"
                : $"截图目录不存在或无法访问，请点“浏览…”重新选择：{ScreenshotFolder}", true);
            _screenshotFolder.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowHelper(string text, bool error)
    {
        _helper.Text = text;
        _helper.ForeColor = error ? Theme.Danger : Theme.Muted;
    }

    private void BeginDialogDrag()
    {
        Capture = false;
        var drag = Message.Create(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        WndProc(ref drag);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        PopupFrame.ApplyRegion(this, 10, DeviceDpi);
    }
}
