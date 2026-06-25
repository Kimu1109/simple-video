using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace simple_video.Preview;

public sealed class FrameBuffer : IDisposable
{
    private readonly Dictionary<int, Bitmap> _frames = new();
    private readonly Queue<int> _order = new();
    private readonly object _lock = new();
    private readonly int _maxFrames;

    public FrameBuffer(int maxFrames = 240)
    {
        _maxFrames = Math.Max(1, maxFrames);
    }

    public bool TryGetFrame(int frameIndex, out Bitmap? bitmap)
    {
        lock (_lock)
        {
            return _frames.TryGetValue(frameIndex, out bitmap);
        }
    }

    public void StoreFrame(int frameIndex, Bitmap bitmap)
    {
        lock (_lock)
        {
            if (_frames.Remove(frameIndex, out var oldBitmap))
            {
                oldBitmap.Dispose();
            }

            _frames[frameIndex] = bitmap;
            _order.Enqueue(frameIndex);

            while (_frames.Count > _maxFrames && _order.TryDequeue(out var oldIndex))
            {
                if (_frames.Remove(oldIndex, out var evicted))
                {
                    evicted.Dispose();
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            foreach (var bitmap in _frames.Values)
            {
                bitmap.Dispose();
            }

            _frames.Clear();
            _order.Clear();
        }
    }

    public void Dispose()
    {
        Clear();
    }
}
