using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SimpleVideo.Encoding;

namespace SimpleVideo.Infrastructure;

public class ProjectCacheManager
{
    private readonly string _baseDirectory;
    private readonly MediaEncoderService _encoderService;

    public string VideoCacheDirectory => Path.Combine(_baseDirectory, "video");
    public string AudioCacheDirectory => Path.Combine(_baseDirectory, "audio");
    public string ImageCacheDirectory => Path.Combine(_baseDirectory, "image");
    public string WaveformCacheDirectory => Path.Combine(_baseDirectory, "waveform");

    public ProjectCacheManager(string baseDirectory, MediaEncoderService encoderService)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _encoderService = encoderService ?? throw new ArgumentNullException(nameof(encoderService));

        EnsureCacheDirectories();
    }

    private void EnsureCacheDirectories()
    {
        Directory.CreateDirectory(VideoCacheDirectory);
        Directory.CreateDirectory(AudioCacheDirectory);
        Directory.CreateDirectory(ImageCacheDirectory);
        Directory.CreateDirectory(WaveformCacheDirectory);
    }

    /// <summary>
    /// Pre-encodes a video source to H.264 All-Intra 480p 24fps in the cache if not already present.
    /// Also automatically pre-encodes its audio track.
    /// </summary>
    /// <returns>The path to the cached preview video file.</returns>
    public async Task<string> PrepareVideoClipAsync(string originalPath)
    {
        if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original video source not found.", originalPath);
        }

        string hash = GetMd5Hash(originalPath);
        string cachedVideoPath = Path.Combine(VideoCacheDirectory, $"{hash}.mp4");
        string cachedAudioPath = Path.Combine(AudioCacheDirectory, $"{hash}.wav");

        // 1. Preencode Video Track (Preview MP4)
        if (!File.Exists(cachedVideoPath))
        {
            await _encoderService.PreencodeVideoAsync(originalPath, cachedVideoPath);
        }

        // 2. Preencode Audio Track (WAV PCM)
        if (!File.Exists(cachedAudioPath))
        {
            try
            {
                await _encoderService.PreencodeAudioAsync(originalPath, cachedAudioPath);
            }
            catch
            {
                // Silent fail if video contains no audio stream
            }
        }

        return cachedVideoPath;
    }

    /// <summary>
    /// Pre-encodes an audio source to 48kHz 16-bit Stereo PCM WAV in the cache if not already present.
    /// </summary>
    /// <returns>The path to the cached WAV audio file.</returns>
    public async Task<string> PrepareAudioClipAsync(string originalPath)
    {
        if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original audio source not found.", originalPath);
        }

        string hash = GetMd5Hash(originalPath);
        string cachedAudioPath = Path.Combine(AudioCacheDirectory, $"{hash}.wav");

        if (!File.Exists(cachedAudioPath))
        {
            await _encoderService.PreencodeAudioAsync(originalPath, cachedAudioPath);
        }

        return cachedAudioPath;
    }

    /// <summary>
    /// Decodes and resizes an image source to max 518400 pixels in the cache if not already present.
    /// </summary>
    /// <returns>The path to the cached PNG image file.</returns>
    public string PrepareImageClip(string originalPath)
    {
        if (string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath))
        {
            throw new FileNotFoundException("Original image source not found.", originalPath);
        }

        string hash = GetMd5Hash(originalPath);
        string cachedImagePath = Path.Combine(ImageCacheDirectory, $"{hash}.png");

        if (!File.Exists(cachedImagePath))
        {
            _encoderService.PreencodeImage(originalPath, cachedImagePath);
        }

        return cachedImagePath;
    }

    /// <summary>
    /// Clean all cached items from the directories.
    /// </summary>
    public void ClearCache()
    {
        ClearDirectory(VideoCacheDirectory);
        ClearDirectory(AudioCacheDirectory);
        ClearDirectory(ImageCacheDirectory);
        ClearDirectory(WaveformCacheDirectory);
    }

    private void ClearDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.GetFiles(path))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Keep locked files silently
            }
        }
    }

    private string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
