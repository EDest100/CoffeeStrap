// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Media.Imaging;
using Froststrap.UI.ViewModels;

namespace Froststrap.Models.APIs.Config
{
    internal partial class CommunityMod : NotifyPropertyChangedViewModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("download")]
        public string DownloadUrl { get; set; } = null!;

        [JsonPropertyName("gradient")]
        public List<GradientStop>? GradientStops { get; set; }

        [JsonPropertyName("angle")]
        public double? GradientAngle { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; } = null!;

        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonIgnore] public string ApplyHint => IsInstalled ?
            ModType == ModType.SkyBox ? "Relaunch to apply; replaces Roblox's default sky only." :
            "Applies on your next CoffeeStrap launch." : "";

        [JsonPropertyName("thumbnail")]
        public string ThumbnailUrl { get; set; } = null!;

        [JsonPropertyName("modtype")]
        public ModType ModType { get; set; } = ModType.ColorMod;

        private bool _installed;
        private bool _enabled;
        [JsonIgnore] public bool IsEnabled { get => _enabled; set { if (SetProperty(ref _enabled, value)) OnPropertyChanged(nameof(ToggleLabel)); } }
        [JsonIgnore] public string ToggleLabel => IsEnabled ? "Disable" : "Enable";
        [JsonIgnore] public object? ToggleCommand { get; set; }
        [JsonIgnore] public object? PreviewCommand { get; set; }
        [JsonIgnore] public object? UninstallCommand { get; set; }
        private string _downloadError = "";
        [JsonIgnore] public bool IsInstalled { get => _installed; set { if (SetProperty(ref _installed, value)) { NotifyDownloadState(); OnPropertyChanged(nameof(ApplyHint)); } } }
        [JsonIgnore] public string DownloadError { get => _downloadError; set { if (SetProperty(ref _downloadError, value)) NotifyDownloadState(); } }
        [JsonIgnore] public bool IsLocalOnly { get; set; }
        [JsonIgnore] public bool CanDownload => !IsLocalOnly && !IsInstalled && !IsDownloading;
        [JsonIgnore] public string DownloadLabel => IsInstalled ? "Installed" : IsDownloading ? "Downloading…" : string.IsNullOrEmpty(DownloadError) ? "Download" : "Retry";
        private void NotifyDownloadState() { OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(DownloadLabel)); }
        private bool _isDownloading;

        [JsonIgnore]
        public bool IsDownloading
        {
            get => _isDownloading;
            set { if (SetProperty(ref _isDownloading, value)) NotifyDownloadState(); }
        }

        private double _downloadProgress;
        [JsonIgnore]
        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        private object? _downloadCommand;
        [JsonIgnore]
        public object? DownloadCommand
        {
            get => _downloadCommand;
            set => SetProperty(ref _downloadCommand, value);
        }

        [JsonIgnore]
        public bool IsCustomTheme => ModType == ModType.CustomTheme;

        [JsonIgnore]
        public bool IsColorMod => ModType == ModType.ColorMod;

        [JsonIgnore]
        public string ModTypeDisplay => ModType switch
        {
            ModType.MiscMod => "Misc Mod",
            ModType.ColorMod => "Color Mod",
            ModType.SkyBox => "SkyBox",
            ModType.Cursor => "Cursor",
            ModType.AvatarEditor => "Avatar Editor",
            ModType.CustomTheme => "Custom Theme",
            _ => "Unknown"
        };

        private Bitmap? _thumbnail;
        [JsonIgnore]
        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        private string GetCacheFilePath()
        {
            string cacheDir = Path.Combine(Paths.Cache, "CommunityMods");
            Directory.CreateDirectory(cacheDir);
            return Path.Combine(cacheDir, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Id ?? ThumbnailUrl))) + ".png");
        }

        public async Task LoadThumbnailAsync()
        {
            if (Thumbnail != null || string.IsNullOrEmpty(ThumbnailUrl))
                return;

            if (ThumbnailUrl == "avares://CoffeeStrap/Resources/CommunityMods/EspressoPreview.png")
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri(ThumbnailUrl));
                Thumbnail = Bitmap.DecodeToWidth(stream, 600);
                return;
            }

            string cachePath = GetCacheFilePath();

            if (File.Exists(cachePath))
            {
                try
                {
                    var bitmap = await Task.Run(() =>
                    {
                        using var fs = File.OpenRead(cachePath);
                        return Bitmap.DecodeToWidth(fs, 600);
                    });
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Thumbnail = bitmap);
                    return;
                }
                catch (Exception ex)
                {
                    App.Logger.Error($"Failed to load cached thumbnail: {ex.Message}");
                    try { File.Delete(cachePath); } catch { }
                }
            }

            try
            {
                using var response = await App.HttpClient.GetAsync(new Uri(ThumbnailUrl));
                if (!response.IsSuccessStatusCode) return;

                await using var stream = await response.Content.ReadAsStreamAsync();
                var bitmap = await Task.Run(() => Bitmap.DecodeToWidth(stream, 600));

                await Task.Run(() =>
                {
                    using var fs = File.Create(cachePath);
                    bitmap.Save(fs, PngBitmapEncoderOptions.Default);
                });

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Thumbnail = bitmap);
            }
            catch (Exception ex)
            {
                App.Logger.Error("Unhandled exception: ", ex.Message);
            }
        }
    }
}
