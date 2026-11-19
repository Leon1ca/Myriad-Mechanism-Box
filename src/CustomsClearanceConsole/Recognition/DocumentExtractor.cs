using System.Text;
using System.Text.RegularExpressions;

namespace CustomsClearanceConsole;

internal sealed partial class DocumentExtractor : IAsyncDisposable
{
    private readonly RapidOcrEngine _rapidOcr = new();
    public ValueTask DisposeAsync() => _rapidOcr.DisposeAsync();
    private static readonly HashSet<string> ImageExtensions =
        new(BatchScanner.SupportedExtensions.Where(x => x != ".pdf"), StringComparer.OrdinalIgnoreCase);

    public async Task<DocumentText> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return await ExtractPdfAsync(path, cancellationToken);
        if (ImageExtensions.Contains(Path.GetExtension(path)))
        {
            // A multi-page TIFF (a scanner's usual output) is read page by page like a PDF.
            var pages = new List<TextPage>();
            var frames = OcrImages.LoadPages(path);
            try
            {
                for (var i = 0; i < frames.Count; i++)
                    pages.Add(WithPageNumber(await OcrBitmapAsync(frames[i], cancellationToken), i + 1));
            }
            finally { foreach (var frame in frames) frame.Dispose(); }
            var declarationIndexes = SelectDeclarationPageIndexes(pages);
            if (declarationIndexes.Count > 0) pages = declarationIndexes.Select(index => pages[index]).ToList();
            return new DocumentText { UsedOcr = true, Pages = pages };
        }
        throw new NotSupportedException("仅支持 PDF 与 PNG、JPG、BMP、TIF/TIFF、GIF、WEBP 图片。");
    }

    private async Task<DocumentText> ExtractPdfAsync(string path, CancellationToken cancellationToken)
    {
        var pages = new List<TextPage>();
        var rendered = new List<(int Index, string Path)>();
        using var workspace = new TemporaryDirectory(AppPaths.TempRoot);
        var temp = workspace.Path;

        using (var document = new PdfiumNative.PdfDocument(path))
        {
            for (var i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.LoadPage(i);
                try
                {
                    var textPage = ExtractPdfText(page, i + 1);
                    pages.Add(textPage);
                    if (textPage.Tokens.Count < 30)
                        rendered.Add((i, PdfiumNative.RenderPageToPng(page, i, temp)));
                }
                finally { PdfiumNative.FPDF_ClosePage(page); }
            }
        }

        var usedOcr = rendered.Count > 0;
        foreach (var item in rendered)
        {
            using var bitmap = OcrImages.LoadPages(item.Path).Single();
            pages[item.Index] = WithPageNumber(await OcrBitmapAsync(bitmap, cancellationToken), item.Index + 1);
        }
        var declarationIndexes = SelectDeclarationPageIndexes(pages);
        if (declarationIndexes.Count > 0) pages = declarationIndexes.Select(index => pages[index]).ToList();
        return new DocumentText { Pages = pages, UsedOcr = usedOcr };
    }

    private static TextPage ExtractPdfText(IntPtr page, int pageNumber)
    {
        var width = PdfiumNative.FPDF_GetPageWidth(page);
        var height = PdfiumNative.FPDF_GetPageHeight(page);
        var rotation = ((PdfiumNative.FPDFPage_GetRotation(page) % 4) + 4) % 4;
        var rawWidth = rotation is 1 or 3 ? height : width;
        var rawHeight = rotation is 1 or 3 ? width : height;
        var textPage = PdfiumNative.FPDFText_LoadPage(page);
        if (textPage == IntPtr.Zero) return new TextPage { PageNumber = pageNumber, Width = width, Height = height };
        try
        {
            var chars = new List<(char Ch, double L, double T, double R, double B)>();
            var count = PdfiumNative.FPDFText_CountChars(textPage);
            for (var i = 0; i < count; i++)
            {
                var unicode = PdfiumNative.FPDFText_GetUnicode(textPage, i);
                if (unicode == 0 || unicode > char.MaxValue) continue;
                var ch = (char)unicode;
                if (!PdfiumNative.FPDFText_GetCharBox(textPage, i, out var left, out var right, out var bottom, out var top)) continue;
                chars.Add(rotation switch
                {
                    1 => (ch, bottom, left, top, right),
                    2 => (ch, rawWidth - right, bottom, rawWidth - left, top),
                    3 => (ch, rawHeight - top, rawWidth - right, rawHeight - bottom, rawWidth - left),
                    _ => (ch, left, rawHeight - top, right, rawHeight - bottom)
                });
            }
            return new TextPage { PageNumber = pageNumber, Width = width, Height = height, HasPdfTextLayer = true,
                Tokens = GroupCharacters(chars) };
        }
        finally { PdfiumNative.FPDFText_ClosePage(textPage); }
    }

    private static List<TextToken> GroupCharacters(List<(char Ch, double L, double T, double R, double B)> chars)
    {
        var tokens = new List<TextToken>();
        var text = new StringBuilder();
        double l = 0, t = 0, r = 0, b = 0;
        var has = false;

        void Flush()
        {
            if (has && text.Length > 0) tokens.Add(new TextToken(text.ToString(), l, t, r, b));
            text.Clear(); has = false;
        }

        foreach (var c in chars)
        {
            if (char.IsWhiteSpace(c.Ch) || c.Ch is '\r' or '\n') { Flush(); continue; }
            var h = Math.Max(1, Math.Max(b - t, c.B - c.T));
            var sameLine = has && Math.Abs(((t + b) / 2) - ((c.T + c.B) / 2)) < Math.Max(2.5, h * .45);
            var gap = has ? c.L - r : 0;
            var join = sameLine && gap < Math.Max(4.2, h * .7) && gap > -Math.Max(5, h * .5);
            if (has && !join) Flush();
            if (!has) { l = c.L; t = c.T; r = c.R; b = c.B; has = true; }
            else { l = Math.Min(l, c.L); t = Math.Min(t, c.T); r = Math.Max(r, c.R); b = Math.Max(b, c.B); }
            text.Append(c.Ch);
        }
        Flush();
        return tokens;
    }

    /// <summary>
    /// One PP-OCRv5 pass per page. The page is scaled to a 2800px long edge (the detector's
    /// working size). Orientation needs no separate engine: a page lying on its side yields
    /// mostly tall text boxes, and an upside-down page mostly lines the angle classifier turns
    /// by 180 degrees; only then is the page rotated and read again, keeping the reading that
    /// looks most like a declaration.
    /// </summary>
    private async Task<TextPage> OcrBitmapAsync(Bitmap source, CancellationToken cancellationToken)
    {
        var first = await RecognizeRotatedAsync(source, 0, cancellationToken);
        var candidates = new List<RapidOcrPage> { first };
        if (first.TallFraction > .5)
        {
            // On its side: turn it upright one way; if the text then reads upside down, the
            // other way is the right one.
            var quarter = await RecognizeRotatedAsync(source, 90, cancellationToken);
            candidates.Add(quarter);
            if (quarter.FlippedFraction > .5) candidates.Add(await RecognizeRotatedAsync(source, 270, cancellationToken));
        }
        else if (first.FlippedFraction > .5)
        {
            candidates.Add(await RecognizeRotatedAsync(source, 180, cancellationToken));
        }
        // The line-angle classifier reads upside-down lines correctly, so an upside-down page
        // still looks like a declaration by its words; only its geometry is mirrored. Prefer
        // the reading with the fewest upside-down lines.
        var best = candidates
            .OrderBy(x => x.TallFraction > .5 ? 1 : 0)
            .ThenBy(x => x.FlippedFraction > .5 ? 1 : 0)
            .ThenByDescending(x => DeclarationPageScore(x.Page))
            .ThenByDescending(x => x.Page.Tokens.Sum(t => t.Text.Length * t.Confidence))
            .First();
        AppLog.Write($"OCR 页面方向：{string.Join("；", candidates.Select(x => $"{x.Rotation}° 竖框 {x.TallFraction:P0} 倒置 {x.FlippedFraction:P0} 得分 {DeclarationPageScore(x.Page)}"))}；采用 {best.Rotation}°。");
        var page = await RecoverTableAsync(source, best, cancellationToken);
        page = await RetryIncompleteDeclarationAsync(source, best.Rotation, page, cancellationToken);
        return await RetryProductSpecificationsAsync(source, best.Rotation, page, cancellationToken);
    }

    private async Task<TextPage> RetryProductSpecificationsAsync(Bitmap source, int rotation, TextPage page,
        CancellationToken cancellationToken)
    {
        var product = page.Tokens.FirstOrDefault(t => t.Text.Contains("规格型号", StringComparison.Ordinal));
        var quantity = page.Tokens.FirstOrDefault(t => t.Text.Contains("数量及单位", StringComparison.Ordinal));
        if (product is null || quantity is null || quantity.Left <= product.Left) return page;

        var tokens = page.Tokens.ToList();
        var footer = page.Tokens.Where(t => t.Top > product.Bottom && t.Text.Contains("特殊关系确认", StringComparison.Ordinal))
            .Select(t => t.Top).DefaultIfEmpty(page.Height * .985).Min();
        bool InProductColumn(TextToken token) => token.CenterY >= product.Bottom && token.CenterY < footer &&
            token.Left > page.Width * .085 && token.Right <= quantity.Left;
        var candidates = tokens.Where(token => token.OcrBox is { Length: 4 } && InProductColumn(token)).ToList();
        if (candidates.Count == 0 && !tokens.Any(t => InProductColumn(t) && DamagedSpecPrefix().IsMatch(t.Text))) return page;

        using var oriented = OcrImages.Orient(source, rotation);
        var scaleX = oriented.Width / page.Width;
        var scaleY = oriented.Height / page.Height;
        if (candidates.Count > 0)
        {
            var recovered = await _rapidOcr.RecoverSpecificationsAsync(OcrImages.ToPng(oriented), candidates, scaleX, scaleY, cancellationToken);
            for (var i = 0; i < tokens.Count; i++)
                if (recovered.TryGetValue(tokens[i], out var text)) tokens[i] = tokens[i] with { Text = text };
        }

        var specs = tokens.Select((token, index) => (token, index))
            .Where(x => InProductColumn(x.token))
            .Select(x => (x.token, x.index, prefix: DamagedSpecPrefix().Match(x.token.Text)))
            .Where(x => x.prefix.Success)
            .ToList();
        foreach (var (token, index, prefix) in specs)
        {
            var expectedPrefix = $"{prefix.Groups["first"].Value}|{prefix.Groups["second"].Value}|";
            var expectedText = WithoutSeparators(expectedPrefix + token.Text[prefix.Length..]);
            var height = token.Bottom - token.Top;
            var left = (int)Math.Round((token.Left - height * .10) * scaleX);
            var top = (int)Math.Round((token.Top + height * .10) * scaleY);
            var right = (int)Math.Round((token.Right - height * .10) * scaleX);
            var bottom = (int)Math.Round((token.Bottom + height * .10) * scaleY);
            var area = Rectangle.Intersect(Rectangle.FromLTRB(left, top, right, bottom),
                new Rectangle(Point.Empty, oriented.Size));
            if (area.Width <= 0 || area.Height <= 0) continue;

            var reread = string.Concat(await ReadCroppedLineAsync(oriented, area, cancellationToken)).Replace('｜', '|');
            if (reread.StartsWith(expectedPrefix, StringComparison.Ordinal) &&
                WithoutSeparators(reread).Equals(expectedText, StringComparison.Ordinal))
                tokens[index] = token with { Text = reread };
        }
        return new TextPage { PageNumber = page.PageNumber, Width = page.Width, Height = page.Height, Tokens = tokens };

        static string WithoutSeparators(string text) => Regex.Replace(text, @"[\s|｜]", "");
    }

    [GeneratedRegex(@"^(?<first>[0-9])(?:[1I](?=[0-9]\p{L}.*[|｜]))?(?<second>[0-9])(?:[{}]|(?=\p{L}.*[|｜]))(?=\p{L})")]
    private static partial Regex DamagedSpecPrefix();

    private async Task<string[]> ReadCroppedLineAsync(Bitmap oriented, Rectangle area, CancellationToken cancellationToken,
        float? threshold = null)
    {
        using var crop = OcrImages.CropWithWhiteBorder(oriented, area, threshold);
        using var enlarged = OcrImages.Prepare(crop, 0);
        var retry = await _rapidOcr.RecognizeAsync(OcrImages.ToPng(enlarged), enlarged.Width, enlarged.Height, cancellationToken);
        return retry.Page.Tokens.Where(t => t.Confidence >= 65).OrderBy(t => t.Left).Select(t => t.Text).ToArray();
    }

    private async Task<TextPage> RetryIncompleteDeclarationAsync(Bitmap source, int rotation, TextPage page,
        CancellationToken cancellationToken)
    {
        var expectedRows = DeclarationParser.ExpectedItemRows(page);
        if (expectedRows == 0) return page;

        var parser = new DeclarationParser();
        DeclarationRecord Parse(TextPage text) => parser.Parse("ocr-page.png",
            new DocumentText { UsedOcr = true, Pages = [text] });
        var first = Parse(page);
        if (!first.NeedsAttention || (first.LineTotals.Count >= expectedRows && first.Consignee.Length > 0)) return page;

        // A different detector scale can separate small glyphs from nearby table rules.
        var retry = await RecognizeRotatedAsync(source, rotation, cancellationToken, longEdgeOverride: 2500);
        var second = Parse(retry.Page);
        if (second.NeedsAttention || second.DeclarationNo != first.DeclarationNo ||
            second.LineTotals.Count < expectedRows ||
            !SameOrMissing(first.ContractNo, second.ContractNo) ||
            !SameOrMissing(first.Consignee, second.Consignee) ||
            !SameOrMissing(first.ExitCustoms, second.ExitCustoms) ||
            !SameOrMissing(first.DestinationCountry, second.DestinationCountry) ||
            !first.LineTotals.Where(line => line.IsReliable).All(line =>
                second.LineTotals.Any(other => line.ItemNo == other.ItemNo && line.Currency == other.Currency &&
                    line.Amount == other.Amount && line.Quantity == other.Quantity && line.UnitPrice == other.UnitPrice &&
                    line.Unit == other.Unit && line.ProductName == other.ProductName)))
            return page;

        AppLog.Write("OCR 备用分辨率补齐报关单字段。");
        return await RefineConsigneeAsync(source, rotation, page, retry.Page, first.Consignee, second.Consignee, cancellationToken);

        static bool SameOrMissing(string original, string candidate) =>
            original.Length == 0 || string.Equals(original.Replace(" ", ""), candidate.Replace(" ", ""),
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TextPage> RefineConsigneeAsync(Bitmap source, int rotation, TextPage original, TextPage retry,
        string firstValue, string retryValue, CancellationToken cancellationToken)
    {
        // A faint scan can lose a stroke even after the page has been recovered. Two contrast
        // levels must agree before replacing any letters in a single-line name.
        if (firstValue.Length < 8 || !firstValue.All(c => c <= 127)) return retry;
        var originalToken = original.Tokens.FirstOrDefault(t => t.CenterY < original.Height * .35 && t.Text == firstValue);
        var tokens = retry.Tokens.ToList();
        var index = tokens.FindIndex(t => t.CenterY < retry.Height * .35 && t.Text == retryValue);
        if (originalToken is null || index < 0) return retry;

        using var oriented = OcrImages.Orient(source, rotation);
        var sx = oriented.Width / original.Width;
        var sy = oriented.Height / original.Height;
        var height = originalToken.Bottom - originalToken.Top;
        var area = Rectangle.Intersect(Rectangle.FromLTRB(
            (int)Math.Round((originalToken.Left - height * .15) * sx),
            (int)Math.Round((originalToken.Top + height * .07) * sy),
            (int)Math.Round((originalToken.Right + height * 1.15) * sx),
            (int)Math.Round((originalToken.Bottom + height * .13) * sy)), new Rectangle(Point.Empty, oriented.Size));
        if (area.Width <= 0 || area.Height <= 0) return retry;

        var lighter = string.Join(" ", await ReadCroppedLineAsync(oriented, area, cancellationToken, 214f / 255));
        var darker = string.Join(" ", await ReadCroppedLineAsync(oriented, area, cancellationToken, 228f / 255));
        var first = Compact(firstValue);
        var recovered = Compact(lighter);
        if (recovered.Length != first.Length || recovered != Compact(darker) ||
            first.Zip(recovered).Count(pair => pair.First != pair.Second) is < 1 or > 2) return retry;

        tokens[index] = tokens[index] with { Text = lighter };
        return new TextPage { PageNumber = retry.PageNumber, Width = retry.Width, Height = retry.Height, Tokens = tokens };

        static string Compact(string value) => Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", "");
    }

    private async Task<TextPage> RecoverTableAsync(Bitmap source, RapidOcrPage reading, CancellationToken cancellationToken)
    {
        var page = reading.Page;
        var header = page.Tokens.FirstOrDefault(t => t.Text.Contains("总价") && t.Text.Contains("币制"));
        if (header is null) return page;
        var parsed = new DeclarationParser().Parse("ocr-page.png", new DocumentText { UsedOcr = true, Pages = [page] });
        if (parsed.LineTotals.Count > 0 && parsed.LineTotals.Count >= DeclarationParser.ExpectedItemRows(page)) return page;

        var footer = page.Tokens.Where(t => t.Top > header.Bottom &&
            (t.Text.Contains("特殊关系确认") || t.Text.Contains("价格影响确认")))
            .Select(t => t.Top).DefaultIfEmpty(page.Height * .9).Min();
        var printed = page.Tokens.Where(t => t.Top > header.Bottom && t.Bottom < footer &&
            t.Left < header.Right + page.Width * .035 && t.Confidence >= 50).ToList();
        if (printed.Count == 0) return page;
        using var prepared = OcrImages.Prepare(source, reading.Rotation);
        var left = Math.Max(0, (int)(page.Width * .025));
        var top = Math.Max(0, (int)(header.Top - page.Height * .008));
        var right = Math.Min(prepared.Width, (int)Math.Ceiling(header.Right + page.Width * .035));
        var bottom = Math.Min(prepared.Height, (int)Math.Ceiling(Math.Min(footer, printed.Max(t => t.Bottom) + page.Height * .02)));
        if (right <= left || bottom <= top) return page;
        using var crop = prepared.Clone(new Rectangle(left, top, right - left, bottom - top), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var enlarged = OcrImages.Prepare(crop, 0);
        var retry = await _rapidOcr.RecognizeAsync(OcrImages.ToPng(enlarged), enlarged.Width, enlarged.Height, cancellationToken);
        var scaleX = crop.Width / (double)enlarged.Width;
        var scaleY = crop.Height / (double)enlarged.Height;
        var tokens = page.Tokens.ToList();
        foreach (var token in retry.Page.Tokens.Where(t => t.Confidence >= 65))
        {
            var mapped = token with { OcrBox = null, Left = left + token.Left * scaleX, Right = left + token.Right * scaleX,
                Top = top + token.Top * scaleY, Bottom = top + token.Bottom * scaleY };
            // Add only missing boxes; a conflicting rereading does not overwrite the first pass.
            var overlaps = tokens.Any(t => Math.Min(t.Right, mapped.Right) - Math.Max(t.Left, mapped.Left) >
                Math.Min(t.Right - t.Left, mapped.Right - mapped.Left) * .5 &&
                Math.Abs(t.CenterY - mapped.CenterY) < Math.Max(t.Bottom - t.Top, mapped.Bottom - mapped.Top) * .65);
            if (!overlaps) tokens.Add(mapped);
        }
        AppLog.Write($"OCR 商品表局部补扫：新增 {tokens.Count - page.Tokens.Count} 个文本框。");
        return new TextPage { PageNumber = page.PageNumber, Width = page.Width, Height = page.Height, Tokens = tokens };
    }

    private async Task<RapidOcrPage> RecognizeRotatedAsync(Bitmap source, int rotation, CancellationToken cancellationToken,
        int? longEdgeOverride = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var prepared = OcrImages.Prepare(source, rotation, longEdgeOverride);
        var png = OcrImages.ToPng(prepared);
        var page = await _rapidOcr.RecognizeAsync(png, prepared.Width, prepared.Height, cancellationToken);
        return page with { Rotation = rotation };
    }

    private static TextPage WithPageNumber(TextPage page, int pageNumber) => new()
    {
        PageNumber = pageNumber,
        Width = page.Width,
        Height = page.Height,
        Tokens = page.Tokens
    };

    private static List<int> SelectDeclarationPageIndexes(IReadOnlyList<TextPage> pages)
    {
        var selected = new List<int>();
        for (var i = 0; i < pages.Count; i++)
            if (DeclarationPageScore(pages[i]) >= 8) selected.Add(i);
        return selected;
    }

    private static int DeclarationPageScore(TextPage page)
    {
        var compact = string.Concat(page.Tokens.OrderBy(x => x.CenterY).ThenBy(x => x.Left).Select(x => x.Text))
            .Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        var score = 0;
        if (compact.Contains("中华人民共和国海关出口货物报关单") || compact.Contains("出口货物报关单")) score += 8;
        if (compact.Contains("海关编号")) score += 4;
        if (compact.Contains("境外收货人")) score += 3;
        if (compact.Contains("项号") && (compact.Contains("总价") || compact.Contains("币制"))) score += 4;
        if (DeclarationNumberPattern().IsMatch(compact)) score += 3;
        if (compact.Contains("页码/页数") || compact.Contains("页码页数")) score += 2;
        if (compact.Contains("通关无纸化出口放行通知书")) score -= 14;
        if (compact.Contains("装货单") || compact.Contains("shippingorder")) score -= 14;
        if (compact.Contains("委托报关协议")) score -= 14;
        return score;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!\d)\d{18}(?!\d)")]
    private static partial System.Text.RegularExpressions.Regex DeclarationNumberPattern();
}
