using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CustomsClearanceConsole;

internal sealed partial class DeclarationParser
{

    private static TextToken? FindAnchor(TextPage page, params string[] labels)
    {
        var targets = labels.Select(NormalizeLabel).Where(x => x.Length > 0).ToArray();
        var matches = new List<(TextToken Token, int Span)>();
        foreach (var line in Lines(page.Tokens, LineTolerance(page)))
        {
            for (var start = 0; start < line.Count; start++)
            {
                var joined = "";
                for (var end = start; end < Math.Min(line.Count, start + 8); end++)
                {
                    joined += NormalizeLabel(line[end].Text);
                    if (!targets.Any(joined.Contains)) continue;
                    var slice = line.Skip(start).Take(end - start + 1).ToList();
                    matches.Add((Union(slice), slice.Count));
                    break;
                }
            }
        }
        return matches
            .OrderBy(x => x.Span)
            .ThenBy(x => x.Token.Top)
            .ThenBy(x => x.Token.Right - x.Token.Left)
            .Select(x => x.Token)
            .FirstOrDefault();
    }

    private static List<TextToken> FindKnownAnchors(TextPage page)
    {
        return KnownLabels
            .Select(label => FindAnchor(page, label))
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => $"{Math.Round(x.Left, 1)}|{Math.Round(x.Top, 1)}|{Math.Round(x.Right, 1)}")
            .Select(x => x.First())
            .ToList();
    }

    private static TextToken Union(IReadOnlyCollection<TextToken> tokens)
    {
        return new TextToken(Join(tokens), tokens.Min(x => x.Left), tokens.Min(x => x.Top),
            tokens.Max(x => x.Right), tokens.Max(x => x.Bottom), tokens.Average(x => x.Confidence));
    }

    private static string NormalizeLabel(string value) => Regex.Replace(value, @"[^\p{L}\p{N}]", "").ToLowerInvariant();

    private static bool Overlaps(TextToken first, TextToken second) =>
        first.Left < second.Right && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;

    private static double LineTolerance(TextPage page) => Math.Max(3.2, page.Height * .006);

    private static string ReadRegion(TextPage page, double x1, double x2, double y1, double y2)
    {
        var tokens = page.Tokens.Where(t =>
            t.CenterX >= page.Width * x1 && t.CenterX <= page.Width * x2 &&
            t.CenterY >= page.Height * y1 && t.CenterY <= page.Height * y2);
        return string.Join(" ", Lines(tokens, LineTolerance(page)).Select(Join)).Trim();
    }

    private static IEnumerable<List<TextToken>> Lines(IEnumerable<TextToken> source, double tolerance = 3.2)
    {
        var lines = new List<List<TextToken>>();
        foreach (var token in source.OrderBy(x => x.CenterY).ThenBy(x => x.Left))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l.Average(x => x.CenterY) - token.CenterY) <= tolerance);
            if (line is null) { line = []; lines.Add(line); }
            line.Add(token);
        }
        return lines.OrderBy(l => l.Average(x => x.CenterY)).Select(l => l.OrderBy(x => x.Left).ToList());
    }

    private static string Join(IEnumerable<TextToken> tokens)
    {
        var ordered = tokens.OrderBy(x => x.Left).ToList();
        if (ordered.Count == 0) return "";
        var result = new StringBuilder(ordered[0].Text);
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            var gap = current.Left - previous.Right;
            var englishBoundary = previous.Text.LastOrDefault() <= 127 && current.Text.FirstOrDefault() <= 127;
            var separatorBoundary = Regex.IsMatch(previous.Text.Trim(), @"^[_./-]$") || Regex.IsMatch(current.Text.Trim(), @"^[_./-]$");
            if (englishBoundary && !separatorBoundary && gap > 1.5) result.Append(' ');
            result.Append(current.Text);
        }
        return result.ToString();
    }
}
