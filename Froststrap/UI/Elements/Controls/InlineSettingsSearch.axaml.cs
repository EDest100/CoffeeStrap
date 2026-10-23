// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Froststrap.UI.ViewModels;

namespace Froststrap.UI.Elements.Controls;

internal partial class InlineSettingsSearch : UserControl
{
    public event EventHandler? IndexRequested;
    public InlineSettingsSearch()
    {
        InitializeComponent();
        SearchField.GotFocus += (_, _) => { IndexRequested?.Invoke(this, EventArgs.Empty); (DataContext as SearchBarViewModel)?.RefreshSearchResults(); };
        SearchField.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && ResultsList.ItemCount > 0) { ResultsList.SelectedIndex = 0; ResultsList.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter) { (DataContext as SearchBarViewModel)?.RefreshSearchResults(); SelectResult(); e.Handled = true; }
            else if (e.Key == Key.Escape) { (DataContext as SearchBarViewModel)?.ClearSearchCommand.Execute(null); e.Handled = true; }
        };
        ResultsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SelectResult(); e.Handled = true; }
            else if (e.Key == Key.Escape) { (DataContext as SearchBarViewModel)?.Clear(); SearchField.Focus(); e.Handled = true; }
            else if (e.Key == Key.Up && ResultsList.SelectedIndex == 0) { SearchField.Focus(NavigationMethod.Tab); e.Handled = true; }
        };
        ResultsList.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            var row = (e.Source as Control)?.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() ?? e.Source as ListBoxItem;
            if (row?.DataContext is not SearchBarItem item) return;
            ResultsList.SelectedItem = item; SelectResult(); e.Handled = true;
        }, RoutingStrategies.Bubble, true);
    }
    internal void FocusSearch() { SearchField.Focus(NavigationMethod.Tab); SearchField.SelectAll(); }
    private void Clear_Click(object? sender, RoutedEventArgs e) { (DataContext as SearchBarViewModel)?.Clear(); SearchField.Focus(NavigationMethod.Tab); }
    private void Retry_Click(object? sender, RoutedEventArgs e) { IndexRequested?.Invoke(this, EventArgs.Empty); SearchField.Focus(NavigationMethod.Tab); }
    private void SelectResult()
    {
        if (DataContext is not SearchBarViewModel model) return;
        var item = ResultsList.SelectedItem as SearchBarItem ?? model.FilteredSearchResults.FirstOrDefault();
        if (item != null) model.SearchResultSelectedCommand.Execute(item);
    }
}
