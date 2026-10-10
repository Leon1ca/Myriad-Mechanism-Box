namespace CustomsClearanceConsole;

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
                var configuredFolder = Path.Combine(configured, "关单核验台数据");
                Directory.CreateDirectory(configuredFolder);
                return configuredFolder;
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(local, "关单核验台");
            try { Directory.CreateDirectory(folder); return folder; }
            catch
            {
                var fallbackRoot = Environment.GetEnvironmentVariable("CUSTOMS_CONSOLE_DATA");
                if (string.IsNullOrWhiteSpace(fallbackRoot)) fallbackRoot = Path.GetTempPath();
                var fallback = Path.Combine(fallbackRoot, "关单核验台数据");
                Directory.CreateDirectory(fallback); return fallback;
            }
        }
    }

    public static void EnsureRuntime()
    {
        if (!File.Exists(PdfiumDll))
            throw new FileNotFoundException("PDF 读取组件缺失，请重新解压完整的程序包。", PdfiumDll);
    }
}
