using System;
using SkiaSharp;

namespace SimpleVideo.Core.Models;

public class ColorClip : IVideoTrackClip
{
    public SKColor BackgroundColor { get; set; } = SKColors.Black;

    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}
