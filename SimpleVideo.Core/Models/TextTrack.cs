using System.Collections.Generic;

namespace SimpleVideo.Core.Models;

public class TextTrack
{
    public List<TextClip> Clips { get; set; } = new();
}
