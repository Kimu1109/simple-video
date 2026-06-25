namespace SimpleVideo.Core.Media;

public class ExportOptions
{
    public string OutputPath { get; set; } = string.Empty;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 24.0;
    public int VideoBitrate { get; set; } = 5000000; // 5 Mbps
    public int AudioBitrate { get; set; } = 192000;  // 192 kbps
}
