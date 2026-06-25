using System;
using SkiaSharp;

namespace SimpleVideo.Core.Models;

public class TextClip : IClip
{
    public string Text { get; set; } = string.Empty;

    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public string FontFamily { get; set; } = "Arial";

    public double FontSize { get; set; } = 24.0;

    public SKColor Color { get; set; } = SKColors.White;

    public TextPositionMode PositionMode { get; set; } = TextPositionMode.Bottom;

    public double X { get; set; }
    public double Y { get; set; }
}
