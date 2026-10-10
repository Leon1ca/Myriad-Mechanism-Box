namespace CustomsClearanceConsole;

/// <summary>
/// Browser profiles for the verification window.
///
/// Edge and Chrome refuse remote debugging on the user's everyday profile (Chromium 136+), and
/// auto-fill plus the long screenshot need that debugging connection, so the verification
/// window can never be the user's own Edge profile with its account, favourites and
/// extensions. It used to get a brand-new empty profile for every session instead, which is
/// why it looked like a different browser each time: default zoom, no remembered window size,
/// no site settings or cookies, and a slow first start while the profile was created.
///
/// Interactive sessions now reuse a dedicated, persistent profile kept in the app's data
/// folder. Settings the user makes in that window (zoom, site permissions, the single-window
/// site's cookies) survive, and later starts are fast. A browser can only own a profile once,
/// so a few numbered slots serve concurrent verification windows; when every slot is taken
/// (or for automated tests) a throw-away profile is used as before.
/// </summary>
internal static class BrowserProfileStore
{
    private const int Slots = 4;
    private static readonly HashSet<string> InUse = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Root of the throw-away session profiles (swept by <see cref="BrowserValidation.PurgeStaleProfiles"/>).</summary>
    public static string TemporaryRoot => Path.Combine(AppLog.Folder, "BrowserProfiles");

    /// <summary>
    /// Reserves a persistent profile folder for <paramref name="browserPath"/>, or returns null
    /// when every slot is in use by this app or by a still-running browser.
    /// </summary>
    public static string? TryAcquirePersistent(string browserPath)
    {
        var browser = Path.GetFileNameWithoutExtension(browserPath).ToLowerInvariant();
        lock (InUse)
        {
            for (var slot = 1; slot <= Slots; slot++)
            {
                var folder = Path.Combine(AppLog.Folder, "BrowserProfile", slot == 1 ? browser : $"{browser}-{slot}");
                if (InUse.Contains(folder) || IsHeldByBrowser(folder)) continue;
                try { Directory.CreateDirectory(folder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write($"无法创建浏览器配置目录：{folder} · {ex.Message}");
                    return null;
                }
                InUse.Add(folder);
                return folder;
            }
        }
        return null;
    }

    public static void Release(string folder)
    {
        lock (InUse) InUse.Remove(folder);
    }

    /// <summary>
    /// Chromium keeps a "lockfile" open (shared for reading only) in a profile it owns, for
    /// example a verification window left open by an earlier run of the app.
    /// </summary>
    private static bool IsHeldByBrowser(string folder)
    {
        var lockFile = Path.Combine(folder, "lockfile");
        if (!File.Exists(lockFile)) return false;
        try
        {
            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }
}
