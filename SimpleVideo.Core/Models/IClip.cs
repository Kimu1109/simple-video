using System;
using System.Text.Json.Serialization;

namespace SimpleVideo.Core.Models;

public interface IClip
{
    TimeSpan StartTime { get; set; }
    TimeSpan EndTime { get; set; }
}

[JsonDerivedType(typeof(VideoClip), "video")]
[JsonDerivedType(typeof(ImageClip), "image")]
[JsonDerivedType(typeof(ColorClip), "color")]
public interface IVideoTrackClip : IClip { }
