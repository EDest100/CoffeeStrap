#pragma warning disable CA1822 // Instance accessors are required for existing serialized/bound contracts.
// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using CommunityToolkit.Mvvm.Input;
using Froststrap.Models;
using Froststrap.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed class HomeViewModel : NotifyPropertyChangedViewModel, IDisposable
{
    public QuickPlayViewModel Games { get; } = new();
    public ObservableCollection<QuickPlayGameItem> ContinueGames { get; } = [];
    public ObservableCollection<QuickPlayGameItem> RecommendedGames { get; } = [];
    private readonly PinnedGamesViewModel _pins = new();
    public ObservableCollection<QuickPlayGameItem> PinnedGames => _pins.Games;
    private readonly CancellationTokenSource _lifetime = new();
    private ObservableCollection<QuickPlayGameItem>? _recentSource, _recommendedSource;
    private string _searchQuery = "";
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) RefreshGames(); } }
    public bool ContinueEmpty => ContinueGames.Count == 0;
    public bool RecommendedEmpty => RecommendedGames.Count == 0;
    public string ContinueMessage => Games.IsLoading ? "Loading your games…" : !string.IsNullOrWhiteSpace(SearchQuery) ? "No Continue games match your search."
        : Games.IsLoggedIn ? "Your Continue list is empty or unavailable." : "Sign in to load your Roblox Continue list.";
    public string RecommendedMessage => Games.IsRecommendedLoading ? "Loading recommendations…" : !string.IsNullOrWhiteSpace(SearchQuery) ? "No recommendations match your search."
        : Games.IsLoggedIn ? "Recommendations are unavailable." : "Sign in to load your Roblox recommendations.";
    public ICommand PlayGameCommand { get; }
    public string VersionLabel => "CoffeeStrap " + App.Version;
    public string LastGame => App.State.Prop.LastGameName ?? "No game launched yet";
    public bool CanReconnect => App.State.Prop.LastPlaceId > 0;
    public string ReconnectHint => "Open the last place launched through CoffeeStrap. Roblox selects the server; the previous instance is not guaranteed.";
    public ObservableCollection<QuickLaunchPin> Pins { get; } = new(App.State.Prop.QuickLaunchPins.Where(p => p.PlaceId > 0).DistinctBy(p => p.PlaceId));
    public bool HasPins => PinnedGames.Count > 0;
    public ObservableCollection<string> Regions { get; } = ["Auto"];
    private string _regionHint = "Enable RoValra for server regions. Auto uses Roblox matchmaking.";
    public string RegionHint { get => _regionHint; private set => SetProperty(ref _regionHint, value); }
    public string PreferredRegion
    {
        get => App.Settings.Prop.PreferredRegion;
        set { if (!_updatingRegionChoices && value != null && Regions.Contains(value)) { App.Settings.Prop.PreferredRegion = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.PreferredRegion)); OnPropertyChanged(); } }
    }
    public bool EnableRoValra
    {
        get => App.Settings.Prop.EnableRoValra;
        set
        {
            App.Settings.Prop.EnableRoValra = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.EnableRoValra));
            OnPropertyChanged(); OnPropertyChanged(nameof(PreferredRegion));
            _ = LoadRegionsAsync();
        }
    }
    public bool AutoReconnect
    {
        get => App.Settings.Prop.AutoReconnect;
        set { App.Settings.Prop.AutoReconnect = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.AutoReconnect)); OnPropertyChanged(); }
    }
    public IAsyncRelayCommand RefreshRegionsCommand { get; }
    private int _regionGeneration;
    private bool _updatingRegionChoices;
    private CancellationTokenSource? _regionRequest;
    private async Task LoadRegionsAsync()
    {
        int generation = ++_regionGeneration;
        var previous = _regionRequest; _regionRequest = null;
        if (previous != null) { await previous.CancelAsync(); previous.Dispose(); }
        if (generation != _regionGeneration) return;
        if (!EnableRoValra)
        {
            _updatingRegionChoices = true;
            try { ResetRegionChoices([]); } finally { _updatingRegionChoices = false; }
            RegionHint = "Enable RoValra for server regions. Auto uses Roblox matchmaking."; return;
        }
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); request.CancelAfter(TimeSpan.FromSeconds(15)); _regionRequest = request;
        RegionHint = "Loading server regions…";
        try
        {
            using var fetcher = new Froststrap.Integrations.RobloxServerFetcher();
            var regions = await fetcher.GetPreferredRegionsAsync(request.Token);
            if (generation != _regionGeneration || _lifetime.IsCancellationRequested) return;
            _updatingRegionChoices = true;
            try { ResetRegionChoices(regions); }
            finally { _updatingRegionChoices = false; }
            OnPropertyChanged(nameof(PreferredRegion));
            RegionHint = "Choose the fullest non-full public server in your region. Explicit friend, private and instance joins keep their destination.";
        }
        catch (OperationCanceledException) { if (generation == _regionGeneration && !_lifetime.IsCancellationRequested && EnableRoValra) RegionHint = "Region lookup timed out. Refresh regions to retry."; }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException or InvalidOperationException)
        { if (generation == _regionGeneration) RegionHint = "Regions unavailable. Retry: " + error.Message; }
    }
    public bool HasUpdate => CoffeeReleasePolicy.CanShow(System.Version.TryParse(App.Version.Split('+')[0], out var version) ? version : new Version(), CoffeeReleasePolicy.KnownRelease, CoffeeReleasePolicy.VerifiedDestination);
    public string? UpdateUrl => HasUpdate ? CoffeeReleasePolicy.VerifiedDestination?.AbsoluteUri : null;
    public ICommand ReconnectCommand { get; }
    public ICommand LaunchPinCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand QuickPlayCommand { get; }

    public HomeViewModel() : this(GameLaunchService.Default, null, null, null) { }
    public HomeViewModel(GameLaunchService launches, Action? launch, Action? settings, Action? quickPlay)
    {
        ResetRegionChoices([]);
        SeedDefaultPin();
        RefreshRegionsCommand = new AsyncRelayCommand(LoadRegionsAsync);
        if (EnableRoValra) _ = LoadRegionsAsync();
        _pins.PropertyChanged += PinnedGamesChanged;
        PlayGameCommand = new RelayCommand<QuickPlayGameItem>(game => { if (game != null) launches.Launch(game.PlaceId, game.Name); });
        Games.PropertyChanged += GamesChanged;
        RefreshGames();
        _ = Games.LoadHomeRecommendationsAsync();
        ReconnectCommand = new RelayCommand(() => launches.Launch(App.State.Prop.LastPlaceId, LastGame), () => CanReconnect);
        LaunchPinCommand = new RelayCommand<QuickLaunchPin>(pin => { if (pin != null) launches.Launch(pin.PlaceId, pin.Name); });
        LaunchCommand = new RelayCommand(() => launch?.Invoke());
        SettingsCommand = new RelayCommand(() => settings?.Invoke());
        QuickPlayCommand = new RelayCommand(() => quickPlay?.Invoke());
    }
    private void ResetRegionChoices(IEnumerable<string> regions)
    {
        string selected = App.Settings.Prop.PreferredRegion;
        Regions.Clear(); Regions.Add("Auto");
        foreach (string region in regions.Where(region => region != "Auto").Distinct(StringComparer.OrdinalIgnoreCase)) Regions.Add(region);
        if (!string.IsNullOrWhiteSpace(selected) && !Regions.Contains(selected)) Regions.Add(selected);
    }
    internal static void SeedDefaultPin() => PinnedGamesViewModel.SeedDefaultPin();
    private void PinnedGamesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(HasPins));
    private void GamesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { RefreshGames(); if (e.PropertyName == nameof(QuickPlayViewModel.IsLoggedIn)) _ = Games.LoadHomeRecommendationsAsync(); }
    private void GamesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshGames();
    private void RefreshGames()
    {
        if (!ReferenceEquals(_recentSource, Games.RecentGames))
        { if (_recentSource != null) _recentSource.CollectionChanged -= GamesCollectionChanged; _recentSource = Games.RecentGames; _recentSource.CollectionChanged += GamesCollectionChanged; }
        if (!ReferenceEquals(_recommendedSource, Games.RecommendedGames))
        { if (_recommendedSource != null) _recommendedSource.CollectionChanged -= GamesCollectionChanged; _recommendedSource = Games.RecommendedGames; _recommendedSource.CollectionChanged += GamesCollectionChanged; }
        void Fill(ObservableCollection<QuickPlayGameItem> target, IEnumerable<QuickPlayGameItem> source)
        {
            var filtered = source.Where(game => string.IsNullOrWhiteSpace(SearchQuery) || game.Name.Contains(SearchQuery.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (target.SequenceEqual(filtered)) return;
            target.Clear(); foreach (var game in filtered) target.Add(game);
        }
        Fill(ContinueGames, Games.RecentGames); Fill(RecommendedGames, Games.RecommendedGames);
        OnPropertyChanged(nameof(ContinueEmpty)); OnPropertyChanged(nameof(RecommendedEmpty));
        OnPropertyChanged(nameof(ContinueMessage)); OnPropertyChanged(nameof(RecommendedMessage));
    }
    public void Dispose()
    {
        _lifetime.Cancel(); _regionRequest?.Cancel(); _regionRequest?.Dispose(); _lifetime.Dispose(); Games.PropertyChanged -= GamesChanged; _pins.PropertyChanged -= PinnedGamesChanged; _pins.Dispose();
        if (_recentSource != null) _recentSource.CollectionChanged -= GamesCollectionChanged;
        if (_recommendedSource != null) _recommendedSource.CollectionChanged -= GamesCollectionChanged;
        Games.Dispose();
    }
    public void RefreshLastPlace()
    {
        try
        {
            if (!File.Exists(App.State.FileLocation)) return;
            using var state = JsonDocument.Parse(File.ReadAllText(App.State.FileLocation));
            if (state.RootElement.TryGetProperty(nameof(App.State.Prop.LastPlaceId), out var place) && place.TryGetInt64(out long id) && id > 0)
            {
                App.State.Prop.LastPlaceId = id;
                if (state.RootElement.TryGetProperty(nameof(App.State.Prop.LastGameName), out var name)) App.State.Prop.LastGameName = name.GetString();
                OnPropertyChanged(nameof(LastGame)); OnPropertyChanged(nameof(CanReconnect));
                ((IRelayCommand)ReconnectCommand).NotifyCanExecuteChanged();
            }
        }
        catch (IOException) { }
        catch (JsonException) { } // A concurrent writer will be read on the next activation.
    }
}
