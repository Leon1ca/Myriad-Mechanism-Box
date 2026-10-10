namespace CustomsClearanceConsole;

/// <summary>
/// Deterministic high-DPI layout.
///
/// The forms used to set AutoScaleMode/AutoScaleDimensions in their constructors outside
/// SuspendLayout. WinForms performs the auto-scale immediately at that point, while the form is
/// still empty, so only the window itself grew by the display scale; every child added
/// afterwards kept its 96-DPI bounds while point-sized text grew with the display. At 150% this
/// clipped labels and pager buttons, and the main window (1440x900 logical = 2160x1350) no
/// longer fitted a 1920x1080 screen, pushing the caption buttons off-screen.
///
/// Forms are now built in 96-DPI logical pixels with auto-scaling off and scaled exactly once,
/// when the window exists and its real DPI is known, and again on every WM_DPICHANGED.
/// </summary>
internal static class DpiLayout
{
    public const int LogicalDpi = 96;

    private static float? _systemDpi;

    /// <summary>The DPI point-sized fonts are converted with (the session's system DPI).</summary>
    public static float SystemDpi
    {
        get
        {
            if (_systemDpi is null)
            {
                using var screen = Graphics.FromHwnd(IntPtr.Zero);
                _systemDpi = screen.DpiX > 0 ? screen.DpiX : LogicalDpi;
            }
            return _systemDpi.Value;
        }
    }

    public static int Scale(int logical, int dpi) =>
        (int)Math.Round(logical * dpi / (double)LogicalDpi, MidpointRounding.AwayFromZero);

    public static Size Scale(Size logical, int dpi) => new(Scale(logical.Width, dpi), Scale(logical.Height, dpi));

    /// <summary>Scales every child (bounds, margins, paddings, recursively) from one DPI to another.</summary>
    public static void ScaleChildren(Control root, int fromDpi, int toDpi)
    {
        if (fromDpi == toDpi || fromDpi <= 0 || toDpi <= 0) return;
        var ratio = toDpi / (float)fromDpi;
        var factor = new SizeF(ratio, ratio);
        foreach (Control child in root.Controls) child.Scale(factor);
    }

    /// <summary>
    /// Converts a width measured by TextRenderer without a device context (which uses the system
    /// DPI) back into logical pixels, so constructor-time measurements are not scaled twice.
    /// </summary>
    public static int MeasuredToLogical(int measured)
    {
        return (int)Math.Ceiling(measured * LogicalDpi / SystemDpi);
    }
}

/// <summary>
/// Base for the borderless dialogs: built in logical pixels, scaled to the real window DPI when
/// the handle is created (keeping the position the presenter centred it on) and on DPI changes.
/// </summary>
internal class DpiDialog : Form
{
    private int _layoutDpi = DpiLayout.LogicalDpi;
    private Size _logicalClientSize;

    /// <summary>
    /// Design client size in logical pixels. Kept separately because ClientSize reads smaller
    /// while the handle is being created, so it cannot be the source of the scaled size.
    /// </summary>
    protected Size LogicalClientSize
    {
        get => _logicalClientSize;
        set { _logicalClientSize = value; ClientSize = DpiLayout.Scale(value, _layoutDpi); }
    }

    /// <summary>The DPI the dialog's child bounds are currently expressed in.</summary>
    internal int LayoutDpi => _layoutDpi;

    public DpiDialog()
    {
        AutoScaleMode = AutoScaleMode.None;
    }

    protected int Px(int logical) => DpiLayout.Scale(logical, _layoutDpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RescaleTo(DeviceDpi, resizeWindow: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Windows already applied the suggested (linearly scaled) window rectangle.
        base.OnDpiChanged(e);
        RescaleTo(e.DeviceDpiNew, resizeWindow: false);
    }

    private void RescaleTo(int dpi, bool resizeWindow)
    {
        if (dpi == _layoutDpi || dpi <= 0) return;
        var logical = _logicalClientSize.IsEmpty ? DpiLayout.Scale(ClientSize, DpiLayout.LogicalDpi * DpiLayout.LogicalDpi / _layoutDpi) : _logicalClientSize;
        var center = new Point(Left + DpiLayout.Scale(logical.Width, _layoutDpi) / 2, Top + DpiLayout.Scale(logical.Height, _layoutDpi) / 2);
        SuspendLayout();
        try
        {
            DpiLayout.ScaleChildren(this, _layoutDpi, dpi);
            if (resizeWindow) ClientSize = DpiLayout.Scale(logical, dpi);
            _layoutDpi = dpi;
        }
        finally { ResumeLayout(true); }
        if (resizeWindow && StartPosition == FormStartPosition.Manual)
            Location = new Point(center.X - Width / 2, center.Y - Height / 2);
        OnLayoutDpiChanged();
    }

    /// <summary>Hook for DPI-dependent values that Control.Scale does not cover.</summary>
    protected virtual void OnLayoutDpiChanged()
    {
    }
}

/// <summary>
/// Lets the borderless main window be resized from its edges. The layout panels and title-bar
/// controls cover the whole client area, so WM_NCHITTEST near the window edge went to them and
/// the form's resize hit-test never ran. Controls that touch an edge answer HTTRANSPARENT
/// within the grip distance, which hands the hit-test to the form underneath.
/// </summary>
internal sealed class EdgeHitPassThrough : NativeWindow
{
    private readonly Control _control;
    private readonly Form _form;

    private EdgeHitPassThrough(Control control, Form form)
    {
        _control = control;
        _form = form;
        if (control.IsHandleCreated) AssignHandle(control.Handle);
        control.HandleCreated += (_, _) => AssignHandle(control.Handle);
        control.HandleDestroyed += (_, _) => ReleaseHandle();
    }

    public static void Attach(Form form, params Control[] controls)
    {
        foreach (var control in controls) _ = new EdgeHitPassThrough(control, form);
    }

    protected override void WndProc(ref Message m)
    {
        const int wmNchittest = 0x84, htTransparent = -1;
        if (m.Msg == wmNchittest && _form.WindowState == FormWindowState.Normal)
        {
            var value = (long)m.LParam;
            var point = _form.PointToClient(new Point((short)value, (short)(value >> 16)));
            var grip = DpiLayout.Scale(6, _form.DeviceDpi);
            if (point.X <= grip || point.Y <= grip || point.X >= _form.ClientSize.Width - grip || point.Y >= _form.ClientSize.Height - grip)
            {
                m.Result = (IntPtr)htTransparent;
                return;
            }
        }
        base.WndProc(ref m);
    }
}
