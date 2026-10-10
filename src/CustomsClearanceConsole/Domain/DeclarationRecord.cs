using System.Globalization;
using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

public sealed class DeclarationRecord
{
    public string SourcePath { get; set; } = "";
    public string DeclarationNo { get; set; } = "";
    public string Consignee { get; set; } = "";
    public string ContractNo { get; set; } = "";
    public string ExitCustoms { get; set; } = "";
    public string DestinationCountry { get; set; } = "";
    public Dictionary<string, decimal> Totals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DeclarationLineTotal> LineTotals { get; set; } = [];
    public int Confidence { get; set; }
    public string Status { get; set; } = "待识别";
    public string Warning { get; set; } = "";
    public string DuplicateWarning { get; set; } = "";
    public bool IsDuplicate { get; set; }
    public bool IsCanonical { get; set; } = true;
    public string ScreenshotPath { get; set; } = "";
    public DateTime ScannedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public bool HasValidDeclarationNo => DeclarationNo.Length == 18 && DeclarationNo.All(char.IsAsciiDigit);
    [JsonIgnore]
    public bool NeedsAttention => Status is "需关注" or "识别失败" || LineTotals.Any(x => !x.IsReliable);
    [JsonIgnore]
    public bool HasScreenshot => !string.IsNullOrWhiteSpace(ScreenshotPath) && File.Exists(ScreenshotPath);
    [JsonIgnore]
    public string DisplayStatus => string.Join(Environment.NewLine,
        new[] { IsDuplicate ? "重复单号" : "", NeedsAttention ? Status == "识别失败" ? "识别失败" : "需关注" : "", !IsDuplicate && !NeedsAttention ? "识别完成" : "" }.Where(x => x.Length > 0));
    [JsonIgnore]
    public string AllWarnings => string.Join("；", new[] { Warning, DuplicateWarning }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());

    [JsonIgnore]
    public string DisplayTotal => Totals.Count == 0
        ? "—"
        : string.Join("  /  ", Totals.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value:N2}"));

    [JsonIgnore]
    public string SourceName => Path.GetFileName(SourcePath);
}
