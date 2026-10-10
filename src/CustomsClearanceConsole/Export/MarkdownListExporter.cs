using System.Globalization;
using System.Text;

namespace CustomsClearanceConsole;

internal static class MarkdownListExporter
{
    public static string Render(IReadOnlyList<DeclarationRecord> records, DateTime exportedAt)
    {
        var output = new StringBuilder();
        output.AppendLine("# 关单列表导出").AppendLine();
        output.AppendLine($"导出时间：{exportedAt:yyyy-MM-dd HH:mm:ss}  ");
        output.AppendLine($"本批共 {records.Count} 份文件；包含所有分页及重复记录，每份文件分别列出。").AppendLine();
        output.AppendLine("分项价格为每个商品项的总价。关单总价沿用列表中的已确认金额，按币种分别列出；未确认的分项保留供校对，不计入总价。").AppendLine();
        for (var i = 0; i < records.Count; i++)
        {
            if (i > 0) output.AppendLine("---").AppendLine();
            var record = records[i];
            output.AppendLine($"## 关单 {i + 1}").AppendLine();
            Table(output, ["信息", "完整内容"],
            [
                ["报关单号", record.DeclarationNo],
                ["境外收货人", record.Consignee],
                ["合同协议号", record.ContractNo],
                ["出境关别", record.ExitCustoms],
                ["目的国", record.DestinationCountry],
                ["源文件", record.SourceName],
                ["识别状态", record.Status + (record.IsDuplicate && !record.Status.Contains("重复单号") ? "；重复单号" : "")],
                ["重复处理", record.IsDuplicate ? record.IsCanonical ? "合计代表（计入去重合计）" : "不计入（去重合计只计代表一份）" : "—"],
                ["截图留存", record.HasScreenshot ? $"已留存（{record.ScreenshotPath}）" : "—"],
                ["识别提示", record.AllWarnings]
            ], [14, 64]);

            output.AppendLine("### 分项价格").AppendLine();
            if (record.LineTotals.Count == 0)
                output.AppendLine("未保存分项价格，请重新识别源文件后导出；此处不会把关单总价当作分项价格。").AppendLine();
            else
                Table(output, ["页码", "项号", "商品名称", "数量·单位", "单价", "币种", "分项总价", "复核总价", "校对说明"],
                    record.LineTotals.OrderBy(x => x.PageNumber).ThenBy(x => x.Sequence).Select(line => new[]
                    {
                        line.PageNumber.ToString(CultureInfo.InvariantCulture), line.ItemNo,
                        string.IsNullOrWhiteSpace(line.ProductName) ? "—" : line.ProductName,
                        line.ExactQuantityUnit,
                        line.ExactUnitPrice,
                        line.Currency,
                        Money(line.Amount), line.VerificationAmount.HasValue ? Money(line.VerificationAmount.Value) : "—",
                        (line.IsReliable ? "已确认" : "未确认，未计入总价") +
                        (line.HasValueDifference ? "；另一引擎内容不同：" + SecondaryDifference(line) : "") +
                        (string.IsNullOrWhiteSpace(line.Note) ? "" : "；" + line.Note)
                    }).ToList(), [6, 8, 30, 16, 12, 8, 20, 20, 40], [6, 7]);

            var incomplete = record.LineTotals.Any(line => !line.IsReliable) ||
                record.Status is "需关注" or "识别失败";
            output.AppendLine(incomplete ? "### 关单总价（已确认部分，需核对完整性）" : "### 关单总价").AppendLine();
            if (record.Totals.Count == 0)
                Table(output, ["币种", "关单总价"], [["—", "未识别，未计入合计"]], [12, 28]);
            else
                Table(output, ["币种", "关单总价"], record.Totals.OrderBy(x => x.Key)
                    .Select(total => new[] { total.Key, Money(total.Value) }).ToList(), [12, 28], [1]);
        }
        return output.ToString();
    }

    public static void Save(string path, IReadOnlyList<DeclarationRecord> records, DateTime exportedAt)
    {
        var content = Encoding.UTF8.GetBytes(Render(records, exportedAt));
        AtomicFile.Write(path, stream => stream.Write(content));
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string SecondaryDifference(DeclarationLineTotal line)
    {
        var parts = new List<string>();
        if (line.HasAmountDifference)
            parts.Add($"总价 {Money(line.VerificationAmount!.Value)}");
        if (!string.IsNullOrWhiteSpace(line.VerificationProductName) && !string.Equals(line.VerificationProductName, line.ProductName, StringComparison.Ordinal))
            parts.Add($"商品名称“{line.VerificationProductName}”");
        if (line.VerificationQuantity is not null && line.VerificationQuantity != line.Quantity)
            parts.Add($"数量 {NumberFormats.Exact(line.VerificationQuantity.Value)}");
        if (!string.IsNullOrWhiteSpace(line.VerificationUnit) && !string.Equals(line.VerificationUnit, line.Unit, StringComparison.Ordinal))
            parts.Add($"单位“{line.VerificationUnit}”");
        if (line.VerificationUnitPrice is not null && line.VerificationUnitPrice != line.UnitPrice)
            parts.Add($"单价 {NumberFormats.Exact(line.VerificationUnitPrice.Value)}");
        return parts.Count == 0 ? "复核引擎提供了不同内容" : string.Join("、", parts);
    }

    // Metadata gets its own wide value column. Pad Markdown source to the longest
    // cell instead of truncating; readers can wrap cells according to their viewport.
    private static void Table(StringBuilder output, string[] headers, IReadOnlyList<string[]> rows,
        int[] minimumWidths, int[]? rightAligned = null)
    {
        var escaped = rows.Select(row => row.Select(Cell).ToArray()).ToList();
        var widths = headers.Select((header, column) => Math.Max(minimumWidths[column],
            Math.Max(Width(header), escaped.Select(row => Width(row[column])).DefaultIfEmpty(0).Max()))).ToArray();
        void Row(string[] cells) => output.AppendLine("| " + string.Join(" | ", cells.Select((cell, column) =>
            cell + new string(' ', Math.Max(0, widths[column] - Width(cell))))) + " |");
        Row(headers);
        output.AppendLine("| " + string.Join(" | ", widths.Select((width, column) =>
            new string('-', width - 1) + ((rightAligned?.Contains(column) ?? false) ? ":" : "-"))) + " |");
        foreach (var row in escaped) Row(row);
        output.AppendLine();
    }

    private static int Width(string value) => value.EnumerateRunes().Sum(rune => rune.Value >= 0x2E80 ? 2 : 1);

    private static string Cell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\\", "&#92;").Replace("|", "&#124;").Replace("`", "&#96;")
            .Replace("*", "&#42;").Replace("_", "&#95;")
            .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
    }
}
