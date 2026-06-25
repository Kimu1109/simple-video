using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using SoundTouch;
using SimpleVideo.Media;
using SimpleVideo.Core.Media;
using SimpleVideo.Core.Rendering;
using SimpleVideo.Core.Models;

namespace SimpleVideo.Encoding;

public class MediaEncoderService : IVideoExporter
{
    private readonly Project _project;
    private readonly IRenderer _renderer;
    
    public MediaEncoderService(Project project, IRenderer renderer)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>
    /// Export the timeline project to a final mp4 video.
    /// </summary>
    public async Task ExportAsync(ExportOptions options)
    {
        string tempVideoPath = Path.Combine(Path.GetDirectoryName(options.OutputPath) ?? ".", $"temp_video_{Guid.NewGuid()}.mp4");
        string tempAudioPath = Path.Combine(Path.GetDirectoryName(options.OutputPath) ?? ".", $"temp_audio_{Guid.NewGuid()}.wav");

        try
        {
            // 1. Export temporary video track
            await ExportTemporaryVideoAsync(tempVideoPath, options.Width, options.Height, options.Fps);

            // 2. Export temporary audio track
            await ExportTemporaryAudioAsync(tempAudioPath);

            // 3. Combine tracks applying hardware acceleration fallbacks
            await MergeVideoAndAudioAsync(tempVideoPath, tempAudioPath, options.OutputPath, options.VideoBitrate, options.AudioBitrate);
        }
        finally
        {
            // Clean up temporary files
            if (File.Exists(tempVideoPath)) File.Delete(tempVideoPath);
            if (File.Exists(tempAudioPath)) File.Delete(tempAudioPath);
        }
    }

    private async Task ExportTemporaryVideoAsync(string outputPath, int width, int height, double fps)
    {
        // Setup ffmpeg process to receive raw RGBA frames from stdout via stdin
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fps} -i - -an -c:v libx264 -crf 18 -pix_fmt yuv420p \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var stdin = process.StandardInput.BaseStream;

        double totalSeconds = CalculateProjectDuration();
        int totalFrames = (int)Math.Max(1, Math.Ceiling(totalSeconds * fps));
        double secondsPerFrame = 1.0 / fps;

        for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
        {
            TimeSpan time = TimeSpan.FromSeconds(frameIdx * secondsPerFrame);
            using var bitmap = _renderer.RenderFrame(time);
            
            // Resize if renderer size doesn't match export size
            SKBitmap frameBitmap = bitmap;
            bool isResized = false;
            if (bitmap.Width != width || bitmap.Height != height)
            {
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var resized = new SKBitmap(info);
                bitmap.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear));
                frameBitmap = resized;
                isResized = true;
            }

            // Write raw bytes directly to ffmpeg pipe
            var pixels = frameBitmap.GetPixelSpan();
            await stdin.WriteAsync(pixels.ToArray(), 0, pixels.Length);
            await stdin.FlushAsync();

            if (isResized)
            {
                frameBitmap.Dispose();
            }
        }

        stdin.Close();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFmpeg temporary video export failed: {error}");
        }
    }

    private async Task ExportTemporaryAudioAsync(string outputPath)
    {
        double durationSeconds = CalculateProjectDuration();
        long totalSamples = (long)(durationSeconds * 48000.0) * 2; // Stereo 48kHz

        // Mix down audio in chunks to avoid high memory allocation
        const int chunkFrameCount = 4096;
        int chunkSampleCount = chunkFrameCount * 2;
        TimeSpan chunkDuration = TimeSpan.FromSeconds((double)chunkFrameCount / 48000.0);

        // We temporarily instantiate WavAudioDecoders to generate the mix.
        // Similar to AudioEngine, but offline.
        var bgmDecoders = new List<(WavAudioDecoder Decoder, AudioClip Clip)>();
        var videoDecoders = new List<(WavAudioDecoder Decoder, VideoClip Clip)>();

        // Cache Directory assumed based on project
        string cacheAudioDir = Path.Combine(Path.GetTempPath(), "SimpleVideoCache", "audio");
        // Fallback or override logic for locating caches should be synced with app settings.
        // For portability, we can look up caches from the local workspace folder.
        string workspaceCacheDir = Path.Combine(Environment.CurrentDirectory, "ProjectCache");
        if (Directory.Exists(workspaceCacheDir))
        {
            cacheAudioDir = Path.Combine(workspaceCacheDir, "audio");
        }

        try
        {
            foreach (var clip in _project.AudioTrack.Clips)
            {
                string wavPath = GetCachedWavPath(cacheAudioDir, clip.SourceFile);
                if (File.Exists(wavPath))
                {
                    bgmDecoders.Add((new WavAudioDecoder(wavPath), clip));
                }
            }

            foreach (var clip in _project.VideoTrack.Clips)
            {
                if (clip is VideoClip videoClip)
                {
                    string wavPath = GetCachedWavPath(cacheAudioDir, videoClip.SourceFile);
                    if (File.Exists(wavPath))
                    {
                        videoDecoders.Add((new WavAudioDecoder(wavPath), videoClip));
                    }
                }
            }

            // Write output WAV file using BinaryWriter
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fileStream);

            // Write standard WAV header (will update sizes at the end)
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0); // placeholder for chunk size
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // subchunk1 size
            writer.Write((short)1); // AudioFormat: 1 = PCM
            writer.Write((short)2); // Channels: 2
            writer.Write(48000); // SampleRate
            writer.Write(48000 * 4); // ByteRate (SampleRate * Channels * BitsPerSample/8)
            writer.Write((short)4); // BlockAlign (Channels * BitsPerSample/8)
            writer.Write((short)16); // BitsPerSample
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(0); // placeholder for data chunk size

            long totalDataBytes = 0;
            TimeSpan time = TimeSpan.Zero;

            // Setup SoundTouch processors for Offline Audio Stretch
            var bgmSoundTouches = new List<SoundTouchProcessor>();
            foreach (var item in bgmDecoders)
            {
                bgmSoundTouches.Add(new SoundTouchProcessor { SampleRate = 48000, Channels = 2, Tempo = (float)item.Clip.PlaybackRate });
            }

            var videoSoundTouches = new List<SoundTouchProcessor>();
            foreach (var item in videoDecoders)
            {
                videoSoundTouches.Add(new SoundTouchProcessor { SampleRate = 48000, Channels = 2, Tempo = (float)item.Clip.PlaybackRate });
            }

            short[] mixBufferBgm = new short[chunkSampleCount];
            short[] mixBufferVideo = new short[chunkSampleCount];
            byte[] outBytes = new byte[chunkSampleCount * 2];

            while (time.TotalSeconds < durationSeconds)
            {
                Array.Clear(mixBufferBgm, 0, mixBufferBgm.Length);
                Array.Clear(mixBufferVideo, 0, mixBufferVideo.Length);

                // Offline Mix BGM Track
                for (int i = 0; i < bgmDecoders.Count; i++)
                {
                    var item = bgmDecoders[i];
                    var st = bgmSoundTouches[i];
                    MixClipAudio(mixBufferBgm, item.Decoder, item.Clip, st, time, chunkDuration);
                }

                // Offline Mix Video Track
                for (int i = 0; i < videoDecoders.Count; i++)
                {
                    var item = videoDecoders[i];
                    var st = videoSoundTouches[i];
                    MixClipAudio(mixBufferVideo, item.Decoder, item.Clip, st, time, chunkDuration);
                }

                // Sum and write to file
                for (int i = 0; i < chunkSampleCount; i++)
                {
                    int sum = mixBufferBgm[i] + mixBufferVideo[i];
                    short sample = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
                    
                    outBytes[i * 2] = (byte)(sample & 0xFF);
                    outBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }

                writer.Write(outBytes);
                totalDataBytes += outBytes.Length;

                time += chunkDuration;
            }

            // Go back and write final sizes into the header
            writer.Seek(4, SeekOrigin.Begin);
            writer.Write((int)(totalDataBytes + 36)); // RIFF chunk size
            writer.Seek(40, SeekOrigin.Begin);
            writer.Write((int)totalDataBytes); // data chunk size
        }
        finally
        {
            foreach (var item in bgmDecoders) item.Decoder.Dispose();
            foreach (var item in videoDecoders) item.Decoder.Dispose();
        }

        await Task.CompletedTask;
    }

    private void MixClipAudio(short[] mixBuffer, WavAudioDecoder decoder, IClip clip, SoundTouchProcessor st, TimeSpan time, TimeSpan duration)
    {
        if (time + duration > clip.StartTime && time < clip.EndTime)
        {
            TimeSpan currentClipTime = time - clip.StartTime;
            TimeSpan readStart = TimeSpan.Zero;
            TimeSpan readDuration = duration;
            int bufferOffset = 0;

            if (currentClipTime < TimeSpan.Zero)
            {
                readStart = TimeSpan.Zero;
                readDuration = duration + currentClipTime;
                bufferOffset = (int)(-currentClipTime.TotalSeconds * 48000.0) * 2;
            }
            else
            {
                readStart = currentClipTime;
            }

            if (readStart + readDuration > clip.EndTime - clip.StartTime)
            {
                readDuration = (clip.EndTime - clip.StartTime) - readStart;
            }

            if (readDuration <= TimeSpan.Zero) return;

            // Generate offline clip pcm
            short[] fragment = GetOfflineClipPcm(decoder, clip, st, readStart, readDuration);
            int samplesToMix = Math.Min(fragment.Length, mixBuffer.Length - bufferOffset);
            for (int i = 0; i < samplesToMix; i++)
            {
                mixBuffer[bufferOffset + i] += fragment[i];
            }
        }
    }

    private short[] GetOfflineClipPcm(WavAudioDecoder decoder, IClip clip, SoundTouchProcessor st, TimeSpan relativeStart, TimeSpan relativeDuration)
    {
        double playbackRate = 1.0;
        TimeSpan trimStart = TimeSpan.Zero;
        if (clip is AudioClip bgm)
        {
            playbackRate = bgm.PlaybackRate;
            trimStart = bgm.TrimStart;
        }
        else if (clip is VideoClip video)
        {
            playbackRate = video.PlaybackRate;
            trimStart = video.TrimStart;
        }

        long startTick = (long)(relativeStart.Ticks * playbackRate);
        TimeSpan mediaStart = trimStart + TimeSpan.FromTicks(startTick);

        if (playbackRate == 1.0)
        {
            var buffer = decoder.GetAudio(mediaStart, relativeDuration);
            return buffer.Samples;
        }
        else
        {
            long outputSamplesNeeded = (long)(relativeDuration.TotalSeconds * 48000.0);
            long inputSamplesNeeded = (long)(outputSamplesNeeded * playbackRate);

            var rawInput = decoder.GetAudio(mediaStart, TimeSpan.FromSeconds((double)inputSamplesNeeded / 48000.0)).Samples;

            float[] floatInput = new float[rawInput.Length];
            for (int i = 0; i < rawInput.Length; i++)
            {
                floatInput[i] = rawInput[i] / 32768f;
            }

            st.PutSamples(floatInput, rawInput.Length / 2);

            float[] floatOutput = new float[outputSamplesNeeded * 2];
            int framesReceived = st.ReceiveSamples(floatOutput, (int)outputSamplesNeeded);

            short[] output = new short[framesReceived * 2];
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (short)Math.Clamp(floatOutput[i] * 32767f, short.MinValue, short.MaxValue);
            }

            return output;
        }
    }

    private string GetCachedWavPath(string cacheAudioDir, string originalSourcePath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(originalSourcePath));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Path.Combine(cacheAudioDir, $"{hex}.wav");
    }

    private double CalculateProjectDuration()
    {
        double maxTime = 0.0;
        foreach (var clip in _project.VideoTrack.Clips)
        {
            if (clip.EndTime.TotalSeconds > maxTime)
            {
                maxTime = clip.EndTime.TotalSeconds;
            }
        }
        foreach (var clip in _project.AudioTrack.Clips)
        {
            if (clip.EndTime.TotalSeconds > maxTime)
            {
                maxTime = clip.EndTime.TotalSeconds;
            }
        }
        foreach (var clip in _project.TextTrack.Clips)
        {
            if (clip.EndTime.TotalSeconds > maxTime)
            {
                maxTime = clip.EndTime.TotalSeconds;
            }
        }
        return maxTime == 0.0 ? 10.0 : maxTime; // Default 10 seconds if project is empty
    }

    private async Task MergeVideoAndAudioAsync(string videoPath, string audioPath, string outputPath, int videoBitrate, int audioBitrate)
    {
        // 21. Hardware encoder priorities: NVENC -> QuickSync -> AMF -> x264 -> x265
        string[] encoders = {
            "h264_nvenc",   // NVIDIA
            "h264_qsv",     // Intel QSV
            "h264_amf",     // AMD AMF
            "libx264"       // CPU fallback
        };

        bool success = false;
        string lastError = "";

        foreach (var encoder in encoders)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{videoPath}\" -i \"{audioPath}\" -c:v {encoder} -b:v {videoBitrate} -c:a aac -b:a {audioBitrate} \"{outputPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                success = true;
                break;
            }
            else
            {
                lastError = await process.StandardError.ReadToEndAsync();
            }
        }

        if (!success)
        {
            throw new InvalidOperationException($"FFmpeg final merge failed with all encoders. Last error: {lastError}");
        }
    }

    // Media Importer Preencoding functions

    public async Task PreencodeVideoAsync(string inputPath, string outputPath)
    {
        // Video: H.264, All Intra (g 1), 24fps, 480p (scale to height 480 maintaining ratio)
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -i \"{inputPath}\" -c:v libx264 -crf 18 -g 1 -keyint_min 1 -an -vf \"scale=-2:480\" -r 24 \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFmpeg video pre-encoding failed: {err}");
        }
    }

    public async Task PreencodeAudioAsync(string inputPath, string outputPath)
    {
        // Audio: 48kHz, 16-bit, Stereo, WAV (pcm_s16le)
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -i \"{inputPath}\" -vn -ar 48000 -ac 2 -c:a pcm_s16le \"{outputPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"FFmpeg audio pre-encoding failed: {err}");
        }
    }

    public void PreencodeImage(string inputPath, string outputPath)
    {
        // Image: limit area to max 518400 pixels (720x720 equivalent), maintaining ratio
        if (!File.Exists(inputPath)) return;

        using var bitmap = SKBitmap.Decode(inputPath);
        if (bitmap == null) throw new InvalidDataException("Could not decode image.");

        double area = bitmap.Width * bitmap.Height;
        const double maxArea = 518400.0;

        SKBitmap finalBitmap = bitmap;
        bool isResized = false;

        if (area > maxArea)
        {
            double scale = Math.Sqrt(maxArea / area);
            int newWidth = (int)Math.Max(1, Math.Round(bitmap.Width * scale));
            int newHeight = (int)Math.Max(1, Math.Round(bitmap.Height * scale));

            var info = new SKImageInfo(newWidth, newHeight, bitmap.ColorType, bitmap.AlphaType);
            var resized = new SKBitmap(info);
            bitmap.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear));
            finalBitmap = resized;
            isResized = true;
        }

        try
        {
            using var image = SKImage.FromBitmap(finalBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);
        }
        finally
        {
            if (isResized)
            {
                finalBitmap.Dispose();
            }
        }
    }
}
