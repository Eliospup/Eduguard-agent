using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal static class ExecutableIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Cache.GetOrAdd(fullPath, ExtractCore);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? ExtractCore(string fullPath)
    {
        using var icon = Icon.ExtractAssociatedIcon(fullPath);
        if (icon is null)
            return null;

        using var bitmap = icon.ToBitmap();
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);
    }
}
