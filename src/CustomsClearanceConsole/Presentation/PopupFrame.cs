using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

/// <summary>
/// Rounded outline for borderless dialogs, menus and drop-down lists. The window region and the
/// border come from the same GDI rounded-rectangle region, and the border is drawn along that
/// region with FrameRgn. The border is therefore exactly the outer ring of the window, equally
/// thick on every side and in the corners. An anti-aliased border path drawn inside a separately
/// rasterised region lost pixels unevenly along the clipped edges. The windows also no longer use
/// the CS_DROPSHADOW class style, whose offset shadow made the right and bottom edges look heavy.
/// </summary>
internal static class PopupFrame
{
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
    [DllImport("gdi32.dll")] private static extern bool FrameRgn(IntPtr hdc, IntPtr region, IntPtr brush, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    /// <summary>Border thickness in device pixels: 1 at 100%, 2 at 150% (like the buttons).</summary>
    public static int BorderWidth(int dpi) => Math.Max(1, DpiLayout.Scale(1, dpi));

    private static IntPtr CreateRegion(Size size, int logicalRadius, int dpi)
    {
        var diameter = DpiLayout.Scale(logicalRadius, dpi) * 2;
        // CreateRoundRectRgn excludes the right and bottom edge, hence the +1.
        return CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, diameter, diameter);
    }

    public static void ApplyRegion(Control control, int logicalRadius, int dpi)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        var handle = CreateRegion(control.Size, logicalRadius, dpi);
        try
        {
            var old = control.Region;
            control.Region = Region.FromHrgn(handle);
            old?.Dispose();
        }
        finally { DeleteObject(handle); }
    }

    public static void PaintBorder(Graphics graphics, Size size, int logicalRadius, int dpi, Color color)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        var region = CreateRegion(size, logicalRadius, dpi);
        var brush = CreateSolidBrush(ColorTranslator.ToWin32(color));
        var hdc = graphics.GetHdc();
        try
        {
            var width = BorderWidth(dpi);
            FrameRgn(hdc, region, brush, width, width);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
            DeleteObject(brush);
            DeleteObject(region);
        }
    }
}
