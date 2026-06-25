using System.Collections.Generic;

namespace SimpleVideo.Core.Models;

public class AudioTrack
{
    public List<AudioClip> Clips { get; set; } = new();
}
