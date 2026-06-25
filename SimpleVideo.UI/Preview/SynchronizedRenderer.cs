using System;
using SkiaSharp;
using SimpleVideo.Core.Rendering;

namespace simple_video.Preview;

public sealed class SynchronizedRenderer : IRenderer, IDisposable
{
    private readonly IRenderer _inner;
    private readonly object _syncRoot;

    public SynchronizedRenderer(IRenderer inner, object syncRoot)
    {
        _inner = inner;
        _syncRoot = syncRoot;
    }

    public SKBitmap RenderFrame(TimeSpan time)
    {
        lock (_syncRoot)
        {
            return _inner.RenderFrame(time);
        }
    }

    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
