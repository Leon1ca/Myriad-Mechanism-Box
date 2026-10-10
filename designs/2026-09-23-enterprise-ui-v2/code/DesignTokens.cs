// 关单核验台 UI v2 设计令牌（与 tokens/design-tokens.json 一致）。
// 所有尺寸为 96 DPI 逻辑像素，使用前经 Ui.Scale(control, px) 按 DeviceDpi 缩放。
// 参考实现：放入 src/CustomsClearanceConsole/Ui/ 后按项目实际命名空间调整。

using System;
using System.Drawing;
using System.Windows.Forms;

namespace CustomsClearanceConsole.Ui;

public static class DesignTokens
{
    private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    public static class Colors
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

        /// <summary>弹窗遮罩：#0F1B2D，不透明度 45%。</summary>
        public static readonly Color Overlay = Color.FromArgb(115, 0x0F, 0x1B, 0x2D);
        public static readonly Color Danger = Hex("#B42318");
        public static readonly Color ToastBg = Hex("#0F1B2D");

        public static readonly Color EmptyStripe = Hex("#F3F5F8");
        public static readonly Color DropZoneBorder = Hex("#BCC6D3");
        public static readonly Color DropZoneBg = Hex("#FAFBFC");
        public static readonly Color KpiPlaceholder = Hex("#AEB9C8");
        public static readonly Color FocusRing = Hex("#D6E4F7");
    }

    /// <summary>业务状态色。Fg 用于文字/图标，Bg 用于标签底，Row 用于整行底色。</summary>
    public static class Status
    {
        public readonly record struct Palette(string Label, Color Fg, Color Bg, Color Row);

        public static readonly Palette Ok = new("正常", Hex("#17714F"), Hex("#E6F4EE"), Color.White);
        public static readonly Palette Duplicate = new("重复", Hex("#B42318"), Hex("#FDECEA"), Hex("#FFF6F5"));
        public static readonly Palette Attention = new("需关注", Hex("#8A5300"), Hex("#FDF1DC"), Hex("#FFFCF4"));
        public static readonly Palette Kept = new("已留存", Hex("#1F5FAE"), Hex("#EAF1FB"), Color.White);
        public static readonly Palette Failed = new("识别失败", Hex("#4A5566"), Hex("#EEF1F5"), Color.White);

        /// <summary>冲突金额虚线下划线色。</summary>
        public static readonly Color AttentionUnderline = Hex("#D9A441");
        /// <summary>金额汇总底部“未确认”提示条底色。</summary>
        public static readonly Color AttentionNotice = Hex("#FFFBF2");

        public static readonly Color RowHover = Hex("#F5F8FC");
        public static readonly Color CellSelected = Hex("#DCE8F8");
        public static readonly Color CellActiveBorder = Hex("#1F5FAE");
    }

    /// <summary>字号（px）。WinForms 字体以 pt 计，用 Type.Pt(px) 转换：pt = px × 72 / 96。</summary>
    public static class Type
    {
        public const float PageTitle = 22f;     // 700
        public const float KpiValue = 26f;      // 700
        public const float DialogTitle = 16f;   // 700
        public const float SectionTitle = 14f;  // 700
        public const float Button = 14f;        // 500
        public const float Body = 13.5f;        // 400
        public const float Label = 13f;         // 400
        public const float TableHeader = 12.5f; // 500
        public const float Caption = 12f;       // 400
        public const float Tag = 12f;           // 500
        public const float MicroTag = 11f;      // 400
        public const float Number = 13f;        // mono 500
        public const float Amount = 13.5f;      // mono 500
        public const float Currency = 11.5f;    // mono 400
        public const float Path = 12.5f;        // mono 400

        public static float Pt(float px) => px * 72f / 96f;
    }

    public static class Space
    {
        public const int XS = 4, S = 8, M = 12, L = 16, XL = 20, XXL = 24;
    }

    public static class Radius
    {
        public const int Tag = 4, Control = 6, Panel = 8, Dialog = 10;
    }

    public static class Metrics
    {
        public static readonly Size MinWindow = new(1200, 720);
        public static readonly Size DesignWindow = new(1440, 900);

        public const int Header = 48;
        public const int ContentPaddingY = 20;
        public const int ContentPaddingX = 24;

        public const int ButtonLarge = 40;
        public const int ButtonDialog = 36;
        public const int ButtonSmall = 28;
        public const int Input = 34;
        public const int TagHeight = 22;

        public const int Toolbar = 56;
        public const int TableHeader = 38;
        public const int TableRow = 54;
        public const int TableFooter = 44;
        public const int TableColumnGap = 12;
        public const int TablePaddingX = 16;

        public const int KpiPanelWidth = 480;
        public const int SearchWidth = 340;

        public static readonly Size DetailDialog = new(600, 480);
        public static readonly Size SettingsDialog = new(520, 400);
        public static readonly Size CleanupDialog = new(440, 260);
    }

    /// <summary>表格列宽（px）。Consignee 为填充列，其余固定。</summary>
    public static class Columns
    {
        public const int Index = 28, Status = 76, Number = 200, ConsigneeMin = 160,
            Contract = 150, Port = 110, Dest = 76, Amount = 160, Detail = 72, Verify = 92;
    }
}

/// <summary>
/// 窗口缩放规则（与 tokens/design-tokens.json → responsive 一致）。
/// 在 ClientSizeChanged 中用“逻辑像素”（ClientSize / (DeviceDpi / 96)）调用 Compute，再把结果经 Ui.Scale 应用到控件。
/// </summary>
public static class Responsive
{
    public sealed record Columns(
        int Index, int Status, int Number, int Consignee, int Contract,
        int Port, int Dest, int PortDest, bool PortDestMerged,
        int Amount, int Detail, int Verify);

    public sealed record Layout(
        bool CompactHeight, int KpiPanelWidth, int SearchWidth,
        int ContentPaddingY, int SectionGap, int KpiValuePx,
        int AmountHeader, int AmountColumnHeader, int AmountRow,
        int Toolbar, int TableRow, Columns Table);

    public const int ConsigneeMin = 180;
    public const int ConsigneeMax = 420;

    /// <param name="width">窗口客户区逻辑宽度（96 DPI）。</param>
    /// <param name="height">窗口客户区逻辑高度（96 DPI）。</param>
    public static Layout Compute(int width, int height)
    {
        width = Math.Max(width, DesignTokens.Metrics.MinWindow.Width);
        height = Math.Max(height, DesignTokens.Metrics.MinWindow.Height);

        var compact = height < 820;
        var kpiWidth = width < 1320 ? 420 : width >= 1680 ? 560 : 480;
        var searchWidth = width >= 1680 ? 420 : 340;

        // 表格可用宽度 = 窗口 - 左右内边距 24×2 - 面板边框 2 - 表格左右内边距 16×2
        var inner = width - 48 - 2 - 32;
        const int gap = DesignTokens.Metrics.TableColumnGap;

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
            consignee = Consignee(); // 余数归收货人列
        }

        var table = new Columns(index, status, number, consignee, contract,
            port, dest, portDest, merged, amount, detail, verify);

        return compact
            ? new Layout(true, kpiWidth, searchWidth, 16, 12, 22, 36, 28, 28, 50, 48, table)
            : new Layout(false, kpiWidth, searchWidth, 20, 16, 26, 40, 30, 32, 56, 54, table);
    }
}

public static class Ui
{
    /// <summary>把 96 DPI 设计尺寸换算为当前控件 DPI 下的像素。</summary>
    public static int Scale(Control control, int px) =>
        (int)Math.Round(px * control.DeviceDpi / 96f, MidpointRounding.AwayFromZero);

    public static Size Scale(Control control, Size size) =>
        new(Scale(control, size.Width), Scale(control, size.Height));
}
