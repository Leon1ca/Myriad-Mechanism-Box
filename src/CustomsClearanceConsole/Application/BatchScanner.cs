namespace CustomsClearanceConsole;

internal sealed partial class BatchScanner : IAsyncDisposable
{
    private IAsyncDisposable? _ownedExtractor;
    public ValueTask DisposeAsync() => _ownedExtractor?.DisposeAsync() ?? ValueTask.CompletedTask;

    public const int MaximumFiles = 200;
    public static readonly string[] SupportedExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".tif", ".tiff", ".gif", ".webp"];
    private readonly Func<string, CancellationToken, Task<DocumentText>> _extract;
    internal BatchScanner(Func<string, CancellationToken, Task<DocumentText>> extract) => _extract = extract;
    private readonly DeclarationParser _parser = new();

    public Task<List<DeclarationRecord>> ScanAsync(string folder,
        IProgress<(int Done, int Total, string File)> progress, CancellationToken cancellationToken,
        IProgress<DeclarationRecord>? itemProgress = null) =>
        ScanAsync(ScanPlan.FromFolder(folder), progress, cancellationToken, itemProgress);

    public Task<List<DeclarationRecord>> ScanFilesAsync(IEnumerable<string> paths,
        IProgress<(int Done, int Total, string File)> progress, CancellationToken cancellationToken,
        IProgress<DeclarationRecord>? itemProgress = null) =>
        ScanAsync(ScanPlan.FromFiles(paths), progress, cancellationToken, itemProgress);

    public async Task<List<DeclarationRecord>> ScanAsync(ScanPlan plan,
        IProgress<(int Done, int Total, string File)> progress, CancellationToken cancellationToken,
        IProgress<DeclarationRecord>? itemProgress = null)
    {
        var files = plan.Files;
        var result = new List<DeclarationRecord>();
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report((i, files.Count, Path.GetFileName(files[i])));
            DeclarationRecord record;
            try
            {
                var text = await Task.Run(() => _extract(files[i], cancellationToken), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                record = _parser.Parse(files[i], text);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.Write($"识别失败：{files[i]}\n{ex}");
                record = new DeclarationRecord
                {
                    SourcePath = files[i], Status = "识别失败", Warning = ex.Message, Confidence = 0
                };
            }
            result.Add(record);
            itemProgress?.Report(record);
        }
        MarkDuplicates(result);
        progress.Report((files.Count, files.Count, "识别完成"));
        return SortRecords(result).ToList();
    }

    public static void MarkDuplicates(List<DeclarationRecord> records)
    {
        foreach (var record in records) { record.IsDuplicate = false; record.IsCanonical = true; record.DuplicateWarning = ""; }
        foreach (var group in records.Where(x => x.HasValidDeclarationNo).GroupBy(x => x.DeclarationNo).Where(x => x.Count() > 1))
        {
            var canonical = group.OrderByDescending(x => x.Confidence).ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).First();
            var valuesDiffer = group.Any(record => !SameContents(record, canonical));
            foreach (var record in group)
            {
                record.IsDuplicate = true;
                record.IsCanonical = ReferenceEquals(record, canonical);
                var duplicateWarning = valuesDiffer
                    ? "同一报关单号的识别内容不一致；去重合计采用完整度更高的一条"
                    : "当前批次存在相同报关单号；去重合计仅计一次";
                record.DuplicateWarning = duplicateWarning;
            }
        }
    }

    private static bool SameContents(DeclarationRecord a, DeclarationRecord b) =>
        a.Consignee == b.Consignee && a.ContractNo == b.ContractNo && a.ExitCustoms == b.ExitCustoms &&
        a.DestinationCountry == b.DestinationCountry && a.Totals.Count == b.Totals.Count &&
        a.Totals.All(x => b.Totals.TryGetValue(x.Key, out var value) && value == x.Value);

    public static IEnumerable<DeclarationRecord> SortRecords(IEnumerable<DeclarationRecord> records) =>
        records.OrderByDescending(x => x.IsDuplicate)
            .ThenBy(x => x.DeclarationNo.Length == 0)
            .ThenBy(x => x.DeclarationNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase);

    public static Dictionary<string, decimal> GrossTotals(IEnumerable<DeclarationRecord> records) => Sum(records);
    public static Dictionary<string, decimal> DeduplicatedTotals(IEnumerable<DeclarationRecord> records) => Sum(records.Where(x => x.IsCanonical));

    private static Dictionary<string, decimal> Sum(IEnumerable<DeclarationRecord> records)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in records.SelectMany(x => x.Totals)) result[pair.Key] = result.GetValueOrDefault(pair.Key) + pair.Value;
        return result;
    }
}
