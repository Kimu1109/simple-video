using System;

namespace SimpleVideo.Core.Media;

public class AudioBuffer
{
    public short[] Samples { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    public AudioBuffer(short[] samples, int sampleRate = 48000, int channels = 2)
    {
        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }
}
