using System.Security.Cryptography;
using System.Text;

namespace CustomsClearanceConsole;

/// <summary>Writes beside the destination, flushes, then atomically publishes the complete file.</summary>
internal static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        var destination = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        var identity = OperatingSystem.IsWindows() ? destination.ToUpperInvariant() : destination;
        using var mutex = new Mutex(false, "CustomsConsole.File." +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("另一个程序正在保存同一文件，请稍后重试。");
            WriteCore(destination, folder, write);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private static void WriteCore(string destination, string folder, Action<Stream> write)
    {
        var temporary = Path.Combine(folder, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { AppLog.Write($"临时写入文件清理失败：{ex.Message}"); }
        }
    }
}
