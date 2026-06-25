using System.Collections.Generic;

namespace SimpleVideo.Core.Models;

public class VideoTrack
{
    public List<IVideoTrackClip> Clips { get; set; } = new();
}
