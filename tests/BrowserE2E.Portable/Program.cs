using System.Reflection;

namespace CustomsClearanceConsole;

/// <summary>
/// Usage:
///   BrowserE2E.Portable &lt;output&gt;                            full 33-scenario suite
///   BrowserE2E.Portable &lt;output&gt; &lt;ScenarioMethod&gt; &lt;count&gt;   one scenario repeated, e.g.
///                                                        RunOopifViewportReadFailureAsync 30
/// The browser comes from CUSTOMS_CONSOLE_E2E_BROWSER (see README.md).
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1) return await BrowserCaptureE2E.RunAsync(args[0]);
        if (args.Length == 3 && int.TryParse(args[2], out var count) && count > 0)
            return await RepeatAsync(args[0], args[1], count);
        Console.Error.WriteLine("用法：BrowserE2E.Portable <输出目录> [<场景方法名> <次数>]");
        return 2;
    }

    /// <summary>Runs one private scenario method repeatedly, each time with a fresh fixture server.</summary>
    private static async Task<int> RepeatAsync(string output, string scenario, int count)
    {
        var harness = typeof(BrowserCaptureE2E);
        var serverType = harness.GetNestedType("TestServer", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 TestServer。");
        var method = harness.GetMethod(scenario, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new ArgumentException($"找不到场景方法：{scenario}");
        var browser = BrowserCaptureE2E.TestBrowser()
            ?? throw new InvalidOperationException("未找到浏览器；请设置 CUSTOMS_CONSOLE_E2E_BROWSER。");
        var failures = 0;
        for (var i = 0; i < count; i++)
        {
            using var server = (IDisposable)Activator.CreateInstance(serverType, nonPublic: true)!;
            serverType.GetMethod("Start")!.Invoke(server, null);
            var task = (Task<BrowserCaptureE2E.Scenario>)method.Invoke(null, [Path.Combine(output, $"run-{i:D3}"), browser, server])!;
            var result = await task;
            if (!result.Pass) failures++;
            Console.WriteLine($"#{i} {(result.Pass ? "PASS" : "FAIL")} {string.Join(" | ", result.Details)}");
        }
        Console.WriteLine($"{scenario}: {count - failures}/{count} passed");
        return failures == 0 ? 0 : 1;
    }
}
