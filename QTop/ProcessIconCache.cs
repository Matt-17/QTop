using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace QTop;

public static class ProcessIconCache
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? GetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        if (Cache.TryGetValue(executablePath, out BitmapSource? cached))
            return cached;

        BitmapSource? icon = File.Exists(executablePath) ? ExtractIcon(executablePath) : null;
        Cache[executablePath] = icon;
        return icon;
    }

    public static void Clear() => Cache.Clear();

    private static BitmapSource? ExtractIcon(string path)
    {
        try
        {
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
