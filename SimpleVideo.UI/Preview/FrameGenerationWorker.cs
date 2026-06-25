using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SimpleVideo.Core.Rendering;

namespace simple_video.Preview;

public sealed class FrameGenerationWorker : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly FrameBuffer _frameBuffer;
    private readonly Func<double> _getFps;
    private readonly Func<TimeSpan> _getDuration;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _requestLock = new();
    private readonly Task _workerTask;

    private TimeSpan _requestedTime;
    private int _requestVersion;
    private bool _disposed;

    public FrameGenerationWorker(
        IRenderer renderer,
        FrameBuffer frameBuffer,
        Func<double> getFps,
        Func<TimeSpan> getDuration)
    {
        _renderer = renderer;
        _frameBuffer = frameBuffer;
        _getFps = getFps;
        _getDuration = getDuration;
        _workerTask = Task.Run(RunAsync);
    }

    public event EventHandler<FrameReadyEventArgs>? FrameReady;

    public void Request(TimeSpan time)
    {
        if (_disposed) return;

        lock (_requestLock)
        {
            _requestedTime = time;
            _requestVersion++;
        }

        _signal.Release();
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            TimeSpan requestTime;
            int requestVersion;
            lock (_requestLock)
            {
                requestTime = _requestedTime;
                requestVersion = _requestVersion;
            }

            GenerateFrames(requestTime, requestVersion, _cts.Token);
        }
    }

    private void GenerateFrames(TimeSpan requestTime, int requestVersion, CancellationToken token)
    {
        var fps = Math.Max(1.0, _getFps());
        var duration = _getDuration();
        var currentIndex = TimeToFrameIndex(requestTime, fps, duration);

        if (TryPublishCachedFrame(currentIndex, requestVersion))
        {
            return;
        }

        RenderAndPublish(currentIndex, requestVersion, token);

        var forwardFrames = (int)Math.Ceiling(fps * 3.0);
        for (var offset = 1; offset <= forwardFrames; offset++)
        {
            if (ShouldStop(requestVersion, token)) return;

            var frameIndex = currentIndex + offset;
            if (FrameIndexToTime(frameIndex, fps) > duration) break;
            RenderMissingFrame(frameIndex, fps, token);
        }

        var backwardFrames = (int)Math.Ceiling(fps * 1.0);
        for (var offset = 1; offset <= backwardFrames; offset++)
        {
            if (ShouldStop(requestVersion, token)) return;

            var frameIndex = currentIndex - offset;
            if (frameIndex < 0) break;
            RenderMissingFrame(frameIndex, fps, token);
        }
    }

    private bool TryPublishCachedFrame(int frameIndex, int requestVersion)
    {
        if (!_frameBuffer.TryGetFrame(frameIndex, out var bitmap) || bitmap == null)
        {
            return false;
        }

        FrameReady?.Invoke(this, new FrameReadyEventArgs(frameIndex, bitmap, requestVersion));
        return true;
    }

    private void RenderAndPublish(int frameIndex, int requestVersion, CancellationToken token)
    {
        var bitmap = RenderMissingFrame(frameIndex, _getFps(), token);
        if (bitmap != null && !ShouldStop(requestVersion, token))
        {
            FrameReady?.Invoke(this, new FrameReadyEventArgs(frameIndex, bitmap, requestVersion));
        }
    }

    private Bitmap? RenderMissingFrame(int frameIndex, double fps, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return null;
        }

        if (_frameBuffer.TryGetFrame(frameIndex, out var cached) && cached != null)
        {
            return cached;
        }

        using var skBitmap = _renderer.RenderFrame(FrameIndexToTime(frameIndex, Math.Max(1.0, fps)));
        var bitmap = AvaloniaBitmapConverter.FromSkiaBitmap(skBitmap);
        _frameBuffer.StoreFrame(frameIndex, bitmap);
        return bitmap;
    }

    private bool ShouldStop(int requestVersion, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return true;
        }

        lock (_requestLock)
        {
            return requestVersion != _requestVersion;
        }
    }

    private static int TimeToFrameIndex(TimeSpan time, double fps, TimeSpan duration)
    {
        var seconds = Math.Clamp(time.TotalSeconds, 0.0, Math.Max(0.0, duration.TotalSeconds));
        return Math.Max(0, (int)Math.Round(seconds * fps));
    }

    private static TimeSpan FrameIndexToTime(int frameIndex, double fps)
    {
        return TimeSpan.FromSeconds(Math.Max(0, frameIndex) / Math.Max(1.0, fps));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _signal.Release();

        try
        {
            _workerTask.Wait();
        }
        catch
        {
            // The app can continue shutting down if a decoder is already being torn down.
        }

        _cts.Dispose();
        _signal.Dispose();
    }
}

public sealed class FrameReadyEventArgs : EventArgs
{
    public FrameReadyEventArgs(int frameIndex, Bitmap bitmap, int requestVersion)
    {
        FrameIndex = frameIndex;
        Bitmap = bitmap;
        RequestVersion = requestVersion;
    }

    public int FrameIndex { get; }
    public Bitmap Bitmap { get; }
    public int RequestVersion { get; }
}
