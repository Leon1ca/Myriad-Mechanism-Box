using System.Text;
using System.Text.Json;

namespace CustomsClearanceConsole;

/// <summary>
/// Builds a synthetic batch that exercises the export edge cases (18-digit text,
/// leading zeros, formula-like text, multiple currencies, duplicate/conflict rows,
/// missing fields and old history) and writes both export formats plus a report.
/// </summary>
internal static class ExportValidation
{
    public sealed record Report(bool Pass, int RecordCount, int CurrencyCount, int DetailRows, IReadOnlyList<string> Checks, IReadOnlyList<string> Issues);

    public static List<DeclarationRecord> BuildSyntheticBatch(int count = 60)
    {
        var records = new List<DeclarationRecord>();
        for (var i = 0; i < count; i++)
        {
            var number = i == 0
                ? "010120260000000001"
                : (310120260000000000L + i).ToString();
            var currency = i % 3 == 0 ? "USD" : i % 3 == 1 ? "CNY" : "EUR";
            // The conflict row's note and verification numbers are derived from the same
            // quantities/unit prices that produce the amount, so they can never drift.
            var quantity = 1200 + i;
            var unitPrice = 2.15m + i;
            var amount = quantity * unitPrice;
            var verifyQuantity = quantity + 1m;
            var verifyUnitPrice = unitPrice + 0.25m;
            var verifyAmount = verifyQuantity * verifyUnitPrice;
            var record = new DeclarationRecord
            {
                DeclarationNo = number,
                SourcePath = $"synthetic-{i + 1:D3}.pdf",
                Consignee = i == 8 ? "SPECIAL & CO <A> | \"B\" \\ END" : i % 5 == 0 ? "NORTH STAR TRADING COMPANY LIMITED" : $"SYNTHETIC BUYER {i + 1}",
                ContractNo = i == 1 ? "=SUM(A1:A2)" : i == 2 ? "000123" : i == 3 ? "+1-2" : i == 8 ? "HT-2026-NL\n0008" : $"HT-2026-{i + 1:D4}",
                ExitCustoms = i % 2 == 0 ? "大连湾海关" : "宁波北仑海关",
                DestinationCountry = i % 4 == 0 ? "美国" : i % 4 == 1 ? "日本" : i % 4 == 2 ? "德国" : "泰国",
                Status = i % 7 == 3 ? "需关注" : "OCR 识别完成",
                Warning = i % 7 == 3 ? "数量×单价与总价不符" : "",
                Confidence = 80 + i % 20,
                ScreenshotPath = i % 9 == 0 ? $"synthetic-{i + 1:D3}.png" : "",
                LineTotals =
                [
                    new DeclarationLineTotal
                    {
                        Sequence = 1, PageNumber = 1, ItemNo = "1",
                        ProductName = i % 6 == 1 ? "" : i == 9 ? "商品|含<标签>\\反斜杠" : $"合成商品 {i + 1}",
                        Quantity = i % 6 == 1 ? null : i == 10 ? 0.0004m : quantity,
                        Unit = i % 6 == 1 ? "" : i % 2 == 0 ? "KG" : "台",
                        UnitPrice = i % 6 == 1 ? null : i == 10 ? 0.1234567m : unitPrice,
                        Currency = currency,
                        Amount = amount,
                        IsReliable = i % 7 != 3,
                        Note = i % 7 == 3 ? $"数量×单价与总价不符（总价 {amount:N2}），未计入合计" : "数量×单价=总价，规则校验通过",
                        VerificationAmount = i % 7 == 3 ? verifyAmount : null,
                        VerificationQuantity = i % 7 == 3 ? verifyQuantity : null,
                        VerificationUnit = i % 7 == 3 ? "KG" : "",
                        VerificationUnitPrice = i % 7 == 3 ? verifyUnitPrice : null,
                        VerificationProductName = i % 7 == 3 ? $"合成商品 {i + 1}（复核）" : ""
                    }
                ]
            };
            if (i % 7 == 3) record.Totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase); // conflict line is not part of the confirmed total
            else record.Totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [currency] = record.LineTotals[0].Amount };
            if (i == 5)
            {
                // Old history: no saved line items at all.
                record.LineTotals = [];
                record.Totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["CNY"] = 2250.00m };
            }
            if (i is 11 or 12 or 13)
            {
                // Explicit consistency samples: an amount-only conflict, a fully verified
                // match, and a partially verified row must export different verdicts.
                var line = record.LineTotals[0];
                line.ProductName = "一致性样本";
                line.Quantity = 10m;
                line.Unit = "KG";
                line.UnitPrice = 10m;
                line.Amount = 100m;
                line.VerificationAmount = i == 11 ? 99m : 100m;
                line.VerificationProductName = "一致性样本";
                line.VerificationQuantity = 10m;
                line.VerificationUnit = "KG";
                line.VerificationUnitPrice = 10m;
                if (i == 13)
                {
                    line.VerificationProductName = "";
                    line.VerificationQuantity = null;
                    line.VerificationUnit = "";
                    line.VerificationUnitPrice = null;
                }
                line.IsReliable = i != 11;
                line.Note = i == 11 ? "金额不一致样本" : i == 12 ? "完整复核一致样本" : "部分复核样本";
                record.Totals = i == 11
                    ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [currency] = line.Amount };
            }
            records.Add(record);
        }

        // Duplicate group: same 18-digit number, different files, one canonical.
        var duplicate = new DeclarationRecord
        {
            DeclarationNo = records[0].DeclarationNo,
            SourcePath = "synthetic-duplicate.pdf",
            Consignee = records[0].Consignee,
            ContractNo = records[0].ContractNo,
            ExitCustoms = records[0].ExitCustoms,
            DestinationCountry = records[0].DestinationCountry,
            Status = "OCR 识别完成",
            Confidence = 95,
            Totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 100m },
            LineTotals = [new DeclarationLineTotal { Sequence = 1, PageNumber = 1, ItemNo = "1", ProductName = "重复样本", Quantity = 1, Unit = "件", UnitPrice = 100m, Currency = "USD", Amount = 100m }]
        };
        records.Add(duplicate);
        BatchScanner.MarkDuplicates(records);
        return records;
    }

    public static Report Run(string outputFolder, int count = 60)
    {
        Directory.CreateDirectory(outputFolder);
        var records = BuildSyntheticBatch(count);
        var exportedAt = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Unspecified);
        var sorted = BatchScanner.SortRecords(records).ToList();
        var xlsxPath = Path.Combine(outputFolder, "关单列表_合成样例.xlsx");
        var mdPath = Path.Combine(outputFolder, "关单列表_合成样例.md");
        var checks = new List<string>();
        var issues = new List<string>();

        ExcelListExporter.Save(xlsxPath, sorted, exportedAt);
        checks.Add("已生成 OOXML .xlsx");
        var workbookIssues = ExcelListExporter.Validate(xlsxPath, sorted);
        issues.AddRange(workbookIssues);
        if (workbookIssues.Count == 0)
            checks.Add("xlsx 独立回读通过：三表表头逐列、单元格类型与值、18 位/前导零/公式型文本、无公式节点、金额数值、复核单位与差异、缺值为空、币种汇总、>50 行与重复记录");

        var markdown = MarkdownListExporter.Render(sorted, exportedAt);
        File.WriteAllText(mdPath, markdown, new UTF8Encoding(false));
        checks.Add("已生成 Markdown");
        var sections = markdown.Split("## 关单 ").Length - 1;
        if (sections != sorted.Count) issues.Add($"Markdown 关单段落 {sections} 与本批 {sorted.Count} 条不一致。");
        foreach (var record in sorted.Where(x => x.HasValidDeclarationNo))
            if (!markdown.Contains(record.DeclarationNo)) issues.Add($"Markdown 缺少报关单号：{record.DeclarationNo}");
        if (!markdown.Contains("=SUM(A1:A2)") || !markdown.Contains("000123"))
            issues.Add("Markdown 未保留公式型外来文本或前导零。");
        var detailRows = sorted.Sum(x => x.LineTotals.Count);
        if (!markdown.Contains("商品名称") || !markdown.Contains("数量·单位") || !markdown.Contains("单价"))
            issues.Add("Markdown 缺少新增明细字段。");
        // Lossless decimals: the export must not round quantities or unit prices.
        if (!markdown.Contains("0.0004") || !markdown.Contains("0.1234567"))
            issues.Add("Markdown 数量/单价发生精度损失。");
        if (issues.Count == 0) checks.Add("Markdown 整批段落、明细字段、转义与无损小数校验通过");

        var currencyCount = sorted.SelectMany(x => x.Totals.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var report = new Report(issues.Count == 0, sorted.Count, currencyCount, detailRows, checks, issues);
        File.WriteAllText(Path.Combine(outputFolder, "export-validation.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return report;
    }
}
