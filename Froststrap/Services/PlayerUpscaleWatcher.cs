// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Froststrap.Services;

internal readonly record struct AutomaticUpscaleOptions(bool Enabled, int ResolutionPercent);

internal interface IAutomaticUpscaleSession : IDisposable
{
    Task Completion { get; }
    string Status { get; }
    UpscalerStopReason StopReason { get; }
    void Stop();
}

internal sealed class AutomaticUpscaleCoordinator(
    Func<AutomaticUpscaleOptions> readOptions,
    Func<RunningPlayerWindow?> readyWindow,
    Func<RunningPlayerWindow, int, CancellationToken, Task<IAutomaticUpscaleSession>> start,
    Action<string> report)
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private CancellationTokenSource? _starting;
    private int _settingsDirty = 1;
    private string? _lastStatus;
    internal void Pulse() => _wake.Writer.TryWrite(true);
    internal void OptionsChanged()
    {
        Interlocked.Exchange(ref _settingsDirty, 1);
        try { Volatile.Read(ref _starting)?.Cancel(); } catch (ObjectDisposedException) { }
        Pulse();
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "A successful startup transfers its linked cancellation source to sessionCancellation; FinishAsync awaits both the session and completion observer before disposing it. Failed startup awaits native cleanup before returning.")]
    internal async Task RunAsync(CancellationToken cancellation)
    {
        AutomaticUpscaleOptions? options = null;
        string? settingsError = null;
        IAutomaticUpscaleSession? session = null;
        CancellationTokenSource? sessionCancellation = null;
        Task completionNotice = Task.CompletedTask;
        RunningPlayerWindow? sessionWindow = null;
        int sessionPercent = 0, revision = 0, sessionRevision = 0;
        (nint Window, int Revision)? blocked = null;
        Pulse();
        try
        {
            while (true)
            {
                await _wake.Reader.ReadAsync(cancellation).ConfigureAwait(false);
                if (session?.Completion.IsCompleted == true)
                {
                    var reason = session.StopReason;
                    string status = session.Status;
                    await FinishAsync(false).ConfigureAwait(false);
                    if (reason is UpscalerStopReason.FocusLost or UpscalerStopReason.DisplayChanged) blocked = null;
                    else if (sessionWindow != null) blocked = (sessionWindow.Handle, sessionRevision);
                    Publish(status + (reason == UpscalerStopReason.FocusLost ? " Scaling resumes when this player is active again."
                        : reason is UpscalerStopReason.EmergencyHotkey or UpscalerStopReason.WindowChanged or UpscalerStopReason.Error or UpscalerStopReason.Requested
                            ? " Toggle upscaling off and on, change its scale, or open a new player window to resume." : ""));
                }
                if (Interlocked.Exchange(ref _settingsDirty, 0) != 0)
                {
                    try
                    {
                        var saved = readOptions();
                        settingsError = null;
                        if (options != saved) { options = saved; revision++; blocked = null; }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                    {
                        options = null; revision++; blocked = null;
                        await FinishAsync(true).ConfigureAwait(false);
                        settingsError = "Automatic upscaling paused: saved settings could not be read. Save the upscaler preference again to retry. " + error.Message;
                        Publish(settingsError);
                        continue;
                    }
                }
                if (options is not { Enabled: true } selected)
                {
                    await FinishAsync(true).ConfigureAwait(false);
                    Publish(settingsError ?? "Upscaler is off. The owned player window and cursor are restored when unchanged.");
                    continue;
                }
                if (session != null && (sessionPercent != selected.ResolutionPercent || sessionRevision != revision))
                    await FinishAsync(true).ConfigureAwait(false);
                if (session != null) continue;
                RunningPlayerWindow? target;
                try { target = readyWindow(); }
                catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException)
                { Publish("Waiting for the launched player: " + error.Message); continue; }
                if (target == null)
                {
                    if (blocked == null) Publish("Upscaler enabled. Waiting for the launched player to be visible and active.");
                    continue;
                }
                if (blocked == (target.Handle, revision)) continue;
                CancellationTokenSource? attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                Volatile.Write(ref _starting, attempt);
                try
                {
                    Publish("Preparing automatic low-resolution fullscreen scaling…");
                    session = await start(target, selected.ResolutionPercent, attempt.Token).ConfigureAwait(false);
                    sessionWindow = target; sessionPercent = selected.ResolutionPercent; sessionRevision = revision;
                    sessionCancellation = attempt; attempt = null;
                    completionNotice = NotifyCompletionAsync(session);
                    Publish(session.Status);
                    if (sessionCancellation.IsCancellationRequested && !cancellation.IsCancellationRequested)
                    { await FinishAsync(true).ConfigureAwait(false); Pulse(); }
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                { Pulse(); }
                catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException or NotSupportedException
                    or ExternalException or UnauthorizedAccessException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
                {
                    App.Logger.Warn(error, "Automatic upscaler startup failed.");
                    RunningPlayerWindow? current = null;
                    try { current = readyWindow(); }
                    catch (Exception unavailable) when (unavailable is Win32Exception or IOException or InvalidOperationException or ArgumentException)
                    { App.Logger.Warn(unavailable, "The player could not be rechecked after upscaler startup failed."); }
                    if (current == null)
                    { blocked = null; Publish("Upscaler paused during startup. Scaling resumes when the launched player is active and ready again."); }
                    else
                    {
                        blocked = (target.Handle, revision);
                        Publish("Automatic upscaling unavailable: " + error.Message + " Toggle the feature or change its scale to retry.");
                        if (current.Handle != target.Handle) Pulse();
                    }
                }
                finally { Volatile.Write(ref _starting, null); attempt?.Dispose(); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            Volatile.Write(ref _starting, null);
            await FinishAsync(true).ConfigureAwait(false);
        }
        async Task NotifyCompletionAsync(IAutomaticUpscaleSession active)
        {
            try { await active.Completion.ConfigureAwait(false); }
            catch (Exception error) { App.Logger.Warn(error, "Automatic upscaler session completion failed."); }
            finally { Pulse(); }
        }
        async Task FinishAsync(bool stop)
        {
            if (session == null) return;
            var ending = session;
            if (stop) ending.Stop();
            try { await ending.Completion.ConfigureAwait(false); }
            finally
            {
                try { await completionNotice.ConfigureAwait(false); }
                finally { ending.Dispose(); session = null; sessionCancellation?.Dispose(); sessionCancellation = null; }
            }
            if (stop) Publish(ending.Status);
        }
    }
    private void Publish(string status)
    {
        if (_lastStatus == status) return;
        _lastStatus = status;
        report(status);
    }
}

internal static class PlayerUpscaleWatcher
{
    internal sealed class NativeSession(PlayerUpscaler session) : IAutomaticUpscaleSession
    {
        public Task Completion => session.Completion;
        public string Status => session.Status;
        public UpscalerStopReason StopReason => session.StopReason;
        public void Stop() => session.Stop();
        public void Dispose() => session.Dispose();
    }
    internal static AutomaticUpscaleOptions ReadOptions(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        if (length is < 2 or > 2097152) throw new InvalidDataException("Saved settings have an unsupported size.");
        byte[] bytes = new byte[(int)length]; stream.ReadExactly(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Saved settings must be an object.");
        bool enabled = false;
        if (document.RootElement.TryGetProperty("UpscaleEnabled", out var enabledValue))
        {
            if (enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException("Saved upscaler enablement must be a boolean.");
            enabled = enabledValue.GetBoolean();
        }
        int percent = 50;
        if (document.RootElement.TryGetProperty("UpscaleResolutionPercent", out var value))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out percent)) throw new JsonException("Saved render scale must be an integer.");
        }
        if (percent is not (50 or 67 or 75)) throw new JsonException("Saved render scale must be 50, 67 or 75 percent.");
        return new(enabled, percent);
    }
    internal static async Task RunForPlayerAsync(int pid, CancellationToken cancellation)
    {
        if (!PlayerUpscaler.IsSupported || cancellation.IsCancellationRequested) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || !process.ProcessName.Equals(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The launched Roblox player is no longer available.");
            long started = process.StartTime.ToUniversalTime().Ticks;
            await RunAsync(pid, App.Settings.FileLocation, () => ReadyPlayer(pid, started),
                async (target, percent, token) => new NativeSession(await PlayerUpscaler.StartAsync(target, percent, token).ConfigureAwait(false)),
                result => SaveResult($"Player {pid}: " + result), cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException
            or ExternalException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
        { App.Logger.Warn(error, "Automatic upscaling is unavailable."); SaveResult($"Player {pid}: Automatic upscaling unavailable: " + error.Message); }
    }
    internal static Task RunAsync(int pid, string settingsPath, Func<RunningPlayerWindow?> readyWindow,
        Func<RunningPlayerWindow, int, CancellationToken, Task<IAutomaticUpscaleSession>> start, Action<string> report, CancellationToken cancellation)
        => RunWithEventsAsync(pid, settingsPath, readyWindow, start, report, WatchWindowsAsync, cancellation);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2025", Justification = "The native event task is awaited by monitor; the finally block cancels the owned lifetime and awaits monitor before disposing process and lifetime resources.")]
    internal static async Task RunWithEventsAsync(int pid, string settingsPath, Func<RunningPlayerWindow?> readyWindow,
        Func<RunningPlayerWindow, int, CancellationToken, Task<IAutomaticUpscaleSession>> start, Action<string> report,
        Func<int, Action, CancellationToken, Task> watchEvents, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task events = Task.CompletedTask, monitor = Task.CompletedTask;
        string? terminalFailure = null;
        Process? process = null;
        FileSystemWatcher? settingsWatcher = null;
        EventHandler? exited = null;
        try
        {
            process = Process.GetProcessById(pid);
            if (process.HasExited) return;
            var coordinator = new AutomaticUpscaleCoordinator(() => ReadOptions(settingsPath), readyWindow, start, report);
            settingsWatcher = WatchSettings(settingsPath, coordinator.OptionsChanged, error =>
            {
                App.Logger.Warn(error, "Automatic upscaler settings notifications failed.");
                ReportFailure("Automatic upscaling stopped because saved settings can no longer be watched: " + error.Message);
                CancelLifetime();
            });
            exited = (_, _) => CancelLifetime();
            process.Exited += exited;
            process.EnableRaisingEvents = true;
            events = watchEvents(pid, coordinator.Pulse, lifetime.Token);
            monitor = MonitorWindowsAsync(events, lifetime, ReportFailure);
            await coordinator.RunAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NotSupportedException
            or ExternalException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
        { App.Logger.Warn(error, "Automatic upscaling is unavailable."); ReportFailure("Automatic upscaling unavailable: " + error.Message); }
        finally
        {
            settingsWatcher?.Dispose();
            if (process != null && exited != null) process.Exited -= exited;
            try { await lifetime.CancelAsync().ConfigureAwait(false); }
            finally
            {
                try { await monitor.ConfigureAwait(false); } catch (OperationCanceledException) { }
                finally
                {
                    process?.Dispose();
                    string? failure = Volatile.Read(ref terminalFailure);
                    if (failure != null) report(failure);
                }
            }
        }
        void ReportFailure(string failure)
        {
            if (failure.Length > 768) failure = failure[..768];
            Interlocked.CompareExchange(ref terminalFailure, failure, null);
            report(Volatile.Read(ref terminalFailure)!);
        }
        void CancelLifetime() { try { lifetime.Cancel(); } catch (ObjectDisposedException) { } }
    }
    private static async Task MonitorWindowsAsync(Task events, CancellationTokenSource lifetime, Action<string> report)
    {
        try { await events.ConfigureAwait(false); }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ExternalException or UnauthorizedAccessException
            or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
        { App.Logger.Warn(error, "Automatic upscaler window events failed."); report("Automatic upscaling stopped: window notifications are unavailable. " + error.Message); }
        finally { await lifetime.CancelAsync().ConfigureAwait(false); }
    }
    internal static FileSystemWatcher WatchSettings(string path, Action changed, Action<Exception> failed)
    {
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 4096 };
        watcher.Changed += (_, _) => changed(); watcher.Created += (_, _) => changed(); watcher.Deleted += (_, _) => changed(); watcher.Renamed += (_, _) => changed();
        watcher.Error += (_, args) => failed(args.GetException()); watcher.EnableRaisingEvents = true;
        return watcher;
    }
    private static void SaveResult(string result)
    {
        App.Logger.Info("Automatic upscaler: " + result);
        App.State.Prop.LastUpscalerResult = result;
        if (!App.State.TrySaveSetting(nameof(App.State.Prop.LastUpscalerResult))) App.Logger.Warn("Could not save the automatic upscaler's last result.");
    }
    private static RunningPlayerWindow? ReadyPlayer(int pid, long started)
    {
        nint window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)pid || !IsWindowVisible(window) || IsIconic(window)) return null;
        using var process = Process.GetProcessById(pid);
        if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != started || process.MainWindowHandle != window) return null;
        string title = new(process.MainWindowTitle.Where(character => !char.IsControl(character)).Take(160).ToArray());
        return new(pid, started, window, title.Length > 0 ? title : "Roblox player");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    { public nint Window; public uint Id; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    private delegate void WindowEvent(nint hook, uint kind, nint window, int objectId, int childId, uint eventThread, uint time);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint minimum, uint maximum, nint module, WindowEvent callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint action);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint procedure);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static Task WatchWindowsAsync(int pid, Action changed, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var hooks = new List<nint>(4);
            GCHandle root = default;
            Exception? failure = null;
            nuint trailingTimer = 0;
            long trailingDue = 0;
            try
            {
                PeekMessage(out _, 0, 0, 0, 0); uint threadId = GetCurrentThreadId();
                using var registration = cancellation.Register(() => PostThreadMessage(threadId, 0x12, 0, 0));
                if (cancellation.IsCancellationRequested) return;
                WindowEvent callback = (_, kind, window, objectId, childId, _, _) =>
                {
                    if (cancellation.IsCancellationRequested || window == 0 || objectId != 0 || childId != 0) return;
                    try
                    {
                        if (kind != 0x8001 && (GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)pid || GetAncestor(window, 2) != window)) return;
                        changed();
                        trailingDue = Environment.TickCount64 + 75;
                        ArmTrailingTimer(75);
                    }
                    catch (Exception error) { failure = error; PostThreadMessage(threadId, 0x12, 0, 0); }
                };
                root = GCHandle.Alloc(callback);
                Add(3, 3, 0); Add(0x16, 0x17, (uint)pid); Add(0x8000, 0x8003, (uint)pid); Add(0x800B, 0x800B, (uint)pid);
                changed();
                while (!cancellation.IsCancellationRequested)
                {
                    int state = GetMessage(out var message, 0, 0, 0);
                    if (state == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (state == 0) break;
                    if (message.Id == 0x113 && trailingTimer != 0 && message.WParam == trailingTimer)
                    {
                        long remaining = trailingDue - Environment.TickCount64;
                        if (remaining > 0) ArmTrailingTimer((uint)Math.Clamp(remaining, 1, 75));
                        else
                        {
                            if (!KillTimer(0, trailingTimer)) throw new Win32Exception(Marshal.GetLastWin32Error());
                            trailingTimer = 0;
                            changed();
                        }
                        continue;
                    }
                    TranslateMessage(ref message); DispatchMessage(ref message);
                }
                void ArmTrailingTimer(uint milliseconds)
                {
                    nuint timer = SetTimer(0, trailingTimer, milliseconds, 0);
                    if (timer == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    trailingTimer = timer;
                }
                void Add(uint minimum, uint maximum, uint processId)
                {
                    nint hook = SetWinEventHook(minimum, maximum, 0, callback, processId, 0, 0);
                    if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    hooks.Add(hook);
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (trailingTimer != 0 && !KillTimer(0, trailingTimer))
                {
                    var error = new Win32Exception(Marshal.GetLastWin32Error());
                    failure ??= error;
                    App.Logger.Warn(error, "Automatic upscaler could not release its pending window confirmation timer.");
                }
                foreach (nint hook in hooks) if (!UnhookWinEvent(hook)) App.Logger.Warn("Automatic upscaler could not release its window event hook: " + Marshal.GetLastWin32Error());
                if (root.IsAllocated) root.Free();
                if (failure != null) completion.TrySetException(failure); else completion.TrySetResult();
            }
        }) { IsBackground = true, Name = "CoffeeStrap automatic upscaler events" };
        thread.Start();
        return completion.Task;
    }
}
