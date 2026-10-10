using System.Text;
using System.Text.Json;

namespace CustomsClearanceConsole;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();

        var commandLineMode = args.Length > 0;
        if (commandLineMode)
        {
            // CI test/report commands must fail fast. The default handler
            // (AppLog.ShowUnexpected) shows a synchronous MessageBox, which hangs an
            // unattended snapshot run until the whole job times out. Write the full
            // exception to stderr and app.log, then exit non-zero straight away.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => FailCommandLine("UI 线程未处理异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                FailCommandLine("进程未处理异常", e.ExceptionObject as Exception ?? new Exception("未知错误"));
        }
        else
        {
            // Normal desktop use keeps the friendly interactive error dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => AppLog.ShowUnexpected(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                AppLog.Write(e.ExceptionObject as Exception ?? new Exception("未知错误"));
        }

        // Fire-and-forget work (browser disposal, background cleanup) must not fail silently.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write(e.Exception);
            e.SetObserved();
        };

        AppFonts.Initialize();
        TryInitializePdfium();

        try
        {
            if (CommandLine.Run(args)) return;
        }
        catch (Exception ex) when (commandLineMode)
        {
            FailCommandLine($"命令 {args[0]} 失败", ex);
        }

        // OCR work folders are removed after each file; a crash or power loss can leave rendered
        // pages behind. Only folders older than a day are swept, never a running scan's.
        _ = Task.Run(() => TemporaryDirectory.PurgeStale(AppPaths.TempRoot, TimeSpan.FromDays(1)));
        Application.Run(new MainForm());
    }

    /// <summary>
    /// Headless fatal path: persist the full exception, mirror it to stderr (captured in the
    /// CI job log), then terminate with a non-zero exit code. Never shows a dialog.
    /// </summary>
    private static void FailCommandLine(string context, Exception ex)
    {
        var detail = $"[CLI-FATAL] {context}: {Environment.NewLine}{ex}";
        try { AppLog.Write(detail); } catch { }
        try
        {
            Console.Error.WriteLine(detail);
            Console.Error.WriteLine($"[CLI-FATAL] AppLog: {AppLog.FilePath}");
            Console.Error.Flush();
            Console.Out.Flush();
        }
        catch { }
        Environment.Exit(1);
    }

    private static void TryInitializePdfium()
    {
        try { PdfiumNative.Initialize(); }
        catch (Exception ex)
        {
            // UI, export and browser-only commands must run even without the PDF runtime;
            // OCR commands surface the missing dependency when they actually extract.
            AppLog.Write($"PDF 组件初始化未完成：{ex.Message}");
        }
    }
}
