using System.Text;
using System.Text.Json;

namespace CustomsClearanceConsole;

/// <summary>One registry for dispatch, argument validation and command help.</summary>
internal static class CommandLine
{
    private sealed record Command(int Minimum, int Maximum, string Usage, Action<string[]> Run);
    private static readonly Dictionary<string, Command> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--self-test"] = new(2, 2, "<目录>", a => SelfTest.RunAsync(a[1]).GetAwaiter().GetResult()),
        ["--regression-test"] = new(2, 2, "<目录>", a => SelfTest.RunKnownRegressionAsync(a[1]).GetAwaiter().GetResult()),
        ["--resource-self-test"] = new(2, 2, "<目录>", a => Environment.ExitCode = ResourceSelfTest.RunAsync(a[1]).GetAwaiter().GetResult()),
        ["--ocr-debug"] = new(2, 2, "<文件>", a => SelfTest.DumpOcrAsync(a[1]).GetAwaiter().GetResult()),
        ["--ocr-self-test"] = new(2, 2, "<目录>", a => Environment.ExitCode = OcrSelfTest.RunAsync(a[1]).GetAwaiter().GetResult()),
        ["--ui-snapshot"] = new(2, 4, "<目录> [宽度] [高度]", a =>
            SelfTest.CaptureUi(a[1], Dimension(a, 2, 1440), Dimension(a, 3, 900))),
        ["--ui-state-snapshot"] = new(2, 2, "<目录>", a => SelfTest.CaptureAllStates(a[1])),
        ["--ui-dialog-snapshot"] = new(3, 3, "<类型> <目录>", a => SelfTest.CaptureDialog(a[1], a[2])),
        ["--ui-contract-self-test"] = new(1, 1, "", _ => SelfTest.RunUiContracts()),
        ["--export-samples"] = new(2, 2, "<目录>", a =>
        {
            var report = ExportValidation.Run(a[1]);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = report.Pass ? 0 : 1;
        }),
        ["--browser-e2e"] = new(2, 2, "<目录>", a => Environment.ExitCode = BrowserCaptureE2E.RunAsync(a[1]).GetAwaiter().GetResult()),
        ["--env-report"] = new(2, 2, "<目录>", a => SelfTest.WriteEnvironmentReport(a[1]))
    };

    public static bool Run(string[] args)
    {
        if (args.Length == 0) return false;
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 1 && args[0] == "--help")
        {
            foreach (var entry in Commands) Console.WriteLine($"{entry.Key} {entry.Value.Usage}".TrimEnd());
            return true;
        }
        if (!Commands.TryGetValue(args[0], out var command))
        {
            Console.Error.WriteLine($"未知命令：{args[0]}。使用 --help 查看用法。");
            Environment.ExitCode = 2;
            return true;
        }
        if (args.Length < command.Minimum || args.Length > command.Maximum ||
            args.Skip(1).Any(string.IsNullOrWhiteSpace))
        {
            Console.Error.WriteLine($"用法：{args[0]} {command.Usage}");
            Environment.ExitCode = 2;
            return true;
        }
        command.Run(args);
        return true;
    }

    private static int Dimension(string[] args, int index, int fallback)
    {
        if (args.Length <= index) return fallback;
        if (int.TryParse(args[index], out var value) && value is > 0 and <= 8192) return value;
        throw new ArgumentException("快照宽度和高度必须为 1–8192 的整数。");
    }
}
