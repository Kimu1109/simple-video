using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Miniaudio;
using SoundTouch;
using SimpleVideo.Core.Models;
using SimpleVideo.Core.Media;
using SimpleVideo.Media;

namespace SimpleVideo.Audio;

public unsafe class AudioEngine : IDisposable
{
    private readonly Project _project;
    private readonly string _cacheDirectory;
    private ma_device* _device;
    private bool _isPlaying;
    private TimeSpan _currentTime = TimeSpan.Zero;
    private readonly object _lock = new();

    // Cache structure representing clips in memory
    private class CachedClip
    {
        public short[] Samples { get; set; } = Array.Empty<short>();
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public TimeSpan TrimStart { get; set; }
        public TimeSpan TrimEnd { get; set; }
        public double PlaybackRate { get; set; } = 1.0;
        public SoundTouchProcessor SoundTouch { get; set; } = new();
    }

    private readonly List<CachedClip> _activeBgmClips = new();
    private readonly List<CachedClip> _activeVideoAudioClips = new();

    // Singleton-like static reference for native callbacks to access the active engine instance
    private static AudioEngine? _instance;

    public TimeSpan CurrentTime
    {
        get
        {
            lock (_lock)
            {
                return _currentTime;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _isPlaying;
            }
        }
    }

    public AudioEngine(Project project, string cacheDirectory)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
        _instance = this;

        InitializeDevice();
    }

    private void InitializeDevice()
    {
        lock (_lock)
        {
            _device = (ma_device*)NativeMemory.Alloc((nuint)sizeof(ma_device));
            
            ma_device_config config = ma.device_config_init(ma_device_type.ma_device_type_playback);
            config.playback.format = ma_format.ma_format_s16; // 16-bit PCM
            config.playback.channels = 2; // Stereo
            config.sampleRate = 48000; // 48kHz
            config.dataCallback = &OnPlaybackCallback;
            config.pUserData = null;

            var result = ma.device_init(null, &config, _device);
            if (result != ma_result.MA_SUCCESS)
            {
                NativeMemory.Free(_device);
                _device = null;
                throw new InvalidOperationException($"Failed to initialize Miniaudio playback device: {result}");
            }
        }
    }

    public void LoadProjectClips()
    {
        lock (_lock)
        {
            _activeBgmClips.Clear();
            _activeVideoAudioClips.Clear();

            // 1. Load BGM Track clips
            foreach (var clip in _project.AudioTrack.Clips)
            {
                var cachedWavPath = GetCachedWavPath(clip.SourceFile);
                if (File.Exists(cachedWavPath))
                {
                    var pcm = LoadWavPcm(cachedWavPath);
                    var st = new SoundTouchProcessor
                    {
                        SampleRate = 48000,
                        Channels = 2,
                        Tempo = (float)clip.PlaybackRate
                    };
                    
                    _activeBgmClips.Add(new CachedClip
                    {
                        Samples = pcm,
                        StartTime = clip.StartTime,
                        EndTime = clip.EndTime,
                        TrimStart = clip.TrimStart,
                        TrimEnd = clip.TrimEnd,
                        PlaybackRate = clip.PlaybackRate,
                        SoundTouch = st
                    });
                }
            }

            // 2. Load Video Track clips' audio
            foreach (var clip in _project.VideoTrack.Clips)
            {
                if (clip is VideoClip videoClip)
                {
                    var cachedWavPath = GetCachedWavPath(videoClip.SourceFile);
                    if (File.Exists(cachedWavPath))
                    {
                        var pcm = LoadWavPcm(cachedWavPath);
                        var st = new SoundTouchProcessor
                        {
                            SampleRate = 48000,
                            Channels = 2,
                            Tempo = (float)videoClip.PlaybackRate
                        };

                        _activeVideoAudioClips.Add(new CachedClip
                        {
                            Samples = pcm,
                            StartTime = videoClip.StartTime,
                            EndTime = videoClip.EndTime,
                            TrimStart = videoClip.TrimStart,
                            TrimEnd = videoClip.TrimEnd,
                            PlaybackRate = videoClip.PlaybackRate,
                            SoundTouch = st
                        });
                    }
                }
            }
        }
    }

    private string GetCachedWavPath(string originalSourcePath)
    {
        // Simple hash based mapping to locate cached WAV file in the cache directory
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(originalSourcePath));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Path.Combine(_cacheDirectory, "audio", $"{hex}.wav");
    }

    private short[] LoadWavPcm(string wavPath)
    {
        using var decoder = new WavAudioDecoder(wavPath);
        // Read the entire file
        // To read the whole file, query a large duration
        var buffer = decoder.GetAudio(TimeSpan.Zero, TimeSpan.FromHours(1));
        return buffer.Samples;
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_device == null || _isPlaying) return;

            // Reset SoundTouch processors' buffers to avoid audio lag or pitch residue from previous runs
            foreach (var clip in _activeBgmClips) clip.SoundTouch.Clear();
            foreach (var clip in _activeVideoAudioClips) clip.SoundTouch.Clear();

            var result = ma.device_start(_device);
            if (result == ma_result.MA_SUCCESS)
            {
                _isPlaying = true;
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_device == null || !_isPlaying) return;

            var result = ma.device_stop(_device);
            if (result == ma_result.MA_SUCCESS)
            {
                _isPlaying = false;
            }
        }
    }

    public void Seek(TimeSpan time)
    {
        lock (_lock)
        {
            _currentTime = time;
            if (_currentTime < TimeSpan.Zero) _currentTime = TimeSpan.Zero;
            
            // Clear tempo stretchers on seek
            foreach (var clip in _activeBgmClips) clip.SoundTouch.Clear();
            foreach (var clip in _activeVideoAudioClips) clip.SoundTouch.Clear();
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnPlaybackCallback(ma_device* pDevice, void* pOutput, void* pInput, uint frameCount)
    {
        if (_instance == null) return;
        _instance.MixAudio(pOutput, frameCount);
    }

    private void MixAudio(void* pOutput, uint frameCount)
    {
        int samplesNeeded = (int)frameCount * 2; // 2 channels (Stereo)
        short* outBuffer = (short*)pOutput;

        // Clear output buffer with silence initially
        for (int i = 0; i < samplesNeeded; i++)
        {
            outBuffer[i] = 0;
        }

        lock (_lock)
        {
            TimeSpan duration = TimeSpan.FromSeconds((double)frameCount / 48000.0);
            TimeSpan time = _currentTime;

            // Allocate temporary mixing buffers
            short[] mixBufferBgm = new short[samplesNeeded];
            short[] mixBufferVideo = new short[samplesNeeded];

            // 1. Gather and mix BGM track audio
            FillMixBuffer(mixBufferBgm, _activeBgmClips, time, duration);

            // 2. Gather and mix video track audio
            FillMixBuffer(mixBufferVideo, _activeVideoAudioClips, time, duration);

            // 3. Mix tracks together with clipping prevention
            for (int i = 0; i < samplesNeeded; i++)
            {
                int sum = mixBufferBgm[i] + mixBufferVideo[i];
                outBuffer[i] = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
            }

            // Update master play clock
            _currentTime += duration;
        }
    }

    private void FillMixBuffer(short[] mixBuffer, List<CachedClip> clips, TimeSpan time, TimeSpan duration)
    {
        foreach (var clip in clips)
        {
            // Check if clip falls within the requested play segment
            if (time + duration > clip.StartTime && time < clip.EndTime)
            {
                // Calculate clip bounds relative to timeline time
                TimeSpan currentClipTime = time - clip.StartTime;
                TimeSpan readStart = TimeSpan.Zero;
                TimeSpan readDuration = duration;
                int bufferOffset = 0;

                if (currentClipTime < TimeSpan.Zero)
                {
                    // Clip starts mid-way through this block
                    readStart = TimeSpan.Zero;
                    readDuration = duration + currentClipTime;
                    bufferOffset = (int)(-currentClipTime.TotalSeconds * 48000.0) * 2;
                }
                else
                {
                    readStart = currentClipTime;
                }

                // Check if clip ends mid-way through this block
                if (readStart + readDuration > clip.EndTime - clip.StartTime)
                {
                    readDuration = (clip.EndTime - clip.StartTime) - readStart;
                }

                if (readDuration <= TimeSpan.Zero) continue;

                // Read sample fragment from cache
                short[] fragment = GetClipPcm(clip, readStart, readDuration);
                
                // Write into mixing buffer
                int samplesToMix = Math.Min(fragment.Length, mixBuffer.Length - bufferOffset);
                for (int i = 0; i < samplesToMix; i++)
                {
                    mixBuffer[bufferOffset + i] = fragment[i];
                }
            }
        }
    }

    private short[] GetClipPcm(CachedClip clip, TimeSpan relativeStart, TimeSpan relativeDuration)
    {
        // 1. Map relative clip time to media source PCM coordinates
        long startTick = (long)(relativeStart.Ticks * clip.PlaybackRate);
        TimeSpan mediaStart = clip.TrimStart + TimeSpan.FromTicks(startTick);

        if (clip.PlaybackRate == 1.0)
        {
            // Simple slice copy
            long startSample = (long)(mediaStart.TotalSeconds * 48000.0) * 2;
            long samplesNeeded = (long)(relativeDuration.TotalSeconds * 48000.0) * 2;

            return ExtractSamples(clip.Samples, startSample, samplesNeeded);
        }
        else
        {
            // Tempo stretch via SoundTouch
            // Calculate how many source samples we need to feed SoundTouch to produce the required output length
            long outputSamplesNeeded = (long)(relativeDuration.TotalSeconds * 48000.0);
            long inputSamplesNeeded = (long)(outputSamplesNeeded * clip.PlaybackRate);

            long startSample = (long)(mediaStart.TotalSeconds * 48000.0) * 2;
            short[] rawInput = ExtractSamples(clip.Samples, startSample, inputSamplesNeeded * 2);

            // Convert input to float for SoundTouch
            float[] floatInput = new float[rawInput.Length];
            for (int i = 0; i < rawInput.Length; i++)
            {
                floatInput[i] = rawInput[i] / 32768f;
            }

            clip.SoundTouch.PutSamples(floatInput, rawInput.Length / 2);

            // Receive processed float samples
            float[] floatOutput = new float[outputSamplesNeeded * 2];
            int framesReceived = clip.SoundTouch.ReceiveSamples(floatOutput, (int)outputSamplesNeeded);

            // Convert back to short samples
            short[] output = new short[framesReceived * 2];
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = (short)Math.Clamp(floatOutput[i] * 32767f, short.MinValue, short.MaxValue);
            }

            return output;
        }
    }

    private short[] ExtractSamples(short[] source, long startSample, long length)
    {
        if (startSample < 0)
        {
            length += startSample;
            startSample = 0;
        }

        if (startSample >= source.Length || length <= 0)
        {
            return Array.Empty<short>();
        }

        if (startSample + length > source.Length)
        {
            length = source.Length - startSample;
        }

        // Keep stereo frame alignment (even numbers of samples)
        length = (length / 2) * 2;

        short[] destination = new short[length];
        Array.Copy(source, startSample, destination, 0, length);
        return destination;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_device != null)
            {
                ma.device_uninit(_device);
                NativeMemory.Free(_device);
                _device = null;
            }

            foreach (var clip in _activeBgmClips)
            {
                // SoundTouchProcessor does not implement IDisposable
            }
            _activeBgmClips.Clear();

            foreach (var clip in _activeVideoAudioClips)
            {
                // SoundTouchProcessor does not implement IDisposable
            }
            _activeVideoAudioClips.Clear();
        }
        
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
