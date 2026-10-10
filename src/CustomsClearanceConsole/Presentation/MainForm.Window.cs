using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RescaleLayout(DeviceDpi, placeWindow: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Windows already applied the suggested window rectangle for the new monitor.
        base.OnDpiChanged(e);
        RescaleLayout(e.DeviceDpiNew, placeWindow: false);
    }

    /// <summary>
    /// Scales every child from the DPI it is laid out at to <paramref name="dpi"/> and re-applies
    /// the responsive layout. On first creation the window is also sized from the logical design
    /// size and fitted to the monitor's working area.
    /// </summary>
    private void RescaleLayout(int dpi, bool placeWindow)
    {
        if (dpi <= 0) return;
        var from = _layoutDpi;
        SuspendLayout();
        try
        {
            if (dpi != from)
            {
                DpiLayout.ScaleChildren(this, from, dpi);
                _layoutDpi = dpi;
                RefreshDpiImages();
            }
            if (placeWindow && !AllowOversizeForSnapshot) PlaceOnScreen();
        }
        finally { ResumeLayout(true); }
        ApplyResponsiveLayout();
        LayoutRecordState();
    }

    /// <summary>
    /// Design size 1440x900 and minimum 1200x720 are logical pixels. A 1920x1080 display at
    /// 150% only offers about 1280x690 logical pixels of working area, so the window is fitted
    /// to the working area (and opens maximized when the design size does not fit) instead of
    /// extending past the screen edge where the caption buttons would be unreachable.
    /// </summary>
    private void PlaceOnScreen()
    {
        var working = Screen.FromHandle(Handle).WorkingArea;
        var design = DpiLayout.Scale(UiTokens.Metrics.DesignWindow, _layoutDpi);
        var minimum = DpiLayout.Scale(UiTokens.Metrics.MinWindow, _layoutDpi);
        MinimumSize = new Size(Math.Min(minimum.Width, working.Width), Math.Min(minimum.Height, working.Height));
        var size = new Size(Math.Min(design.Width, working.Width), Math.Min(design.Height, working.Height));
        Bounds = new Rectangle(
            working.Left + (working.Width - size.Width) / 2,
            working.Top + (working.Height - size.Height) / 2,
            size.Width, size.Height);
        if (design.Width > working.Width || design.Height > working.Height)
            WindowState = FormWindowState.Maximized;
    }

    /// <summary>Button images are bitmaps loaded for a DPI; reload them after a DPI change.</summary>
    private void RefreshDpiImages()
    {
        if (_settings is not null) { _settings.Image?.Dispose(); _settings.Image = UiV2Icons.Load(Ui2.SettingsWhite, 16, _layoutDpi); }
        if (_cleanupButton is not null) { _cleanupButton.Image?.Dispose(); _cleanupButton.Image = UiV2Icons.Load(Ui2.TrashInk, 16, _layoutDpi); }
        if (_scan is not null && _exportButton is not null) RefreshAll();
    }

    /// <summary>
    /// Re-runs the record-state layout once the window is really shown. Ancestors report
    /// Visible == false until the first show and the grid host has no useful ClientSize
    /// during construction, so the initial empty/ready guidance must be laid out and raised
    /// here for the first real frame (P1).
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutRecordState();
    }

    private void ToggleMaximize() => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

    private void BeginWindowDrag()
    {
        if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 0 || Height <= 0) return;
        if (WindowState == FormWindowState.Maximized) { var maximizedRegion = Region; Region = null; maximizedRegion?.Dispose(); return; }
        using var path = Theme.RoundedPath(new RectangleF(0, 0, Width, Height), 10F * DeviceDpi / 96F);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
        ApplyResponsiveLayout();
        LayoutRecordState();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    private bool _composedResize;

    protected override void WndProc(ref Message message)
    {
        const int wmNchittest = 0x84, wmGetMinMaxInfo = 0x24;
        var grip = DpiLayout.Scale(6, DeviceDpi);
        // A borderless window is normally clamped to the physical desktop tracking size.
        // Snapshot rendering explicitly lifts that clamp; the normal window keeps it.
        if (message.Msg == wmGetMinMaxInfo && AllowOversizeForSnapshot)
        {
            base.WndProc(ref message);
            var info = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
            info.MaxTrackSize = new Point(8192, 8192);
            Marshal.StructureToPtr(info, message.LParam, false);
            return;
        }
        if (message.Msg == wmGetMinMaxInfo && IsHandleCreated)
        {
            // A borderless window maximizes over the whole monitor, covering the taskbar.
            // Maximize to the working area of the monitor the window is on instead
            // (MaxPosition is relative to that monitor's top-left corner).
            base.WndProc(ref message);
            var info = Marshal.PtrToStructure<MinMaxInfo>(message.LParam);
            var screen = Screen.FromHandle(Handle);
            var working = screen.WorkingArea;
            info.MaxPosition = new Point(working.Left - screen.Bounds.Left, working.Top - screen.Bounds.Top);
            info.MaxSize = new Point(working.Width, working.Height);
            Marshal.StructureToPtr(info, message.LParam, false);
            return;
        }
        const int wmSizing = 0x0214, wmExitSizeMove = 0x0232, gwlExStyle = -20, wsExComposited = 0x02000000;
        if (message.Msg == wmSizing && !_composedResize)
        {
            // While the user drags the window edge, compose the whole window in one off-screen
            // pass so panels, grid and buttons move together instead of repainting one by one
            // (flicker / tearing while stretching). Only a resize needs this: moving the window
            // repaints nothing, and composing plus the full repaint afterwards made every drag
            // of the title bar stutter. Normal use keeps the cheaper painting.
            _composedResize = true;
            SetWindowLong(Handle, gwlExStyle, GetWindowLong(Handle, gwlExStyle) | wsExComposited);
        }
        else if (message.Msg == wmExitSizeMove && _composedResize)
        {
            _composedResize = false;
            SetWindowLong(Handle, gwlExStyle, GetWindowLong(Handle, gwlExStyle) & ~wsExComposited);
            Invalidate(true);
        }
        if (message.Msg == wmNchittest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref message);
            var value = (long)message.LParam;
            var point = PointToClient(new Point((short)value, (short)(value >> 16)));
            var left = point.X <= grip;
            var right = point.X >= ClientSize.Width - grip;
            var top = point.Y <= grip;
            var bottom = point.Y >= ClientSize.Height - grip;
            if (left && top) message.Result = (IntPtr)13;
            else if (right && top) message.Result = (IntPtr)14;
            else if (left && bottom) message.Result = (IntPtr)16;
            else if (right && bottom) message.Result = (IntPtr)17;
            else if (left) message.Result = (IntPtr)10;
            else if (right) message.Result = (IntPtr)11;
            else if (top) message.Result = (IntPtr)12;
            else if (bottom) message.Result = (IntPtr)15;
            return;
        }
        base.WndProc(ref message);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
