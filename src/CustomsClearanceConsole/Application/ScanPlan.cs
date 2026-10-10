namespace CustomsClearanceConsole;

/// <summary>A validated snapshot. Creating a plan never changes the current batch.</summary>
internal sealed class ScanPlan
{
    private static readonly HashSet<string> Extensions = new(BatchScanner.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> Files { get; }
    private ScanPlan(List<string> files) => Files = files.AsReadOnly();
    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));

    public static ScanPlan FromFolder(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("请先设置有效的关单读取目录。");
        return FromFiles(Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly));
    }

    public static ScanPlan FromFiles(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).Where(IsSupported).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (files.Count == 0) throw new InvalidOperationException("没有可识别的关单文件。支持 PDF、PNG、JPG、BMP、TIF/TIFF。");
        if (files.Count > BatchScanner.MaximumFiles)
            throw new InvalidOperationException($"本批有 {files.Count} 个支持的文件，超过每批 {BatchScanner.MaximumFiles} 个的上限。请分批识别；当前列表保持不变。");
        return new ScanPlan(files);
    }
}

internal sealed class ScanSession(ScanPlan plan) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    public ScanPlan Plan { get; } = plan;
    public List<DeclarationRecord> Completed { get; } = [];
    public CancellationToken Token => _cancellation.Token;
    public void Cancel() => _cancellation.Cancel();
    public void Dispose() => _cancellation.Dispose();
}
