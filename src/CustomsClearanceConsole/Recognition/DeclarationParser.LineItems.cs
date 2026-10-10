using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomsClearanceConsole;

internal sealed partial class DeclarationParser
{

    private static List<DeclarationLineTotal> ReadLineTotals(IEnumerable<TextPage> pages)
    {
        var result = new List<DeclarationLineTotal>();
        foreach (var page in pages)
        {
            var pageTotals = new List<DeclarationLineTotal>();
            var header = FindPriceHeader(page);
            if (header is not null && header.Top >= page.Height * .07 && header.Top <= page.Height * .76)
            {
                var originHeader = FindAnchor(page, "原产国");
                var left = Math.Max(0, header.Left - page.Width * .02);
                var right = originHeader is not null &&
                            Math.Abs(originHeader.CenterY - header.CenterY) < Math.Max(5, page.Height * .018)
                    ? originHeader.Left - 1
                    : Math.Min(page.Width, header.Right + page.Width * .10);
                var footer = FindAnchor(page, "特殊关系确认", "支付特许权使用费确认", "价格影响确认");
                var bottom = footer?.Top ?? page.Height * .985;

                var column = page.Tokens.Where(t => IsRowText(page, t))
                    .Where(t => t.CenterX >= left && t.CenterX <= right)
                    .Where(t => t.Top > header.Bottom && t.Top < bottom)
                    .ToList();
                var lines = Lines(column, Math.Max(2.5, page.Height * .0048)).ToList();
                double? previousRowBottom = null;
                for (var i = 0; i < lines.Count; i++)
                {
                    var currency = NormalizeCurrencyInPriceColumn(page, lines[i], header);
                    if (currency is null) continue;

                    IReadOnlyList<TextToken>? totalLine = null;
                    decimal? total = TryAmountFromLine(lines[i], out var sameLineAmount) && sameLineAmount != 0
                        ? sameLineAmount
                        : null;
                    if (total is not null) totalLine = lines[i];

                    var currencyY = lines[i].Average(x => x.CenterY);
                    for (var j = i - 1; total is null && j >= 0; j--)
                    {
                        var candidateY = lines[j].Average(x => x.CenterY);
                        if (currencyY - candidateY > page.Height * .06) break;
                        if (TryAmountFromLine(lines[j], out var value)) { total = value; totalLine = lines[j]; }
                    }
                    if (total is null || totalLine is null) continue;
                    var searchTop = previousRowBottom ?? header.Bottom;
                    var itemToken = FindItemNoToken(page, searchTop, currencyY);
                    // Anchor the row at its own item number when present so wrapped
                    // product/unit lines from the previous row cannot leak into it.
                    var rowTop = itemToken is null ? searchTop : Math.Max(searchTop, itemToken.Top - Math.Max(2.5, page.Height * .0048));
                    var line = new DeclarationLineTotal
                    {
                        PageNumber = page.PageNumber,
                        ItemNo = itemToken?.Text.Trim() ?? "",
                        Currency = currency,
                        Amount = total.Value
                    };
                    AttachRowDetails(page, line, left, right, rowTop, currencyY, totalLine.Average(x => x.CenterY));
                    pageTotals.Add(line);
                    previousRowBottom = Math.Max(lines[i].Max(x => x.Bottom), itemToken?.Bottom ?? 0);
                }
            }

            // Some screenshots preserve every amount and currency but blur the compound
            // "单价/总价/币制" header. In that case, anchor each item at its currency row
            // and select the closest numeric line immediately above it.
            if (pageTotals.Count == 0) pageTotals = ReadLineTotalsFromCurrencyRows(page);
            result.AddRange(pageTotals);
        }
        for (var i = 0; i < result.Count; i++) result[i].Sequence = i + 1;
        NormalizeItemNumbers(result);
        return result;
    }

    private static void NormalizeItemNumbers(IReadOnlyList<DeclarationLineTotal> lines)
    {
        if (lines.Count == 0) return;
        var parsed = lines.Select(x => int.TryParse(x.ItemNo, out var item) ? item : 0).ToList();
        var distinctValid = parsed.Where(x => x > 0).Distinct().Count();
        var strictlyIncreasing = parsed.Where(x => x > 0).Zip(parsed.Where(x => x > 0).Skip(1), (a, b) => b > a).All(x => x);
        if (distinctValid >= Math.Ceiling(lines.Count * .7) && strictlyIncreasing)
        {
            for (var i = 0; i < lines.Count; i++)
                if (parsed[i] == 0) lines[i].ItemNo = (i + 1).ToString(CultureInfo.InvariantCulture);
            return;
        }
        for (var i = 0; i < lines.Count; i++) lines[i].ItemNo = (i + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static List<DeclarationLineTotal> ReadLineTotalsFromCurrencyRows(TextPage page)
    {
        var result = new List<DeclarationLineTotal>();
        var footer = FindAnchor(page, "特殊关系确认", "支付特许权使用费确认", "价格影响确认");
        var bottom = footer?.Top ?? page.Height * .985;
        var currencyTokens = page.Tokens
            .Where(t => t.Top >= page.Height * .32 && t.Top < bottom)
            .Where(t => NormalizeCurrencyInPriceColumn(page, [t], null) is not null)
            .ToList();

        double? previousRowBottom = null;
        foreach (var currencyLine in Lines(currencyTokens, Math.Max(2.5, page.Height * .0048)))
        {
            var currency = NormalizeCurrencyInPriceColumn(page, currencyLine, null);
            if (currency is null) continue;

            var currencyY = currencyLine.Average(x => x.CenterY);
            var left = Math.Max(0, currencyLine.Min(x => x.Left) - page.Width * .045);
            var right = Math.Min(page.Width, currencyLine.Max(x => x.Right) + page.Width * .065);
            var amountLines = Lines(page.Tokens.Where(t => IsRowText(page, t)).Where(t =>
                    t.CenterX >= left && t.CenterX <= right &&
                    t.CenterY < currencyY && currencyY - t.CenterY <= page.Height * .055),
                    Math.Max(2.5, page.Height * .0048))
                .OrderByDescending(line => line.Average(x => x.CenterY));

            foreach (var amountLine in amountLines)
            {
                if (!TryAmountFromLine(amountLine, out var total) || total <= 0) continue;
                var searchTop = previousRowBottom ?? page.Height * .30;
                var itemToken = FindItemNoToken(page, searchTop, currencyY);
                var rowTop = itemToken is null ? searchTop : Math.Max(searchTop, itemToken.Top - Math.Max(2.5, page.Height * .0048));
                var line = new DeclarationLineTotal
                {
                    PageNumber = page.PageNumber,
                    ItemNo = itemToken?.Text.Trim() ?? "",
                    Currency = currency,
                    Amount = total
                };
                AttachRowDetails(page, line, left, right, rowTop, currencyY, amountLine.Average(x => x.CenterY));
                result.Add(line);
                previousRowBottom = Math.Max(currencyLine.Max(x => x.Bottom), itemToken?.Bottom ?? 0);
                break;
            }
        }
        return result;
    }

    private static readonly HashSet<string> KnownUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "KG", "KGS", "千克", "公斤", "台", "个", "件", "套", "米", "吨", "辆", "只", "张", "双", "支",
        "箱", "包", "卷", "对", "副", "条", "床", "盏", "把", "块", "根", "顶", "本", "册", "部", "片", "粒", "瓶", "桶", "罐", "袋", "头", "匹", "PCS", "PCE", "SET", "CTN", "M", "平方米", "立方米", "升", "克"
    };

    /// <summary>
    /// Unit evidence for the quantity column. A token counts only when it is a complete known
    /// unit, optionally attached to a leading number such as "12.5KG" or "12.5千克"; substring
    /// matches are never accepted, so a word like "HEADSET" cannot be read as the unit "SET".
    /// </summary>
    private static bool IsKnownUnitToken(string text)
    {
        var candidate = text.Trim().Trim('(', ')', '（', '）', '/', '：', ':');
        if (candidate.Length == 0) return false;
        if (KnownUnits.Contains(candidate)) return true;
        // "12.5KG" / "12.5千克": the remainder after the numeric prefix must itself be a
        // complete known unit. Substring matching is never used, so "HEADSET"/"RESET"
        // (which merely contain "SET") are not accepted as unit evidence.
        var numeric = Regex.Match(candidate, @"^[0-9][0-9,.]*");
        return numeric.Success && numeric.Length < candidate.Length && KnownUnits.Contains(candidate[numeric.Length..]);
    }

    /// <summary>
    /// Horizontal band of the "数量" column, taken from its table header. When the header
    /// also covers the unit ("数量及单位"), only its leading part is treated as the
    /// quantity column so the unit sub-column cannot admit a number.
    /// </summary>
    private static (double Left, double Right)? FindQuantityColumn(TextPage page, double rowTop)
    {
        var header = page.Tokens
            .Where(t => t.Text.Contains("数量", StringComparison.Ordinal))
            .Where(t => t.Bottom <= rowTop + Math.Max(4, page.Height * .012))
            .OrderByDescending(t => t.Bottom)
            .FirstOrDefault();
        if (header is null) return null;
        var quantityFraction = header.Text.Contains("单位", StringComparison.Ordinal) ? .6 : 1.0;
        return (header.Left - page.Width * .005,
                header.Left + (header.Right - header.Left) * quantityFraction + page.Width * .01);
    }

    /// <summary>
    /// Best-effort extraction of product name, quantity, unit and unit price for one
    /// already-reconciled total row. Nothing is ever derived from the total amount:
    /// a value that cannot be located in the source stays empty and renders as “—”.
    /// </summary>
    private static void AttachRowDetails(TextPage page, DeclarationLineTotal line,
        double priceLeft, double priceRight, double rowTop, double currencyY, double totalLineY)
    {
        if (priceLeft >= priceRight) return;
        var tolerance = Math.Max(2.5, page.Height * .0048);
        var itemNoRight = page.Width * .085;
        var bandBottom = currencyY + page.Height * .008;

        // Unit price: the closest numeric line strictly above the total inside the row band.
        var unitPriceTokens = page.Tokens.Where(t => IsRowText(page, t))
            .Where(t => t.CenterX >= priceLeft - 1 && t.CenterX <= priceRight + 1)
            .Where(t => t.CenterY >= rowTop && t.CenterY < totalLineY - Math.Max(1, page.Height * .0015))
            .ToList();
        foreach (var candidate in Lines(unitPriceTokens, tolerance).OrderByDescending(l => l.Average(x => x.CenterY)))
        {
            if (!TryAmountFromLine(candidate, out var value) || value <= 0) continue;
            line.UnitPrice = value;
            break;
        }

        // Quantity and unit: a number is only promoted to a quantity when there is
        // explicit column evidence — it sits inside the "数量" header band, or a unit
        // token follows it on the same visual line. Otherwise a right-aligned model
        // number inside the product name (e.g. "2026") would be misattributed as the
        // quantity, so the field deliberately stays empty.
        var leftTokens = page.Tokens.Where(t => IsRowText(page, t))
            .Where(t => t.CenterX >= itemNoRight && t.CenterX < priceLeft - 1)
            .Where(t => t.CenterY >= rowTop && t.CenterY <= bandBottom)
            .ToList();
        var quantityColumn = FindQuantityColumn(page, rowTop);
        double? quantityLeft = null;
        var maxFragmentGap = page.Width * .015;
        // OCR usually reads a quantity with its unit as one token ("4辆", "5020千克"). Right-aligned,
        // such tokens can reach under the price header, so they are looked for up to the price
        // column's right edge; a number with a unit is never a price. The first one is the
        // declared quantity, the others are the legal/second-unit quantities.
        var quantityBand = page.Tokens.Where(t => IsRowText(page, t))
            .Where(t => t.CenterX >= priceLeft - page.Width * .18 && t.CenterX <= priceRight + 1)
            .Where(t => t.CenterY >= rowTop && t.CenterY <= bandBottom)
            .ToList();
        var withUnits = quantityBand
            .Select(t => (Token: t, Match: Regex.Match(t.Text.Trim(), @"^([0-9][0-9,.]*)\s*(\D+)$")))
            .Where(x => x.Match.Success && IsKnownUnitToken(x.Token.Text))
            .Select(x => (x.Token, Ok: TryAmount(x.Match.Groups[1].Value, out var value), Value: value, Unit: x.Match.Groups[2].Value.Trim()))
            .Where(x => x.Ok && x.Value > 0)
            .OrderBy(x => x.Token.CenterY)
            .ToList();
        line.QuantityCandidates.AddRange(withUnits.Select(x => x.Value));
        foreach (var token in quantityBand.Where(t => t.CenterX < priceLeft - 1))
            if (Regex.IsMatch(token.Text.Trim(), @"^[0-9][0-9,.]*$") && TryAmount(token.Text.Trim(), out var plain) && plain > 0)
                line.QuantityCandidates.Add(plain);
        if (withUnits.Count > 0)
        {
            // Select an actually printed quantity/unit pair that prices this item. The last
            // matching pair is the declared quantity in the three-unit form. Never divide
            // amount by price to invent a quantity.
            var selected = line.UnitPrice is { } price
                ? withUnits.LastOrDefault(x => PriceMatches(x.Value, price, line.Amount))
                : default;
            if (selected.Token is null) selected = withUnits[0];
            line.Quantity = selected.Value;
            line.Unit = selected.Unit;
            quantityLeft = withUnits.Min(x => x.Token.Left);
        }
        foreach (var visualLine in Lines(leftTokens, tolerance).OrderByDescending(l => l.Max(t => t.CenterX)))
        {
            if (line.Quantity is not null) break;
            var ordered = visualLine.OrderBy(t => t.Left).ToList();
            for (var k = ordered.Count - 1; k >= 0; k--)
            {
                var anchor = ordered[k];
                if (!Regex.IsMatch(anchor.Text.Trim(), @"^[0-9][0-9,.]*$")) continue;
                // The quantity sits in the column immediately left of the price column;
                // a number further left belongs to the product name/specification.
                if (anchor.CenterX < priceLeft - page.Width * .18) break;
                var fragments = new List<string>();
                var lastLeft = anchor.Left;
                for (var m = k; m >= 0; m--)
                {
                    var token = ordered[m];
                    var text = token.Text.Trim();
                    if (!Regex.IsMatch(text, @"^[0-9][0-9,.]*$")) break;
                    // Only merge fragments that are actually adjacent; never join two
                    // numbers separated by a gap (e.g. a model year and a quantity).
                    if (m < k && token.Right < lastLeft - maxFragmentGap) break;
                    fragments.Insert(0, text);
                    lastLeft = token.Left;
                }
                if (!TryAmount(string.Concat(fragments), out var quantity) || quantity <= 0) break;
                var inQuantityColumn = quantityColumn is { } column &&
                    anchor.CenterX >= column.Left && anchor.Right <= column.Right;
                // Unit evidence is adjacency based: the very next token must be a complete
                // unit and must sit right next to the number. Any later token on the line is
                // ignored, so a product word can never promote a model number to a quantity.
                var following = ordered.Count > k + 1 ? ordered[k + 1] : null;
                var adjacentUnit = following is not null &&
                    following.Left - anchor.Right <= maxFragmentGap &&
                    IsKnownUnitToken(following.Text);
                if (!inQuantityColumn && !adjacentUnit) break;
                line.Quantity = quantity;
                quantityLeft = anchor.Left;
                // Only a recognized unit token may be written; when the quantity came from
                // the header column alone the row keeps an empty unit.
                line.Unit = ordered.Skip(k + 1)
                    .Select(t => t.Text.Trim())
                    .FirstOrDefault(t => t.Length > 0 && IsKnownUnitToken(t)) ?? "";
                break;
            }
            if (line.Quantity is not null) break;
        }

        // Product name: everything else between the item number and the quantity/price column.
        var productRight = quantityLeft ?? priceLeft;
        var productTokens = page.Tokens
            // Diagonal watermarks have much taller character boxes than the row text.
            .Where(t => t.Bottom - t.Top <= Math.Max(12, page.Height * .028))
            .Where(t => t.CenterX >= itemNoRight && t.CenterX < productRight - 1)
            .Where(t => t.CenterY >= rowTop && t.CenterY <= bandBottom)
            .ToList();
        var product = string.Join(" ", Lines(productTokens, tolerance)
            .Select(Join)
            .Select(value => Regex.Replace(value.Trim(), @"^\d{10}(?=\D|$)\s*", ""))
            .Where(value => value.Length > 0 && !Regex.IsMatch(value, @"^[\p{P}\p{S}\s]+$")));
        line.ProductName = Regex.Replace(product, @"\s+", " ").Trim();

        // A quantity without a recognized unit token leaves the unit empty so the dialog
        // and exports show “—”; an arbitrary following word is never stored as the unit.
        if (line.Quantity is null) line.Unit = "";
        if (line.Unit.Length > 0 && KnownUnits.Contains(line.Unit)) line.Unit = line.Unit.ToUpperInvariant();
    }

    private static bool IsRowText(TextPage page, TextToken token) =>
        token.Bottom - token.Top <= Math.Max(12, page.Height * .028);

    private static TextToken? FindItemNoToken(TextPage page, double tableTop, double currencyY)
    {
        return page.Tokens
            .Where(t => t.CenterX <= page.Width * .085 && t.CenterY > tableTop)
            .Where(t => t.CenterY <= currencyY && currencyY - t.CenterY <= page.Height * .12)
            .Where(t => Regex.IsMatch(t.Text.Trim(), @"^(\d{1,3})$"))
            .OrderBy(t => Math.Abs(t.CenterY - currencyY))
            .ThenByDescending(t => t.CenterY)
            .FirstOrDefault();
    }

    internal static Dictionary<string, decimal> SumReliableLineTotals(IEnumerable<DeclarationLineTotal> lines)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Where(x => x.IsReliable && x.Amount > 0 && !string.IsNullOrWhiteSpace(x.Currency)))
            result[line.Currency] = result.GetValueOrDefault(line.Currency) + line.Amount;
        return result;
    }

    private static string? NormalizeCurrencyInPriceColumn(
        TextPage page,
        IReadOnlyCollection<TextToken> tokens,
        TextToken? priceHeader)
    {
        var joined = Join(tokens);
        var exact = CurrencyNames.Normalize(joined);
        if (exact is not null) return exact;

        // Low-resolution scans can reduce “美元” to a single “美” (primary OCR)
        // or confuse both characters as “上米” (verification OCR). Only accept
        // those aliases inside the normalized price column and item-table band,
        // so an isolated character elsewhere in the declaration cannot create a total.
        var compact = NormalizeLabel(joined);
        if (compact is not ("美" or "上米")) return null;

        var centerX = tokens.Average(x => x.CenterX);
        var centerY = tokens.Average(x => x.CenterY);
        if (centerX < page.Width * .45 || centerX > page.Width * .68 ||
            centerY < page.Height * .34 || centerY > page.Height * .78)
            return null;

        if (priceHeader is not null &&
            (centerY <= priceHeader.Bottom ||
             centerX < priceHeader.Left - page.Width * .035 ||
             centerX > priceHeader.Right + page.Width * .08))
            return null;

        return "USD";
    }

    private static bool TryAmountFromLine(IEnumerable<TextToken> line, out decimal value)
    {
        var numericFragments = line
            .OrderBy(x => x.Left)
            .Select(x => x.Text.Trim().Replace("，", ","))
            .Where(x => Regex.IsMatch(x, @"^[0-9][0-9,.]*$|^[.,][0-9]+$"))
            .ToList();
        if (numericFragments.Count == 0) { value = 0; return false; }
        return TryAmount(string.Concat(numericFragments), out value);
    }

    private static TextToken? FindPriceHeader(TextPage page)
    {
        var compound = FindAnchor(page, "单价总价币制", "单价/总价/币制", "总价币制");
        if (compound is not null) return compound;

        var currency = FindAnchor(page, "币制");
        var unitPrice = FindAnchor(page, "单价");
        if (currency is null) return null;
        if (unitPrice is not null && Math.Abs(unitPrice.CenterY - currency.CenterY) <= Math.Max(5, page.Height * .018))
            return Union([unitPrice, currency]);
        return currency;
    }

    private static bool TryAmount(string text, out decimal value)
    {
        var normalized = text.Trim().Replace(" ", "").Replace("，", ",");
        if (!AmountRegex().IsMatch(normalized)) { value = 0; return false; }
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out value);
    }
}
