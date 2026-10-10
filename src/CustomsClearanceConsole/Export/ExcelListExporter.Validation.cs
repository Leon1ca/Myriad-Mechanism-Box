using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CustomsClearanceConsole;

internal static partial class ExcelListExporter
{

    private sealed record CellInfo(string Reference, int Column, string? Type, string? Text, string? Value, bool Formula);

    public static IReadOnlyList<string> Validate(string path, IReadOnlyList<DeclarationRecord>? expected = null)
    {
        var issues = new List<string>();
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var required in new[] { "xl/workbook.xml", "xl/styles.xml", "xl/worksheets/sheet1.xml", "xl/worksheets/sheet2.xml", "xl/worksheets/sheet3.xml" })
                if (archive.GetEntry(required) is null) issues.Add($"缺少 OOXML 部件：{required}");
            if (issues.Count > 0) return issues;

            var workbook = Load(archive, "xl/workbook.xml");
            var styles = Load(archive, "xl/styles.xml");
            var sheetNames = workbook.Descendants(Main + "sheet").Select(x => (string?)x.Attribute("name")).ToList();
            foreach (var name in new[] { "关单列表", "关单明细", "币种汇总" })
                if (!sheetNames.Contains(name)) issues.Add($"缺少工作表：{name}");

            var sheet1 = Load(archive, "xl/worksheets/sheet1.xml");
            var sheet2 = Load(archive, "xl/worksheets/sheet2.xml");
            var sheet3 = Load(archive, "xl/worksheets/sheet3.xml");
            foreach (var sheet in new[] { sheet1, sheet2, sheet3 })
                if (sheet.Descendants(Main + "f").Any())
                    issues.Add("工作簿包含公式节点，外来文本可能被当作公式执行。");

            var rows1 = ReadRows(sheet1);
            if (rows1.Count == 0) issues.Add("关单列表为空。");
            else AssertHeader(rows1[0], ["序号", "报关单号", "境外收货人", "合同协议号", "出境关别", "目的国", "关单总货值", "源文件", "识别状态", "需关注", "重复单号", "合计代表", "截图留存", "分项数"], "关单列表", issues);
            var rows2 = ReadRows(sheet2);
            if (rows2.Count == 0) issues.Add("关单明细为空。");
            else AssertHeader(rows2[0], ["记录序号", "报关单号", "源文件", "页码", "项号", "商品名称", "数量", "单位", "单价", "总价", "币制", "复核总价", "复核数量", "复核单价", "复核商品名称", "复核单位", "整项一致", "金额确认", "说明"], "关单明细", issues);
            var rows3 = ReadRows(sheet3);
            if (rows3.Count == 0) issues.Add("币种汇总为空。");
            else AssertHeader(rows3[0], ["币种", "去重前", "去重后（计入）", "重复扣减", "确认口径"], "币种汇总", issues);

            // R4-6: the raw XML keeps the full decimal, but a cell format with fewer decimal
            // places would round it on screen (e.g. 0.0004 shown as 0). Resolve each
            // quantity/unit-price cell's real number format and flag anything it cannot show.
            var numberFormats = ReadNumberFormats(styles);
            var cellXfs = styles.Descendants(Main + "cellXfs").Elements(Main + "xf")
                .Select(x => (int?)x.Attribute("numFmtId") ?? 0)
                .ToList();
            foreach (var cell in sheet2.Descendants(Main + "c"))
            {
                var column = ColumnIndex((string?)cell.Attribute("r") ?? "");
                if (column is not (7 or 9 or 13 or 14) || cell.Attribute("t") is not null) continue;
                var raw = cell.Element(Main + "v")?.Value;
                if (raw is null || !decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) continue;
                var styleIndex = (int?)cell.Attribute("s") ?? 0;
                if (styleIndex < 0 || styleIndex >= cellXfs.Count) continue;
                var displayedDecimals = DisplayedDecimalPlaces(ResolveNumberFormat(cellXfs[styleIndex], numberFormats));
                if (displayedDecimals is null) continue;
                if (value != 0 && decimal.Round(value, Math.Min(displayedDecimals.Value, 28)) != value)
                    issues.Add($"数量/单价单元格 {cell.Attribute("r")?.Value} 的值 {raw} 超出显示格式可保留的小数位，将显示为零或四舍五入。");
            }

            // A declaration number must always be an inline string, never a numeric value.
            foreach (var cell in sheet1.Descendants(Main + "c"))
            {
                if (cell.Attribute("t") is null && cell.Element(Main + "v") is { } value &&
                    value.Value.Length == 18 && value.Value.All(char.IsAsciiDigit))
                    issues.Add($"18 位报关单号被保存为数值：{value.Value}");
            }

            if (expected is not null)
            {
                var sorted = BatchScanner.SortRecords(expected).ToList();
                if (rows1.Count - 1 != sorted.Count) issues.Add($"关单列表行数 {rows1.Count - 1} 与本批 {sorted.Count} 条不一致。");
                var detailRows = rows2.Count - 1;
                var expectedDetails = sorted.Sum(x => x.LineTotals.Count);
                if (detailRows != expectedDetails) issues.Add($"关单明细行数 {detailRows} 与分项 {expectedDetails} 条不一致。");

                // Exact per-cell read-back for every declaration list row.
                for (var i = 0; i < sorted.Count; i++)
                {
                    var record = sorted[i];
                    var row = rows1[i + 1];
                    if (CellText(row.GetValueOrDefault(2)) != XmlSafe(record.DeclarationNo)) issues.Add($"第 {i + 1} 行报关单号与本批顺序不一致：{CellText(row.GetValueOrDefault(2))}");
                    if (record.HasValidDeclarationNo && row.GetValueOrDefault(2)?.Type != "inlineStr") issues.Add($"报关单号 {record.DeclarationNo} 不是文本单元。");
                    if (CellText(row.GetValueOrDefault(3)) != XmlSafe(record.Consignee)) issues.Add($"第 {i + 1} 行收货人丢失：{CellText(row.GetValueOrDefault(3))}");
                    if (CellText(row.GetValueOrDefault(4)) != XmlSafe(record.ContractNo)) issues.Add($"第 {i + 1} 行合同号丢失或转义错误：{CellText(row.GetValueOrDefault(4))}");
                    if (CellText(row.GetValueOrDefault(8)) != XmlSafe(record.SourceName)) issues.Add($"第 {i + 1} 行源文件丢失。");
                }

                // Leading zeros and formula-like text survive verbatim.
                var leadingZero = sorted.FirstOrDefault(x => x.DeclarationNo.StartsWith('0'));
                if (leadingZero is not null && !rows1.Skip(1).Any(r => CellText(r.GetValueOrDefault(2)) == leadingZero.DeclarationNo))
                    issues.Add($"前导零报关单号未按文本保留：{leadingZero.DeclarationNo}");
                var formulaLike = sorted.Select(x => x.ContractNo).FirstOrDefault(x => x.StartsWith('=') || x.StartsWith('+') || x.StartsWith('-') || x.StartsWith('@'));
                if (formulaLike is not null && !rows1.Skip(1).Any(r => CellText(r.GetValueOrDefault(4)) == formulaLike))
                    issues.Add($"公式型外来文本未按文本保留：{formulaLike}");

                // Currency summary values are present and numeric.
                var summaryText = rows3.Skip(1).SelectMany(r => r.Values).Where(c => c.Type == "inlineStr").Select(c => c.Text).ToList();
                foreach (var currency in sorted.SelectMany(x => x.Totals.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (!summaryText.Contains(currency)) issues.Add($"币种汇总缺少 {currency}。");

                // Detail sheet: missing descriptive values stay empty; verification fields survive.
                var expectedByNumber = sorted.GroupBy(x => x.DeclarationNo).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
                foreach (var group in rows2.Skip(1).GroupBy(r => CellText(r.GetValueOrDefault(2)) ?? ""))
                {
                    if (!expectedByNumber.TryGetValue(group.Key, out var source)) continue;
                    var sourceLines = source.SelectMany(x => x.LineTotals).OrderBy(x => x.PageNumber).ThenBy(x => x.Sequence).ToList();
                    var detailLines = group.ToList();
                    for (var i = 0; i < Math.Min(sourceLines.Count, detailLines.Count); i++)
                    {
                        var line = sourceLines[i];
                        var row = detailLines[i];
                        if (string.IsNullOrWhiteSpace(line.ProductName) && row.TryGetValue(6, out var product) && !string.IsNullOrEmpty(product.Text))
                            issues.Add($"缺失商品名称被写成空以外的值：{product.Text}");
                        if (line.Quantity is null && row.TryGetValue(7, out var quantity) && quantity.Value is not null)
                            issues.Add("缺失数量被写成数值，未保持为空。");
                        if (line.UnitPrice is null && row.TryGetValue(9, out var unitPrice) && unitPrice.Value is not null)
                            issues.Add("缺失单价被写成数值，未保持为空。");
                        if (line.Quantity is not null && row.TryGetValue(7, out var quantityCell) &&
                            (!decimal.TryParse(quantityCell.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var quantityValue) || quantityValue != line.Quantity.Value))
                            issues.Add($"数量精度丢失：期望 {line.Quantity.Value}，实际 {quantityCell.Value}。");
                        if (line.UnitPrice is not null && row.TryGetValue(9, out var unitPriceCell) &&
                            (!decimal.TryParse(unitPriceCell.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var unitPriceValue) || unitPriceValue != line.UnitPrice.Value))
                            issues.Add($"单价精度丢失：期望 {line.UnitPrice.Value}，实际 {unitPriceCell.Value}。");
                        if (!string.IsNullOrWhiteSpace(line.VerificationUnit) && CellText(row.GetValueOrDefault(16)) != XmlSafe(line.VerificationUnit))
                            issues.Add($"复核单位未导出：期望 {line.VerificationUnit}。");
                        // The overall verdict must never call a row consistent when the
                        // amount differs or when the second engine did not verify it.
                        var consistency = CellText(row.GetValueOrDefault(17));
                        if (line.HasValueDifference && consistency != "存在差异")
                            issues.Add($"存在复核差异的分项未被标记：{consistency}。");
                        if (consistency != line.ItemConsistency)
                            issues.Add($"分项整项结论写成“{consistency}”，应为“{line.ItemConsistency}”。");
                        if (!line.HasValueDifference && !line.IsFullyVerified && line.RuleCheckPassed is null && consistency != "未完整复核")
                            issues.Add($"未完整复核的分项被写成“{consistency}”，应为“未完整复核”。");
                        if (!line.HasValueDifference && line.IsFullyVerified && consistency != "一致")
                            issues.Add($"完整复核且一致的分项被写成“{consistency}”。");
                        var amountCheck = CellText(row.GetValueOrDefault(18));
                        if (line.HasAmountDifference && amountCheck != "金额不一致")
                            issues.Add($"金额不一致的分项金额确认写成“{amountCheck}”。");
                        if (amountCheck != line.AmountVerification)
                            issues.Add($"分项金额确认写成“{amountCheck}”，应为“{line.AmountVerification}”。");
                        if (!line.HasAmountDifference && line.AmountVerification == "金额未复核" && amountCheck != "金额未复核")
                            issues.Add($"未复核金额被写成“{amountCheck}”，应为“金额未复核”。");
                        if (!line.HasAmountDifference && line.AmountVerification == "金额一致" && amountCheck != "金额一致")
                            issues.Add($"复核金额一致的分项金额确认写成“{amountCheck}”。");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add($"xlsx 校验读取失败：{ex.Message}");
        }
        return issues;
    }

    private static void AssertHeader(IReadOnlyDictionary<int, CellInfo> row, IReadOnlyList<string> expected, string sheet, List<string> issues)
    {
        for (var i = 0; i < expected.Count; i++)
        {
            var actual = row.TryGetValue(i + 1, out var cell) ? cell.Text : null;
            if (actual != expected[i]) issues.Add($"{sheet} 表头第 {i + 1} 列应为“{expected[i]}”，实际“{actual}”。");
        }
    }

    private static string? CellText(CellInfo? cell) => cell?.Text;

    private static Dictionary<int, string> ReadNumberFormats(XDocument styles) =>
        styles.Descendants(Main + "numFmt")
            .Select(x => (Id: (int?)x.Attribute("numFmtId"), Code: (string?)x.Attribute("formatCode")))
            .Where(x => x.Id is not null && x.Code is not null)
            .ToDictionary(x => x.Id!.Value, x => x.Code!);

    /// <summary>Maps a style's numFmtId to its format code, covering the custom ids and the
    /// standard builtins 0-49 (0 = General in particular). Unknown ids yield null.</summary>
    private static string? ResolveNumberFormat(int numFmtId, IReadOnlyDictionary<int, string> custom)
    {
        if (custom.TryGetValue(numFmtId, out var code)) return code;
        return numFmtId switch
        {
            0 => "General",
            1 => "0",
            2 => "0.00",
            3 => "#,##0",
            4 => "#,##0.00",
            5 => "$#,##0_);($#,##0)",
            6 => "$#,##0_);[Red]($#,##0)",
            7 => "$#,##0.00_);($#,##0.00)",
            8 => "$#,##0.00_);[Red]($#,##0.00)",
            9 => "0%",
            10 => "0.00%",
            11 => "0.00E+00",
            12 => "# ?/?",
            13 => "# ??/??",
            14 => "mm-dd-yy",
            15 => "d-mmm-yy",
            16 => "d-mmm",
            17 => "mmm-yy",
            18 => "h:mm AM/PM",
            19 => "h:mm:ss AM/PM",
            20 => "h:mm",
            21 => "h:mm:ss",
            22 => "m/d/yy h:mm",
            37 => "#,##0 ;(#,##0)",
            38 => "#,##0 ;[Red](#,##0)",
            39 => "#,##0.00;(#,##0.00)",
            40 => "#,##0.00;[Red](#,##0.00)",
            41 => "_(* #,##0_);_(* (#,##0);_(* \"-\"_);_(@_)",
            42 => "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)",
            43 => "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)",
            44 => "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)",
            45 => "mm:ss",
            46 => "[h]:mm:ss",
            47 => "mmss.0",
            48 => "##0.0E+0",
            49 => "@",
            _ => null
        };
    }

    /// <summary>Maximum number of decimal places a format code can display, or null when the
    /// code cannot be interpreted (in which case the value is not constrained).</summary>
    private static int? DisplayedDecimalPlaces(string? formatCode)
    {
        if (string.IsNullOrWhiteSpace(formatCode)) return null;
        if (formatCode.Equals("General", StringComparison.OrdinalIgnoreCase)) return 15;
        // Only the first (positive) section governs this comparison; drop quoted literals and
        // escaped characters so only number placeholders remain.
        var section = formatCode.Split(';')[0];
        section = Regex.Replace(section, "\"[^\"]*\"", "");
        section = Regex.Replace(section, @"\\.", "");
        var dot = section.IndexOf('.');
        if (dot < 0) return 0;
        var count = 0;
        for (var i = dot + 1; i < section.Length; i++)
        {
            if (section[i] is '0' or '#') count++;
            else if (section[i] is '%' or 'E' or 'e') break;
        }
        return count;
    }

    private static List<Dictionary<int, CellInfo>> ReadRows(XDocument sheet)
    {
        var rows = new List<Dictionary<int, CellInfo>>();
        foreach (var row in sheet.Descendants(Main + "row"))
        {
            var cells = new Dictionary<int, CellInfo>();
            foreach (var cell in row.Elements(Main + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? "";
                var info = new CellInfo(
                    reference,
                    ColumnIndex(reference),
                    (string?)cell.Attribute("t"),
                    cell.Element(Main + "is")?.Element(Main + "t")?.Value,
                    cell.Element(Main + "v")?.Value,
                    cell.Element(Main + "f") is not null);
                cells[info.Column] = info;
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var character in reference)
        {
            if (character < 'A' || character > 'Z') break;
            index = index * 26 + (character - 'A' + 1);
        }
        return index;
    }

    private static XDocument Load(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }
}
