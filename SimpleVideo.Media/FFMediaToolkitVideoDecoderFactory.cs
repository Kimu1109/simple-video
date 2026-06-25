using SimpleVideo.Core.Media;

namespace SimpleVideo.Media;

public class FFMediaToolkitVideoDecoderFactory : IVideoDecoderFactory
{
    public IVideoDecoder CreateDecoder(string videoPath)
    {
        return new FFMediaToolkitVideoDecoder(videoPath);
    }
}
