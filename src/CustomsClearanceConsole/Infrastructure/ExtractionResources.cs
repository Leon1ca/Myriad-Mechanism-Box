namespace CustomsClearanceConsole;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }
    public TemporaryDirectory(string root)
    {
        Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
        catch (Exception ex) { AppLog.Write($"临时文件清理失败：{Path} · {ex.Message}"); }
    }

    /// <summary>Deletes work folders under <paramref name="root"/> older than <paramref name="age"/>; best effort.</summary>
    public static void PurgeStale(string root, TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var cutoff = DateTime.UtcNow - age;
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(folder) < cutoff) Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) { AppLog.Write($"清理过期临时文件失败：{ex.Message}"); }
    }
}
