// 加载随程序分发的开源字体（SIL OFL 1.1），不依赖系统已安装字体。
//
// 目录约定（相对 关单核验台.exe 所在的 app 目录）：
//   fonts\NotoSansSC-Regular.ttf  fonts\NotoSansSC-Medium.ttf  fonts\NotoSansSC-Bold.ttf
//   fonts\JetBrainsMono-Regular.ttf  fonts\JetBrainsMono-Medium.ttf
//
// 为什么同时用两种注册方式：
//   * PrivateFontCollection：给 GDI+ 构造 Font 对象用。
//   * AddFontResourceEx(FR_PRIVATE)：让 GDI（TextRenderer、原生控件，即 UseCompatibleTextRendering = false 的默认路径）
//     在本进程内也能按字体名匹配到这些字体；否则 Label/Button/DataGridView 可能回退到系统字体。
//
// GDI+ 只识别 Regular / Bold 两种样式，Medium 字重以独立族名（如 "Noto Sans SC Medium"）出现，
// 因此下面按族名查找，找不到时回退到 Regular。
//
// 在 Program.Main 中、创建任何窗体之前调用 AppFonts.Initialize()。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole.Ui;

public enum UiWeight { Regular, Medium, Bold }

public static class AppFonts
{
    private const uint FR_PRIVATE = 0x10;

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string name, uint fl, IntPtr res);

    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    private static readonly string[] FontFiles =
    {
        "NotoSansSC-Regular.ttf", "NotoSansSC-Medium.ttf", "NotoSansSC-Bold.ttf",
        "JetBrainsMono-Regular.ttf", "JetBrainsMono-Medium.ttf",
    };

    public static void Initialize(string? fontDirectory = null)
    {
        if (_initialized) return;
        _initialized = true;

        fontDirectory ??= Path.Combine(AppContext.BaseDirectory, "fonts");
        foreach (var file in FontFiles)
        {
            var path = Path.Combine(fontDirectory, file);
            if (!File.Exists(path)) continue; // 缺失时回退到系统字体，不阻断启动
            Collection.AddFontFile(path);
            AddFontResourceEx(path, FR_PRIVATE, IntPtr.Zero);
        }

        foreach (var family in Collection.Families)
            Families[family.Name] = family;
    }

    /// <summary>界面文字字体（Noto Sans SC）。sizePx 为 96 DPI 设计像素。</summary>
    public static Font Ui(float sizePx, UiWeight weight = UiWeight.Regular) => weight switch
    {
        UiWeight.Bold => Create(new[] { "Noto Sans SC" }, sizePx, FontStyle.Bold),
        UiWeight.Medium => Create(new[] { "Noto Sans SC Medium", "Noto Sans SC" }, sizePx, FontStyle.Regular),
        _ => Create(new[] { "Noto Sans SC" }, sizePx, FontStyle.Regular),
    };

    /// <summary>单号、金额、路径等宽字体（JetBrains Mono）。</summary>
    public static Font Mono(float sizePx, bool medium = false) => medium
        ? Create(new[] { "JetBrains Mono Medium", "JetBrains Mono" }, sizePx, FontStyle.Regular)
        : Create(new[] { "JetBrains Mono" }, sizePx, FontStyle.Regular);

    private static Font Create(IEnumerable<string> candidates, float sizePx, FontStyle style)
    {
        var sizePt = DesignTokens.Type.Pt(sizePx);
        foreach (var name in candidates)
        {
            if (Families.TryGetValue(name, out var family) && family.IsStyleAvailable(style))
                return new Font(family, sizePt, style, GraphicsUnit.Point);
        }
        // 字体文件缺失时的兜底：系统默认界面字体
        return new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, sizePt, style, GraphicsUnit.Point);
    }

    /// <summary>用于诊断：列出已成功加载的字体族名。</summary>
    public static IReadOnlyList<string> LoadedFamilies => Families.Keys.OrderBy(n => n).ToList();
}
