using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using simple_video.Preview;
using SimpleVideo.Audio;
using SimpleVideo.Core.Models;
using SimpleVideo.Encoding;
using SimpleVideo.Infrastructure;
using SimpleVideo.Media;
using SimpleVideo.Rendering;
using SkiaSharp;

namespace simple_video.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly object _projectSync = new();
    private readonly FrameBuffer _frameBuffer = new();
    private readonly DispatcherTimer _playbackTimer = new();
    private readonly string _projectCacheDirectory = Path.Combine(Environment.CurrentDirectory, "ProjectCache");
    private FrameGenerationWorker? _frameWorker;
    private SynchronizedRenderer? _renderer;
    private AudioEngine? _audioEngine;
    private bool _isAudioClockAvailable;
    private bool _isUpdatingFromAudioClock;
    private bool _disposed;

    [ObservableProperty]
    private Project _project = new();

    [ObservableProperty]
    private int _projectWidth = 1920;

    [ObservableProperty]
    private int _projectHeight = 1080;

    [ObservableProperty]
    private double _projectFps = 24.0;

    [ObservableProperty]
    private double _currentTimeSeconds;

    [ObservableProperty]
    private string _newText = "テロップ";

    [ObservableProperty]
    private string _statusMessage = "新規プロジェクト";

    [ObservableProperty]
    private MediaAssetViewModel? _selectedAsset;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private bool _isPreviewRendering;

    [ObservableProperty]
    private bool _isPlaying;

    public ObservableCollection<MediaAssetViewModel> MediaAssets { get; } = new();
    public ObservableCollection<TimelineClipViewModel> TimelineClips { get; } = new();

    public string ResolutionText => $"{ProjectWidth} x {ProjectHeight}";
    public string CurrentTimeText => FormatTime(TimeSpan.FromSeconds(CurrentTimeSeconds));
    public double DurationSeconds => Math.Max(10.0, GetProjectDuration().TotalSeconds);
    public string PlayPauseText => IsPlaying ? "pause" : "play";

    public MainWindowViewModel()
    {
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0);
        _playbackTimer.Tick += OnPlaybackTimerTick;
        RebuildPreviewPipeline();
        RebuildAudioEngine();
    }

    partial void OnProjectWidthChanged(int value)
    {
        lock (_projectSync)
        {
            Project.Width = Math.Max(1, value);
        }

        OnPropertyChanged(nameof(ResolutionText));
        RebuildPreviewPipeline();
    }

    partial void OnProjectHeightChanged(int value)
    {
        lock (_projectSync)
        {
            Project.Height = Math.Max(1, value);
        }

        OnPropertyChanged(nameof(ResolutionText));
        RebuildPreviewPipeline();
    }

    partial void OnProjectFpsChanged(double value)
    {
        lock (_projectSync)
        {
            Project.Fps = Math.Max(1.0, value);
        }

        _playbackTimer.Interval = TimeSpan.FromSeconds(1.0 / Math.Min(60.0, Math.Max(1.0, Project.Fps)));
        RequestPreviewFrame();
    }

    partial void OnCurrentTimeSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(CurrentTimeText));
        if (IsPlaying && _isAudioClockAvailable && !_isUpdatingFromAudioClock)
        {
            _audioEngine?.Seek(TimeSpan.FromSeconds(CurrentTimeSeconds));
        }

        RequestPreviewFrame();
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseText));
    }

    [RelayCommand]
    private void NewProject()
    {
        LoadProjectIntoView(new Project(), clearMediaAssets: true);
        StatusMessage = "新規プロジェクトを作成しました";
    }

    [RelayCommand]
    private void AddSelectedAssetToTimeline()
    {
        if (SelectedAsset == null)
        {
            StatusMessage = "追加するメディアを選択してください";
            return;
        }

        var start = TimeSpan.FromSeconds(CurrentTimeSeconds);
        var end = start + SelectedAsset.DefaultDuration;

        lock (_projectSync)
        {
            switch (SelectedAsset.Kind)
            {
                case MediaAssetKind.Video:
                    Project.VideoTrack.Clips.Add(new VideoClip
                    {
                        SourceFile = SelectedAsset.Path,
                        StartTime = start,
                        EndTime = end,
                        PlaybackRate = 1.0
                    });
                    break;
                case MediaAssetKind.Image:
                    Project.VideoTrack.Clips.Add(new ImageClip
                    {
                        SourceFile = SelectedAsset.Path,
                        StartTime = start,
                        EndTime = end,
                        PositionMode = PositionMode.Center,
                        Scale = 1.0
                    });
                    break;
                case MediaAssetKind.Audio:
                    Project.AudioTrack.Clips.Add(new AudioClip
                    {
                        SourceFile = SelectedAsset.Path,
                        StartTime = start,
                        EndTime = end,
                        PlaybackRate = 1.0
                    });
                    break;
            }
        }

        RefreshTimeline();
        RebuildPreviewPipeline();
        RebuildAudioEngine();
        StatusMessage = $"{SelectedAsset.Name} をタイムラインに追加しました";
    }

    [RelayCommand]
    private void AddTextClip()
    {
        var text = string.IsNullOrWhiteSpace(NewText) ? "テロップ" : NewText.Trim();
        var start = TimeSpan.FromSeconds(CurrentTimeSeconds);
        var clip = new TextClip
        {
            Text = text,
            StartTime = start,
            EndTime = start + TimeSpan.FromSeconds(5),
            FontSize = 54,
            Color = SKColors.White,
            PositionMode = TextPositionMode.Bottom
        };

        lock (_projectSync)
        {
            Project.TextTrack.Clips.Add(clip);
        }

        RefreshTimeline();
        RebuildPreviewPipeline();
        RebuildAudioEngine();
        StatusMessage = "テロップを追加しました";
    }

    [RelayCommand]
    private void AddBackgroundClip()
    {
        var start = TimeSpan.FromSeconds(CurrentTimeSeconds);
        lock (_projectSync)
        {
            Project.VideoTrack.Clips.Add(new ColorClip
            {
                BackgroundColor = new SKColor(18, 20, 24),
                StartTime = start,
                EndTime = start + TimeSpan.FromSeconds(10)
            });
        }

        RefreshTimeline();
        RebuildPreviewPipeline();
        RebuildAudioEngine();
        StatusMessage = "背景クリップを追加しました";
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying)
        {
            _playbackTimer.Stop();
            _audioEngine?.Pause();
            IsPlaying = false;
            StatusMessage = "プレビューを停止しました";
            return;
        }

        if (CurrentTimeSeconds >= DurationSeconds)
        {
            CurrentTimeSeconds = 0;
        }

        IsPlaying = true;
        _audioEngine?.Seek(TimeSpan.FromSeconds(CurrentTimeSeconds));
        _audioEngine?.Play();
        _playbackTimer.Start();
        StatusMessage = _isAudioClockAvailable ? "音声クロックでプレビューを再生しています" : "プレビューを再生しています";
    }

    public async System.Threading.Tasks.Task ImportMediaAsync(string path, MediaAssetKind kind)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "メディアファイルが見つかりません";
            return;
        }

        StatusMessage = $"{Path.GetFileName(path)} をキャッシュ準備中です";

        var timelinePath = path;
        try
        {
            timelinePath = await PrepareMediaCacheAsync(path, kind);
        }
        catch (Exception ex)
        {
            StatusMessage = $"キャッシュ準備をスキップしました: {ex.Message}";
        }

        var defaultDuration = kind switch
        {
            MediaAssetKind.Audio => TimeSpan.FromSeconds(30),
            MediaAssetKind.Video => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(5)
        };

        var asset = new MediaAssetViewModel(kind, timelinePath, defaultDuration);
        MediaAssets.Add(asset);
        SelectedAsset = asset;
        StatusMessage = $"{asset.Name} を読み込みました";
    }

    public void SaveProject(string path)
    {
        lock (_projectSync)
        {
            ProjectSerializer.SaveProject(Project, path);
        }

        StatusMessage = $"保存しました: {Path.GetFileName(path)}";
    }

    public void LoadProject(string path)
    {
        var loadedProject = ProjectSerializer.LoadProject(path);
        LoadProjectIntoView(loadedProject, clearMediaAssets: false);
        StatusMessage = $"読み込みました: {Path.GetFileName(path)}";
    }

    private void LoadProjectIntoView(Project project, bool clearMediaAssets)
    {
        lock (_projectSync)
        {
            Project = project;
        }

        ProjectWidth = project.Width;
        ProjectHeight = project.Height;
        ProjectFps = project.Fps;
        CurrentTimeSeconds = 0;

        if (clearMediaAssets)
        {
            MediaAssets.Clear();
        }

        RefreshTimeline();
        RebuildPreviewPipeline();
        RebuildAudioEngine();
    }

    private void RefreshTimeline()
    {
        TimelineClips.Clear();

        lock (_projectSync)
        {
            foreach (var clip in Project.VideoTrack.Clips)
            {
                TimelineClips.Add(TimelineClipViewModel.FromVideoClip(clip));
            }

            foreach (var clip in Project.TextTrack.Clips)
            {
                TimelineClips.Add(TimelineClipViewModel.FromTextClip(clip));
            }

            foreach (var clip in Project.AudioTrack.Clips)
            {
                TimelineClips.Add(TimelineClipViewModel.FromAudioClip(clip));
            }
        }

        var ordered = TimelineClips.OrderBy(c => c.StartSeconds).ThenBy(c => c.TrackName).ToList();
        TimelineClips.Clear();
        foreach (var clip in ordered)
        {
            TimelineClips.Add(clip);
        }

        OnPropertyChanged(nameof(DurationSeconds));
    }

    private TimeSpan GetProjectDuration()
    {
        long maxTicks;
        lock (_projectSync)
        {
            maxTicks = Project.VideoTrack.Clips.Cast<IClip>()
                .Concat(Project.TextTrack.Clips)
                .Concat(Project.AudioTrack.Clips)
                .Select(clip => clip.EndTime.Ticks)
                .DefaultIfEmpty(TimeSpan.FromSeconds(10).Ticks)
                .Max();
        }

        return TimeSpan.FromTicks(maxTicks);
    }

    private void RebuildPreviewPipeline()
    {
        if (_disposed) return;

        IsPreviewRendering = true;
        _frameWorker?.Dispose();
        _renderer?.Dispose();
        _frameBuffer.Clear();

        var renderer = new SkiaRenderer(Project, new FFMediaToolkitVideoDecoderFactory());
        _renderer = new SynchronizedRenderer(renderer, _projectSync);
        _frameWorker = new FrameGenerationWorker(_renderer, _frameBuffer, () => Project.Fps, GetProjectDuration);
        _frameWorker.FrameReady += OnFrameReady;

        RequestPreviewFrame();
    }

    private async System.Threading.Tasks.Task<string> PrepareMediaCacheAsync(string path, MediaAssetKind kind)
    {
        Directory.CreateDirectory(_projectCacheDirectory);

        if (_renderer == null)
        {
            RebuildPreviewPipeline();
        }

        if (_renderer == null)
        {
            return path;
        }

        var encoder = new MediaEncoderService(Project, _renderer);
        var cacheManager = new ProjectCacheManager(_projectCacheDirectory, encoder);

        return kind switch
        {
            MediaAssetKind.Video => await PrepareVideoImportAsync(cacheManager, path),
            MediaAssetKind.Audio => await PrepareAudioImportAsync(cacheManager, path),
            MediaAssetKind.Image => cacheManager.PrepareImageClip(path),
            _ => path
        };
    }

    private static async System.Threading.Tasks.Task<string> PrepareVideoImportAsync(ProjectCacheManager cacheManager, string path)
    {
        await cacheManager.PrepareVideoClipAsync(path);
        return path;
    }

    private static async System.Threading.Tasks.Task<string> PrepareAudioImportAsync(ProjectCacheManager cacheManager, string path)
    {
        await cacheManager.PrepareAudioClipAsync(path);
        return path;
    }

    private void RebuildAudioEngine()
    {
        if (_disposed) return;

        _audioEngine?.Pause();
        _audioEngine?.Dispose();
        _audioEngine = null;
        _isAudioClockAvailable = false;

        if (!HasAudioSources())
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.Combine(_projectCacheDirectory, "audio"));
            _audioEngine = new AudioEngine(Project, _projectCacheDirectory);
            _audioEngine.LoadProjectClips();
            _isAudioClockAvailable = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"音声デバイスを使用できません: {ex.Message}";
        }
    }

    private bool HasAudioSources()
    {
        lock (_projectSync)
        {
            return Project.AudioTrack.Clips.Count > 0 || Project.VideoTrack.Clips.OfType<VideoClip>().Any();
        }
    }

    private void RequestPreviewFrame()
    {
        if (_disposed || _frameWorker == null) return;

        IsPreviewRendering = true;
        _frameWorker.Request(TimeSpan.FromSeconds(CurrentTimeSeconds));
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        var nextTime = _isAudioClockAvailable && _audioEngine != null
            ? _audioEngine.CurrentTime.TotalSeconds
            : CurrentTimeSeconds + _playbackTimer.Interval.TotalSeconds;

        if (nextTime >= DurationSeconds)
        {
            CurrentTimeSeconds = DurationSeconds;
            _playbackTimer.Stop();
            _audioEngine?.Pause();
            IsPlaying = false;
            StatusMessage = "プレビューの末尾です";
            return;
        }

        _isUpdatingFromAudioClock = _isAudioClockAvailable;
        CurrentTimeSeconds = nextTime;
        _isUpdatingFromAudioClock = false;
    }

    private void OnFrameReady(object? sender, FrameReadyEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;

            PreviewImage = e.Bitmap;
            IsPreviewRendering = false;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _playbackTimer.Stop();
        _audioEngine?.Pause();
        _audioEngine?.Dispose();
        _frameWorker?.Dispose();
        _renderer?.Dispose();
        _frameBuffer.Dispose();
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100:0}";
    }
}

public enum MediaAssetKind
{
    Video,
    Image,
    Audio
}

public sealed class MediaAssetViewModel
{
    public MediaAssetViewModel(MediaAssetKind kind, string path, TimeSpan defaultDuration)
    {
        Kind = kind;
        Path = path;
        DefaultDuration = defaultDuration;
    }

    public MediaAssetKind Kind { get; }
    public string Path { get; }
    public TimeSpan DefaultDuration { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string KindLabel => Kind.ToString();
    public string DurationLabel => $"{DefaultDuration.TotalSeconds:0}s";
}

public sealed class TimelineClipViewModel
{
    private TimelineClipViewModel(string trackName, string label, TimeSpan startTime, TimeSpan endTime)
    {
        TrackName = trackName;
        Label = label;
        StartSeconds = startTime.TotalSeconds;
        DurationSeconds = Math.Max(0.1, (endTime - startTime).TotalSeconds);
        RangeLabel = $"{FormatTime(startTime)} - {FormatTime(endTime)}";
    }

    public string TrackName { get; }
    public string Label { get; }
    public double StartSeconds { get; }
    public double DurationSeconds { get; }
    public string RangeLabel { get; }

    public static TimelineClipViewModel FromVideoClip(IVideoTrackClip clip)
    {
        return clip switch
        {
            VideoClip video => new TimelineClipViewModel("Video", Path.GetFileName(video.SourceFile), video.StartTime, video.EndTime),
            ImageClip image => new TimelineClipViewModel("Video", Path.GetFileName(image.SourceFile), image.StartTime, image.EndTime),
            ColorClip color => new TimelineClipViewModel("Video", $"Background #{color.BackgroundColor.Red:X2}{color.BackgroundColor.Green:X2}{color.BackgroundColor.Blue:X2}", color.StartTime, color.EndTime),
            _ => new TimelineClipViewModel("Video", clip.GetType().Name, clip.StartTime, clip.EndTime)
        };
    }

    public static TimelineClipViewModel FromTextClip(TextClip clip)
    {
        return new TimelineClipViewModel("Text", clip.Text, clip.StartTime, clip.EndTime);
    }

    public static TimelineClipViewModel FromAudioClip(AudioClip clip)
    {
        return new TimelineClipViewModel("BGM", Path.GetFileName(clip.SourceFile), clip.StartTime, clip.EndTime);
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100:0}";
    }
}
