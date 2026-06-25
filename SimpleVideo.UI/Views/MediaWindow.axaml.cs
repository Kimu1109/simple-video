using System.Collections.Generic;
using System.Linq;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using simple_video.ViewModels;

namespace simple_video.Views;

public partial class MediaWindow : Window
{
    private MainWindowViewModel ViewModel {get;}
    public MediaWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        this.ViewModel = viewModel;
        this.DataContext = viewModel;
    }

    private async void ImportVideoButton_Click(object? sender, RoutedEventArgs e)
    {
        await ImportFilesAsync(MediaAssetKind.Video, "動画を選択", new FilePickerFileType("Video")
        {
            Patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm" },
            MimeTypes = new[] { "video/*" }
        });
    }

    private async void ImportImageButton_Click(object? sender, RoutedEventArgs e)
    {
        await ImportFilesAsync(MediaAssetKind.Image, "画像を選択", new FilePickerFileType("Image")
        {
            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp" },
            MimeTypes = new[] { "image/*" }
        });
    }

    private async void ImportAudioButton_Click(object? sender, RoutedEventArgs e)
    {
        await ImportFilesAsync(MediaAssetKind.Audio, "音声を選択", new FilePickerFileType("Audio")
        {
            Patterns = new[] { "*.wav", "*.mp3", "*.m4a", "*.aac", "*.flac", "*.ogg" },
            MimeTypes = new[] { "audio/*" }
        });
    }

    private async System.Threading.Tasks.Task ImportFilesAsync(MediaAssetKind kind, string title, FilePickerFileType fileType)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = new List<FilePickerFileType> { fileType }
        });

        foreach (var file in files)
        {
            if (file.Path.LocalPath is { Length: > 0 } path)
            {
                await ViewModel.ImportMediaAsync(path, kind);
            }
        }
    }

}