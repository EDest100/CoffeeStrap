// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Collections.ObjectModel;
using Froststrap.Services;

namespace Froststrap.UI.ViewModels;

internal sealed class PinnedGamesViewModel : NotifyPropertyChangedViewModel, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private List<QuickPlayGameItem> _allGames = [];
    private string _searchQuery = "";
    private bool _disposed;
    public ObservableCollection<QuickPlayGameItem> Games { get; } = [];
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) FilterGames(); } }
    public bool IsEmpty => Games.Count == 0;
    public string EmptyMessage => _allGames.Count == 0 ? "Pin games from Home to add shortcuts here." : "No pinned games match your search.";

    public PinnedGamesViewModel()
    {
        SeedDefaultPin();
        RefreshPins();
        GameLaunchService.PinsChanged += PinsChanged;
    }
    internal static void SeedDefaultPin()
    {
        if (App.State.Prop.HasSeededJjsPin) return;
        const long place = 9391468976, universe = 3508322461;
        if (!App.State.Prop.QuickLaunchPins.Any(pin => pin.PlaceId == place))
            App.State.Prop.QuickLaunchPins.Add(new QuickLaunchPin { PlaceId = place, UniverseId = universe, Name = "Jujutsu Shenanigans" });
        App.State.Prop.HasSeededJjsPin = true;
        App.State.SaveSetting(nameof(App.State.Prop.QuickLaunchPins)); App.State.SaveSetting(nameof(App.State.Prop.HasSeededJjsPin));
    }
    private void PinsChanged(object? sender, EventArgs e) => RefreshPins();
    private void RefreshPins()
    {
        if (_disposed) return;
        var existing = _allGames.ToDictionary(game => game.PlaceId);
        var added = new List<QuickPlayGameItem>();
        _allGames = App.State.Prop.QuickLaunchPins.Where(pin => pin.PlaceId > 0).DistinctBy(pin => pin.PlaceId).Select(pin =>
        {
            if (existing.TryGetValue(pin.PlaceId, out var game) && game.Name == pin.Name && game.UniverseId == pin.UniverseId) return game;
            game = new QuickPlayGameItem { PlaceId = pin.PlaceId, UniverseId = pin.UniverseId, Name = pin.Name, ThumbnailUrl = pin.ThumbnailUrl };
            added.Add(game); return game;
        }).ToList();
        FilterGames();
        _ = GameArtworkService.LoadAsync(added, _lifetime.Token);
    }
    private void FilterGames()
    {
        var filtered = _allGames.Where(game => string.IsNullOrWhiteSpace(SearchQuery) || game.Name.Contains(SearchQuery.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (!Games.SequenceEqual(filtered)) { Games.Clear(); foreach (var game in filtered) Games.Add(game); }
        OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyMessage));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; GameLaunchService.PinsChanged -= PinsChanged;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}
