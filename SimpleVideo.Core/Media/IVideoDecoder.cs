using System;

namespace SimpleVideo.Core.Media;

public interface IVideoDecoder : IDisposable
{
    VideoFrame GetFrame(TimeSpan time);
}
