using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomsClearanceConsole;

internal sealed partial class DeclarationParser
{

    /// <summary>
    /// Consistency rules for both OCR and PDF text-layer readings. A text layer can contain
    /// incorrect source arithmetic or incomplete extracted rows. The values
    /// the declaration itself makes redundant are checked against each other.
    /// </summary>
    private static List<string> ApplyOcrRules(DeclarationRecord record, IReadOnlyList<TextPage> pages)
    {
        var problems = new List<string>();
        var first = pages[0];

        // The number is printed up to three times in the header (预录入编号, 海关编号, barcode
        // text); a misread digit in one of them shows up as a second distinct number.
        var numbers = page18DigitNumbers(first);
        if (numbers.Count > 1)
            problems.Add($"报关单号在表头读取不一致（{string.Join(" / ", numbers)}）");

        // Every line: some quantity of the row times the unit price must give the total.
        foreach (var line in record.LineTotals)
        {
            if (line.UnitPrice is not { } price || line.QuantityCandidates.Count == 0) continue;
            var matches = line.QuantityCandidates.Any(quantity =>
                PriceMatches(quantity, price, line.Amount));
            line.RuleCheckPassed = matches;
            if (matches)
            {
                line.Note = "数量×单价=总价，规则校验通过";
                continue;
            }
            line.IsReliable = false;
            line.Note = $"数量×单价与总价不符（单价 {price:0.####}，总价 {line.Amount:N2}），未计入合计";
            problems.Add($"第{line.PageNumber}页项{(string.IsNullOrWhiteSpace(line.ItemNo) ? line.Sequence.ToString(CultureInfo.InvariantCulture) : line.ItemNo)}数量×单价与总价不符");
        }
        if (record.LineTotals.Any(x => !x.IsReliable)) record.Totals = SumReliableLineTotals(record.LineTotals);
        foreach (var page in pages)
        {
            var expectedRows = ExpectedItemRows(page);
            var actualRows = record.LineTotals.Count(x => x.PageNumber == page.PageNumber);
            if (expectedRows > actualRows)
                problems.Add($"第{page.PageNumber}页检测到 {expectedRows} 个商品行，仅识别 {actualRows} 项金额，请核对完整性");
        }

        if (record.DestinationCountry.Length > 0 && !CountryNames.IsName(record.DestinationCountry))
            problems.Add($"目的国“{record.DestinationCountry}”不在国别表中");
        if (record.ExitCustoms.Length > 0 && !IsCustomsName(record.ExitCustoms))
            problems.Add($"出境关别“{record.ExitCustoms}”不像关区名称");
        return problems;

        // Only the title/number band above the first box: the boxes below hold 18-character
        // credit codes (境内发货人, 生产销售单位) that can be all digits.
        static List<string> page18DigitNumbers(TextPage page) => page.Tokens
            .Where(t => t.Bottom <= (FindAnchor(page, "境内发货人")?.Top ?? page.Height * .2))
            .SelectMany(t => DeclarationRegex().Matches(t.Text).Select(m => m.Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // Half of the printed price's last decimal place is the maximum rounding error.
    // Do not use a percentage of the total: it can hide a misread large amount.
    private static bool PriceMatches(decimal quantity, decimal price, decimal amount)
    {
        var places = (decimal.GetBits(price)[3] >> 16) & 0xff;
        var step = 1m;
        for (var i = 0; i < places; i++) step /= 10m;
        // An integer price is exact; a missing decimal point must not gain a 0.5 tolerance.
        var tolerance = .011m + (places == 0 ? 0 : quantity * step / 2m);
        return Math.Abs(quantity * price - amount) <= tolerance;
    }

    internal static int ExpectedItemRows(TextPage page)
    {
        var header = FindPriceHeader(page);
        if (header is null) return 0;
        var footer = FindAnchor(page, "特殊关系确认", "支付特许权使用费确认", "价格影响确认");
        var bottom = footer?.Top ?? page.Height * .985;
        var numbers = page.Tokens.Count(t => t.Top > header.Bottom && t.Top < bottom &&
            t.CenterX < page.Width * .085 && Regex.IsMatch(t.Text.Trim(), @"^[1-9][0-9]{0,2}$"));
        var codes = page.Tokens.Count(t => t.Top > header.Bottom && t.Top < bottom &&
            t.Left < page.Width * .2 && Regex.IsMatch(t.Text.Trim(), @"^\d{10}(?=\D|$)"));
        return Math.Max(numbers, codes);
    }
}
