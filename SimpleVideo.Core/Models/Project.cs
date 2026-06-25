namespace SimpleVideo.Core.Models;

public class Project
{
    public VideoTrack VideoTrack { get; set; } = new();
    public TextTrack TextTrack { get; set; } = new();
    public AudioTrack AudioTrack { get; set; } = new();

    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;

    public double Fps { get; set; } = 24.0;
}
