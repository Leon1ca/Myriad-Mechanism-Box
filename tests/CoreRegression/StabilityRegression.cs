using System.Text;
using System.Text.Json;

namespace CustomsClearanceConsole;

internal static class StabilityRegression
{
    public static async Task RunAsync(Action<bool, string> check, string folder)
    {
        var destination = Path.Combine(folder, "atomic.json");
        File.WriteAllText(destination, "original");
        try
        {
            AtomicFile.Write(destination, stream =>
            {
                stream.Write(Encoding.UTF8.GetBytes("incomplete"));
                throw new IOException("simulated interruption");
            });
        }
        catch (IOException) { }
        check(File.ReadAllText(destination) == "original", "保存中途异常保留原文件");
        check(!Directory.EnumerateFiles(folder, ".atomic.json.*.tmp").Any(), "保存失败不遗留临时文件");

        var shared = Path.Combine(folder, "concurrent.json");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            new StateStore(shared).Save(new AppState { LastFolder = $"batch-{i}", Records = [new() { ContractNo = $"contract-{i}" }] }))));
        var concurrent = new StateStore(shared).Load();
        check(concurrent.Records.Count == 1 &&
            concurrent.LastFolder.Split('-')[1] == concurrent.Records[0].ContractNo.Split('-')[1],
            "并发保存使用独立临时文件，最终历史是完整的同一批次");
        check(!Directory.EnumerateFiles(folder, ".concurrent.json.*.tmp").Any(), "并发保存完成后清理全部临时文件");

        var history = Path.Combine(folder, "nullable-history.json");
        File.WriteAllText(history, """
            {"LastFolder":null,"PageSize":13,"Records":[null,{"DeclarationNo":null,"Status":null,
            "Consignee":null,"Totals":null,"LineTotals":[null,{"ProductName":null,"BackfilledFields":null}]}]}
            """);
        var normalized = new StateStore(history).Load();
        check(normalized.PageSize == 50 && normalized.LastFolder == "" && normalized.Records.Count == 1,
            "历史显式空值和空记录不会丢弃同批有效记录");
        var row = normalized.Records[0];
        check(row.NeedsAttention && row.LineTotals.Count == 1 && row.LineTotals[0].ItemConsistency == "未完整复核",
            "空状态和空明细字段恢复为可展示的待核对记录");
        File.WriteAllText(history, """{"Records":null,"ScreenshotFolder":null}""");
        check(new StateStore(history).Load().Records.Count == 0, "空历史列表正常恢复");

        File.WriteAllText(history, """{"Records":[{"Totals":{"usd":10,"USD":10}}]}""");
        check(new StateStore(history).Load().Records[0].Totals["USD"] == 10,
            "历史币种大小写重复不会重复计价或导致整批丢失");
        File.WriteAllText(history, """{"Records":[{"Totals":{"usd":10,"USD":20,"EUR":5}}]}""");
        var conflict = new StateStore(history).Load().Records[0];
        check(conflict.NeedsAttention && conflict.Totals.Count == 1 && conflict.Totals["EUR"] == 5,
            "冲突历史币种排除合计并提示，其余币种保留");

        File.WriteAllText(history, """{"Records":[{"LineTotals":[{"Amount":10,"VerificationAmount":11,"ProductName":"旧商品","VerificationProductName":"另一商品","IsReliable":false,"BackfilledFields":["ProductName"],"AmountOnlyFromSecondary":true}]}]}""");
        var historical = new StateStore(history).Load();
        new StateStore(history).Save(historical);
        var historicalLine = new StateStore(history).Load().Records[0].LineTotals[0];
        check(historicalLine.HasValueDifference && !historicalLine.IsReliable &&
            historicalLine.BackfilledFields.Contains("ProductName") && historicalLine.AmountOnlyFromSecondary,
            "删除双引擎执行代码后，旧历史差异和来源信息完整往返");
        File.WriteAllText(history, "{invalid");
        new StateStore(history).Load();
        new StateStore(history).Load();
        check(Directory.EnumerateFiles(folder, "nullable-history.json.corrupt-*").Count() == 2,
            "同秒多次损坏历史备份互不覆盖");

        var file = Path.Combine(folder, "late-cancel.pdf");
        File.WriteAllText(file, "fixture");
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        await using var scanner = new BatchScanner((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new DocumentText());
        });
        try
        {
            await scanner.ScanFilesAsync([file], new InlineProgress<(int, int, string)>(_ => { }),
                cancellation.Token, new InlineProgress<DeclarationRecord>(_ => count++));
        }
        catch (OperationCanceledException) { }
        check(count == 0, "引擎暂不响应取消时，返回的迟到结果不回填");

        await using var callbackScanner = new BatchScanner((_, _) => Task.FromResult(new DocumentText()));
        var callbackCount = 0;
        var propagated = false;
        try
        {
            await callbackScanner.ScanFilesAsync([file], new InlineProgress<(int, int, string)>(_ => { }),
                CancellationToken.None, new InlineProgress<DeclarationRecord>(_ =>
                { callbackCount++; throw new InvalidOperationException("callback failed"); }));
        }
        catch (InvalidOperationException) { propagated = true; }
        check(propagated && callbackCount == 1, "界面回调异常不伪装成第二条文件识别失败记录");

        var nested = Path.Combine(folder, "new-export-folder", "export.md");
        MarkdownListExporter.Save(nested, [], DateTime.Now);
        check(File.Exists(nested), "Markdown 与 Excel 一样支持创建导出目录");
    }
}
