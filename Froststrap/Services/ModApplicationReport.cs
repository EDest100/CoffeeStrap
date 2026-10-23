// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal sealed record ModApplicationEvent(string Path, string Status, string Source, string Detail);
internal sealed class ModApplicationReport
{
    public int Schema { get; set; } = 1;
    public string Target { get; set; } = "Player";
    public string Version { get; set; } = "Unknown";
    public DateTime StartedUtc { get; set; }
    public DateTime FinishedUtc { get; set; }
    public string Outcome { get; set; } = "Incomplete";
    public Dictionary<string, int> Counts { get; set; } = [];
    public List<ModApplicationEvent> Entries { get; set; } = [];
    public int Omitted { get; set; }
}

internal sealed class ModApplicationReporter(bool studio, string? version) : IDisposable
{
    internal const int MaximumEntries = 256;
    internal const int MaximumBytes = 1048576;
    internal static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal)
    { "Applied", "Current", "Restored", "Removed", "Skipped", "Shadowed", "Failed" };
    private readonly object _gate = new();
    private readonly ModApplicationReport _report = new() { Target = studio ? "Studio" : "Player", Version = Limit(version ?? "Unknown", 64), StartedUtc = DateTime.UtcNow };
    private bool _disposed;
    public bool HasFailures { get { lock (_gate) return _report.Counts.GetValueOrDefault("Failed") != 0; } }
    internal static string FileLocation(bool studio = false) => Path.Combine(Paths.Base, "Reports", "ModApplication-" + (studio ? "Studio" : "Player") + ".json");
    private static string Limit(string value, int maximum)
    {
        string text = new(value.Take(maximum).Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        return value.Length > maximum ? text[..^1] + "…" : text;
    }
    public void Record(string path, string status, string source, string detail)
    {
        if (!Statuses.Contains(status)) throw new ArgumentOutOfRangeException(nameof(status));
        lock (_gate)
        {
            _report.Counts[status] = _report.Counts.GetValueOrDefault(status) + 1;
            if (_report.Entries.Count < MaximumEntries) _report.Entries.Add(new(Limit(path, 384), status, Limit(source, 80), Limit(detail, 128)));
            else
            {
                if (status == "Failed")
                {
                    int replace = _report.Entries.FindLastIndex(entry => entry.Status != "Failed");
                    if (replace >= 0) _report.Entries[replace] = new(Limit(path, 384), status, Limit(source, 80), Limit(detail, 128));
                }
                _report.Omitted++;
            }
        }
    }
    public void Complete(bool success)
    {
        lock (_gate) _report.Outcome = success && _report.Counts.GetValueOrDefault("Failed") == 0 ? "Completed" : "Completed with errors";
    }
    public static ModApplicationReport? Read(bool studio = false)
    {
        string path = FileLocation(studio);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("The application report is too large.");
        var report = JsonSerializer.Deserialize<ModApplicationReport>(File.ReadAllText(path)) ?? throw new InvalidDataException("The application report is empty.");
        if (report.Schema != 1 || report.Target != (studio ? "Studio" : "Player") || report.Version == null || report.Version.Length > 64 ||
            report.Outcome is not ("Completed" or "Completed with errors" or "Incomplete") || report.StartedUtc == default || report.FinishedUtc == default ||
            report.Counts == null || report.Counts.Count > Statuses.Count || report.Counts.Any(pair => !Statuses.Contains(pair.Key) || pair.Value < 0) ||
            report.Entries == null || report.Entries.Count > MaximumEntries || report.Entries.Any(entry => entry == null || !Statuses.Contains(entry.Status) ||
                entry.Path == null || entry.Path.Length > 384 || entry.Source == null || entry.Source.Length > 80 || entry.Detail == null || entry.Detail.Length > 128) ||
            report.Omitted < 0 || report.Counts.Values.Sum(value => (long)value) != (long)report.Entries.Count + report.Omitted)
            throw new InvalidDataException("The application report has an unsupported format.");
        return report;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        string path = FileLocation(studio), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            lock (_gate)
            {
                _report.FinishedUtc = DateTime.UtcNow;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(_report));
                File.Move(temporary, path, true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { App.Logger.Warn(error, "Could not save the file-mod application report."); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { App.Logger.Warn(error, "Could not remove a temporary application report."); }
        }
    }
}
