// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Media.Imaging;

namespace Froststrap.Models
{
    internal class QuickPlayGameItem : Froststrap.UI.ViewModels.NotifyPropertyChangedViewModel
    {
        public long UniverseId { get; set; }
        public long PlaceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
        private Bitmap? _thumbnailBitmap;
        public Bitmap? ThumbnailBitmap { get => _thumbnailBitmap; set => SetProperty(ref _thumbnailBitmap, value); }
        public long Playing { get; set; }
        public long Visits { get; set; }
        public int ServerCount { get; set; }
        public string? LastJobId { get; set; }
        public UniverseDetails? OriginalDetails { get; set; }
        public GameSource Source { get; set; }
        public long LastPlayedTicks { get; set; }
        [JsonIgnore]
        public bool IsPinned
        {
            get => App.State.Prop.QuickLaunchPins.Any(pin => pin.PlaceId == PlaceId);
            set { if (value == IsPinned) return; Froststrap.Services.GameLaunchService.SetPinned(this, value); OnPropertyChanged(nameof(IsPinned)); }
        }
        internal void RefreshPinState() => OnPropertyChanged(nameof(IsPinned));
    }
}
