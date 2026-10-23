// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LucideAvalonia.Enum;
using System.Collections.ObjectModel;

namespace Froststrap.UI.ViewModels
{
    internal class SearchResultEntry
    {
        public required SearchBarItem Item { get; init; }
        public required bool IsLastInGroup { get; init; }

        public string Connector => IsLastInGroup ? "└" : "├";
        public string DisplayName => Item.DisplayName;
        public string Description => Item.Description ?? string.Empty;
        public bool HasDescription => !string.IsNullOrEmpty(Item.Description);
    }

    internal class SearchResultGroup
    {
        public string PageName { get; init; } = string.Empty;
        public LucideIconNames IconSymbol { get; init; }
        public ObservableCollection<SearchResultEntry> Items { get; init; } = [];
    }

    internal partial class SearchBarViewModel : ObservableObject, IDisposable
    {
        private string _searchQuery = string.Empty;
        private CancellationTokenSource? _debounceCts;
        private bool _isDropDownOpen;
        private bool _disposed;
        private bool _isIndexing;
        private bool _isSearching;
        private bool _indexingIncomplete;
        public bool IndexingIncomplete { get => _indexingIncomplete; set => SetProperty(ref _indexingIncomplete, value); }
        private int _totalResults;
        private Task _debounceTask = Task.CompletedTask;
        internal Task PendingSearch => _debounceTask;
        private sealed record SearchDocument(SearchBarItem Item, string Title, string Description, string Context);
        private List<SearchDocument> _documents = [];
        internal const int MaximumResults = 100;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                value = (value ?? string.Empty)[..Math.Min(value?.Length ?? 0, 256)];
                if (!_disposed && SetProperty(ref _searchQuery, value))
                {
                    CancelPending();
                    _isSearching = HasQuery;
                    _totalResults = 0;
                    FilteredSearchResults = [];
                    GroupedSearchResults = [];
                    IsDropDownOpen = HasQuery;
                    OnPropertyChanged(nameof(HasQuery));
                    NotifyResults();
                    if (HasQuery)
                    {
                        _debounceCts = new CancellationTokenSource();
                        _debounceTask = DebounceAsync(_debounceCts.Token);
                    }
                }
            }
        }
        private async Task DebounceAsync(CancellationToken cancellation)
        {
            try
            {
                await Task.Delay(150, cancellation);
                await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed && !cancellation.IsCancellationRequested) FilterSearchResults(); });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        private void CancelPending()
        { _debounceCts?.Cancel(); _debounceCts?.Dispose(); _debounceCts = null; }

        public bool IsDropDownOpen
        {
            get => _isDropDownOpen;
            set => SetProperty(ref _isDropDownOpen, value);
        }

        public bool IsIndexing
        {
            get => _isIndexing;
            set
            {
                if (SetProperty(ref _isIndexing, value))
                {
                    OnPropertyChanged(nameof(ShowEmptyState));
                    OnPropertyChanged(nameof(ShowNoResults));
                    OnPropertyChanged(nameof(ShowResults));
                    OnPropertyChanged(nameof(ResultsCountText));
                }
            }
        }

        private ObservableCollection<SearchBarItem> _filteredSearchResults = [];
        public ObservableCollection<SearchBarItem> FilteredSearchResults
        {
            get => _filteredSearchResults;
            private set
            {
                SetProperty(ref _filteredSearchResults, value);
            }
        }

        private ObservableCollection<SearchResultGroup> _groupedSearchResults = [];
        public ObservableCollection<SearchResultGroup> GroupedSearchResults
        {
            get => _groupedSearchResults;
            private set
            {
                SetProperty(ref _groupedSearchResults, value);
                OnPropertyChanged(nameof(HasResults));
                OnPropertyChanged(nameof(ResultsCountText));
                OnPropertyChanged(nameof(ShowNoResults));
                OnPropertyChanged(nameof(ShowResults));
            }
        }

        public bool HasResults => GroupedSearchResults.Count > 0;
        public bool HasQuery => !string.IsNullOrWhiteSpace(SearchQuery);
        public bool ShowEmptyState => !HasQuery && !IsIndexing;
        public bool ShowNoResults => HasQuery && !HasResults && !IsIndexing && !_isSearching;
        public bool ShowResults => HasResults;

        public string ResultsCountText
        {
            get
            {
                if (!HasQuery) return string.Empty;

                if (IsIndexing) return "Indexing settings…";
                if (_isSearching) return "Searching settings…";
                string word = _totalResults == 1 ? "result" : "results";
                return _totalResults > MaximumResults ? $"Showing {MaximumResults} of {_totalResults} {word}" : $"{_totalResults} {word} for {SearchQuery.Trim()}";
            }
        }

        private List<SearchBarItem> _searchIndex = [];

        public IRelayCommand<SearchBarItem> SearchResultSelectedCommand { get; }
        public IRelayCommand ClearSearchCommand { get; }

        public event EventHandler<SearchBarItem>? SearchResultSelected;

        public SearchBarViewModel()
        {
            SearchResultSelectedCommand = new RelayCommand<SearchBarItem>(HandleSearchResultSelected);
            ClearSearchCommand = new RelayCommand(Clear);
        }

        public void SetSearchIndex(List<SearchBarItem> searchIndex)
        {
            if (_disposed) return;
            _searchIndex = (searchIndex ?? []).DistinctBy(item => (item.PageTag, item.DisplayName, item.Category, item.ParentSectionName)).Take(4096).ToList();
            _documents = _searchIndex.Select(item => new SearchDocument(item, Normalize(item.DisplayName, 256), Normalize(item.Description, 1024), Normalize(item.PageName + " " + item.ParentSectionName, 512))).ToList();
            FilterSearchResults();
        }
        public void AddSearchItems(IEnumerable<SearchBarItem> items) => SetSearchIndex([.. _searchIndex, .. items]);

        public List<SearchBarItem> GetSearchIndex() => _searchIndex;

        public void RefreshSearchResults() => FilterSearchResults();

        private void FilterSearchResults()
        {
            if (_disposed) return;
            CancelPending(); _isSearching = false;
            var query = SearchQuery;

            if (string.IsNullOrWhiteSpace(query))
            {
                _totalResults = 0; FilteredSearchResults = []; GroupedSearchResults = []; IsDropDownOpen = false; NotifyResults();
                return;
            }

            string phrase = Normalize(query, 256).Trim();
            var terms = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(16).ToArray();
            var matches = _documents.Select(document => (document.Item, Score: Score(document, phrase, terms)))
                .Where(match => match.Score > 0).OrderByDescending(match => match.Score).ThenBy(match => match.Item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            _totalResults = matches.Count;
            var filtered = matches.Take(MaximumResults).Select(match => match.Item).ToList();

            var groups = filtered
                .GroupBy(item => item.PageName ?? "Other")
                .Select(g =>
                {
                    var items = g.ToList();
                    return new SearchResultGroup
                    {
                        PageName = g.Key,
                        IconSymbol = g.First().IconSymbol ?? LucideIconNames.CircleQuestionMark,
                        Items = new ObservableCollection<SearchResultEntry>(
                            items.Select((item, i) => new SearchResultEntry
                            {
                                Item = item,
                                IsLastInGroup = i == items.Count - 1
                            }))
                    };
                })
                .ToList();

            FilteredSearchResults = new ObservableCollection<SearchBarItem>(filtered);
            GroupedSearchResults = new ObservableCollection<SearchResultGroup>(groups);
            IsDropDownOpen = true;
            NotifyResults();
        }
        private void NotifyResults()
        { OnPropertyChanged(nameof(ResultsCountText)); OnPropertyChanged(nameof(ShowEmptyState)); OnPropertyChanged(nameof(ShowNoResults)); OnPropertyChanged(nameof(ShowResults)); }
        private static string Normalize(string? value, int maximum)
        {
            value ??= string.Empty; value = value[..Math.Min(value.Length, maximum)];
            return string.Create(value.Length, value, static (result, source) => { for (int i = 0; i < source.Length; i++) result[i] = char.IsLetterOrDigit(source[i]) ? char.ToLowerInvariant(source[i]) : ' '; });
        }
        private static int Score(SearchDocument document, string phrase, string[] terms)
        {
            if (terms.Length == 0) return 0;
            int score = 0;
            foreach (string term in terms)
            {
                int part = document.Title.Contains(term, StringComparison.Ordinal) ? 8 : document.Context.Contains(term, StringComparison.Ordinal) ? 4 : document.Description.Contains(term, StringComparison.Ordinal) ? 1 : 0;
                if (part == 0) return 0; score += part;
            }
            return score + (document.Title == phrase ? 50 : document.Title.StartsWith(phrase, StringComparison.Ordinal) ? 20 : document.Title.Contains(phrase, StringComparison.Ordinal) ? 10 : 0);
        }

        private void HandleSearchResultSelected(SearchBarItem? item)
        {
            if (item == null) return;
            if (_disposed || !_searchIndex.Contains(item)) return;
            SearchQuery = string.Empty;
            IsDropDownOpen = false;
            SearchResultSelected?.Invoke(this, item);
        }

        public void Clear()
        {
            if (_disposed) return;
            SearchQuery = string.Empty;
            FilterSearchResults();
        }

        #region IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                CancelPending();
            }
            _disposed = true;
        }
        #endregion
    }
}
