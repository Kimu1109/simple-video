using System;

namespace SimpleVideo.Core.Models;

public class AudioClip : IClip
{
    public string SourceFile { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public TimeSpan TrimStart { get; set; }
    public TimeSpan TrimEnd { get; set; }

    public double PlaybackRate { get; set; } = 1.0;
}
