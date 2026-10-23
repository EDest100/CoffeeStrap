// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal sealed class ReconnectState
{
    public long Place { get; private set; }
    private bool _joined, _eligible;
    private bool _publicSession = true;
    public bool Read(string line)
    {
        if (LastPlaceRecorder.ParsePlace(line) is { } place) { Place = place; _joined = false; _eligible = false; }
        if (line.Contains("[FLog::Network] serverId:", StringComparison.Ordinal)) _joined = Place > 0;
        if (line.Contains("[FLog::SingleSurfaceApp] leaveUGCGameInternal", StringComparison.Ordinal)) { _joined = false; _eligible = false; }
        if (line.Contains("UgcExperienceController: doTeleport:", StringComparison.Ordinal))
        {
            _joined = false; _eligible = false;
            var kind = Regex.Match(line, "JoinTypeId\"%3a(\\d+)%2c", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            _publicSession = kind.Success && kind.Groups[1].Value is "1" or "2" or "5" or "10";
        }
        if (line.Contains("[FLog::Network] Sending disconnect with reason:", StringComparison.Ordinal))
        {
            var reason = Regex.Match(line, @"reason: (\d+)\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            _eligible = reason.Success && reason.Groups[1].Value is "1" or "277";
        }
        if (!line.Contains("[FLog::Network] Time to disconnect replication data:", StringComparison.Ordinal)) return false;
        bool reconnect = _joined && _eligible && _publicSession;
        _joined = false; _eligible = false;
        return reconnect;
    }
}

internal sealed class RobloxSession(WatcherData watcher, HttpClient? client = null)
{
    private readonly ReconnectState _reconnect = new();
    private string _name = watcher.GameName ?? "Home";
    private long _place = watcher.PlaceId;
    private volatile bool _requested;
    public string Title => FormatTitle(Volatile.Read(ref _name));
    internal static string FormatTitle(string name) => "Roblox - " + name;
    internal static bool TryReserveReconnect(DateTime now)
    {
        if (now - App.State.Prop.AutoReconnectWindowUtc > TimeSpan.FromMinutes(5) || now < App.State.Prop.AutoReconnectWindowUtc)
        { App.State.Prop.AutoReconnectWindowUtc = now; App.State.Prop.AutoReconnectAttempts = 0; }
        if (App.State.Prop.AutoReconnectAttempts >= 3) return false;
        App.State.Prop.AutoReconnectAttempts++;
        App.State.SaveSetting(nameof(App.State.Prop.AutoReconnectWindowUtc)); App.State.SaveSetting(nameof(App.State.Prop.AutoReconnectAttempts));
        return true;
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "Every started observer task is awaited in the finally block before the window and scheduling target are disposed.")]
    public async Task ObserveAsync(CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var scheduling = SessionScheduling.ForPlayer(watcher.ProcessId);
        Integrations.WindowManipulation? window = null;
        Task log = Task.CompletedTask, title = Task.CompletedTask, names = Task.CompletedTask, background = Task.CompletedTask, upscaling = Task.CompletedTask;
        try
        {
            window = OperatingSystem.IsWindows() && App.Settings.Prop.ShowGameInTitle ? new Integrations.WindowManipulation(watcher.ProcessId) : null;
            title = window?.RunTitleAsync(() => Title, lifetime.Token) ?? Task.CompletedTask;
            names = window != null ? UpdateNamesAsync(lifetime.Token) : Task.CompletedTask;
            background = BackgroundMinimizeWatcher.RunForPlayerAsync(watcher.ProcessId, scheduling, lifetime.Token);
            upscaling = PlayerUpscaleWatcher.RunForPlayerAsync(watcher.ProcessId, lifetime.Token);
            log = LastPlaceRecorder.ObserveAsync(watcher.LogFile, lifetime.Token, ReadLine);
            await Task.WhenAll(log, Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            try { await lifetime.CancelAsync(); }
            finally
            {
                try { await Task.WhenAll(log, title, names, background, upscaling); } catch (OperationCanceledException) { }
                finally { window?.Dispose(); scheduling?.Dispose(); }
            }
        }
    }
    internal void ReadLine(string line)
    {
        if (line.Contains("[FLog::SingleSurfaceApp] leaveUGCGameInternal", StringComparison.Ordinal))
        { Volatile.Write(ref _place, 0); Volatile.Write(ref _name, "Home"); }
        if (LastPlaceRecorder.ParsePlace(line) is { } place && place != _place)
        {
            Volatile.Write(ref _place, place); _requested = false;
            Volatile.Write(ref _name, $"Place {place}");
        }
        if (_reconnect.Read(line) && App.Settings.Prop.AutoReconnect && watcher.PublicJoin && string.IsNullOrEmpty(watcher.AccessCode)) _requested = true;
    }
    public async Task<bool> ReconnectIfRequestedAsync(CancellationToken cancellation)
    {
        if (!_requested || !App.Settings.Prop.AutoReconnect || !TryReserveReconnect(DateTime.UtcNow)) return false;
        _requested = false;
        try
        {
            using var process = Process.GetProcessById(watcher.ProcessId);
            if (process.ProcessName != Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName) && process.ProcessName != "sober") return false;
            if (!process.HasExited)
            {
                if (!process.CloseMainWindow()) return false;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(deadline.Token);
            }
        }
        catch (ArgumentException) { }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { App.Logger.Warn("Auto Reconnect could not close the disconnected player."); return false; }
        cancellation.ThrowIfCancellationRequested();
        return true;
    }
    public void LaunchReconnect() => GameLaunchService.Default.Launch(_reconnect.Place, Volatile.Read(ref _name));
    private async Task UpdateNamesAsync(CancellationToken cancellation)
    {
        long previous = 0;
        int attempts = 0;
        DateTime retry = DateTime.MinValue;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                long place = Volatile.Read(ref _place);
                if (place != previous) { previous = place; attempts = 0; retry = DateTime.MinValue; }
                if (place > 0 && attempts < 3 && DateTime.UtcNow >= retry)
                {
                    attempts++;
                    if (await ResolveNameAsync(place, cancellation)) attempts = 3;
                    else retry = DateTime.UtcNow.AddSeconds(5 * attempts);
                }
            }
        }
        catch (OperationCanceledException) { }
    }
    internal async Task<bool> ResolveNameAsync(long place, CancellationToken cancellation)
    {
        try
        {
            using var response = await (client ?? App.HttpClient).GetAsync(UrlBuilder.BuildApiUrl("apis", $"universes/v1/places/{place}/universe"), cancellation);
            response.EnsureSuccessStatusCode();
            using var universe = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            long id = universe.RootElement.GetProperty("universeId").GetInt64();
            using var detailsResponse = await (client ?? App.HttpClient).GetAsync(UrlBuilder.BuildApiUrl("games", $"v1/games?universeIds={id}"), cancellation);
            detailsResponse.EnsureSuccessStatusCode();
            using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStringAsync(cancellation));
            string? name = details.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault().GetProperty("name").GetString();
            if (_place == place && !string.IsNullOrWhiteSpace(name) && !cancellation.IsCancellationRequested) { Volatile.Write(ref _name, name); GameLaunchService.RecordPlace(place, name); return true; }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        { App.Logger.Warn(error, "Game name unavailable for title."); }
        return false;
    }
}
