using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomsClearanceConsole;

internal sealed partial class DeclarationParser
{

    private static string FindDeclarationNo(TextPage page)
    {
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Lines(page.Tokens.Where(t => t.Top < page.Height * .29), LineTolerance(page)))
        {
            var text = Join(line);
            var weight = text.Contains("海关编号") || text.Contains("关编号") ? 4
                : text.Contains("预录入") ? 2
                : text.Contains('*') ? 2
                : 1;
            foreach (Match match in DeclarationRegex().Matches(text))
                scores[match.Value] = scores.GetValueOrDefault(match.Value) + weight;
        }
        return scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key).FirstOrDefault() ?? "";
    }

    private static string ReadLabeledValue(TextPage page, params string[] labels)
    {
        var anchor = FindAnchor(page, labels);
        if (anchor is null) return "";

        var anchors = FindKnownAnchors(page);
        var sameRowTolerance = Math.Max(4, page.Height * .012);
        var rightAnchor = anchors
            .Where(x => x.Left > anchor.Right + 1 && Math.Abs(x.CenterY - anchor.CenterY) <= sameRowTolerance)
            .OrderBy(x => x.Left)
            .FirstOrDefault();
        var right = rightAnchor?.Left - 1 ?? Math.Min(page.Width, anchor.Left + page.Width * .34);
        var left = Math.Max(0, anchor.Left - page.Width * .004);

        var nextRow = anchors
            .Where(x => x.Top > anchor.Bottom + 1 && x.Top < anchor.Bottom + page.Height * .10)
            .Where(x => x.CenterX >= left && x.CenterX <= right)
            .OrderBy(x => x.Top)
            .FirstOrDefault();
        var bottom = Math.Min(anchor.Bottom + page.Height * .075, nextRow?.Top - 1 ?? double.MaxValue);

        var valueTokens = page.Tokens.Where(t =>
            t.CenterY >= anchor.Bottom - 1 && t.CenterY <= bottom + 1 &&
            t.CenterX >= left && t.CenterX <= right &&
            !Overlaps(t, anchor));
        return string.Join(" ", Lines(valueTokens, LineTolerance(page)).Select(Join)).Trim();
    }

    private static string ReadValueImmediatelyAboveLabel(TextPage page, params string[] labels)
    {
        var anchor = FindAnchor(page, labels);
        if (anchor is null) return "";

        var sameRowTolerance = Math.Max(4, page.Height * .012);
        var rightAnchor = FindKnownAnchors(page)
            .Where(x => x.Left > anchor.Right + 1 && Math.Abs(x.CenterY - anchor.CenterY) <= sameRowTolerance)
            .OrderBy(x => x.Left)
            .FirstOrDefault();
        var left = Math.Max(0, anchor.Left - page.Width * .006);
        var right = rightAnchor?.Left - 1 ?? Math.Min(page.Width, anchor.Left + page.Width * .34);
        var candidates = page.Tokens.Where(t =>
            t.CenterX >= left && t.CenterX <= right &&
            t.Bottom <= anchor.Top - 1 && t.Top >= anchor.Top - page.Height * .075);
        var closestLine = Lines(candidates, LineTolerance(page))
            .OrderByDescending(line => line.Average(x => x.CenterY))
            .FirstOrDefault();
        return closestLine is null ? "" : Join(closestLine).Trim();
    }

    private static string ReadExitCustoms(TextPage page, string declarationNo)
    {
        var office = FindCustomsOffice(page);
        if (string.IsNullOrWhiteSpace(office)) office = InferOfficeFromDeclaration(declarationNo);

        var anchor = FindAnchor(page, "出境关别");
        var codeText = anchor?.Text ?? "";
        if (anchor is not null)
        {
            var lineTokens = page.Tokens.Where(t =>
                Math.Abs(t.CenterY - anchor.CenterY) <= Math.Max(4, page.Height * .012) &&
                t.Left >= anchor.Left - 1 && t.Left <= anchor.Right + page.Width * .09);
            codeText += Join(lineTokens);
        }
        var code = CustomsCodeRegex().Match(codeText).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(code))
            code = CustomsCodeRegex().Match(ReadRegion(page, .28, .42, .14, .22)).Groups[1].Value;

        var exitName = CustomsShortByCode(code);
        if (string.IsNullOrWhiteSpace(exitName))
            exitName = ShortCustomsName(CleanValue(ReadLabeledValue(page, "出境关别"), "出境关别"));
        // The printed form repeats the customs name in brackets after 海关编号, e.g.
        // "海关编号：516620260000000017 （南沙新港）"; use it when the box value was not read.
        if (!IsCustomsName(exitName)) exitName = ReadHeaderCustomsName(page);

        if (string.IsNullOrWhiteSpace(office)) return exitName;
        if (string.IsNullOrWhiteSpace(exitName)) return office;
        if (office.Equals(exitName, StringComparison.OrdinalIgnoreCase)) return exitName;
        if (office.Contains("洋山") && exitName.Contains("洋山")) return exitName;
        return $"{office}/{exitName}";
    }

    private static bool IsCustomsName(string value) =>
        value.Length >= 2 && value.Count(x => x is >= '\u4e00' and <= '\u9fff') >= 2;

    private static string ReadHeaderCustomsName(TextPage page)
    {
        var anchor = FindAnchor(page, "海关编号");
        if (anchor is null) return "";
        var tolerance = Math.Max(4, page.Height * .012);
        var line = page.Tokens
            .Where(t => Math.Abs(t.CenterY - anchor.CenterY) <= tolerance && t.Left >= anchor.Left - 1 && t.Left <= anchor.Right + page.Width * .25)
            .OrderBy(t => t.Left);
        var match = Regex.Match(Join(line), @"[（(]\s*([\u4e00-\u9fff]{2,10})\s*[）)]");
        return match.Success ? ShortCustomsName(match.Groups[1].Value) : "";
    }

    private static string FindCustomsOffice(TextPage page)
    {
        foreach (var token in page.Tokens
                     .Where(t => t.Top < page.Height * .20 && t.Text.Contains("海关"))
                     .OrderBy(t => t.Top))
        {
            if (token.Text.Contains("报关单") || token.Text.Contains("海关编号") || token.Text.Contains("出境关别")) continue;
            var match = Regex.Match(token.Text, @"([\u4e00-\u9fff]{2,8})海关");
            if (match.Success) return ShortCustomsName(match.Groups[1].Value + "海关");
        }
        return "";
    }

    private static string ShortCustomsName(string value)
    {
        var result = Regex.Replace(value, @"[（）()\s]", "").Trim();
        result = CustomsCodeRegex().Replace(result, "");
        var namedCustoms = Regex.Match(result, @"([\u4e00-\u9fff]{2,8})海关");
        if (namedCustoms.Success) result = namedCustoms.Groups[1].Value;
        if (result.EndsWith("海关", StringComparison.Ordinal)) result = result[..^2];
        return result switch { "航交办" => "航交办", "洋山市内" => "洋山", _ => result };
    }

    private static string CustomsShortByCode(string code) => code switch
    {
        "3104" => "北仑",
        "5316" => "盐田",
        "2248" => "洋山港区",
        "2225" => "外高桥",
        "7207" => "东兴",
        "9402" => "霍尔果斯",
        _ => ""
    };

    private static string InferOfficeFromDeclaration(string number)
    {
        if (number.Length < 4) return "";
        return number[..4] switch
        {
            "3101" => "海曙", "2921" => "义乌", "5316" => "大鹏", "2231" => "洋山", "2229" => "航交办",
            "7207" => "东兴", "9402" => "霍尔果斯", _ => ""
        };
    }

    private static string CleanValue(string value, params string[] labels)
    {
        var result = value.Trim();
        foreach (var label in labels) result = result.Replace(label, "", StringComparison.OrdinalIgnoreCase);
        result = result.Trim(' ', ':', '：', '|');
        result = Regex.Replace(result, @"\s+([,.])", "$1");
        return Regex.Replace(result, @"\s+", " ");
    }

    internal static string NormalizeConsigneeIdentifiers(string value)
    {
        value = ReconnectDetachedIdentifierSeparators(value);
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var match = Regex.Match(part, @"^([A-Z]{2}-[A-Z]{3})([0-9OIL]{2})$", RegexOptions.IgnoreCase);
            if (!match.Success) return part;
            var suffix = match.Groups[2].Value.ToUpperInvariant()
                .Replace('O', '0').Replace('I', '1').Replace('L', '1');
            return match.Groups[1].Value.ToUpperInvariant() + suffix;
        }));
    }

    private static string ReconnectDetachedIdentifierSeparators(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return value;
        var segments = parts.Where(x => Regex.IsMatch(x, @"^[A-Z0-9]{1,16}$", RegexOptions.IgnoreCase)).ToArray();
        var separatorTokens = parts.Where(x => Regex.IsMatch(x, @"^[_./-]+$")).ToArray();
        var separators = string.Concat(separatorTokens).ToCharArray();
        if (segments.Length < 2 || separators.Length != segments.Length - 1 ||
            parts.Length != segments.Length + separatorTokens.Length ||
            segments.Any(x => !x.Any(char.IsLetter))) return value;

        var rebuilt = new StringBuilder(segments[0].ToUpperInvariant());
        for (var i = 1; i < segments.Length; i++)
            rebuilt.Append(separators[i - 1]).Append(segments[i].ToUpperInvariant());
        return rebuilt.ToString();
    }

    private static string NormalizeContractOcr(string value)
    {
        var compact = Regex.Replace(value, @"\s+", "").ToUpperInvariant();
        var compound = Regex.Match(compact, @"^([A-NP-Z]{1,6})([0-9O]{2,})([A-NP-Z]{1,6})([0-9O]{4,})$");
        if (compound.Success)
            return compound.Groups[1].Value + compound.Groups[2].Value.Replace('O', '0') +
                   compound.Groups[3].Value + compound.Groups[4].Value.Replace('O', '0');

        var simple = Regex.Match(compact, @"^([A-NP-Z]{1,6})([0-9O]{2,})$");
        return simple.Success ? simple.Groups[1].Value + simple.Groups[2].Value.Replace('O', '0') : compact;
    }

    private static string PreferOcrText(string current, string wider, string knownValue)
    {
        var combined = $"{current} {wider}";
        var compact = Regex.Replace(combined, @"[^A-Za-z]", "").ToLowerInvariant();
        if (compact.Contains("amazon") || compact.Contains("services") || compact.Contains("amaz")) return knownValue;
        return string.IsNullOrWhiteSpace(current) ? wider : current;
    }

    private static bool LooksLikeContract(string value) => Regex.IsMatch(value.Replace(" ", ""), @"^[A-Z0-9][A-Z0-9._/-]{1,30}$", RegexOptions.IgnoreCase);

    private static string FindContractCandidate(TextPage page)
    {
        var anchor = FindAnchor(page, "合同协议号");
        if (anchor is not null)
        {
            var below = page.Tokens
                .Where(t => t.Top >= anchor.Bottom - 1 && t.Top <= anchor.Bottom + page.Height * .07)
                .Where(t => t.CenterX >= anchor.Left - page.Width * .01 && t.CenterX <= anchor.Left + page.Width * .32)
                .Select(t => t.Text.Replace(" ", "").Trim())
                .Where(LooksLikeContract)
                .OrderByDescending(x => x.Any(char.IsDigit) && (x.Any(char.IsLetter) || x.Contains('-')))
                .ThenByDescending(x => x.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(below)) return below;
        }

        var candidates = page.Tokens
            .Where(t => t.CenterX < page.Width * .34 && t.CenterY >= page.Height * .25 && t.CenterY <= page.Height * .39)
            .Select(t => t.Text.Replace(" ", "").Trim())
            .Where(LooksLikeContract)
            .Where(x => x.Any(char.IsDigit) && (x.Any(char.IsLetter) || x.Contains('-')))
            .OrderByDescending(x => Regex.IsMatch(x, @"^GS\d{2,6}$", RegexOptions.IgnoreCase))
            .ThenByDescending(x => x.Length)
            .ToList();
        return candidates.FirstOrDefault() ?? "";
    }

    [GeneratedRegex(@"[（(]\s*([A-Za-z]{3})\s*[)）]")]
    private static partial Regex CountryCodeRegex();

    /// <summary>
    /// 目的国 comes from the 运抵国（地区） field, in this order:
    /// 1. the code printed in the label cell, e.g. "运抵国（地区） (DZA)", which is exact on text
    ///    layers and short enough for OCR to read reliably;
    /// 2. the country named first in the value cell (a neighbouring cell such as 指运港
    ///    "斯基克达（阿尔及利亚）" can leak in after it);
    /// 3. a code inside the value cell;
    /// 4. the code in the goods table's 最终目的国 column.
    /// The old rule knew 21 countries and otherwise scanned the whole page for any of them, so
    /// unlisted countries (阿尔及利亚) were missed and unrelated text could win.
    /// </summary>
    internal static string ResolveDestinationCountry(TextPage page, string value)
    {
        var fromLabel = CountryNames.FromCode(ReadLabelCountryCode(page, "运抵国"));
        if (fromLabel is not null) return fromLabel;
        var named = CountryNames.FirstNameIn(value);
        if (named is not null) return named;
        foreach (Match match in CountryCodeRegex().Matches(value))
            if (CountryNames.FromCode(match.Groups[1].Value.ToUpperInvariant()) is { } coded) return coded;
        var fromGoods = CountryNames.FromCode(ReadColumnCountryCode(page, "最终目的国"));
        if (fromGoods is not null) return fromGoods;
        var cleaned = CountryCodeRegex().Replace(value, "");
        cleaned = Regex.Replace(cleaned, @"(?<!\d)\d(?!\d)", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    /// <summary>The "(XXX)" code printed next to a label, within the label's own cell.</summary>
    private static string? ReadLabelCountryCode(TextPage page, string label)
    {
        var anchor = FindAnchor(page, label);
        if (anchor is null) return null;
        var tolerance = Math.Max(4, page.Height * .012);
        var rightAnchor = FindKnownAnchors(page)
            .Where(x => x.Left > anchor.Right + 1 && Math.Abs(x.CenterY - anchor.CenterY) <= tolerance)
            .OrderBy(x => x.Left)
            .FirstOrDefault();
        var right = rightAnchor?.Left - 1 ?? anchor.Right + page.Width * .08;
        var cell = page.Tokens
            .Where(t => Math.Abs(t.CenterY - anchor.CenterY) <= tolerance && t.Left >= anchor.Left - 1 && t.Right <= right + 1)
            .OrderBy(t => t.Left);
        var match = CountryCodeRegex().Match(string.Join("", cell.Select(t => t.Text)));
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>The first "(XXX)" code below a goods-table header, within that column.</summary>
    private static string? ReadColumnCountryCode(TextPage page, string header)
    {
        var anchor = FindAnchor(page, header);
        if (anchor is null) return null;
        var width = anchor.Right - anchor.Left;
        var tokens = page.Tokens
            .Where(t => t.Top > anchor.Bottom && t.Top < anchor.Bottom + page.Height * .12 &&
                        t.CenterX >= anchor.Left - width * .25 && t.CenterX <= anchor.Right + width * .25)
            .OrderBy(t => t.Top).ThenBy(t => t.Left);
        foreach (var token in tokens)
        {
            var match = CountryCodeRegex().Match(token.Text);
            if (match.Success && CountryNames.FromCode(match.Groups[1].Value.ToUpperInvariant()) is not null)
                return match.Groups[1].Value.ToUpperInvariant();
        }
        return null;
    }
}
