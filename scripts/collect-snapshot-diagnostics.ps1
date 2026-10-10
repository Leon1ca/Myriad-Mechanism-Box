# Collects whatever evidence exists when the native UI snapshot process hangs or is killed.
# Runs before the child is terminated so window/process state is still observable, and again
# after (safe to run twice). Never throws: diagnostics must not mask the real failure.
param(
    [Parameter(Mandatory = $true)][string]$OutputFolder,
    [Parameter(Mandatory = $false)][int]$ProcessId = 0,
    [Parameter(Mandatory = $false)][string]$Label = 'snapshot'
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null
$summary = [ordered]@{
    label       = $Label
    collectedAt = (Get-Date).ToString('o')
    processId   = $ProcessId
    errors      = @()
}

function Add-Note([string]$message) {
    Write-Host "[snapshot-diagnostics] $message"
    $summary.errors += $message
}

# --- process state (window titles tell us whether a modal dialog is blocking) ---
if ($ProcessId -gt 0) {
    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
        $summary.process = [ordered]@{
            id            = $proc.Id
            name          = $proc.ProcessName
            responding    = $proc.Responding
            cpuSeconds    = $proc.CPU
            workingSetMB  = [math]::Round($proc.WorkingSet64 / 1MB, 1)
            threadCount   = $proc.Threads.Count
            startTime     = $proc.StartTime.ToString('o')
            mainWindowTitle = $proc.MainWindowTitle
        }
        Write-Host "[snapshot-diagnostics] process=$($proc.ProcessName) responding=$($proc.Responding) title='$($proc.MainWindowTitle)'"
    } catch { Add-Note "读取进程 $ProcessId 失败：$($_.Exception.Message)" }

    try {
        Add-Type -AssemblyName System.Windows.Forms
        # Enumerate top-level windows owned by the process via the Win32 API (works cross-process).
        $signature = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinEnum {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    public static List<string> Titles(uint targetPid) {
        var result = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) {
                var sb = new StringBuilder(512);
                GetWindowTextW(h, sb, sb.Capacity);
                if (sb.Length > 0) result.Add(sb.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@
        Add-Type -TypeDefinition $signature -ErrorAction SilentlyContinue
        $titles = [WinEnum]::Titles([uint32]$ProcessId)
        $summary.windowTitles = $titles
        Write-Host "[snapshot-diagnostics] visible windows: $($titles -join ' | ')"
    } catch { Add-Note "枚举窗口失败：$($_.Exception.Message)" }
}

# --- desktop capture (proves what the unattended session actually showed) ---
try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $path = Join-Path $OutputFolder "$Label-desktop.png"
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    $summary.desktopCapture = $path
    $summary.virtualScreen = $bounds.ToString()
    Write-Host "[snapshot-diagnostics] desktop capture saved to $path ($($bounds.Width)x$($bounds.Height))"
} catch { Add-Note "桌面抓图失败：$($_.Exception.Message)" }

# --- evidence inventory with timestamps (which state was being produced?) ---
try {
    $inventory = Get-ChildItem -LiteralPath $OutputFolder -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime |
        ForEach-Object { [ordered]@{ file = $_.FullName.Substring($OutputFolder.Length).TrimStart([char[]]@('\', '/')); bytes = $_.Length; writtenAt = $_.LastWriteTime.ToString('o') } }
    $summary.evidence = $inventory
    $summary.evidenceCount = @($inventory).Count
    Write-Host "[snapshot-diagnostics] $($summary.evidenceCount) evidence files"
} catch { Add-Note "清点证据失败：$($_.Exception.Message)" }

# --- app logs: the process pins CUSTOMS_CONSOLE_APILOG, plus per-state copies may exist ---
try {
    $logOverride = $env:CUSTOMS_CONSOLE_APILOG
    if (-not [string]::IsNullOrWhiteSpace($logOverride) -and (Test-Path -LiteralPath $logOverride)) {
        $tail = Join-Path $OutputFolder "$Label-app.log"
        Copy-Item -LiteralPath $logOverride -Destination $tail -Force
        Write-Host "[snapshot-diagnostics] app log copied from $logOverride"
        Write-Host "----- app.log tail -----"
        Get-Content -LiteralPath $logOverride -Tail 120 | ForEach-Object { Write-Host $_ }
        Write-Host "----- end app.log tail -----"
    }
    Get-ChildItem -LiteralPath $OutputFolder -Recurse -Filter 'app.log' -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Host "per-state app.log: $($_.FullName)"; Get-Content -LiteralPath $_.FullName -Tail 40 | ForEach-Object { Write-Host $_ } }
    $localApp = Join-Path $env:LOCALAPPDATA '关单核验台\app.log'
    if (Test-Path -LiteralPath $localApp) {
        Copy-Item -LiteralPath $localApp -Destination (Join-Path $OutputFolder "$Label-localappdata-app.log") -Force
        Write-Host "LOCALAPPDATA app.log copied."
    }
} catch { Add-Note "收集 app.log 失败：$($_.Exception.Message)" }

try {
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputFolder "$Label-diagnostics.json") -Encoding UTF8
    Write-Host "[snapshot-diagnostics] summary written to $Label-diagnostics.json"
} catch { Write-Host "[snapshot-diagnostics] 写摘要失败：$($_.Exception.Message)" }
