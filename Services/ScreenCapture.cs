using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal static class ScreenCapture
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public static byte[] CaptureDesktopJpeg()
    {
        var x = GetSystemMetrics(SmXVirtualScreen);
        var y = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Could not read virtual screen dimensions.");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using var scaled = ScaleDown(bitmap, Config.ScreenshotMaxWidthPixels);
        return EncodeJpeg(scaled, Config.ScreenshotJpegQuality);
    }

    private static Bitmap ScaleDown(Bitmap source, int maxWidth)
    {
        if (source.Width <= maxWidth)
            return (Bitmap)source.Clone();

        var scale = maxWidth / (double)source.Width;
        var targetWidth = maxWidth;
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

        var scaled = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(scaled);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
        return scaled;
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, int quality)
    {
        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        using var stream = new MemoryStream();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 1, 100));
        bitmap.Save(stream, encoder, encoderParams);
        return stream.ToArray();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
