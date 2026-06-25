using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using SimpleVideo.Core.Models;
using SimpleVideo.Core.Rendering;
using SimpleVideo.Core.Media;

namespace SimpleVideo.Rendering;

public class SkiaRenderer : IRenderer, IDisposable
{
    private readonly Project _project;
    private readonly IVideoDecoderFactory _decoderFactory;
    
    // Cache for decoders and loaded images to prevent high IO overhead during seeking/playback
    private readonly Dictionary<string, IVideoDecoder> _decoderCache = new();
    private readonly Dictionary<string, SKBitmap> _imageCache = new();
    private readonly object _lock = new();

    public SkiaRenderer(Project project, IVideoDecoderFactory decoderFactory)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
    }

    public SKBitmap RenderFrame(TimeSpan time)
    {
        lock (_lock)
        {
            var bitmap = new SKBitmap(_project.Width, _project.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);

            // 1. Draw Background (ColorClip or default Black)
            DrawBackground(canvas, time);

            // 2. Draw Video Clips (VideoClip)
            DrawVideo(canvas, time);

            // 3. Draw Images (ImageClip)
            DrawImages(canvas, time);

            // 4. Draw Subtitles/Texts (TextClip)
            DrawText(canvas, time);

            return bitmap;
        }
    }

    private void DrawBackground(SKCanvas canvas, TimeSpan time)
    {
        SKColor backgroundColor = SKColors.Black;

        foreach (var clip in _project.VideoTrack.Clips)
        {
            if (clip is ColorClip colorClip && time >= colorClip.StartTime && time < colorClip.EndTime)
            {
                backgroundColor = colorClip.BackgroundColor;
                break;
            }
        }

        canvas.Clear(backgroundColor);
    }

    private void DrawVideo(SKCanvas canvas, TimeSpan time)
    {
        foreach (var clip in _project.VideoTrack.Clips)
        {
            if (clip is VideoClip videoClip && time >= videoClip.StartTime && time < videoClip.EndTime)
            {
                try
                {
                    var decoder = GetOrCreateDecoder(videoClip.SourceFile);
                    
                    // Calculate media time mapping trim and playback rate
                    var elapsed = time - videoClip.StartTime;
                    var elapsedMediaTicks = (long)(elapsed.Ticks * videoClip.PlaybackRate);
                    var decodeTime = videoClip.TrimStart + TimeSpan.FromTicks(elapsedMediaTicks);

                    using var frame = decoder.GetFrame(decodeTime);
                    if (frame?.Bitmap != null)
                    {
                        // Draw video frame scaling to fit project size (or letterbox, default stretch/fit)
                        var destRect = new SKRect(0, 0, _project.Width, _project.Height);
                        canvas.DrawBitmap(frame.Bitmap, destRect);
                    }
                }
                catch
                {
                    // Fail-silent or draw placeholder for corrupted video decoder
                    using var font = new SKFont(SKTypeface.Default, 40f);
                    using var paint = new SKPaint { Color = SKColors.Red };
                    canvas.DrawText("Video Error", 20, 60, SKTextAlign.Left, font, paint);
                }
                break; // Only draw one video clip at a time (video track is single layer)
            }
        }
    }

    private void DrawImages(SKCanvas canvas, TimeSpan time)
    {
        foreach (var clip in _project.VideoTrack.Clips)
        {
            if (clip is ImageClip imageClip && time >= imageClip.StartTime && time < imageClip.EndTime)
            {
                var bitmap = GetOrCreateImage(imageClip.SourceFile);
                if (bitmap == null) continue;

                canvas.Save();

                // Compute alignment reference coordinates
                double refX = _project.Width / 2.0;
                double refY = _project.Height / 2.0;

                // Scaling context
                float displayWidth = (float)(bitmap.Width * imageClip.Scale);
                float displayHeight = (float)(bitmap.Height * imageClip.Scale);

                switch (imageClip.PositionMode)
                {
                    case PositionMode.Top:
                        refX = _project.Width / 2.0;
                        refY = 15.0 + displayHeight / 2.0;
                        break;
                    case PositionMode.Center:
                        refX = _project.Width / 2.0;
                        refY = _project.Height / 2.0;
                        break;
                    case PositionMode.Bottom:
                        refX = _project.Width / 2.0;
                        refY = _project.Height - 15.0 - displayHeight / 2.0;
                        break;
                    case PositionMode.Custom:
                        refX = imageClip.X;
                        refY = imageClip.Y;
                        break;
                }

                // Apply translations for rotation and scale centered at destination
                canvas.Translate((float)refX, (float)refY);
                canvas.RotateDegrees((float)imageClip.Rotation);
                canvas.Scale((float)imageClip.Scale);

                // Draw centered bitmap
                canvas.DrawBitmap(bitmap, -bitmap.Width / 2f, -bitmap.Height / 2f);

                canvas.Restore();
            }
        }
    }

    private void DrawText(SKCanvas canvas, TimeSpan time)
    {
        foreach (var clip in _project.TextTrack.Clips)
        {
            if (time >= clip.StartTime && time < clip.EndTime)
            {
                using var typeface = SKTypeface.FromFamilyName(clip.FontFamily, SKFontStyle.Normal);
                using var font = new SKFont(typeface, (float)clip.FontSize);
                using var paint = new SKPaint { Color = clip.Color, IsAntialias = true };

                float drawX = _project.Width / 2f;
                float drawY = _project.Height / 2f;

                // Retrieve font metrics for baseline calculation
                font.GetFontMetrics(out var metrics);
                float fontHeight = metrics.Descent - metrics.Ascent;

                SKTextAlign textAlign = SKTextAlign.Center;

                switch (clip.PositionMode)
                {
                    case TextPositionMode.Top:
                        textAlign = SKTextAlign.Center;
                        drawX = _project.Width / 2f;
                        drawY = 50.0f - metrics.Ascent; // padding from top
                        break;
                    case TextPositionMode.Center:
                        textAlign = SKTextAlign.Center;
                        drawX = _project.Width / 2f;
                        drawY = (_project.Height / 2f) - (fontHeight / 2f) - metrics.Ascent;
                        break;
                    case TextPositionMode.Bottom:
                        textAlign = SKTextAlign.Center;
                        drawX = _project.Width / 2f;
                        drawY = _project.Height - 50.0f - metrics.Descent; // padding from bottom
                        break;
                    case TextPositionMode.Custom:
                        textAlign = SKTextAlign.Left; // custom alignment left-aligned
                        drawX = (float)clip.X;
                        drawY = (float)clip.Y;
                        break;
                }

                canvas.DrawText(clip.Text, drawX, drawY, textAlign, font, paint);
                break; // Text track allows only 1 overlapping clip by design
            }
        }
    }

    private IVideoDecoder GetOrCreateDecoder(string sourceFile)
    {
        if (!_decoderCache.TryGetValue(sourceFile, out var decoder))
        {
            decoder = _decoderFactory.CreateDecoder(sourceFile);
            _decoderCache[sourceFile] = decoder;
        }
        return decoder;
    }

    private SKBitmap? GetOrCreateImage(string sourceFile)
    {
        if (string.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
        {
            return null;
        }

        if (!_imageCache.TryGetValue(sourceFile, out var bitmap))
        {
            try
            {
                bitmap = SKBitmap.Decode(sourceFile);
                if (bitmap != null)
                {
                    _imageCache[sourceFile] = bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
        return bitmap;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var decoder in _decoderCache.Values)
            {
                decoder.Dispose();
            }
            _decoderCache.Clear();

            foreach (var bitmap in _imageCache.Values)
            {
                bitmap.Dispose();
            }
            _imageCache.Clear();
        }
    }
}
