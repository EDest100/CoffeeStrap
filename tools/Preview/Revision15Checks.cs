// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Models;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision15()
    {
        VerifyContextSearch();
        VerifySearchVisibility();
        VerifySearchLifetime();
        VerifyIndexRetry();
        foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderContextSearch(theme, width);
    }
    private static void VerifyContextSearch()
    {
        using var model = new SearchBarViewModel();
        var water = new SearchBarItem { DisplayName = "Water normal maps", Description = "Reduce ripple texture detail", PageName = "Game Tweaks", PageTag = "fastflags", ParentSectionName = "Texture resolution budget" };
        var exact = new SearchBarItem { DisplayName = "Water", Description = "Default sky color", PageName = "Texture Editor", PageTag = "mods" };
        var aa = new SearchBarItem { DisplayName = "Anti-aliasing", Description = "Multisample preference", PageName = "Game Tweaks", PageTag = "fastflags" };
        var unicode = new SearchBarItem { DisplayName = "Éclat", Description = "Éclairage", PageName = "Settings" };
        model.SetSearchIndex([water, exact, aa, unicode, water]);
        void Query(string query) { model.SearchQuery = query; Await(model.PendingSearch); }
        Query("water ripple");
        Check(model.FilteredSearchResults.SequenceEqual(new[] { water }) && model.ResultsCountText == "1 result for water ripple", "context search matches all words across title and description with accurate counts");
        Query("game tweaks ripple"); Check(model.FilteredSearchResults.Single() == water, "settings page names participate in multi-word matching");
        Query("resolution water"); Check(model.FilteredSearchResults.Single() == water && water.DisplayContext == "Game Tweaks › Texture resolution budget", "parent sections are searchable and shown as result context");
        Query("water"); Check(model.FilteredSearchResults.First() == exact && model.FilteredSearchResults.Count == 2, "exact titles rank above longer titles and duplicate index entries are removed");
        Query("anti aliasing"); Check(model.FilteredSearchResults.Single() == aa, "punctuation normalization finds hyphenated setting names");
        Query("ÉCLAT"); Check(model.FilteredSearchResults.Single() == unicode, "context matching retains Unicode letters and ignores casing");
        Query("water missingword"); Check(model.FilteredSearchResults.Count == 0 && model.ShowNoResults && model.IsDropDownOpen, "unmatched terms retain an explicit no-results popup instead of silently dismissing it");
        Query("***"); Check(!model.HasResults && model.ShowNoResults, "punctuation-only queries cannot accidentally match every setting");
        Query("water water"); Check(model.FilteredSearchResults.Count == 2, "duplicate query words do not duplicate results");
        model.AddSearchItems([new SearchBarItem { DisplayName = "Foam", Description = "Water ripple", PageName = "Game Tweaks" }]);
        Query("ripple"); Check(model.FilteredSearchResults.Count == 2 && model.ResultsCountText.StartsWith("2 results", StringComparison.Ordinal), "incremental page indexing refreshes contextual documents and counts");
        int selected = 0; model.SearchResultSelected += (_, _) => selected++;
        model.SearchResultSelectedCommand.Execute(new SearchBarItem { DisplayName = "foreign" });
        Check(selected == 0, "unindexed search targets cannot trigger navigation");
        model.SearchResultSelectedCommand.Execute(water);
        Check(selected == 1 && !model.HasQuery && !model.IsDropDownOpen, "known result selection clears the query and emits one navigation event");
        model.SetSearchIndex(Enumerable.Range(0, 5000).Select(index => new SearchBarItem { DisplayName = "Water " + index.ToString("D4"), Description = "Ripple texture detail", PageName = "Game Tweaks" }).ToList());
        Query("water");
        Check(model.GetSearchIndex().Count == 4096 && model.FilteredSearchResults.Count == 100 && model.ResultsCountText == "Showing 100 of 4096 results", "search bounds its index and rendered result list while preserving the full match count");
        var elapsed = new List<double>();
        for (int i = 0; i < 45; i++)
        {
            model.SearchQuery = i % 2 == 0 ? "water ripple" : "water detail";
            var timer = Stopwatch.StartNew(); model.RefreshSearchResults(); timer.Stop(); if (i >= 5) elapsed.Add(timer.Elapsed.TotalMilliseconds);
        }
        elapsed.Sort();
        File.WriteAllText(Path.Combine(_output, "settings-search-probe.json"), JsonSerializer.Serialize(new { workload = "4096 cached documents; standalone view model; 5 warmups + 40 queries", medianElapsedMs = elapsed[elapsed.Count / 2], p95ElapsedMs = elapsed[(int)Math.Ceiling(elapsed.Count * .95) - 1], maximumElapsedMs = elapsed[^1], maxRenderedResults = 100, gameTested = false }, new JsonSerializerOptions { WriteIndented = true }));
        Query(new string('w', 1000)); Check(model.SearchQuery.Length == 256, "programmatic and pasted search input is bounded to 256 characters");
        model.Clear(); Check(!model.HasQuery && !model.IsDropDownOpen && !model.HasResults, "clear resets the query, results and popup together");
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++) { model.SearchQuery = "rapid " + i; tasks.Add(model.PendingSearch); }
        model.Clear(); Await(Task.WhenAll(tasks));
        Check(tasks.All(task => task.IsCompletedSuccessfully) && !model.HasResults && !model.IsDropDownOpen, "rapid replaced searches cancel cleanly without stale results or faulted debounce tasks");
    }
    private static void VerifySearchLifetime()
    {
        var model = new SearchBarViewModel(); model.SetSearchIndex([new SearchBarItem { DisplayName = "Water" }]); model.SearchQuery = "water";
        Task pending = model.PendingSearch; model.Dispose(); int notifications = 0; model.PropertyChanged += (_, _) => notifications++;
        Await(pending); model.SearchQuery = "new"; model.RefreshSearchResults();
        Check(pending.IsCompletedSuccessfully && notifications == 0, "disposed search models stop pending work and ignore later queries");
        var window = new SettingsWindow(false) { Width = 900, Height = 800 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!;
        window.StartIndexingAllPagesIfNeeded(); Task indexing = window.SearchIndexingTask; int count = owner.SearchBar.GetSearchIndex().Count;
        owner.SaveSettings(); window.Close(); Await(indexing); var stage = window.FindControl<Border>("OffscreenIndexingCanvas")!;
        Check(indexing.IsCompletedSuccessfully && !stage.IsVisible && stage.Child == null && !owner.SearchBar.IsIndexing && owner.SearchBar.GetSearchIndex().Count == count,
            "closing a window cancels offscreen indexing, clears staged views and does not publish later entries", new { indexing.Status, stage.IsVisible, child = stage.Child?.GetType().Name, owner.SearchBar.IsIndexing, before = count, after = owner.SearchBar.GetSearchIndex().Count, windowVisible = window.IsVisible });
        window.StartIndexingAllPagesIfNeeded(); Check(ReferenceEquals(indexing, window.SearchIndexingTask), "a closed settings window cannot restart search indexing");
    }
    private static void VerifySearchVisibility()
    {
        var visible = new OptionControl { Header = "Shared name", Description = "Visible setting", InnerContent = new TextBox() };
        var hidden = new Border { IsVisible = false, Child = new OptionControl { Header = "Shared name", Description = "Hidden setting", InnerContent = new TextBox() } };
        var action = new CardAction { Header = "Open detail", Description = "Real action header" };
        var root = new StackPanel { Children = { visible, hidden, action } }; var window = new Window { Width = 700, Height = 400, Content = root }; window.Show(); RunFor(70);
        var index = new Froststrap.UI.Utility.SearchIndexBuilder().ScanRenderedPageForElements(root, "fixture");
        Check(index.Count(item => item.DisplayName == "Shared name") == 1 && index.Single(item => item.DisplayName == "Shared name").Description == "Visible setting", "hidden ancestor filtering preserves a visible control with the same title");
        Check(index.Any(item => item.DisplayName == "Open detail" && item.Category == "Action" && item.Description == "Real action header"), "action indexing uses the actual header and description");
        window.Close();
    }
    private static void VerifyIndexRetry()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var window = new SettingsWindow(false) { Width = 760, Height = 900 }; window.Show(); RunFor(60);
            var owner = (MainWindowViewModel)window.DataContext!; var stage = window.FindControl<Border>("OffscreenIndexingCanvas")!;
            void Block(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == Border.ChildProperty && stage.Child != null) throw new InvalidOperationException("Isolated staging failure fixture."); }
            stage.PropertyChanged += Block;
            window.StartIndexingAllPagesIfNeeded(); Await(window.SearchIndexingTask); stage.PropertyChanged -= Block;
            Check(owner.SearchBar.IndexingIncomplete && !stage.IsVisible && stage.Child == null, "failed indexing clears staged content and exposes a retry state: " + theme);
            owner.SearchBar.SearchQuery = "ripple"; Await(owner.SearchBar.PendingSearch); RunFor(60);
            var search = window.FindControl<InlineSettingsSearch>("SettingsSearch")!;
            CapturePopup(search, theme, "revision15-search-index-error");
            ClickSearchPopup(search.FindControl<Button>("RetryButton")!); Await(window.SearchIndexingTask);
            Check(!owner.SearchBar.IndexingIncomplete && owner.SearchBar.GetSearchIndex().Any(item => item.DisplayName == "Water normal maps") && !stage.IsVisible && stage.Child == null,
                "actual Retry button rebuilds missing pages after an indexing failure: " + theme);
            owner.SearchBar.Clear(); owner.SaveSettings(); window.Close();
        }
    }
    private static void RenderContextSearch(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(70);
        var owner = (MainWindowViewModel)window.DataContext!;
        var search = window.FindControl<InlineSettingsSearch>("SettingsSearch")!;
        window.KeyPress(Key.K, RawInputModifiers.Control, default, null); window.KeyRelease(Key.K, RawInputModifiers.Control, default, null); RunFor(40);
        Check(search.FindControl<TextBox>("SearchField")!.IsFocused, "Ctrl+K focuses the real slim settings search: " + theme + " " + width);
        Await(window.SearchIndexingTask);
        var items = owner.SearchBar.GetSearchIndex();
        Check(items.Any(item => item.DisplayName == "Water normal maps" && item.ParentSectionName == "Texture resolution budget") && items.Where(item => item.Category == "Setting").All(item => !string.IsNullOrWhiteSpace(item.Tag)),
            "production indexing includes contextual tweak rows with nonempty navigation tags: " + theme + " " + width);
        owner.SearchBar.SearchQuery = "water ripple"; Await(owner.SearchBar.PendingSearch); RunFor(70);
        Check(owner.SearchBar.FilteredSearchResults.Any(item => item.DisplayName == "Water normal maps") && owner.SearchBar.ResultsCountText.Contains("results", StringComparison.Ordinal), "real search finds description text on unvisited Game Tweaks: " + theme + " " + width);
        CapturePopup(search, theme, "revision15-context-search-" + width);
        if (width == 760) { window.SetRenderScaling(1.5); RunFor(80); CapturePopup(search, theme, "revision15-context-search-150"); window.SetRenderScaling(1); }
        owner.SearchBar.SearchQuery = "unavailable setting xyz"; Await(owner.SearchBar.PendingSearch); RunFor(50);
        CapturePopup(search, theme, "revision15-no-search-results-" + width);
        Check(owner.SearchBar.ShowNoResults && search.GetVisualDescendants().OfType<Popup>().Single().IsOpen, "actual popup exposes a visible no-results state: " + theme + " " + width);
        ClickSearchPopup(search.FindControl<IconButton>("PopupClearButton")!); RunFor(40);
        Check(!owner.SearchBar.HasQuery && !owner.SearchBar.IsDropDownOpen, "clear icon resets the actual search field: " + theme + " " + width);
        owner.SearchBar.SearchQuery = "resolution ripple"; Await(owner.SearchBar.PendingSearch); RunFor(40);
        var field = search.FindControl<TextBox>("SearchField")!; field.Focus();
        window.KeyPress(Key.Down, RawInputModifiers.None, default, null); window.KeyRelease(Key.Down, RawInputModifiers.None, default, null);
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(250);
        Check(owner.SelectedPage == "fastflags" && window.LastSearchFocusTarget is ComboBox && window.LastSearchFocusTarget.GetVisualAncestors().OfType<OptionControl>().Any(option => option.Header == "Water normal maps"),
            "arrow/Enter selection reveals the correct collapsed group and real texture control: " + theme + " " + width);
        var section = owner.SearchBar.GetSearchIndex().Single(item => item.Category == "Section" && item.DisplayName == "Texture resolution budget");
        var group = window.GetVisualDescendants().OfType<CardExpander>().Single(control => Equals(control.Header, section.DisplayName)); group.IsExpanded = false; RunFor(50);
        owner.SearchBar.SearchResultSelectedCommand.Execute(section); RunFor(150);
        Check(group.IsExpanded, "selecting a section opens that section itself: " + theme + " " + width);
        var duplicate = owner.SearchBar.GetSearchIndex().Single(item => item.DisplayName == "Texture quality" && item.ParentSectionName == "Roblox graphics");
        owner.SearchBar.SearchResultSelectedCommand.Execute(duplicate); RunFor(180);
        Check(window.LastSearchFocusTarget?.GetVisualAncestors().OfType<CardExpander>().Any(expander => Equals(expander.Header, "Roblox graphics")) == true,
            "duplicate titles reveal the selected section's actual control: " + theme + " " + width);
        Capture(window, theme, "revision15-revealed-texture-control-" + width);
        owner.SearchBar.SearchQuery = "resolution ripple"; Await(owner.SearchBar.PendingSearch); RunFor(50);
        var row = search.FindControl<ListBox>("ResultsList")!.GetVisualDescendants().OfType<ListBoxItem>().First(); int picked = 0;
        owner.SearchBar.SearchResultSelected += (_, _) => picked++;
        ClickSearchPopup(row); RunFor(150);
        Check(picked == 1 && !owner.SearchBar.HasQuery && owner.SelectedPage == "fastflags", "pointer selection activates one result without a nested button: " + theme + " " + width);
        field.Focus(); owner.SearchBar.SearchQuery = "resolution ripple";
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(150);
        Check(!owner.SearchBar.HasQuery && window.LastSearchFocusTarget?.GetVisualAncestors().OfType<OptionControl>().Any(option => option.Header == "Water normal maps") == true,
            "immediate Enter searches the current text without waiting on debounce: " + theme + " " + width);
        owner.SaveSettings(); window.Close();
    }
    private static void ClickSearchPopup(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!; var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point, RawInputModifiers.None); root.MouseDown(point, MouseButton.Left, RawInputModifiers.None); root.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Pump();
    }
}
