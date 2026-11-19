using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using RapidOCRLib.Models;

namespace CustomsClearanceConsole;

internal static partial class OcrProductSpecification
{
    public static bool IsCandidate(string text) => SpecificationPrefix().IsMatch(text);

    public static string? TryRead(Mat line, string original, Func<Mat, TextLine> recognize)
    {
        using var gray = new Mat();
        CvInvoke.CvtColor(line, gray, ColorConversion.Bgr2Gray);
        var stride = gray.Step;
        var pixels = new byte[gray.Rows * stride];
        Marshal.Copy(gray.DataPointer, pixels, 0, pixels.Length);
        var sourceDigits = SpecificationPrefix().Match(original).Value.Where(char.IsAsciiDigit).ToArray();

        var firstBand = ReadBand(190);
        var secondBand = ReadBand(205);
        if (firstBand is null || secondBand is null) return null;
        // Keep strokes found at both contrast levels. A narrow letter that touches its
        // neighbours at one level is not enough evidence for an extra separator.
        var separators = secondBand.Separators.Where(bar => firstBand.Separators.Any(
            other => other.Left < bar.Right && other.Right > bar.Left)).ToArray();
        if (separators.Length < 2) return null;
        var first = ReadFields(firstBand, separators, 1);
        var second = ReadFields(secondBand, separators, 2);
        var confirmed = new[] { first, second }.Where(text => text is not null && MatchesOriginal(text, original))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (confirmed.Length == 1) return confirmed[0];
        if (first is null || first != second) return null;
        // Two equal fragment readings can recover a missed Chinese character as long as
        // the remaining printed content still agrees with the full-line reading.
        for (var i = 0; i < first.Length; i++)
            if (first[i] is >= '\u4e00' and <= '\u9fff' && MatchesOriginal(first.Remove(i, 1), original))
                return first;
        return null;

        InkBand? ReadBand(byte threshold)
        {
            var rowInk = new int[gray.Rows];
            for (var y = 0; y < gray.Rows; y++)
                for (var x = 0; x < gray.Cols; x++)
                    if (pixels[y * stride + x] < threshold) rowInk[y]++;

            var minimumRowInk = Math.Max(2, (int)Math.Round(rowInk.Max() * .075));
            var (top, bottom) = FindTextBand(rowInk, minimumRowInk);
            if (bottom - top < 4) return null;
            var columnInk = new int[gray.Cols];
            for (var y = top; y < bottom; y++)
                for (var x = 0; x < gray.Cols; x++)
                    if (pixels[y * stride + x] < threshold) columnInk[x]++;
            var minimumColumnInk = Math.Max(1, (bottom - top) * .035);
            for (var x = 0; x < columnInk.Length; x++)
                if (columnInk[x] <= minimumColumnInk) columnInk[x] = 0;
            return new InkBand(top, bottom, columnInk, FindSeparators(top, bottom, columnInk, threshold));
        }

        string? ReadFields(InkBand band, IEnumerable<(int Left, int Right)> separators, int verticalMargin)
        {
            var fields = new List<string>();
            var start = 0;
            foreach (var (left, right) in separators.Append((gray.Cols, gray.Cols)))
            {
                var end = left;
                while (start < end && band.ColumnInk[start] == 0) start++;
                while (end > start && band.ColumnInk[end - 1] == 0) end--;
                if (end <= start)
                {
                    fields.Add("");
                }
                else
                {
                    var cropTop = Math.Max(0, band.Top - verticalMargin);
                    var cropBottom = Math.Min(line.Rows, band.Bottom + verticalMargin);
                    using var fragment = new Mat(line, Rectangle.FromLTRB(start, cropTop, end, cropBottom));
                    using var padded = new Mat();
                    var margin = Math.Max(1, (band.Bottom - band.Top) / 7);
                    CvInvoke.CopyMakeBorder(fragment, padded, margin, margin, margin, margin,
                        BorderType.Constant, new MCvScalar(255, 255, 255));
                    var read = recognize(padded);
                    var value = read.Text.Trim();
                    var minimumConfidence = IsDigit(value) && fields.Count < 2 ? .50 : .70;
                    var confidence = read.CharScores is { Count: > 0 } ? read.CharScores.Average() : 0;
                    if (fields.Count < 2 && sourceDigits.Length == 2 && end - start < band.Bottom - band.Top &&
                        (!IsDigit(value) || confidence < minimumConfidence))
                    {
                        // At screenshot resolution an isolated digit can be harder to read than
                        // the full line. Keep the two digits already read from that line.
                        value = sourceDigits[fields.Count].ToString();
                    }
                    else if (value.Length == 0 || confidence < minimumConfidence)
                    {
                        return null;
                    }
                    fields.Add(value);
                }
                start = right;
            }
            if (fields.Count < 3 || !IsDigit(fields[0]) || !IsDigit(fields[1]) ||
                !fields[2].Any(char.IsLetter)) return null;
            return string.Join('|', fields);
        }

        List<(int Left, int Right)> FindSeparators(int top, int bottom, int[] columnInk, byte threshold)
        {
            var result = new List<(int, int)>();
            var textHeight = bottom - top;
            var minimumGap = Math.Max(2, (int)Math.Round(textHeight * .075));
            for (var x = 0; x < columnInk.Length;)
            {
                if (columnInk[x] == 0) { x++; continue; }
                var left = x++;
                while (x < columnInk.Length && columnInk[x] > 0) x++;
                var width = x - left;
                if (width > Math.Max(2, textHeight * .27)) continue;
                // A separated vertical stroke also needs clear space on both sides.
                // A thin component of a neighbouring Chinese character can have the same height.
                if (left < minimumGap || x + minimumGap > columnInk.Length ||
                    Enumerable.Range(left - minimumGap, minimumGap).Any(col => columnInk[col] != 0) ||
                    Enumerable.Range(x, minimumGap).Any(col => columnInk[col] != 0)) continue;

                var firstRow = -1;
                var lastRow = -1;
                var occupied = 0;
                for (var y = top; y < bottom; y++)
                {
                    if (!Enumerable.Range(left, width).Any(col => pixels[y * stride + col] < threshold)) continue;
                    if (firstRow < 0) firstRow = y;
                    lastRow = y;
                    occupied++;
                }
                var height = lastRow - firstRow + 1;
                if (height >= textHeight * .65 && occupied >= height * .85 &&
                    width <= Math.Max(2, height * .27))
                    result.Add((left, x));
            }
            return result;
        }
    }

    private static bool IsDigit(string text) => text.Length == 1 && char.IsAsciiDigit(text[0]);

    private static (int Top, int Bottom) FindTextBand(int[] rowInk, int minimum)
    {
        var best = (Top: 0, Bottom: 0);
        var bestInk = 0;
        for (var y = 0; y < rowInk.Length;)
        {
            if (rowInk[y] < minimum) { y++; continue; }
            var top = y;
            var ink = 0;
            while (y < rowInk.Length && rowInk[y] >= minimum) ink += rowInk[y++];
            if (ink <= bestInk) continue;
            bestInk = ink;
            best = (top, y);
        }
        return best;
    }

    private static bool MatchesOriginal(string recovered, string original)
    {
        // Allow OCR lookalikes only where the reread has a visually confirmed separator
        // or the final letter in the unit ml. Other digits and letters must still match.
        var compact = WhiteSpace().Replace(recovered, "");
        var pattern = new System.Text.StringBuilder("^");
        for (var i = 0; i < compact.Length; i++)
        {
            var c = compact[i];
            if (c == '|')
            {
                if (i == 0 || compact[i - 1] != '|') pattern.Append("[|｜I1l{}]*");
            }
            else if ((c is 'l' or 'L') && i >= 2 && (compact[i - 1] is 'm' or 'M') && char.IsAsciiDigit(compact[i - 2]))
                pattern.Append("[lL1I]");
            else
                pattern.Append(Regex.Escape(c.ToString()));
        }
        return Regex.IsMatch(WhiteSpace().Replace(original, ""), pattern.Append('$').ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    private sealed record InkBand(int Top, int Bottom, int[] ColumnInk, List<(int Left, int Right)> Separators);

    [GeneratedRegex(@"^[0-9][0-9|｜Ilil{}]{1,6}\s*(?=\p{L})")]
    private static partial Regex SpecificationPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
