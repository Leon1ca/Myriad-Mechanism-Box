using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CustomsClearanceConsole;

internal static partial class ExcelListExporter
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    // cellXfs indices
    private const int StyleText = 0;
    private const int StyleHeader = 1;
    private const int StyleBody = 2;
    private const int StyleInteger = 3;
    private const int StyleMoney = 4;
    private const int StyleQuantity = 5;
    private const int StyleUnitPrice = 6;
    private const int StyleBold = 7;

    public static void Save(string path, IReadOnlyList<DeclarationRecord> records, DateTime exportedAt) =>
        AtomicFile.Write(path, stream => Write(stream, records, exportedAt));

    public static void Write(Stream stream, IReadOnlyList<DeclarationRecord> records, DateTime exportedAt)
    {
        var sorted = BatchScanner.SortRecords(records).ToList();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Add(archive, "[Content_Types].xml", Serialize(BuildContentTypes()));
        Add(archive, "_rels/.rels", Serialize(BuildRootRels()));
        Add(archive, "xl/workbook.xml", Serialize(BuildWorkbook()));
        Add(archive, "xl/_rels/workbook.xml.rels", Serialize(BuildWorkbookRels()));
        Add(archive, "xl/styles.xml", Serialize(BuildStyles()));
        Add(archive, "xl/worksheets/sheet1.xml", Serialize(BuildListSheet(sorted)));
        Add(archive, "xl/worksheets/sheet2.xml", Serialize(BuildDetailSheet(sorted)));
        Add(archive, "xl/worksheets/sheet3.xml", Serialize(BuildSummarySheet(sorted)));
    }

    private static string Serialize(XDocument document) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" + document.ToString(SaveOptions.DisableFormatting);

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }

    // ---- workbook plumbing ----

    private static XDocument BuildContentTypes()
    {
        XElement Override(string part, string type) =>
            new(ContentTypes + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));
        return new XDocument(
            new XElement(ContentTypes + "Types",
                new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                Override("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                Override("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
                Override("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                Override("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                Override("/xl/worksheets/sheet3.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
    }

    private static XDocument BuildRootRels() => new(
        new XElement(PackageRel + "Relationships",
            new XElement(PackageRel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml"))));

    private static XDocument BuildWorkbook() => new(
        new XElement(Main + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", Rel.NamespaceName),
            new XElement(Main + "sheets",
                Sheet("关单列表", 1, "rId1"),
                Sheet("关单明细", 2, "rId2"),
                Sheet("币种汇总", 3, "rId3"))));

    private static XElement Sheet(string name, int id, string relId) =>
        new(Main + "sheet", new XAttribute("name", name), new XAttribute("sheetId", id), new XAttribute(Rel + "id", relId));

    private static XDocument BuildWorkbookRels() => new(
        new XElement(PackageRel + "Relationships",
            WorksheetRel("rId1", "worksheets/sheet1.xml"),
            WorksheetRel("rId2", "worksheets/sheet2.xml"),
            WorksheetRel("rId3", "worksheets/sheet3.xml"),
            new XElement(PackageRel + "Relationship",
                new XAttribute("Id", "rId4"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml"))));

    private static XElement WorksheetRel(string id, string target) =>
        new(PackageRel + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
            new XAttribute("Target", target));

    private static XDocument BuildStyles()
    {
        XElement Font(int size, bool bold, string? color = null) =>
            new(Main + "font",
                new XElement(Main + "sz", new XAttribute("val", size)),
                new XElement(Main + "name", new XAttribute("val", "Microsoft YaHei")),
                bold ? new XElement(Main + "b") : null,
                color is null ? null : new XElement(Main + "color", new XAttribute("rgb", color)));

        XElement Fill(string rgb) => new(Main + "fill",
            new XElement(Main + "patternFill", new XAttribute("patternType", "solid"),
                new XElement(Main + "fgColor", new XAttribute("rgb", rgb)),
                new XElement(Main + "bgColor", new XAttribute("indexed", "64"))));

        XElement Border() => new(Main + "border",
            new XElement(Main + "left", new XAttribute("style", "thin"), new XElement(Main + "color", new XAttribute("rgb", "FFDDE2E9"))),
            new XElement(Main + "right", new XAttribute("style", "thin"), new XElement(Main + "color", new XAttribute("rgb", "FFDDE2E9"))),
            new XElement(Main + "top", new XAttribute("style", "thin"), new XElement(Main + "color", new XAttribute("rgb", "FFDDE2E9"))),
            new XElement(Main + "bottom", new XAttribute("style", "thin"), new XElement(Main + "color", new XAttribute("rgb", "FFDDE2E9"))));

        XElement Xf(int fontId, int fillId, int borderId, int numFmtId, string? horizontal, bool wrap, bool verticalTop = false) =>
            new(Main + "xf",
                new XAttribute("numFmtId", numFmtId),
                new XAttribute("fontId", fontId),
                new XAttribute("fillId", fillId),
                new XAttribute("borderId", borderId),
                new XAttribute("applyNumberFormat", numFmtId != 0 ? 1 : 0),
                new XAttribute("applyFont", 1),
                new XAttribute("applyAlignment", 1),
                new XElement(Main + "alignment",
                    new XAttribute("horizontal", horizontal ?? "left"),
                    new XAttribute("vertical", verticalTop ? "top" : "center"),
                    wrap ? new XAttribute("wrapText", "1") : null));

        return new XDocument(
            new XElement(Main + "styleSheet",
                new XElement(Main + "numFmts",
                    NumFmt(164, "#,##0"),
                    NumFmt(165, "#,##0.00"),
                    NumFmt(166, "#,##0.############################"),
                    NumFmt(167, "#,##0.############################")),
                new XElement(Main + "fonts",
                    Font(11, false),
                    Font(11, true),
                    Font(11, false, "FF5B6778")),
                new XElement(Main + "fills",
                    new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "gray125"))),
                    Fill("FFF7F8FA")),
                new XElement(Main + "borders",
                    new XElement(Main + "border"),
                    Border()),
                new XElement(Main + "cellStyleXfs", Xf(0, 0, 0, 0, null, false)),
                new XElement(Main + "cellXfs",
                    Xf(0, 0, 0, 0, "left", false),        // StyleText
                    Xf(1, 2, 1, 0, "center", true),       // StyleHeader
                    Xf(0, 0, 0, 0, "left", true, true),   // StyleBody
                    Xf(0, 0, 0, 164, "right", false),     // StyleInteger
                    Xf(0, 0, 0, 165, "right", false),     // StyleMoney
                    Xf(0, 0, 0, 166, "right", false),     // StyleQuantity
                    Xf(0, 0, 0, 167, "right", false),     // StyleUnitPrice
                    Xf(1, 0, 0, 0, "left", false)),       // StyleBold
                new XElement(Main + "cellStyles", new XElement(Main + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0)))));
    }

    private static XElement NumFmt(int id, string code) =>
        new(Main + "numFmt", new XAttribute("numFmtId", id), new XAttribute("formatCode", code));

    // ---- sheet writers ----

    private static XDocument BuildListSheet(IReadOnlyList<DeclarationRecord> records)
    {
        var currencies = records.SelectMany(x => x.Totals.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var headers = new List<string>
        {
            "序号", "报关单号", "境外收货人", "合同协议号", "出境关别", "目的国", "关单总货值",
            "源文件", "识别状态", "需关注", "重复单号", "合计代表", "截图留存", "分项数"
        };
        headers.AddRange(currencies.Select(c => $"货值 {c}"));
        var widths = new List<int> { 6, 22, 34, 22, 14, 12, 30, 30, 16, 10, 10, 12, 12, 8 };
        widths.AddRange(currencies.Select(_ => 16));

        var rows = new List<List<Cell>> { headers.Select(h => Cell.OfText(h, StyleHeader)).ToList() };
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            var row = new List<Cell>
            {
                Cell.OfNumber(i + 1, StyleInteger),
                Cell.OfText(record.DeclarationNo),
                Cell.OfText(record.Consignee),
                Cell.OfText(record.ContractNo),
                Cell.OfText(record.ExitCustoms),
                Cell.OfText(record.DestinationCountry),
                Cell.OfText(record.Totals.Count == 0 ? "—" : record.DisplayTotal),
                Cell.OfText(record.SourceName),
                Cell.OfText(record.Status),
                Cell.OfText(record.NeedsAttention ? "是" : "否"),
                Cell.OfText(record.IsDuplicate ? "是" : "否"),
                Cell.OfText(!record.IsDuplicate ? "—" : record.IsCanonical ? "是" : "否"),
                Cell.OfText(record.HasScreenshot ? "已留存" : "—"),
                Cell.OfNumber(record.LineTotals.Count, StyleInteger)
            };
            foreach (var currency in currencies)
                row.Add(record.Totals.TryGetValue(currency, out var amount) ? Cell.OfNumber(amount, StyleMoney) : Cell.Empty);
            rows.Add(row);
        }
        return BuildSheet(rows, widths);
    }

    private static XDocument BuildDetailSheet(IReadOnlyList<DeclarationRecord> records)
    {
        var headers = new[]
        {
            "记录序号", "报关单号", "源文件", "页码", "项号", "商品名称", "数量", "单位", "单价", "总价", "币制",
            "复核总价", "复核数量", "复核单价", "复核商品名称", "复核单位", "整项一致", "金额确认", "说明"
        };
        var widths = new[] { 8, 22, 28, 6, 6, 30, 12, 10, 12, 14, 8, 14, 12, 12, 30, 10, 10, 10, 40 };
        var rows = new List<List<Cell>> { headers.Select(h => Cell.OfText(h, StyleHeader)).ToList() };
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            foreach (var line in record.LineTotals.OrderBy(x => x.PageNumber).ThenBy(x => x.Sequence))
            {
                rows.Add(
                [
                    Cell.OfNumber(i + 1, StyleInteger),
                    Cell.OfText(record.DeclarationNo),
                    Cell.OfText(record.SourceName),
                    Cell.OfNumber(line.PageNumber, StyleInteger),
                    Cell.OfText(line.ItemNo),
                    Cell.OfText(line.ProductName),
                    line.Quantity is null ? Cell.Empty : Cell.OfNumber(line.Quantity.Value, StyleQuantity),
                    Cell.OfText(line.Unit),
                    line.UnitPrice is null ? Cell.Empty : Cell.OfNumber(line.UnitPrice.Value, StyleUnitPrice),
                    Cell.OfNumber(line.Amount, StyleMoney),
                    Cell.OfText(line.Currency),
                    line.VerificationAmount is null ? Cell.Empty : Cell.OfNumber(line.VerificationAmount.Value, StyleMoney),
                    line.VerificationQuantity is null ? Cell.Empty : Cell.OfNumber(line.VerificationQuantity.Value, StyleQuantity),
                    line.VerificationUnitPrice is null ? Cell.Empty : Cell.OfNumber(line.VerificationUnitPrice.Value, StyleUnitPrice),
                    Cell.OfText(line.VerificationProductName),
                    Cell.OfText(line.VerificationUnit),
                    Cell.OfText(line.ItemConsistency),
                    Cell.OfText(line.AmountVerification),
                    Cell.OfText(line.Note)
                ]);
            }
        }
        return BuildSheet(rows, widths);
    }

    private static XDocument BuildSummarySheet(IReadOnlyList<DeclarationRecord> records)
    {
        var gross = BatchScanner.GrossTotals(records);
        var net = BatchScanner.DeduplicatedTotals(records);
        var currencies = gross.Keys.Union(net.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var headers = new[] { "币种", "去重前", "去重后（计入）", "重复扣减", "确认口径" };
        var widths = new[] { 10, 18, 18, 18, 40 };
        var rows = new List<List<Cell>> { headers.Select(h => Cell.OfText(h, StyleHeader)).ToList() };
        foreach (var currency in currencies)
        {
            var before = gross.GetValueOrDefault(currency);
            var after = net.GetValueOrDefault(currency);
            var deduction = after - before;
            rows.Add(
            [
                Cell.OfText(currency),
                Cell.OfNumber(before, StyleMoney),
                Cell.OfNumber(after, StyleMoney),
                deduction == 0 ? Cell.Empty : Cell.OfNumber(deduction, StyleMoney),
                Cell.OfText("仅汇总已确认的可靠分项；重复单号只计合计代表一份，不做汇率换算。")
            ]);
        }
        if (currencies.Count == 0)
            rows.Add([Cell.OfText("—"), Cell.Empty, Cell.Empty, Cell.Empty, Cell.OfText("尚无已确认金额。")]);
        return BuildSheet(rows, widths);
    }

    private readonly record struct Cell(string? Text, decimal? Number, int Style, bool HasValue)
    {
        public static Cell Empty => new(null, null, StyleText, false);
        public static Cell OfText(string? value, int style = StyleBody)
        {
            var text = XmlSafe(value);
            return new(text, null, style, text.Length > 0);
        }
        public static Cell OfNumber(decimal value, int style) => new(null, value, style, true);
    }

    /// <summary>
    /// Drops characters XML 1.0 cannot carry (C0 controls other than tab/CR/LF, lone surrogates,
    /// U+FFFE/U+FFFF). A PDF text layer can emit such control codes; one of them in any field
    /// made XDocument throw and the whole export fail. Valid surrogate pairs are kept.
    /// </summary>
    internal static string XmlSafe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }
            if (XmlConvert.IsXmlChar(c)) { builder?.Append(c); continue; }
            builder ??= new StringBuilder(value.Length).Append(value, 0, i);
        }
        return builder?.ToString() ?? value;
    }

    private static XDocument BuildSheet(IReadOnlyList<IReadOnlyList<Cell>> rows, IReadOnlyList<int> widths)
    {
        var sheetData = new XElement(Main + "sheetData");
        for (var r = 0; r < rows.Count; r++)
        {
            var rowElement = new XElement(Main + "row", new XAttribute("r", r + 1));
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = rows[r][c];
                if (!cell.HasValue) continue;
                var reference = ColumnName(c + 1) + (r + 1);
                if (cell.Number is not null)
                {
                    rowElement.Add(new XElement(Main + "c",
                        new XAttribute("r", reference),
                        new XAttribute("s", cell.Style),
                        new XElement(Main + "v", cell.Number.Value.ToString(CultureInfo.InvariantCulture))));
                }
                else
                {
                    rowElement.Add(new XElement(Main + "c",
                        new XAttribute("r", reference),
                        new XAttribute("s", cell.Style),
                        new XAttribute("t", "inlineStr"),
                        new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), cell.Text))));
                }
            }
            sheetData.Add(rowElement);
        }

        var dimension = rows.Count == 0 ? "A1" : $"A1:{ColumnName(rows.Max(r => r.Count))}{rows.Count}";
        var cols = new XElement(Main + "cols");
        for (var i = 0; i < widths.Count; i++)
            cols.Add(new XElement(Main + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", widths[i]), new XAttribute("customWidth", 1)));

        var freeze = new XElement(Main + "sheetViews",
            new XElement(Main + "sheetView",
                new XAttribute("workbookViewId", "0"),
                new XElement(Main + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen"))));

        return new XDocument(
            new XElement(Main + "worksheet",
                new XElement(Main + "dimension", new XAttribute("ref", dimension)),
                freeze,
                new XElement(Main + "sheetFormatPr", new XAttribute("defaultRowHeight", 15)),
                cols,
                sheetData));
    }

    private static string ColumnName(int index)
    {
        var name = "";
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }
}
