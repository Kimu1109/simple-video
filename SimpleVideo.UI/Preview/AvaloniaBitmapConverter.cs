using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace simple_video.Preview;

public static class AvaloniaBitmapConverter
{
    public static WriteableBitmap FromSkiaBitmap(SKBitmap source)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(source.Width, source.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var locked = bitmap.Lock();
        var pixels = source.GetPixelSpan().ToArray();
        Marshal.Copy(pixels, 0, locked.Address, pixels.Length);

        return bitmap;
    }
}
