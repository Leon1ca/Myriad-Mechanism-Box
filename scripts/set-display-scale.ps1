param(
    [Parameter(Mandatory = $true)][int]$ScalePercent,
    [int]$MinWidth = 0,
    [int]$MinHeight = 0
)

# Switches the primary display to a larger mode (when requested) and then to the given
# scaling percentage, the same way Settings > Display > Scale does it: through the
# per-source DPI scale of DisplayConfig. The change is live for per-monitor-aware apps
# started afterwards; the session's system DPI stays at its logon value, so this exercises
# the path where the window DPI differs from the system DPI. Used only by CI.
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class DisplayScale
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);
    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathArray, ref uint modes, byte[] modeArray, IntPtr topology);
    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(byte[] packet);
    [DllImport("user32.dll")]
    static extern int DisplayConfigSetDeviceInfo(byte[] packet);

    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    static DEVMODE NewMode() { var m = new DEVMODE(); m.Size = (short)Marshal.SizeOf(typeof(DEVMODE)); return m; }

    public static string EnsureMode(int minWidth, int minHeight)
    {
        var current = NewMode();
        EnumDisplaySettings(null, -1, ref current);
        if (current.PelsWidth >= minWidth && current.PelsHeight >= minHeight)
            return "mode " + current.PelsWidth + "x" + current.PelsHeight + " already large enough";
        DEVMODE best = NewMode(); bool found = false; string all = "";
        for (int i = 0; ; i++)
        {
            var m = NewMode();
            if (!EnumDisplaySettings(null, i, ref m)) break;
            if (all.IndexOf(m.PelsWidth + "x" + m.PelsHeight + " ", StringComparison.Ordinal) < 0) all += m.PelsWidth + "x" + m.PelsHeight + " ";
            if (m.PelsWidth < minWidth || m.PelsHeight < minHeight) continue;
            if (!found || (long)m.PelsWidth * m.PelsHeight < (long)best.PelsWidth * best.PelsHeight) { best = m; found = true; }
        }
        if (!found)
        {
            // Take the largest mode available; the caller reports what was reached.
            for (int i = 0; ; i++)
            {
                var m = NewMode();
                if (!EnumDisplaySettings(null, i, ref m)) break;
                if (!found || (long)m.PelsWidth * m.PelsHeight > (long)best.PelsWidth * best.PelsHeight) { best = m; found = true; }
            }
        }
        best.Fields = 0x80000 | 0x100000;
        int result = ChangeDisplaySettings(ref best, 0);
        var after = NewMode();
        EnumDisplaySettings(null, -1, ref after);
        return "modes: " + all.Trim() + " | requested " + best.PelsWidth + "x" + best.PelsHeight + " result=" + result + " now " + after.PelsWidth + "x" + after.PelsHeight;
    }

    public static string SetScale(int percent)
    {
        uint pathCount, modeCount;
        int rc = GetDisplayConfigBufferSizes(2, out pathCount, out modeCount);
        if (rc != 0) return "GetDisplayConfigBufferSizes failed " + rc;
        var paths = new byte[pathCount * 72];
        var modes = new byte[modeCount * 64];
        rc = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (rc != 0) return "QueryDisplayConfig failed " + rc;
        string report = "";
        for (int p = 0; p < pathCount; p++)
        {
            int offset = p * 72;
            uint low = BitConverter.ToUInt32(paths, offset);
            int high = BitConverter.ToInt32(paths, offset + 4);
            uint sourceId = BitConverter.ToUInt32(paths, offset + 8);

            // DISPLAYCONFIG_DEVICE_INFO_HEADER (type -3 = get DPI scale) + min/cur/max relative steps.
            var get = new byte[32];
            BitConverter.GetBytes(-3).CopyTo(get, 0);
            BitConverter.GetBytes(32).CopyTo(get, 4);
            BitConverter.GetBytes(low).CopyTo(get, 8);
            BitConverter.GetBytes(high).CopyTo(get, 12);
            BitConverter.GetBytes(sourceId).CopyTo(get, 16);
            rc = DisplayConfigGetDeviceInfo(get);
            if (rc != 0) { report += "source " + sourceId + ": get failed " + rc + "; "; continue; }
            int min = BitConverter.ToInt32(get, 20), cur = BitConverter.ToInt32(get, 24), max = BitConverter.ToInt32(get, 28);
            int recommended = Math.Abs(min);
            int target = Array.IndexOf(Steps, percent);
            if (target < 0) return "unsupported percent " + percent;
            int relative = target - recommended;
            string range = "recommended " + Steps[recommended] + "% range " + Steps[recommended + min] + "%.." + Steps[Math.Min(Steps.Length - 1, recommended + max)] + "% current " + Steps[recommended + cur] + "%";
            if (relative < min || relative > max) { report += "source " + sourceId + ": " + percent + "% not allowed (" + range + "); "; continue; }

            var set = new byte[24];
            BitConverter.GetBytes(-4).CopyTo(set, 0);
            BitConverter.GetBytes(24).CopyTo(set, 4);
            BitConverter.GetBytes(low).CopyTo(set, 8);
            BitConverter.GetBytes(high).CopyTo(set, 12);
            BitConverter.GetBytes(sourceId).CopyTo(set, 16);
            BitConverter.GetBytes(relative).CopyTo(set, 20);
            rc = DisplayConfigSetDeviceInfo(set);
            report += "source " + sourceId + ": " + range + " -> set " + percent + "% rc=" + rc + "; ";
        }
        return report;
    }
}
'@

if ($MinWidth -gt 0 -and $MinHeight -gt 0) {
    Write-Host ([DisplayScale]::EnsureMode($MinWidth, $MinHeight))
}
$result = [DisplayScale]::SetScale($ScalePercent)
Write-Host $result
if ($result -notmatch "set $ScalePercent% rc=0") { throw "显示缩放未能设为 $ScalePercent%：$result" }
Start-Sleep -Seconds 2
