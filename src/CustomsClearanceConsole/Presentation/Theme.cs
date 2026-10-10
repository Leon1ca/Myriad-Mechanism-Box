using System.Drawing.Drawing2D;

namespace CustomsClearanceConsole;

/// <summary>UI v2 icon stems (without size suffix) provided by the design package.</summary>
internal static class Ui2
{
    public const string Play = "play-white";
    public const string Export = "export-navy";
    public const string ExportDisabled = "export-disabled";
    public const string FileExcel = "file-excel-green";
    public const string FileMarkdownInk = "file-markdown-ink";
    public const string FileMarkdownBlue = "file-markdown-blue";
    public const string Camera = "camera-white";
    public const string CameraBlue = "camera-blue";
    public const string External = "external-navy";
    public const string CopyNavy = "copy-navy";
    public const string CopyInk = "copy-ink";
    public const string TrashInk = "trash-ink";
    public const string TrashRed = "trash-red";
    public const string TrashDisabled = "trash-disabled";
    public const string Folder = "folder-white";
    public const string FolderBlue = "folder-blue";
    public const string FolderUpload = "folder-upload-blue";
    public const string LockInk = "lock-ink";
    public const string LockDisabled = "lock-disabled";
    public const string Search = "search-muted";
    public const string Alert = "alert-amber";
    public const string CheckBlue = "check-blue";
    public const string CheckGreen = "check-green";
    public const string CheckWhite = "check-white";
    public const string Error = "error-red";
    public const string Info = "info-ink";
    public const string Close = "close-ink";
    public const string Minus = "minus-muted";
    public const string AmountEmpty = "amount-empty-disabled";
    public const string SettingsInk = "settings-ink";
    public const string SettingsWhite = "settings-white";
    public const string Stop = "stop-red";
    public const string ChevronDownNavy = "chevron-down-navy";
    public const string ChevronDownMuted = "chevron-down-muted";
    public const string ChevronUpMuted = "chevron-up-muted";
    public const string ChevronDownDisabled = "chevron-down-disabled";
}

/// <summary>Loads the design package PNG icons, choosing the render size for the current DPI.</summary>
internal static class UiV2Icons
{
    private static readonly int[] Sizes = [16, 24, 32, 48];
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);

    // Decoded masters per stem/size. Icons are drawn from OnPaint and per grid cell, so reading
    // and decoding the PNG from disk on every call made each repaint pay file I/O per visible row.
    // A null entry records a missing/unreadable file so it is not probed again.
    private static readonly Dictionary<string, Bitmap?> Decoded = new(StringComparer.Ordinal);

    /// <summary>Returns a caller-owned copy (dispose it) of the icon closest to the DPI-scaled size.</summary>
    public static Bitmap? Load(string stem, int logicalSize, float dpi)
    {
        var target = (int)Math.Round(logicalSize * dpi / 96F, MidpointRounding.AwayFromZero);
        var ordered = Sizes.OrderBy(size => Math.Abs(size - target)).ToArray();
        // GDI+ images are not safe to read from two threads at once, so the copy is made under
        // the same lock that guards the cache.
        lock (Decoded)
        {
            foreach (var size in ordered)
            {
                var master = Master(stem, size);
                if (master is null) continue;
                var copy = new Bitmap(master);
                copy.SetResolution(dpi, dpi);
                return copy;
            }
            if (Missing.Add(stem)) AppLog.Write($"缺少界面图标：{stem}");
            return null;
        }
    }

    private static Bitmap? Master(string stem, int size)
    {
        var key = $"{stem}|{size}";
        if (Decoded.TryGetValue(key, out var cached)) return cached;
        Bitmap? master = null;
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "ui-v2", "icons", "png", $"{stem}-{size}.png");
        if (File.Exists(path))
        {
            try
            {
                // Copy so the master does not keep the PNG file open for the process lifetime.
                using var source = new Bitmap(path);
                master = new Bitmap(source);
            }
            catch (Exception ex)
            {
                if (Missing.Add($"{path}|{ex.GetType().Name}")) AppLog.Write($"图标加载失败：{path} · {ex.Message}");
            }
        }
        Decoded[key] = master;
        return master;
    }

    public static Bitmap? Load(string stem, float dpi) => Load(stem, 16, dpi);

    public static bool AssetExists(string stem, int size) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "ui-v2", "icons", "png", $"{stem}-{size}.png"));
}

internal static class Theme
{
    public static readonly Color HeaderBg = UiTokens.Colors.HeaderBg;
    public static readonly Color Navy = UiTokens.Colors.HeaderBg;
    public static readonly Color Primary = UiTokens.Colors.Primary;
    public static readonly Color Blue = UiTokens.Colors.Primary;
    public static readonly Color BlueHover = UiTokens.Colors.HeaderBg;
    public static readonly Color Accent = UiTokens.Colors.Accent;
    public static readonly Color AccentSoft = UiTokens.Colors.AccentSoft;
    public static readonly Color Surface = UiTokens.Colors.Panel;
    public static readonly Color Canvas = UiTokens.Colors.Canvas;
    public static readonly Color Border = UiTokens.Colors.Border;
    public static readonly Color BorderControl = UiTokens.Colors.BorderControl;
    public static readonly Color Divider = UiTokens.Colors.Divider;
    public static readonly Color Text = UiTokens.Colors.Ink;
    public static readonly Color Ink2 = UiTokens.Colors.Ink2;
    public static readonly Color Muted = UiTokens.Colors.Muted;
    public static readonly Color Placeholder = UiTokens.Colors.Placeholder;
    public static readonly Color DisabledText = UiTokens.Colors.DisabledText;
    public static readonly Color DisabledFill = UiTokens.Colors.DisabledFill;
    public static readonly Color PanelSubtle = UiTokens.Colors.PanelSubtle;
    public static readonly Color SegmentBg = UiTokens.Colors.SegmentBg;
    public static readonly Color Danger = UiTokens.Colors.Danger;
    public static readonly Color DangerSoft = ColorTranslator.FromHtml("#FDECEA");
    public static readonly Color Success = UiTokens.Colors.Success;
    public static readonly Color Warning = UiTokens.Colors.WarningFg;
    public static readonly Color HeaderSoft = UiTokens.Colors.HeaderSoft;
    public static readonly Color CommandBar = UiTokens.Colors.PanelSubtle;
    public static readonly Color StrongValue = UiTokens.Colors.Ink;
    public static readonly Color FieldText = UiTokens.Colors.Ink;
    public static readonly Color DialogFooter = UiTokens.Colors.DialogFooter;
    public static readonly Color DetailButtonBg = UiTokens.Colors.DetailButtonBg;

    public static Font UiFont(float pixels, FontStyle style = FontStyle.Regular) =>
        style == FontStyle.Bold ? AppFonts.Ui(pixels, UiWeight.Bold) : AppFonts.Ui(pixels);

    public static Font MonoFont(float pixels) => AppFonts.Mono(pixels);
    public static Font MonoFont(float pixels, bool medium) => AppFonts.Mono(pixels, medium);

    public static Button PrimaryButton(string text) => Style(text, UiTokens.Colors.Primary, Color.White, UiTokens.Colors.Primary, UiTokens.Colors.HeaderBg, UiTokens.Colors.DisabledPrimaryFill);
    public static Button SecondaryButton(string text) => Style(text, Color.White, UiTokens.Colors.Primary, UiTokens.Colors.BorderControl, ColorTranslator.FromHtml("#F4F8FD"), UiTokens.Colors.DisabledFill);
    public static Button QuietButton(string text) => Style(text, Color.White, UiTokens.Colors.Ink2, UiTokens.Colors.BorderControl, UiTokens.Colors.SegmentBg, UiTokens.Colors.DisabledFill);
    public static Button DangerOutlineButton(string text) => Style(text, Color.White, UiTokens.Colors.Danger, ColorTranslator.FromHtml("#E4A6A0"), ColorTranslator.FromHtml("#FDECEA"), UiTokens.Colors.DisabledFill);
    public static Button DangerSolidButton(string text) => Style(text, UiTokens.Colors.Danger, Color.White, UiTokens.Colors.Danger, ColorTranslator.FromHtml("#9A1D14"), ColorTranslator.FromHtml("#D9A5A1"));
    public static Button DetailButton(string text) => Style(text, UiTokens.Colors.DetailButtonBg, UiTokens.Colors.Primary, UiTokens.Colors.DetailButtonBg, ColorTranslator.FromHtml("#E3E8EF"), UiTokens.Colors.DisabledFill);
    public static Button DangerButton(string text) => DangerOutlineButton(text);
    public static Button TonalButton(string text) => Style(text, UiTokens.Colors.AccentSoft, UiTokens.Colors.Accent, UiTokens.Colors.AccentSoft, ColorTranslator.FromHtml("#DCE8F8"), UiTokens.Colors.DisabledFill);

    public static Button IconButton(string text, Button baseButton, string? iconStem = null, int iconSize = 16, float dpi = 96F)
    {
        baseButton.Text = text;
        if (string.IsNullOrEmpty(baseButton.AccessibleName)) baseButton.AccessibleName = text;
        baseButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        baseButton.ImageAlign = ContentAlignment.MiddleCenter;
        if (iconStem is not null)
        {
            var image = UiV2Icons.Load(iconStem, iconSize, dpi);
            if (image is not null) baseButton.Image = image;
        }
        return baseButton;
    }

    private static Button Style(string text, Color back, Color fore, Color border, Color hover, Color disabledFill)
    {
        var button = new RoundedButton
        {
            // The factories own their caption: the previous implementation discarded the
            // text argument and every plain factory button rendered as an empty rectangle
            // (R4-1). The caption and accessible name are set here so callers that do not
            // re-assign Text (unlike IconButton) still show and announce their label.
            Text = text,
            AccessibleName = string.IsNullOrWhiteSpace(text) ? null : text,
            BackColor = back,
            ForeColor = fore,
            BorderColor = border,
            HoverBackColor = hover,
            PressedBackColor = Blend(hover, Color.Black, .05F),
            DisabledFill = disabledFill,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = AppFonts.Ui(14F, UiWeight.Medium),
            UseVisualStyleBackColor = false,
            TabStop = true,
            Radius = 6
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    public static Color Blend(Color foreground, Color background, float backgroundRatio)
    {
        backgroundRatio = Math.Clamp(backgroundRatio, 0F, 1F);
        var foregroundRatio = 1F - backgroundRatio;
        return Color.FromArgb(
            (int)(foreground.R * foregroundRatio + background.R * backgroundRatio),
            (int)(foreground.G * foregroundRatio + background.G * backgroundRatio),
            (int)(foreground.B * foregroundRatio + background.B * backgroundRatio));
    }

    public static GraphicsPath RoundedPath(RectangleF rectangle, float radius)
    {
        var diameter = Math.Max(1F, radius * 2F);
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
