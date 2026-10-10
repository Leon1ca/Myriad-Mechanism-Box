namespace CustomsClearanceConsole;

/// <summary>Official product name, used by every window title, dialog and data folder.</summary>
internal static class AppInfo
{
    public const string DisplayName = "千机匣";
    public const string EnglishName = "Myriad Mechanism Box";
    /// <summary>Data folder name used up to v0.1.0, migrated on first start of a newer version.</summary>
    public const string LegacyFolderName = "关单核验台";
}

internal static class AppPaths
{
    public static string BaseDirectory
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            return current.Name.Equals("app", StringComparison.OrdinalIgnoreCase) && current.Parent is not null
                ? current.Parent.FullName
                : current.FullName;
        }
    }
    public static string PdfiumDll => Path.Combine(BaseDirectory, "tools", "pdfium", "pdfium.dll");
    public static string RapidOcrModels => Path.Combine(AppContext.BaseDirectory, "ocr-models");
    public static string TempRoot => Path.Combine(Path.GetTempPath(), "CustomsClearanceConsole");
    public static string DataFolder
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("CUSTOMS_CONSOLE_DATA");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var configuredFolder = Path.Combine(configured, AppInfo.DisplayName + "数据");
                Directory.CreateDirectory(configuredFolder);
                return configuredFolder;
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(local, AppInfo.DisplayName);
            MigrateLegacyFolder(Path.Combine(local, AppInfo.LegacyFolderName), folder);
            if (_legacyFallback is not null && Directory.Exists(_legacyFallback)) return _legacyFallback;
            try { Directory.CreateDirectory(folder); return folder; }
            catch
            {
                var fallbackRoot = Environment.GetEnvironmentVariable("CUSTOMS_CONSOLE_DATA");
                if (string.IsNullOrWhiteSpace(fallbackRoot)) fallbackRoot = Path.GetTempPath();
                var fallback = Path.Combine(fallbackRoot, AppInfo.DisplayName + "数据");
                Directory.CreateDirectory(fallback); return fallback;
            }
        }
    }

    private static int _migrationChecked;

    /// <summary>
    /// History, settings, logs and the browser profile lived under the old product name. The
    /// folder is moved once to the new name so nothing is lost; if it cannot be moved (for
    /// example a file is in use), the old folder keeps being used until the next start.
    /// </summary>
    private static void MigrateLegacyFolder(string legacy, string current)
    {
        if (Interlocked.Exchange(ref _migrationChecked, 1) != 0) return;
        try
        {
            if (Directory.Exists(legacy) && !Directory.Exists(current)) Directory.Move(legacy, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _legacyFallback = legacy;
        }
    }

    private static string? _legacyFallback;

    public static void EnsureRuntime()
    {
        if (!File.Exists(PdfiumDll))
            throw new FileNotFoundException("PDF 读取组件缺失，请重新解压完整的程序包。", PdfiumDll);
    }
}
