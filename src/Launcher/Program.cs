using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CustomsLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var host = Path.Combine(root, "runtime", "dotnet.exe");
            var appFolder = Path.Combine(root, "app");
            var app = Path.Combine(appFolder, "关单核验台.dll");
            if (!File.Exists(host) || !File.Exists(app))
            {
                MessageBox.Show("程序组件不完整。请完整解压压缩包后，再双击“关单核验台.exe”。", "关单核验台", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                var arguments = new StringBuilder();
                arguments.Append(Quote(app));
                foreach (var arg in args) arguments.Append(' ').Append(Quote(arg));
                var start = new ProcessStartInfo
                {
                    FileName = host,
                    Arguments = arguments.ToString(),
                    WorkingDirectory = appFolder,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var process = Process.Start(start);
                if (args.Length > 0 && process != null)
                {
                    process.WaitForExit();
                    Environment.ExitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序启动失败：" + ex.Message, "关单核验台", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Windows command-line quoting (CommandLineToArgvW rules). Backslashes are only special
        // before a quote, so a folder argument ending in '\' (for example "D:\out\") must have its
        // trailing backslashes doubled or it would escape the closing quote and swallow the next
        // argument. Kept to C# 5 syntax because build-launcher.ps1 compiles with the .NET
        // Framework csc.
        private static string Quote(string value)
        {
            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(c);
                }
                backslashes = 0;
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
