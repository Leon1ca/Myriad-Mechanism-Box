using RapidOCRLib;

namespace CustomsClearanceConsole;

/// <summary>
/// The OCR engine: PP-OCRv5 (detection, line-angle classification, recognition) on ONNX Runtime.
/// </summary>
internal sealed class RapidOcrEngine : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OcrLite? _engine;
    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _gate.WaitAsync();
        try { _engine?.Dispose(); _engine = null; }
        finally { _gate.Release(); }
    }

    public async Task<RapidOcrPage> RecognizeAsync(byte[] image, int width, int height, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var engine = await GetEngineAsync();
            cancellationToken.ThrowIfCancellationRequested();

            const int padding = 40;
            RapidOCRLib.Models.OcrResult? result = null;
            try
            {
                // Decoded from memory: OpenCV cannot open a file path with non-ASCII characters
                // (for example a Windows user name in the temp folder).
                result = await engine.DetectAsync(
                    image,
                    padding: padding,
                    maxSideLen: 2800,
                    boxScoreThresh: .38f,
                    boxThresh: .25f,
                    unClipRatio: 1.65f,
                    doAngle: true,
                    mostAngle: false);

                var tokens = new List<TextToken>();
                int lines = 0, tall = 0, flipped = 0;
                foreach (var block in result.TextBlocks ?? [])
                {
                    if (string.IsNullOrWhiteSpace(block.Text) || block.BoxPoints is null || block.BoxPoints.Count == 0)
                        continue;

                    var left = Math.Clamp(block.BoxPoints.Min(x => x.X) - padding, 0, width);
                    var top = Math.Clamp(block.BoxPoints.Min(x => x.Y) - padding, 0, height);
                    var right = Math.Clamp(block.BoxPoints.Max(x => x.X) - padding, 0, width);
                    var bottom = Math.Clamp(block.BoxPoints.Max(x => x.Y) - padding, 0, height);
                    if (right <= left || bottom <= top) continue;
                    if (block.Text.Trim().Length >= 2)
                    {
                        lines++;
                        if (bottom - top > (right - left) * 1.5) tall++;
                        if (block.AngleIndex == 1) flipped++;
                    }

                    var charConfidence = block.CharScores is { Count: > 0 } ? block.CharScores.Average() : block.BoxScore;
                    var confidence = Math.Clamp(Math.Min(block.BoxScore, charConfidence) * 100d, 0, 100);
                    tokens.Add(new TextToken(block.Text.Trim(), left, top, right, bottom, confidence));
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new RapidOcrPage(new TextPage { Width = width, Height = height, Tokens = tokens },
                    lines == 0 ? 0 : tall / (double)lines, lines == 0 ? 0 : flipped / (double)lines, 0);
            }
            finally
            {
                result?.BoxImg?.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OcrLite> GetEngineAsync()
    {
        if (_engine is not null) return _engine;
        var folder = AppPaths.RapidOcrModels;
        var required = new[]
        {
            "ch_PP-OCRv5_mobile_det.onnx",
            "ch_ppocr_mobile_v2.0_cls_infer.onnx",
            "ch_PP-OCRv5_rec_mobile_infer.onnx",
            "ppocrv5_dict.txt"
        };
        var missing = required.Where(x => !File.Exists(Path.Combine(folder, x))).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException($"OCR 模型缺失，请重新解压完整的程序包：{string.Join("、", missing)}");

        var engine = new OcrLite
        {
            DetPath = Path.Combine(folder, required[0]),
            ClsPath = Path.Combine(folder, required[1]),
            RecPath = Path.Combine(folder, required[2]),
            KeyDicPath = Path.Combine(folder, required[3]),
            ThreadNum = Math.Clamp(Environment.ProcessorCount / 2, 1, 6)
        };
        try { await engine.InitModels(); }
        catch { engine.Dispose(); throw; }
        _engine = engine;
        return engine;
    }
}

/// <param name="TallFraction">Share of text lines whose box is much taller than wide (page on its side).</param>
/// <param name="FlippedFraction">Share of text lines the angle classifier turned by 180 degrees.</param>
/// <param name="Rotation">Clockwise rotation applied to the page before this reading.</param>
internal sealed record RapidOcrPage(TextPage Page, double TallFraction, double FlippedFraction, int Rotation);
