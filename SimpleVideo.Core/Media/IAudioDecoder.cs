using System;

namespace SimpleVideo.Core.Media;

public interface IAudioDecoder : IDisposable
{
    AudioBuffer GetAudio(TimeSpan start, TimeSpan duration);
}
