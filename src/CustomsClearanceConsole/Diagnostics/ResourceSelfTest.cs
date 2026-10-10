using RapidOCRLib;

namespace CustomsClearanceConsole;

internal static class ResourceSelfTest
{
    public static async Task<int> RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        using (var empty = new OcrLite()) { empty.Dispose(); }
        checks.Add("PASS: 未加载模型及重复释放安全");

        var corrupt = Path.Combine(output, "invalid-model.onnx");
        File.WriteAllText(corrupt, "deliberately invalid model");
        var failed = false;
        using (var partial = new OcrLite())
        {
            try
            {
                await partial.InitModels(
                    Path.Combine(AppPaths.RapidOcrModels, "ch_PP-OCRv5_mobile_det.onnx"),
                    Path.Combine(AppPaths.RapidOcrModels, "ch_ppocr_mobile_v2.0_cls_infer.onnx"),
                    corrupt, Path.Combine(AppPaths.RapidOcrModels, "ppocrv5_dict.txt"), 1);
            }
            catch (Microsoft.ML.OnnxRuntime.OnnxRuntimeException) { failed = true; }
        }
        if (!failed) throw new InvalidOperationException("损坏模型未被拒绝");
        checks.Add("PASS: 已加载部分模型后的初始化失败可安全释放");

        await using (var disposed = new RapidOcrEngine())
        {
            await disposed.DisposeAsync();
            try { await disposed.RecognizeAsync([], 1, 1, CancellationToken.None); throw new Exception("Disposed engine accepted work"); }
            catch (ObjectDisposedException) { }
        }
        checks.Add("PASS: 已释放 OCR 引擎拒绝新任务");

        var console = Console.Out;
        await using (var engine = new RapidOcrEngine())
        {
            failed = false;
            try { await engine.RecognizeAsync([1, 2, 3, 4], 1, 1, CancellationToken.None); }
            catch (Exception ex) when (ex is InvalidDataException or Emgu.CV.Util.CvException or ArgumentException or InvalidOperationException) { failed = true; }
            if (!failed || !ReferenceEquals(console, Console.Out))
                throw new InvalidOperationException("损坏图片错误或控制台输出未正确保留");
        }
        checks.Add("PASS: 损坏图片明确报错且不改变进程 Console.Out");
        var users = typeof(PdfiumNative).GetField("_libraryUsers",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var before = (int)users.GetValue(null)!;
        try { using var missing = new PdfiumNative.PdfDocument(Path.Combine(output, "missing.pdf")); }
        catch (FileNotFoundException) { }
        if ((int)users.GetValue(null)! != before) throw new Exception("Missing PDF leaked native library ownership");
        checks.Add("PASS: PDF 文件读取失败不泄漏原生库引用");
        var badPdf = Path.Combine(output, "invalid.pdf");
        File.WriteAllText(badPdf, "invalid PDF");
        try { using var invalid = new PdfiumNative.PdfDocument(badPdf); throw new Exception("Invalid PDF accepted"); }
        catch (InvalidDataException) { }
        if ((int)users.GetValue(null)! != before) throw new Exception("Invalid PDF leaked native library ownership");
        File.Delete(badPdf);
        checks.Add("PASS: PDF 打开失败释放固定内存和原生库引用");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        checks.Add("PASS: 失败与释放后的终结过程正常");
        File.Delete(corrupt);
        var report = string.Join(Environment.NewLine, checks) + Environment.NewLine + $"RESOURCE_SELF_TEST_OK: {checks.Count} checks";
        File.WriteAllText(Path.Combine(output, "report.txt"), report);
        Console.WriteLine(report);
        return 0;
    }
}
