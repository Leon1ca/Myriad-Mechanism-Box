using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace CustomsClearanceConsole;

/// <summary>Image decoding and preparation for OCR.</summary>
internal static class OcrImages
{
    private const int TargetLongEdge = 2800;

    /// <summary>
    /// Every page of an image file. GDI+ reads PNG/JPEG/BMP/GIF/TIFF (all TIFF pages); formats it
    /// cannot decode, such as WebP, are decoded by OpenCV. Files are read into memory first, so
    /// neither decoder keeps the file locked or trips over a non-ASCII path.
    /// </summary>
    public static List<Bitmap> LoadPages(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            var pages = new List<Bitmap>();
            var count = image.FrameDimensionsList.Contains(FrameDimension.Page.Guid) ? image.GetFrameCount(FrameDimension.Page) : 1;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    if (count > 1) image.SelectActiveFrame(FrameDimension.Page, i);
                    pages.Add(new Bitmap(image));
                }
                return pages;
            }
            catch
            {
                foreach (var page in pages) page.Dispose();
                throw;
            }
        }
        catch (ArgumentException)
        {
            using var mat = new Mat();
            CvInvoke.Imdecode(bytes, ImreadModes.ColorBgr, mat);
            if (mat.IsEmpty) throw new InvalidDataException("无法解码图片，文件可能已损坏或格式不受支持。");
            using var png = new VectorOfByte();
            CvInvoke.Imencode(".png", mat, png);
            using var stream = new MemoryStream(png.ToArray());
            using var image = Image.FromStream(stream);
            return [new Bitmap(image)];
        }
    }

    /// <summary>Rotates clockwise by <paramref name="rotation"/> degrees and scales to the OCR working size on white.</summary>
    public static Bitmap Prepare(Bitmap source, int rotation)
    {
        using var rotated = new Bitmap(source);
        rotated.RotateFlip(rotation switch
        {
            90 => RotateFlipType.Rotate90FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        });
        var longEdge = Math.Max(rotated.Width, rotated.Height);
        var factor = longEdge < 2400 || longEdge > 3000 ? Math.Clamp(TargetLongEdge / (double)longEdge, .25, 6d) : 1d;
        var bitmap = new Bitmap(Math.Max(1, (int)(rotated.Width * factor)), Math.Max(1, (int)(rotated.Height * factor)), PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(rotated, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        return bitmap;
    }

    public static byte[] ToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
