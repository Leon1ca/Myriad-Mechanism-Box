using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CustomsClearanceConsole;

internal static class PdfiumNative
{
    private const string LibraryName = "pdfium.dll";
    private static bool _initialized;
    private static int _libraryUsers;
    private static readonly object LibrarySync = new();

    public static void Initialize()
    {
        lock (LibrarySync)
        {
            if (_initialized) return;
            NativeLibrary.SetDllImportResolver(typeof(PdfiumNative).Assembly, Resolve);
            _initialized = true;
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals(LibraryName, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        return NativeLibrary.Load(AppPaths.PdfiumDll);
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_InitLibrary();
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_DestroyLibrary();
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, [MarshalAs(UnmanagedType.LPStr)] string? password);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFPage_GetRotation(IntPtr page);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FPDFText_GetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    internal sealed class PdfDocument : IDisposable
    {
        private readonly byte[] _data;
        private GCHandle _pin;
        private bool _holdsLibrary;
        internal IntPtr Handle { get; private set; }
        public int PageCount => FPDF_GetPageCount(Handle);

        public PdfDocument(string path)
        {
            AppPaths.EnsureRuntime();
            _data = File.ReadAllBytes(path);
            try
            {
                lock (LibrarySync)
                {
                    if (_libraryUsers == 0) FPDF_InitLibrary();
                    _libraryUsers++;
                    _holdsLibrary = true;
                }
                _pin = GCHandle.Alloc(_data, GCHandleType.Pinned);
                Handle = FPDF_LoadMemDocument64(_pin.AddrOfPinnedObject(), (nuint)_data.LongLength, null);
                if (Handle == IntPtr.Zero)
                    throw new InvalidDataException("PDF 无法打开，文件可能损坏或已加密。");
            }
            catch { Dispose(); throw; }
        }

        public IntPtr LoadPage(int index)
        {
            ObjectDisposedException.ThrowIf(Handle == IntPtr.Zero, this);
            var page = FPDF_LoadPage(Handle, index);
            if (page == IntPtr.Zero) throw new InvalidDataException($"PDF 第 {index + 1} 页无法读取。");
            return page;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) FPDF_CloseDocument(Handle);
            Handle = IntPtr.Zero;
            if (_pin.IsAllocated) _pin.Free();
            lock (LibrarySync)
            {
                if (_holdsLibrary && --_libraryUsers == 0) FPDF_DestroyLibrary();
                _holdsLibrary = false;
            }
        }
    }

    public static string RenderPageToPng(IntPtr page, int pageNumber, string folder, int dpi = 300)
    {
        var scale = dpi / 72d;
        var width = Math.Max(1, (int)Math.Ceiling(FPDF_GetPageWidth(page) * scale));
        var height = Math.Max(1, (int)Math.Ceiling(FPDF_GetPageHeight(page) * scale));
        var bitmap = FPDFBitmap_CreateEx(width, height, 4, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero) throw new OutOfMemoryException("无法创建 PDF 页面图像。");
        try
        {
            FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 0x01 | 0x08 | 0x10);
            using var view = new Bitmap(width, height, FPDFBitmap_GetStride(bitmap), PixelFormat.Format32bppArgb, FPDFBitmap_GetBuffer(bitmap));
            using var copy = view.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format24bppRgb);
            Directory.CreateDirectory(folder);
            var output = Path.Combine(folder, $"page-{pageNumber + 1:D3}.png");
            copy.Save(output, ImageFormat.Png);
            return output;
        }
        finally
        {
            FPDFBitmap_Destroy(bitmap);
        }
    }
}
