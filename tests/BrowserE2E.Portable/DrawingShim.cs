// The minimal System.Drawing surface that BrowserValidation/BrowserCaptureE2E use (tile
// stitching, PNG save/load and pixel reads), implemented on SkiaSharp so the production E2E
// runs off Windows. Color/Rectangle come from System.Drawing.Primitives, which is portable.
global using System.Drawing;
using SkiaSharp;

namespace System.Drawing.Imaging
{
    public enum PixelFormat { Format24bppRgb }

    public sealed class ImageFormat
    {
        public static readonly ImageFormat Png = new();
    }
}

namespace System.Drawing
{
    public sealed class Bitmap : IDisposable
    {
        internal readonly SKBitmap Native;

        public Bitmap(int width, int height, Imaging.PixelFormat format) =>
            Native = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        public Bitmap(Stream stream) =>
            Native = SKBitmap.Decode(stream) ?? throw new ArgumentException("无法解码图像。");

        public Bitmap(Bitmap source) => Native = source.Native.Copy();

        public Bitmap(string path) =>
            Native = SKBitmap.Decode(path) ?? throw new ArgumentException($"无法解码图像：{path}");

        public int Width => Native.Width;
        public int Height => Native.Height;

        public Color GetPixel(int x, int y)
        {
            var pixel = Native.GetPixel(x, y);
            return Color.FromArgb(pixel.Alpha, pixel.Red, pixel.Green, pixel.Blue);
        }

        public void Save(string path, Imaging.ImageFormat format)
        {
            using var image = SKImage.FromBitmap(Native);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
        }

        public void Dispose() => Native.Dispose();
    }

    public sealed class Graphics : IDisposable
    {
        private readonly SKCanvas _canvas;

        private Graphics(Bitmap target) => _canvas = new SKCanvas(target.Native);

        public static Graphics FromImage(Bitmap target) => new(target);

        public void Clear(Color color) => _canvas.Clear(new SKColor(color.R, color.G, color.B, color.A));

        public void DrawImageUnscaled(Bitmap image, int x, int y) => _canvas.DrawBitmap(image.Native, x, y);

        public void Dispose()
        {
            _canvas.Flush();
            _canvas.Dispose();
        }
    }
}
