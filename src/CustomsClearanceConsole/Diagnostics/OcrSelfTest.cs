using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace CustomsClearanceConsole;

/// <summary>
/// End-to-end OCR check for CI (--ocr-self-test): renders a declaration with the layout of a real
/// one (the anonymised PP-OCR token geometry used by the core regression), saves it in every
/// supported image format, as a multi-page TIFF, rotated and as an image-only PDF, and runs each
/// file through the real extractor (PP-OCRv5) and parser with the consistency rules.
/// </summary>
internal static class OcrSelfTest
{
    private const string Number = "516620260000000017";

    public static async Task<int> RunAsync(string folder)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Directory.CreateDirectory(folder);
        var failures = new List<string>();
        var report = new List<string>();
        using var page = RenderDeclaration();
        page.Save(Path.Combine(folder, "rendered.png"), ImageFormat.Png);

        var files = new List<string>
        {
            Save(folder, "declaration.png", page, ImageFormat.Png),
            SaveJpeg(folder, "declaration.jpg", page),
            Save(folder, "declaration.bmp", page, ImageFormat.Bmp),
            SaveTwoPageTiff(folder, "declaration-2pages.tif", page),
            SaveRotated(folder, "declaration-rot90.png", page, RotateFlipType.Rotate90FlipNone),
            SaveRotated(folder, "declaration-rot180.png", page, RotateFlipType.Rotate180FlipNone),
            SaveRotated(folder, "declaration-rot270.png", page, RotateFlipType.Rotate270FlipNone),
            SaveImagePdf(folder, "declaration-scan.pdf", page)
        };
        var webp = SaveWebp(folder, "declaration.webp", page);
        if (webp is null) failures.Add("无法生成 WebP 测试图（OpenCV 编码失败）。");
        else files.Add(webp);

        await using var extractor = new DocumentExtractor();
        var parser = new DeclarationParser();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var watch = Stopwatch.StartNew();
            try
            {
                var record = parser.Parse(file, await extractor.ExtractAsync(file, CancellationToken.None));
                watch.Stop();
                var problems = new List<string>();
                if (record.DeclarationNo != Number) problems.Add($"报关单号 {record.DeclarationNo}");
                if (record.ExitCustoms != "南沙新港") problems.Add($"出境关别 {record.ExitCustoms}");
                if (record.DestinationCountry != "阿尔及利亚") problems.Add($"目的国 {record.DestinationCountry}");
                if (record.ContractNo != "TESTX-DX-20260101-001") problems.Add($"合同协议号 {record.ContractNo}");
                if (!record.Totals.TryGetValue("USD", out var total) || total != 5000.00m || record.Totals.Count != 1)
                    problems.Add($"总价 {record.DisplayTotal}");
                if (record.Status != "OCR 识别完成") problems.Add($"状态 {record.Status}：{record.Warning}");
                report.Add($"{(problems.Count == 0 ? "PASS" : "FAIL")}: {name} · {watch.Elapsed.TotalSeconds:F1}s · {record.DeclarationNo} · {record.ExitCustoms} · {record.DestinationCountry} · {record.DisplayTotal} · {record.Status}：{record.Warning}");
                Console.WriteLine($"{(problems.Count == 0 ? "PASS" : "FAIL")}: {name} · {watch.Elapsed.TotalSeconds:F1}s · {record.DeclarationNo} · {record.ExitCustoms} · {record.DestinationCountry} · {record.DisplayTotal} · {record.Status}");
                if (problems.Count > 0) failures.Add($"{name}：{string.Join("；", problems)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {name} · {ex.GetBaseException().Message}");
                failures.Add($"{name}：{ex.GetBaseException().Message}");
            }
        }
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        File.WriteAllLines(Path.Combine(folder, "report.txt"), report.Concat(failures));
        Console.WriteLine(failures.Count == 0 ? $"OCR_SELF_TEST_OK: {files.Count} files" : $"OCR_SELF_TEST_FAILED: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Bitmap RenderDeclaration()
    {
        using var stream = typeof(OcrSelfTest).Assembly.GetManifestResourceStream("ocr-selftest-layout.json")
            ?? throw new InvalidOperationException("缺少 OCR 自检版式资源。");
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var pen = new Pen(Color.Black, 2);
        // The declaration's box grid (outer frame and row rules), as on the printed form.
        graphics.DrawRectangle(pen, 78, 188, 1656, 1010);
        foreach (var y in new[] { 240, 290, 340, 390, 440, 490, 588, 614, 688, 1036, 1082 })
            graphics.DrawLine(pen, 78, y, 1734, y);
        foreach (var token in root.GetProperty("tokens").EnumerateArray())
        {
            var text = token.GetProperty("text").GetString() ?? "";
            if (text is "二" or "示例货运代理" or "报关专用章") continue;
            var box = RectangleF.FromLTRB((float)token.GetProperty("l").GetDouble(), (float)token.GetProperty("t").GetDouble(),
                (float)token.GetProperty("r").GetDouble(), (float)token.GetProperty("b").GetDouble());
            var size = box.Height * .72f;
            Font font;
            while (true)
            {
                font = AppFonts.Ui(size);
                if (graphics.MeasureString(text, font).Width <= box.Width * 1.04f || size < 9) break;
                font.Dispose();
                size -= .5f;
            }
            using (font)
                graphics.DrawString(text, font, Brushes.Black, box.Left, box.Top + (box.Height - font.GetHeight(graphics)) / 2);
        }
        return bitmap;
    }

    private static string Save(string folder, string name, Bitmap page, ImageFormat format)
    {
        var path = Path.Combine(folder, name);
        page.Save(path, format);
        return path;
    }

    private static string SaveJpeg(string folder, string name, Bitmap page)
    {
        var path = Path.Combine(folder, name);
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        page.Save(path, codec, parameters);
        return path;
    }

    private static string SaveRotated(string folder, string name, Bitmap page, RotateFlipType rotation)
    {
        using var rotated = new Bitmap(page);
        rotated.RotateFlip(rotation);
        return Save(folder, name, rotated, ImageFormat.Png);
    }

    /// <summary>A scanner-style TIFF: a blank cover page, then the declaration.</summary>
    private static string SaveTwoPageTiff(string folder, string name, Bitmap page)
    {
        var path = Path.Combine(folder, name);
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Tiff.Guid);
        using var blank = new Bitmap(page.Width, page.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(blank)) graphics.Clear(Color.White);
        using var first = new EncoderParameters(1);
        first.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
        blank.Save(path, codec, first);
        using var next = new EncoderParameters(1);
        next.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.FrameDimensionPage);
        blank.SaveAdd(page, next);
        using var flush = new EncoderParameters(1);
        flush.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag, (long)EncoderValue.Flush);
        blank.SaveAdd(flush);
        return path;
    }

    private static string? SaveWebp(string folder, string name, Bitmap page)
    {
        try
        {
            using var mat = new Mat();
            CvInvoke.Imdecode(OcrImages.ToPng(page), ImreadModes.ColorBgr, mat);
            using var buffer = new VectorOfByte();
            CvInvoke.Imencode(".webp", mat, buffer);
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, buffer.ToArray());
            return path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WebP 编码失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>A scanned declaration as an image-only PDF (no text layer), A4 landscape.</summary>
    private static string SaveImagePdf(string folder, string name, Bitmap page)
    {
        byte[] jpeg;
        using (var memory = new MemoryStream())
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);
            page.Save(memory, codec, parameters);
            jpeg = memory.ToArray();
        }
        const int pageWidth = 842, pageHeight = 595;
        var content = Encoding.ASCII.GetBytes($"q {pageWidth} 0 0 {pageHeight} 0 0 cm /Im0 Do Q");
        using var output = new MemoryStream();
        var offsets = new List<long>();
        void Write(string text) { var bytes = Encoding.ASCII.GetBytes(text); output.Write(bytes); }
        void Object(string body) { offsets.Add(output.Position); Write($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }
        void StreamObject(string dictionary, byte[] data)
        {
            offsets.Add(output.Position);
            Write($"{offsets.Count} 0 obj\n{dictionary}\nstream\n");
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }
        Write("%PDF-1.4\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
        StreamObject($"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>", jpeg);
        StreamObject($"<< /Length {content.Length} >>", content);
        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, output.ToArray());
        return path;
    }
}
