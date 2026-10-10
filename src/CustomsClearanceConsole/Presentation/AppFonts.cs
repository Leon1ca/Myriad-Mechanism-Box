using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal enum UiWeight { Regular, Medium, Bold }

/// <summary>
/// Loads the open-source fonts shipped with the portable package (SIL OFL 1.1).
/// Both GDI+ (PrivateFontCollection) and GDI (AddFontResourceEx, FR_PRIVATE) are
/// registered so native controls that do not use compatible text rendering also
/// resolve the families. Missing files never block startup; a system fallback is used.
/// </summary>
internal static class AppFonts
{
    private const uint FrPrivate = 0x10;
    private const string UiFamily = "Noto Sans SC";
    private const string MonoFamily = "JetBrains Mono";

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);

    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    internal static readonly string[] FontFiles =
    [
        "NotoSansSC-Regular.ttf", "NotoSansSC-Medium.ttf", "NotoSansSC-Bold.ttf",
        "JetBrainsMono-Regular.ttf", "JetBrainsMono-Medium.ttf", "JetBrainsMono-Bold.ttf"
    ];

    internal static readonly string[] LicenseFiles = ["NotoSansSC-OFL.txt", "JetBrainsMono-OFL.txt"];

    internal static void Initialize(string? fontDirectory = null)
    {
        if (_initialized) return;
        _initialized = true;
        fontDirectory ??= Path.Combine(AppContext.BaseDirectory, "fonts");
        foreach (var file in FontFiles)
        {
            var path = Path.Combine(fontDirectory, file);
            if (!File.Exists(path)) continue;
            try
            {
                Collection.AddFontFile(path);
                AddFontResourceEx(path, FrPrivate, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLog.Write($"字体加载失败：{file} · {ex.Message}");
            }
        }
        foreach (var family in Collection.Families) Families[family.Name] = family;
    }

    /// <summary>
    /// Verifies that the package ships real static Regular/Medium/Bold faces and that
    /// GDI+ resolves the requested weights from the private collection. Returns a
    /// failure detail instead of a silent fallback so the cloud build can fail on it.
    /// </summary>
    internal static (bool Ok, string Detail) VerifyShipset()
    {
        var fontDirectory = Path.Combine(AppContext.BaseDirectory, "fonts");
        var missing = FontFiles.Where(name => !File.Exists(Path.Combine(fontDirectory, name))).ToList();
        if (missing.Count > 0) return (false, "缺少随包静态字体：" + string.Join("、", missing));
        var missingLicenses = LicenseFiles.Where(name => !File.Exists(Path.Combine(fontDirectory, name))).ToList();
        if (missingLicenses.Count > 0) return (false, "缺少字体原始许可：" + string.Join("、", missingLicenses));
        if (!HasUiFamily) return (false, "Noto Sans SC 未随包加载");
        if (!HasMonoFamily) return (false, "JetBrains Mono 未随包加载");
        if (FindFamily(UiFamily + " Medium") is null) return (false, "缺少 Noto Sans SC Medium 静态字重");
        if (FindFamily(MonoFamily + " Medium") is null) return (false, "缺少 JetBrains Mono Medium 静态字重");
        using (var regular = Ui(13F))
        using (var bold = Ui(13F, UiWeight.Bold))
        using (var medium = Ui(13F, UiWeight.Medium))
        using (var mono = Mono(13F))
        using (var monoMedium = Mono(13F, true))
        {
            if (!regular.FontFamily.Name.StartsWith(UiFamily, StringComparison.OrdinalIgnoreCase))
                return (false, "正文未解析到 Noto Sans SC：" + regular.FontFamily.Name);
            if (!bold.Bold || !bold.FontFamily.Name.StartsWith(UiFamily, StringComparison.OrdinalIgnoreCase))
                return (false, "Noto Sans SC Bold 未解析为真实粗体：" + bold.FontFamily.Name + "/" + bold.Style);
            if (!medium.FontFamily.Name.Contains("Medium", StringComparison.OrdinalIgnoreCase))
                return (false, "Noto Sans SC Medium 未解析到静态 Medium：" + medium.FontFamily.Name);
            if (!mono.FontFamily.Name.StartsWith(MonoFamily, StringComparison.OrdinalIgnoreCase))
                return (false, "等宽未解析到 JetBrains Mono：" + mono.FontFamily.Name);
            if (!monoMedium.FontFamily.Name.Contains("Medium", StringComparison.OrdinalIgnoreCase))
                return (false, "JetBrains Mono Medium 未解析到静态 Medium：" + monoMedium.FontFamily.Name);
            return (true, string.Join("；", new[]
            {
                $"regular={regular.FontFamily.Name}/{regular.Style}",
                $"bold={bold.FontFamily.Name}/{bold.Style}",
                $"medium={medium.FontFamily.Name}/{medium.Style}",
                $"mono={mono.FontFamily.Name}/{mono.Style}",
                $"monoMedium={monoMedium.FontFamily.Name}/{monoMedium.Style}"
            }));
        }
    }

    public static IReadOnlyList<string> LoadedFamilies => Families.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();

    public static bool HasUiFamily => FindFamily(UiFamily) is not null;
    public static bool HasMonoFamily => FindFamily(MonoFamily) is not null;

    public static Font Ui(float sizePx, UiWeight weight = UiWeight.Regular) => weight switch
    {
        UiWeight.Bold => Create([UiFamily], sizePx, FontStyle.Bold),
        UiWeight.Medium => Create([UiFamily + " Medium", UiFamily], sizePx, FontStyle.Regular),
        _ => Create([UiFamily], sizePx, FontStyle.Regular)
    };

    public static Font Mono(float sizePx, bool medium = false) => medium
        ? Create([MonoFamily + " Medium", MonoFamily], sizePx, FontStyle.Regular)
        : Create([MonoFamily], sizePx, FontStyle.Regular);

    private static FontFamily? FindFamily(string name) => Families.TryGetValue(name, out var family) ? family : null;

    private static Font Create(IEnumerable<string> candidates, float sizePx, FontStyle style)
    {
        var sizePt = sizePx * 72F / 96F;
        foreach (var name in candidates)
        {
            var family = FindFamily(name);
            if (family is null) continue;
            // Prefer the requested weight. A variable or single-weight face may not
            // expose it; try the real style (GDI/GDI+ can often synthesize it) and only
            // fall back to Regular within the same family when the style is rejected.
            try { return new Font(family, sizePt, style, GraphicsUnit.Point); }
            catch (ArgumentException) { }
            try { return new Font(family, sizePt, FontStyle.Regular, GraphicsUnit.Point); }
            catch (ArgumentException) { }
        }
        return new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, sizePt, style, GraphicsUnit.Point);
    }
}
