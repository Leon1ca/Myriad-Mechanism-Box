using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomsClearanceConsole;

internal sealed partial class DeclarationParser
{
    private static List<string> ReconcileRepeatedItems(DeclarationRecord record, IReadOnlyList<TextPage> pages)
    {
        var problems = new List<string>();
        if (pages.Count < 2) return problems;

        var identities = pages.GroupBy(page => page.PageNumber)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(page => FindAnchor(page, "海关编号", "预录入编号") is not null)
            .Select(page => (page.PageNumber, Numbers: HeaderDeclarationNumbers(page)))
            .Where(page => page.Numbers.Count == 1)
            .ToDictionary(page => page.PageNumber, page => page.Numbers[0]);
        var repeated = record.LineTotals
            .Where(line => identities.ContainsKey(line.PageNumber) && line.PrintedItemNo.Length > 0)
            .GroupBy(line => (DeclarationNo: identities[line.PageNumber], ItemNo: line.PrintedItemNo))
            .Where(group => group.Select(line => line.PageNumber).Distinct().Count() > 1)
            .ToList();
        if (repeated.Count == 0) return problems;

        var removed = new HashSet<DeclarationLineTotal>();
        foreach (var group in repeated)
        {
            var first = group.First();
            var complete = group.All(line => line.IsReliable && line.RuleCheckPassed == true &&
                line.CommodityCode.Length == 10 && line.ProductName.Length > 0 &&
                line.Quantity is not null && line.UnitPrice is not null && line.Unit.Length > 0);
            if (complete && group.Select(line => line.PageNumber).Distinct().Count() == group.Count() &&
                group.All(line => SameItem(first, line)))
            {
                foreach (var duplicate in group.Skip(1)) removed.Add(duplicate);
                continue;
            }

            var sourcePages = string.Join("、", group.Select(line => line.PageNumber).Distinct());
            var warning = $"报关单{group.Key.DeclarationNo}第{sourcePages}页项{group.Key.ItemNo}重复，内容不一致或复核不完整，请核对；该项未计入合计";
            foreach (var line in group)
            {
                line.IsReliable = false;
                line.Note = JoinWarnings(line.Note, warning);
            }
            problems.Add(warning);
        }

        // Completeness and price checks run on every original page before this merge.
        // Restore the printed numbers afterwards so duplicate 1–5 cannot become items 6–10.
        record.LineTotals.RemoveAll(removed.Contains);
        for (var i = 0; i < record.LineTotals.Count; i++)
        {
            var line = record.LineTotals[i];
            line.Sequence = i + 1;
            if (int.TryParse(line.PrintedItemNo, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
                line.ItemNo = number.ToString(CultureInfo.InvariantCulture);
        }
        record.Totals = SumReliableLineTotals(record.LineTotals);
        return problems;
    }

    private static bool SameItem(DeclarationLineTotal first, DeclarationLineTotal second) =>
        first.CommodityCode == second.CommodityCode &&
        CompactItemText(first.ProductName) == CompactItemText(second.ProductName) &&
        first.Quantity == second.Quantity && first.UnitPrice == second.UnitPrice && first.Amount == second.Amount &&
        string.Equals(first.Unit, second.Unit, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(first.Currency, second.Currency, StringComparison.OrdinalIgnoreCase);

    private static string CompactItemText(string value) =>
        Regex.Replace(value.Normalize(NormalizationForm.FormKC), @"\s+", "");
}
