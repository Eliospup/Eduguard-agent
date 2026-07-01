using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace EduGuardAgent.Views;

/// <summary>
/// Swaps a mascot's vector drawing for a real PNG illustration when one is present
/// in <c>Assets/Mascots/</c>. Lets us drop in finished artwork (or iterate on it)
/// without touching the vector fallback code. If no PNG exists, the vector shows.
/// </summary>
internal static class MascotImageLoader
{
    private static readonly string MascotDir =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Mascots");

    public static void Apply(Image image, UIElement vectorFallback, string fileName)
    {
        try
        {
            var path = Path.Combine(MascotDir, fileName);
            if (!File.Exists(path))
                return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;               // read fully so the file isn't locked
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // pick up a replaced file on restart
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            image.Source = bmp;
            image.Visibility = Visibility.Visible;
            vectorFallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Missing/corrupt/locked file — keep the vector fallback visible.
        }
    }
}
