using System.Drawing;

namespace CustomsClearanceConsole;

/// <summary>
/// UI v2 design tokens (96 DPI logical pixels), mirroring
/// designs/2026-09-23-enterprise-ui-v2/tokens/design-tokens.json.
/// Use <see cref="UiScale.Px"/> to convert a logical pixel value to the current DPI.
/// </summary>
internal static class UiTokens
{
    private static Color Hex(string value) => ColorTranslator.FromHtml(value);

    internal static class Colors
    {
        public static readonly Color HeaderBg = Hex("#0E2747");
        public static readonly Color Primary = Hex("#13355E");
        public static readonly Color Accent = Hex("#1F5FAE");
        public static readonly Color AccentSoft = Hex("#EAF1FB");

        public static readonly Color Ink = Hex("#0F1B2D");
        public static readonly Color Ink2 = Hex("#3D4A5C");
        public static readonly Color Muted = Hex("#5B6778");
        public static readonly Color Placeholder = Hex("#6B7686");
        public static readonly Color DisabledText = Hex("#8A94A3");
        public static readonly Color DisabledFill = Hex("#F7F8FA");
        public static readonly Color DisabledPrimaryFill = Hex("#C3CCD8");

        public static readonly Color Canvas = Hex("#F2F4F7");
        public static readonly Color Panel = Color.White;
        public static readonly Color PanelSubtle = Hex("#F7F8FA");
        public static readonly Color DialogFooter = Hex("#FAFBFC");
        public static readonly Color SegmentBg = Hex("#EEF1F5");
        public static readonly Color DetailButtonBg = Hex("#F0F3F7");

        public static readonly Color Border = Hex("#DDE2E9");
        public static readonly Color BorderControl = Hex("#C9D1DC");
        public static readonly Color BorderAccentSoft = Hex("#A9BEDA");
        public static readonly Color Divider = Hex("#EBEEF2");

        public static readonly Color Overlay = Color.FromArgb(115, 0x0F, 0x1B, 0x2D);
        public static readonly Color Danger = Hex("#B42318");
        public static readonly Color ToastBg = Hex("#0F1B2D");

        public static readonly Color EmptyStripe = Hex("#F3F5F8");
        public static readonly Color DropZoneBorder = Hex("#BCC6D3");
        public static readonly Color DropZoneBg = Hex("#FAFBFC");
        public static readonly Color KpiPlaceholder = Hex("#AEB9C8");
        public static readonly Color FocusRing = Hex("#D6E4F7");
        public static readonly Color HeaderSoft = Hex("#F1F3F6");
        public static readonly Color Success = Hex("#17714F");
        public static readonly Color WarningFg = Hex("#8A5300");
    }

    internal readonly record struct StatusPalette(string Label, Color Fg, Color Bg, Color Row);

    internal static class Status
    {
        public static readonly StatusPalette Ok = new("正常", Colors.Success, Hex("#E6F4EE"), Color.White);
        public static readonly StatusPalette Duplicate = new("重复", Colors.Danger, Hex("#FDECEA"), Hex("#FFF6F5"));
        public static readonly StatusPalette Attention = new("需关注", Colors.WarningFg, Hex("#FDF1DC"), Hex("#FFFCF4"));
        public static readonly StatusPalette Kept = new("已留存", Colors.Accent, Hex("#EAF1FB"), Color.White);
        public static readonly StatusPalette Failed = new("识别失败", Hex("#4A5566"), Hex("#EEF1F5"), Color.White);
        public static readonly Color AttentionUnderline = Hex("#D9A441");
        public static readonly Color AttentionNotice = Hex("#FFFBF2");
        public static readonly Color RowHover = Hex("#F5F8FC");
        public static readonly Color CellSelected = Hex("#DCE8F8");
    }

    internal static class Metrics
    {
        public static readonly Size MinWindow = new(1200, 720);
        public static readonly Size DesignWindow = new(1440, 900);
        public const int Header = 48;
        public const int ButtonLarge = 40;
        public const int ButtonDialog = 36;
        public const int ButtonSmall = 28;
        public const int Input = 34;
        public const int TagHeight = 22;
        public const int TableColumnGap = 12;
        public const int TablePaddingX = 16;
        public static readonly Size DetailDialog = new(600, 480);
        public static readonly Size SettingsDialog = new(520, 400);
        public static readonly Size CleanupDialog = new(440, 260);
    }
}

internal static class UiScale
{
    public static int Px(Control control, int px) =>
        (int)Math.Round(px * control.DeviceDpi / 96F, MidpointRounding.AwayFromZero);

    public static Size Px(Control control, Size size) => new(Px(control, size.Width), Px(control, size.Height));

    public static int Px(int dpi, int px) => (int)Math.Round(px * dpi / 96F, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Responsive layout rules (design spec section 9). All inputs and outputs are
/// 96 DPI logical pixels; call once per resize inside SuspendLayout/ResumeLayout.
/// </summary>
internal static class Responsive
{
    internal sealed record ColumnLayout(
        int Index, int Status, int Number, int Consignee, int Contract,
        int Port, int Dest, int PortDest, bool PortDestMerged,
        int Amount, int Detail, int Verify);

    internal sealed record Layout(
        bool CompactHeight, int KpiPanelWidth, int SearchWidth,
        int ContentPaddingY, int SectionGap, int KpiValuePx,
        int AmountHeader, int AmountColumnHeader, int AmountRow,
        int Toolbar, int TableRow, ColumnLayout Table);

    public const int ConsigneeMin = 180;
    public const int ConsigneeMax = 420;

    public static Layout Compute(int width, int height)
    {
        width = Math.Max(width, UiTokens.Metrics.MinWindow.Width);
        height = Math.Max(height, UiTokens.Metrics.MinWindow.Height);

        var compact = height < 820;
        var kpiWidth = width < 1320 ? 420 : width >= 1680 ? 560 : 480;
        var searchWidth = width >= 1680 ? 420 : 340;

        var inner = width - 48 - 2 - 32;
        const int gap = UiTokens.Metrics.TableColumnGap;

        int number = 200, contract = 150, port = 110, dest = 76, amount = 160, portDest = 120;
        const int index = 28, status = 76, detail = 72, verify = 92;
        var merged = false;

        int Consignee() => inner
            - (merged ? 8 : 9) * gap
            - (index + status + number + contract + (merged ? portDest : port + dest) + amount + detail + verify);

        var consignee = Consignee();
        if (consignee < ConsigneeMin)
        {
            number = 192; contract = 124; port = 96; dest = 64; amount = 150;
            consignee = Consignee();
        }
        if (consignee < ConsigneeMin)
        {
            merged = true;
            consignee = Consignee();
        }
        if (consignee > ConsigneeMax)
        {
            var extra = (consignee - ConsigneeMax) / 3;
            contract += extra; port += extra; dest += extra;
            consignee = Consignee();
        }

        var table = new ColumnLayout(index, status, number, consignee, contract,
            port, dest, portDest, merged, amount, detail, verify);

        return compact
            ? new Layout(true, kpiWidth, searchWidth, 16, 12, 22, 36, 28, 28, 50, 48, table)
            : new Layout(false, kpiWidth, searchWidth, 20, 16, 26, 40, 30, 32, 56, 54, table);
    }
}
