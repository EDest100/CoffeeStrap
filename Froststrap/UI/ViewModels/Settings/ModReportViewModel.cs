// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using CommunityToolkit.Mvvm.Input;
using Froststrap.Services;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed partial class ModReportViewModel : NotifyPropertyChangedViewModel
{
    private ModApplicationReport? _report;
    private string _message = "No player file-mod report yet. Launch Roblox through CoffeeStrap, then refresh here.";
    private string _filter = "All events";
    public static IReadOnlyList<string> Filters { get; } = ["All events", "Needs attention", "Applied", "Current", "Restored", "Removed", "Skipped", "Shadowed", "Failed"];
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string Filter
    {
        get => _filter;
        set { if (Filters.Contains(value) && SetProperty(ref _filter, value)) RefreshEntries(); }
    }
    public IReadOnlyList<ModApplicationEvent> Entries { get; private set; } = [];
    public bool HasReport => _report != null;
    public bool IsEmpty => HasReport && Entries.Count == 0;
    public bool HasEntries => Entries.Count > 0;
    public string Coverage => _report == null ? "" : $"Showing {Entries.Count} of {_report.Entries.Count} retained events; {_report.Omitted} additional events omitted. Long values may be shortened. This records file preparation, not visible game results.";
    public ModReportViewModel() => Refresh();
    [RelayCommand] private void Refresh()
    {
        try
        {
            _report = ModApplicationReporter.Read();
            Message = _report == null ? "No player file-mod report yet. Launch Roblox through CoffeeStrap, then refresh here." :
                $"{_report.Outcome} · {_report.Version} · {_report.FinishedUtc.ToLocalTime():g}\n" + string.Join(" · ", ModApplicationReporter.Statuses.Where(status => _report.Counts.GetValueOrDefault(status) > 0).Select(status => status + ": " + _report.Counts[status]));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { _report = null; Message = "Could not read the last application report. Refresh after the next CoffeeStrap launch."; App.Logger.Warn(error, "Could not read the application report."); }
        RefreshEntries(); OnPropertyChanged(nameof(HasReport)); OnPropertyChanged(nameof(Coverage));
    }
    private void RefreshEntries()
    {
        Entries = _report?.Entries.Where(entry => Filter == "All events" || Filter == entry.Status ||
            Filter == "Needs attention" && entry.Status is "Failed" or "Skipped" or "Shadowed").ToArray() ?? [];
        OnPropertyChanged(nameof(Entries)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasEntries)); OnPropertyChanged(nameof(Coverage));
    }
}
