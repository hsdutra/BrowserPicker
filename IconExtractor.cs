using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace BrowserPicker;

internal static class IconExtractor
{
    internal static async Task<BitmapImage?> ExtractIconAsync(string executablePath)
    {
        if (!File.Exists(executablePath))
            return null;

        try
        {
            // ExtractAssociatedIcon usa COM/Shell — deve correr na STA (UI thread)
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
                return null;

            using var bitmap  = icon.ToBitmap();
            using var resized = new Bitmap(bitmap, new System.Drawing.Size(36, 36));

            using var ms           = new InMemoryRandomAccessStream();
            using var dotNetStream = ms.AsStream();
            resized.Save(dotNetStream, ImageFormat.Png);
            dotNetStream.Flush();
            ms.Seek(0);

            var bitmapImage = new BitmapImage();
            await bitmapImage.SetSourceAsync(ms);
            return bitmapImage;
        }
        catch
        {
            return CreateFallbackIcon();
        }
    }

    private static BitmapImage? CreateFallbackIcon()
    {
        try
        {
            // 1×1 transparent PNG as neutral placeholder
            var bitmapImage = new BitmapImage();
            bitmapImage.UriSource = new Uri("ms-appx:///Assets/browser_fallback.png");
            return bitmapImage;
        }
        catch
        {
            return null;
        }
    }
}
