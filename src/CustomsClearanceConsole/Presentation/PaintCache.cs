using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

/// <summary>
/// Shared GDI resources for paint code. Grid cells and owner-drawn panels repaint many
/// times per second while scrolling, hovering or receiving scan results; creating a font,
/// pen, brush or icon copy for every cell made each repaint allocate (and finalise) dozens
/// of GDI objects. Everything returned here is owned by the cache: never dispose it.
/// Paint code runs on the UI thread only, so no locking is needed beyond the icon cache.
/// </summary>
internal static class PaintCache
{
    private static readonly Dictionary<(float Px, UiWeight Weight), Font> UiFonts = [];
    private static readonly Dictionary<(float Px, bool Medium), Font> MonoFonts = [];
    private static readonly Dictionary<int, SolidBrush> Brushes = [];
    private static readonly Dictionary<int, Pen> Pens = [];
    private static readonly Dictionary<(string Stem, int Size, int Dpi), Bitmap?> Icons = [];

    public static Font Ui(float px, UiWeight weight = UiWeight.Regular)
    {
        if (!UiFonts.TryGetValue((px, weight), out var font))
            UiFonts[(px, weight)] = font = AppFonts.Ui(px, weight);
        return font;
    }

    public static Font Mono(float px, bool medium = false)
    {
        if (!MonoFonts.TryGetValue((px, medium), out var font))
            MonoFonts[(px, medium)] = font = AppFonts.Mono(px, medium);
        return font;
    }

    public static SolidBrush Brush(Color color)
    {
        var key = color.ToArgb();
        if (!Brushes.TryGetValue(key, out var brush)) Brushes[key] = brush = new SolidBrush(color);
        return brush;
    }

    /// <summary>A 1px solid pen. Pens with a width or dash style are created by the caller.</summary>
    public static Pen Pen(Color color)
    {
        var key = color.ToArgb();
        if (!Pens.TryGetValue(key, out var pen)) Pens[key] = pen = new Pen(color);
        return pen;
    }

    private static readonly Dictionary<(Font Font, int Dpi), float> HalfInk = [];

    /// <summary>
    /// Vertical offset that puts <paramref name="font"/>'s baseline on <paramref name="reference"/>'s
    /// baseline when both are drawn vertically centred in the same rectangle. JetBrains Mono has a
    /// much smaller ascent/descent box than Noto Sans SC, so centred numbers sat 1–2px higher
    /// than the Chinese text and currency codes next to them.
    /// </summary>
    public static int BaselineShift(Font font, Font reference, int dpi) =>
        (int)Math.Round(Half(reference, dpi) - Half(font, dpi), MidpointRounding.AwayFromZero);

    /// <summary>Distance from the centre of the font's cell box to its baseline, in device pixels.</summary>
    private static float Half(Font font, int dpi)
    {
        if (HalfInk.TryGetValue((font, dpi), out var half)) return half;
        var family = font.FontFamily;
        var em = Math.Max(1, family.GetEmHeight(font.Style));
        var ascent = family.GetCellAscent(font.Style);
        var descent = family.GetCellDescent(font.Style);
        half = (ascent - descent) / (float)em * font.SizeInPoints * dpi / 72F / 2F;
        return HalfInk[(font, dpi)] = half;
    }

    /// <summary>Cached icon for the DPI; unlike <see cref="UiV2Icons.Load(string, int, float)"/> it must not be disposed.</summary>
    public static Bitmap? Icon(string stem, int logicalSize, int dpi)
    {
        lock (Icons)
        {
            var key = (stem, logicalSize, dpi);
            if (!Icons.TryGetValue(key, out var icon)) Icons[key] = icon = UiV2Icons.Load(stem, logicalSize, dpi);
            return icon;
        }
    }
}

/// <summary>
/// Batches visual updates of a control tree into a single repaint. WM_SETREDRAW stops the
/// window and its children from painting while rows, panels and row heights change; the
/// matching resume invalidates the whole tree once, so double-buffered children present
/// their final state instead of every intermediate one (the visible "flash" when the list
/// was cleared and refilled row by row).
/// </summary>
internal static class Redraw
{
    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001, RdwErase = 0x0004, RdwAllChildren = 0x0080, RdwFrame = 0x0400;
    private static readonly Dictionary<IntPtr, int> Depth = [];

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);

    public static Scope Suspend(Control control)
    {
        if (control.IsDisposed || !control.IsHandleCreated || !control.Visible) return default;
        var handle = control.Handle;
        var depth = Depth.GetValueOrDefault(handle);
        Depth[handle] = depth + 1;
        if (depth == 0) SendMessage(handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        return new Scope(control, handle);
    }

    internal readonly struct Scope(Control? control, IntPtr handle) : IDisposable
    {
        public void Dispose()
        {
            if (control is null || handle == IntPtr.Zero) return;
            var depth = Depth.GetValueOrDefault(handle) - 1;
            if (depth > 0) { Depth[handle] = depth; return; }
            Depth.Remove(handle);
            if (control.IsDisposed || !control.IsHandleCreated) return;
            SendMessage(handle, WmSetRedraw, (IntPtr)1, IntPtr.Zero);
            RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwFrame);
        }
    }
}
