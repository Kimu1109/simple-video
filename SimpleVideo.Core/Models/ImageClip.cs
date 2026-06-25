using System;

namespace SimpleVideo.Core.Models;

public class ImageClip : IVideoTrackClip
{
    public string SourceFile { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public double Scale { get; set; } = 1.0;
    public double Rotation { get; set; } = 0.0;

    public PositionMode PositionMode { get; set; } = PositionMode.Center;

    public double X { get; set; }
    public double Y { get; set; }
}
