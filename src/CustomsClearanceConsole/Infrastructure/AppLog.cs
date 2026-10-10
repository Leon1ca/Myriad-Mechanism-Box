using System.Text;

namespace CustomsClearanceConsole;

internal static partial class AppLog
{
    private static readonly object Gate = new();
    public static string Folder => AppPaths.DataFolder;

    /// <summary>
    /// Log file used by all writes. CI can pin it to a path inside the uploaded evidence
    /// folder via CUSTOMS_CONSOLE_APILOG, otherwise it stays next to the data folder.
    /// </summary>
    public static string FilePath
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("CUSTOMS_CONSOLE_APILOG");
            return string.IsNullOrWhiteSpace(configured) ? Path.Combine(Folder, "app.log") : configured;
        }
    }

    public static void Write(Exception ex) => Write(ex.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                RotateIfLarge(path);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }

    private const long MaximumLogBytes = 5L * 1024 * 1024;

    /// <summary>Keeps the log bounded: past the limit the current file becomes app.log.1 (one generation).</summary>
    private static void RotateIfLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaximumLogBytes) return;
            File.Move(path, path + ".1", overwrite: true);
        }
        catch { }
    }

}
