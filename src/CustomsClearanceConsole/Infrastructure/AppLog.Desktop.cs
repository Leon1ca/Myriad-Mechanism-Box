namespace CustomsClearanceConsole;

internal static partial class AppLog
{
    public static void ShowUnexpected(Exception ex)
    {
        Write(ex);
        MessageBox.Show($"程序遇到意外错误，已写入日志：\n{FilePath}\n\n{ex.Message}", "关单核验台", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
