namespace SimpleVideo.Core.Media;

public interface IVideoDecoderFactory
{
    IVideoDecoder CreateDecoder(string videoPath);
}
