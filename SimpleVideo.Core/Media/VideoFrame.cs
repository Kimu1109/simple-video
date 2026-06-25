using System;
using SkiaSharp;

namespace SimpleVideo.Core.Media;

public class VideoFrame : IDisposable
{
    public SKBitmap Bitmap { get; }
    public TimeSpan PresentationTime { get; }

    public VideoFrame(SKBitmap bitmap, TimeSpan presentationTime)
    {
        Bitmap = bitmap;
        PresentationTime = presentationTime;
    }

    public void Dispose()
    {
        Bitmap?.Dispose();
    }
}
