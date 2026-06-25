using System;
using System.IO;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using SkiaSharp;
using SimpleVideo.Core.Media;

namespace SimpleVideo.Media;

public class FFMediaToolkitVideoDecoder : IVideoDecoder
{
    private readonly MediaFile _mediaFile;
    private static bool _ffmpegInitialized;
    private readonly object _lock = new();

    public FFMediaToolkitVideoDecoder(string videoPath)
    {
        InitializeFFmpeg();

        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("Video file not found.", videoPath);
        }

        var options = new MediaOptions
        {
            VideoPixelFormat = ImagePixelFormat.Bgra32
        };

        _mediaFile = MediaFile.Open(videoPath, options);
    }

    private static void InitializeFFmpeg()
    {
        if (_ffmpegInitialized) return;

        lock (typeof(FFMediaToolkitVideoDecoder))
        {
            if (_ffmpegInitialized) return;

            // Linux systems common FFmpeg library paths
            string[] searchPaths = {
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib",
                "/usr/local/lib"
            };

            foreach (var path in searchPaths)
            {
                if (Directory.Exists(path))
                {
                    try
                    {
                        FFmpegLoader.FFmpegPath = path;
                        _ffmpegInitialized = true;
                        break;
                    }
                    catch
                    {
                        // Try next path if registration fails
                    }
                }
            }

            if (!_ffmpegInitialized)
            {
                try
                {
                    FFmpegLoader.FFmpegPath = "/usr/lib/x86_64-linux-gnu";
                    _ffmpegInitialized = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to initialize FFmpeg binaries for FFMediaToolkit.", ex);
                }
            }
        }
    }

    public VideoFrame GetFrame(TimeSpan time)
    {
        if (!_mediaFile.HasVideo)
        {
            throw new InvalidOperationException("No video stream found in the media file.");
        }

        lock (_lock)
        {
            var duration = _mediaFile.Video.Info.Duration;
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            if (time > duration) time = duration;

            var frame = _mediaFile.Video.GetFrame(time);
            
            var width = _mediaFile.Video.Info.FrameSize.Width;
            var height = _mediaFile.Video.Info.FrameSize.Height;

            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            
            // Copy pixels to SKBitmap via Span safely without unsafe block
            frame.Data.CopyTo(bitmap.GetPixelSpan());

            // Use the requested time as the presentation time
            return new VideoFrame(bitmap, time);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _mediaFile?.Dispose();
        }
    }
}
