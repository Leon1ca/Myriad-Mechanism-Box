using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace CustomsClearanceConsole;

internal sealed record CleanupResult(int Moved, IReadOnlyList<string> Failed, IReadOnlySet<string> MovedPaths);

internal static partial class FileCleanupService
{
    public static List<string> GetDeclarationFiles(string folder)
    {
        var safeFolder = ValidateTargetFolder(folder);
        var extensions = new HashSet<string>(BatchScanner.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(safeFolder, "*", System.IO.SearchOption.TopDirectoryOnly)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static List<string> GetScreenshotFiles(string folder, IEnumerable<DeclarationRecord> records)
    {
        var safeFolder = ValidateTargetFolder(folder);
        var known = records
            .Select(x => x.ScreenshotPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(safeFolder, "*", System.IO.SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Where(path => known.Contains(Path.GetFullPath(path)) || ScreenshotName().IsMatch(Path.GetFileName(path)))
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static CleanupResult MoveToRecycleBin(IEnumerable<string> candidates, string expectedFolder)
    {
        var safeFolder = ValidateTargetFolder(expectedFolder);
        var moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new List<string>();
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (!Path.GetDirectoryName(fullPath)!.Equals(safeFolder, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("文件不在已确认的当前文件夹中。");
                if (!File.Exists(fullPath)) continue;
                FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                moved.Add(fullPath);
            }
            catch (Exception ex)
            {
                AppLog.Write($"移入回收站失败：{candidate}\n{ex}");
                failed.Add(Path.GetFileName(candidate));
            }
        }
        return new CleanupResult(moved.Count, failed, moved);
    }

    public static string ValidateTargetFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("请先在“目录设置”中选择有效文件夹。");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? "");
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("为保护数据，不能清理磁盘根目录。");

        var protectedFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.TrimEndingDirectorySeparator(Path.GetFullPath(x)));

        if (protectedFolders.Any(x => full.Equals(x, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("为保护数据，不能直接清理系统、桌面、文档或程序根目录；请选择其中的具体业务文件夹。");
        return full;
    }

    [GeneratedRegex(@"^\d{18}(?:_\d{8}_\d{6})?\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScreenshotName();
}
