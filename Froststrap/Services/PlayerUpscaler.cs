// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Froststrap.Services;

internal sealed class PlayerUpscaler : IDisposable
{
    private const uint Layered = 0x80000, Transparent = 0x20, NoActivate = 0x8000000, ToolWindow = 0x80, Topmost = 8;
    private const uint CloseMessage = 0x10, TimerMessage = 0x113, HotkeyMessage = 0x312, StopMessage = 0x8001;
    private const uint ForegroundEvent = 3, DestroyEvent = 0x8001, LocationEvent = 0x800B;
    private static int _running;
    private static readonly WindowProcedure Procedure = WindowProc;
    private static readonly Lazy<ushort> WindowClass = new(RegisterWindowClass);
    private readonly TaskCompletionSource<PlayerUpscaler> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RunningPlayerWindow _target;
    private readonly int _percent;
    private readonly bool _ownedFixture;
    private readonly bool _useNativeGuard;
    private readonly CancellationToken _cancellation;
    private readonly WinEventProcedure _eventProcedure;
    private readonly EventHandler _exitHandler;
    private Thread? _thread;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The session thread owns and disposes its geometry lease in Run's finally block; Dispose waits for that cleanup.")]
    private PlayerWindowSizing? _sizing;
    private nint _overlay, _cursorWindow, _thumbnail, _process, _foregroundHook, _locationHook, _destroyHook;
    private Rectangle _source, _destination, _expectedWindow, _oldClip, _ownedClip;
    private nint _cursor;
    private Rectangle _cursorBounds;
    private bool _cursorShown;
    private int _cursorWidth, _cursorHeight, _cursorHotX, _cursorHotY;
    private bool _magnifier, _cursorHidden, _clipped, _hotkey, _gameConfined;
    private volatile bool _active, _stop;
    private string _status = "Starting the Windows compositor upscaler…";
    internal static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10);
    internal bool IsActive => _active;
    internal Task Completion => _completed.Task;
    internal string Status => Volatile.Read(ref _status);
    internal nint OverlayHandle => _overlay;
    internal Rectangle SourceBounds => _source;
    internal Rectangle DestinationBounds => _destination;
    internal static Task<PlayerUpscaler> StartAsync(RunningPlayerWindow target, int resolutionPercent, CancellationToken cancellation = default)
        => Start(target, resolutionPercent, false, true, cancellation);
    internal static Task<PlayerUpscaler> StartOwnedFixtureAsync(nint window, int resolutionPercent, bool useNativeGuard = false, CancellationToken cancellation = default)
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        return Start(new(current.Id, current.StartTime.ToUniversalTime().Ticks, window, "CoffeeStrap owned fixture"), resolutionPercent, true, useNativeGuard, cancellation);
    }
    private static Task<PlayerUpscaler> Start(RunningPlayerWindow target, int percent, bool fixture, bool guard, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) throw new PlatformNotSupportedException("The external upscaler requires Windows 10 or later.");
        if (percent is not (50 or 67 or 75)) throw new ArgumentOutOfRangeException(nameof(percent));
        cancellation.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) throw new InvalidOperationException("An upscaler session is already running. Stop it before starting another.");
        var session = new PlayerUpscaler(target, percent, fixture, guard, cancellation);
        try
        {
            var thread = new Thread(session.Run) { IsBackground = true, Name = "CoffeeStrap external upscaler" };
            session._thread = thread;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return session._started.Task;
        }
        catch { Interlocked.Exchange(ref _running, 0); throw; }
    }
    private PlayerUpscaler(RunningPlayerWindow target, int percent, bool fixture, bool guard, CancellationToken cancellation)
    { _target = target; _percent = percent; _ownedFixture = fixture; _useNativeGuard = guard; _cancellation = cancellation; _eventProcedure = OnWindowEvent; _exitHandler = (_, _) => Dispose(); }
    internal void Stop() => RequestStop("Upscaler stopped. The original window is restored when it has not been changed.");
    public void Dispose()
    {
        Stop();
        if (_thread != Thread.CurrentThread && !_completed.Task.Wait(TimeSpan.FromSeconds(3)))
            Volatile.Write(ref _status, "Upscaler cleanup is still pending. Keep CoffeeStrap open until the session has stopped.");
    }
    private void RequestStop(string status)
    {
        if (_stop) return;
        Volatile.Write(ref _status, status); _stop = true;
        nint window = _overlay;
        if (window != 0) PostMessage(window, StopMessage, 0, 0);
    }
    private bool TargetExists() => GetWindowThreadProcessId(_target.Handle, out uint pid) != 0 && pid == (uint)_target.ProcessId;
    private void Run()
    {
        GCHandle state = default;
        CursorGuard? guard = null;
        nint previousDpi = 0;
        try
        {
            AppDomain.CurrentDomain.ProcessExit += _exitHandler;
            _cancellation.ThrowIfCancellationRequested();
            previousDpi = SetThreadDpiAwarenessContext(-4);
            if (previousDpi == 0) ThrowLastError();
            if (!TargetExists() || (!_ownedFixture && !RunningPlayerWindows.IsCurrent(_target))) throw new IOException("The selected Roblox window has closed or changed. Refresh players and try again.");
            _process = OpenProcess(0x101000, false, (uint)_target.ProcessId);
            if (_process == 0) ThrowLastError();
            Ensure(GetProcessTimes(_process, out long created, out _, out _, out _));
            if (DateTime.FromFileTimeUtc(created).Ticks != _target.StartedUtcTicks) throw new IOException("The selected player process has changed.");
            EnsureHResult(DwmIsCompositionEnabled(out bool composition));
            if (!composition) throw new IOException("Windows desktop composition is unavailable in this session.");
            var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            Ensure(GetMonitorInfo(MonitorFromWindow(_target.Handle, 2), ref monitor));
            if (monitor.Monitor.Width < 640 || monitor.Monitor.Height < 360 || monitor.Monitor.Width > 16384 || monitor.Monitor.Height > 16384)
                throw new IOException("The selected display does not have a supported size.");
            int width = Math.Max(640, monitor.Monitor.Width * _percent / 100), height = Math.Max(360, monitor.Monitor.Height * _percent / 100);
            double ratio = (double)monitor.Monitor.Width / monitor.Monitor.Height;
            if ((double)width / height > ratio) height = (int)Math.Ceiling(width / ratio); else width = (int)Math.Ceiling(height * ratio);
            _sizing = new(_target.Handle, (uint)_target.ProcessId, width, height);
            for (int attempt = 0; attempt < 40 && !_sizing.Confirmed; attempt++)
            { _cancellation.ThrowIfCancellationRequested(); if (!TargetExists()) throw new IOException("The selected player closed while its render size was being changed."); Thread.Sleep(25); }
            if (!_sizing.Confirmed) throw new IOException("Windows did not confirm the low-resolution player size.");
            Ensure(GetWindowRect(_target.Handle, out _expectedWindow));
            Ensure(GetClientRect(_target.Handle, out Rectangle client));
            var origin = new Point(); Ensure(ClientToScreen(_target.Handle, ref origin));
            _source = new(origin.X, origin.Y, origin.X + client.Width, origin.Y + client.Height);
            _destination = Fit(monitor.Monitor, client.Width, client.Height);
            _ = WindowClass.Value;
            state = GCHandle.Alloc(this);
            _overlay = CreateWindowEx(Layered | Transparent | NoActivate | ToolWindow | Topmost, ClassName, "CoffeeStrap Upscaler", 0x80000000,
                monitor.Monitor.Left, monitor.Monitor.Top, monitor.Monitor.Width, monitor.Monitor.Height, _target.Handle, 0, GetModuleHandle(null), GCHandle.ToIntPtr(state));
            if (_overlay == 0) ThrowLastError();
            Ensure(SetLayeredWindowAttributes(_overlay, 0, 255, 2));
            EnsureHResult(DwmRegisterThumbnail(_overlay, _target.Handle, out _thumbnail));
            var properties = new ThumbnailProperties { Flags = 1 | 4 | 8 | 16, Destination = new(_destination.Left - monitor.Monitor.Left, _destination.Top - monitor.Monitor.Top, _destination.Right - monitor.Monitor.Left, _destination.Bottom - monitor.Monitor.Top), Opacity = 255, Visible = true, ClientOnly = true };
            EnsureHResult(DwmUpdateThumbnailProperties(_thumbnail, ref properties));
            _cursorWindow = CreateWindowEx(Layered | Transparent | NoActivate | ToolWindow | Topmost, ClassName, "CoffeeStrap Upscaler Cursor", 0x80000000,
                0, 0, 1, 1, _overlay, 0, GetModuleHandle(null), GCHandle.ToIntPtr(state));
            if (_cursorWindow == 0) ThrowLastError();
            Ensure(SetLayeredWindowAttributes(_cursorWindow, 0xFF00FF, 255, 1));
            _foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, 0, _eventProcedure, 0, 0, 0);
            _locationHook = SetWinEventHook(LocationEvent, LocationEvent, 0, _eventProcedure, (uint)_target.ProcessId, 0, 0);
            _destroyHook = SetWinEventHook(DestroyEvent, DestroyEvent, 0, _eventProcedure, (uint)_target.ProcessId, 0, 0);
            if (_foregroundHook == 0 || _locationHook == 0 || _destroyHook == 0) ThrowLastError();
            Ensure(RegisterHotKey(_overlay, 1, 0x4003, 0x55)); _hotkey = true;
            Ensure(GetClipCursor(out _oldClip));
            Ensure(MagInitialize()); _magnifier = true;
            if (GetForegroundWindow() != _target.Handle && !SetForegroundWindow(_target.Handle))
                throw new IOException("Windows did not allow the player to become active. Select Roblox and try again.");
            if (GetForegroundWindow() != _target.Handle) throw new IOException("The selected player is not the active window.");
            ShowWindow(_overlay, 4);
            Ensure(SetWindowPos(_overlay, -1, monitor.Monitor.Left, monitor.Monitor.Top, monitor.Monitor.Width, monitor.Monitor.Height, 0x10));
            _ownedClip = Intersection(_source, _oldClip);
            if (_ownedClip.Width < 1 || _ownedClip.Height < 1) throw new IOException("The current cursor confinement does not overlap the selected player. Release the other application's mouse capture first.");
            if (_useNativeGuard) guard = CursorGuard.Start(_oldClip, _ownedClip);
            _cancellation.ThrowIfCancellationRequested();
            if (_stop) throw new OperationCanceledException("Upscaler startup was stopped.");
            Ensure(ClipCursor(ref _ownedClip)); _clipped = true;
            Ensure(MagShowSystemCursor(false)); _cursorHidden = true;
            if (SetTimer(_overlay, 1, 16, 0) == 0) ThrowLastError();
            _active = true;
            Volatile.Write(ref _status, $"Upscaling {client.Width} × {client.Height} to {_destination.Width} × {_destination.Height} with Windows composition. Ctrl+Alt+U stops and restores.");
            UpdateCursor();
            _cancellation.ThrowIfCancellationRequested();
            _started.TrySetResult(this);
            using var cancellation = _cancellation.Register(Stop);
            nint[] handles = guard == null ? [_process] : [_process, guard.WaitHandle];
            while (!_stop)
            {
                uint result = MsgWaitForMultipleObjects((uint)handles.Length, handles, false, uint.MaxValue, 0x04FF);
                if (result == 0) { RequestStop("The selected player closed. Upscaler stopped."); break; }
                if (guard != null && result == 1) { RequestStop("The cursor recovery helper exited unexpectedly. Upscaler stopped and restored the cursor."); break; }
                if (result == uint.MaxValue) ThrowLastError();
                while (PeekMessage(out var message, 0, 0, 0, 1))
                {
                    if (message.Id == 0x12) { RequestStop("Upscaler stopped."); break; }
                    TranslateMessage(ref message); DispatchMessage(ref message);
                }
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException or ExternalException or OperationCanceledException
            or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            Volatile.Write(ref _status, "Upscaler unavailable: " + error.Message);
            if (error is OperationCanceledException) _started.TrySetCanceled(_cancellation); else _started.TrySetException(error);
        }
        finally
        {
            var cleanup = new List<string>();
            _active = false; _stop = true;
            if (_cursorHidden && !MagShowSystemCursor(true)) cleanup.Add("Windows did not confirm cursor visibility restoration");
            if (_clipped)
            {
                if (!GetClipCursor(out var currentClip)) cleanup.Add("Windows did not return the final mouse confinement");
                else if (currentClip == _ownedClip && !ClipCursor(ref _oldClip)) cleanup.Add("Windows did not restore the previous mouse confinement");
            }
            if (guard != null)
            {
                try { guard.Stop(); }
                catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException) { cleanup.Add("the cursor recovery helper did not confirm exit: " + error.Message); }
                finally { guard.Dispose(); }
            }
            if (_hotkey && _overlay != 0) UnregisterHotKey(_overlay, 1);
            if (_foregroundHook != 0) UnhookWinEvent(_foregroundHook);
            if (_locationHook != 0) UnhookWinEvent(_locationHook);
            if (_destroyHook != 0) UnhookWinEvent(_destroyHook);
            if (_thumbnail != 0 && DwmUnregisterThumbnail(_thumbnail) < 0) cleanup.Add("Windows did not unregister the live thumbnail");
            if (_cursorWindow != 0) DestroyWindow(_cursorWindow);
            if (_overlay != 0) { KillTimer(_overlay, 1); DestroyWindow(_overlay); }
            _cursorWindow = _overlay = 0;
            if (_magnifier && !MagUninitialize()) cleanup.Add("Windows did not close the magnification cursor session");
            if (_sizing != null)
            {
                bool expectedRestore = _sizing.Confirmed;
                _sizing.Dispose();
                if (expectedRestore)
                {
                    for (int attempt = 0; attempt < 40 && TargetExists() && !_sizing.OriginalRestored; attempt++) Thread.Sleep(25);
                    if (TargetExists() && !_sizing.OriginalRestored) cleanup.Add("the original player geometry was not confirmed");
                }
            }
            if (_process != 0) CloseHandle(_process);
            if (state.IsAllocated) state.Free();
            if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
            AppDomain.CurrentDomain.ProcessExit -= _exitHandler;
            if (cleanup.Count > 0) Volatile.Write(ref _status, Status + " Cleanup: " + string.Join("; ", cleanup) + ".");
            _started.TrySetException(new IOException(Status));
            Interlocked.Exchange(ref _running, 0);
            _completed.TrySetResult();
        }
    }
    private void OnWindowEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint eventThread, uint time)
    {
        if (!_active || _stop) return;
        if (eventId == ForegroundEvent && window != _target.Handle) RequestStop("Upscaler stopped when focus left Roblox.");
        else if (window == _target.Handle && objectId == 0 && childId == 0)
        {
            if (eventId == DestroyEvent) RequestStop("The selected player window closed. Upscaler stopped.");
            else if (eventId == LocationEvent && (!GetWindowRect(window, out var rectangle) || rectangle != _expectedWindow))
                RequestStop("Upscaler stopped after the player window changed. Your later window change is kept.");
        }
    }
    private void UpdateCursor()
    {
        if (!_active || _stop) return;
        if (!TargetExists() || GetForegroundWindow() != _target.Handle) { RequestStop("Upscaler stopped when the selected player became unavailable or lost focus."); return; }
        if (!GetWindowRect(_target.Handle, out var geometry) || geometry != _expectedWindow) { RequestStop("Upscaler stopped after the player window changed. Your later window change is kept."); return; }
        if (!GetClipCursor(out var currentClip)) ThrowLastError();
        if (currentClip != _ownedClip)
        {
            _clipped = false;
            if (currentClip.Width > 0 && currentClip.Height > 0 && Intersection(currentClip, _source) == currentClip) _gameConfined = true;
            else if (_gameConfined && currentClip == DesktopBounds())
            {
                _gameConfined = false;
                Ensure(ClipCursor(ref _ownedClip)); _clipped = true;
            }
            else { RequestStop("Upscaler stopped after mouse confinement moved outside the selected player."); return; }
        }
        var info = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
        Ensure(GetCursorInfo(ref info));
        if ((info.Flags & 1) == 0 || info.Cursor == 0)
        {
            if (_cursorShown) { ShowWindow(_cursorWindow, 0); _cursorShown = false; }
            return;
        }
        if (_cursor != info.Cursor)
        {
            Ensure(GetIconInfo(info.Cursor, out var icon));
            try
            {
                nint bitmap = icon.Color != 0 ? icon.Color : icon.Mask;
                if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out Bitmap image) == 0) ThrowLastError();
                int height = icon.Color != 0 ? image.Height : image.Height / 2;
                double scale = Math.Min((double)_destination.Width / _source.Width, Math.Min(256.0 / Math.Max(1, image.Width), 256.0 / Math.Max(1, height)));
                _cursorWidth = Math.Clamp((int)Math.Round(image.Width * scale), 1, 256);
                _cursorHeight = Math.Clamp((int)Math.Round(height * scale), 1, 256);
                _cursorHotX = (int)Math.Round(icon.HotX * scale); _cursorHotY = (int)Math.Round(icon.HotY * scale);
                _cursor = info.Cursor;
            }
            finally { if (icon.Color != 0) DeleteObject(icon.Color); if (icon.Mask != 0) DeleteObject(icon.Mask); }
            InvalidateRect(_cursorWindow, 0, true);
        }
        Point point = MapPoint(info.Position, _source, _destination);
        var bounds = new Rectangle(point.X - _cursorHotX, point.Y - _cursorHotY, point.X - _cursorHotX + _cursorWidth, point.Y - _cursorHotY + _cursorHeight);
        if (!_cursorShown || bounds != _cursorBounds)
        {
            Ensure(SetWindowPos(_cursorWindow, -1, bounds.Left, bounds.Top, bounds.Width, bounds.Height, _cursorShown ? 0x14u : 0x50u));
            _cursorBounds = bounds; _cursorShown = true;
        }
    }
    internal static Point MapPoint(Point point, Rectangle source, Rectangle destination)
    {
        if (source.Width < 2 || source.Height < 2 || destination.Width < 2 || destination.Height < 2) throw new ArgumentOutOfRangeException(nameof(source));
        return new(destination.Left + (int)Math.Round((double)Math.Clamp(point.X - source.Left, 0, source.Width - 1) * (destination.Width - 1) / (source.Width - 1)),
            destination.Top + (int)Math.Round((double)Math.Clamp(point.Y - source.Top, 0, source.Height - 1) * (destination.Height - 1) / (source.Height - 1)));
    }
    internal static Rectangle Fit(Rectangle monitor, int width, int height)
    {
        if (width < 1 || height < 1 || monitor.Width < 1 || monitor.Height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        double scale = Math.Min((double)monitor.Width / width, (double)monitor.Height / height);
        int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        int x = monitor.Left + (monitor.Width - w) / 2, y = monitor.Top + (monitor.Height - h) / 2;
        return new(x, y, x + w, y + h);
    }
    private static Rectangle Intersection(Rectangle left, Rectangle right) => new(Math.Max(left.Left, right.Left), Math.Max(left.Top, right.Top), Math.Min(left.Right, right.Right), Math.Min(left.Bottom, right.Bottom));
    private static Rectangle DesktopBounds()
    {
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
        return new(x, y, x + GetSystemMetrics(78), y + GetSystemMetrics(79));
    }
    private static string ClassName => "CoffeeStrap.ExternalUpscaler." + Environment.ProcessId;
    private static ushort RegisterWindowClass()
    {
        var definition = new WindowClassDefinition { Size = (uint)Marshal.SizeOf<WindowClassDefinition>(), Procedure = Procedure, Instance = GetModuleHandle(null), Name = ClassName };
        ushort value = RegisterClassEx(ref definition); if (value == 0) ThrowLastError(); return value;
    }
    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == 0x81) SetWindowLongPtr(window, -21, Marshal.PtrToStructure<CreateStructure>(lParam).Parameter);
        nint pointer = GetWindowLongPtr(window, -21);
        if (pointer != 0 && GCHandle.FromIntPtr(pointer).Target is PlayerUpscaler session)
        {
            if (message is CloseMessage or HotkeyMessage or StopMessage) { session.RequestStop("Upscaler stopped. The original window is restored when it has not been changed."); return 0; }
            if (message is 0x7E or 0x02E0) { session.RequestStop("Upscaler stopped after the display or DPI changed. Start again on the current monitor."); return 0; }
            if (message == TimerMessage)
            {
                try { session.UpdateCursor(); }
                catch (Exception error) when (error is Win32Exception or IOException or ExternalException) { session.RequestStop("Upscaler stopped: " + error.Message); }
                return 0;
            }
            if (message == 0x0F)
            {
                nint dc = BeginPaint(window, out Paint paint);
                if (dc != 0)
                {
                    GetClientRect(window, out var rectangle);
                    nint brush = CreateSolidBrush(window == session._cursorWindow ? 0xFF00FFu : 0);
                    if (FillRect(dc, ref rectangle, brush) == 0) session.RequestStop("Upscaler stopped because Windows did not paint the presentation window.");
                    DeleteObject(brush);
                    if (window == session._cursorWindow && session._cursor != 0 && !DrawIconEx(dc, 0, 0, session._cursor, session._cursorWidth, session._cursorHeight, 0, 0, 3))
                        session.RequestStop("Upscaler stopped because Windows did not render the mapped cursor.");
                    EndPaint(window, ref paint);
                }
                return 0;
            }
            if (message == 0x14) return 1;
            if (message == 0x02) return 0;
        }
        return DefWindowProc(window, message, wParam, lParam);
    }
    private static void Ensure(bool success) { if (!success) ThrowLastError(); }
    private static void EnsureHResult(int value) { if (value < 0) Marshal.ThrowExceptionForHR(value); }
    private static void ThrowLastError() => throw new Win32Exception(Marshal.GetLastWin32Error());
    private sealed class CursorGuard : IDisposable
    {
        private readonly EventWaitHandle _ready, _stop;
        private System.Diagnostics.Process? _process;
        private bool _disposed;
        private CursorGuard(EventWaitHandle ready, EventWaitHandle stop) { _ready = ready; _stop = stop; }
        internal nint WaitHandle => _process?.SafeHandle.DangerousGetHandle() ?? throw new InvalidOperationException("The cursor recovery process is unavailable.");
        internal static CursorGuard Start(Rectangle original, Rectangle owned)
        {
            nint entry = GetProcAddress(GetModuleHandle(null), "CoffeeStrapUpscalerGuardVersion");
            if (entry == 0 || Marshal.GetDelegateForFunctionPointer<GuardVersion>(entry)() != 1)
                throw new IOException("Use the packaged CoffeeStrap Release EXE to enable cursor recovery before starting the upscaler.");
            string prefix = $"Local\\CoffeeStrap.Upscaler.{Environment.ProcessId}.{Guid.NewGuid():N}";
            var access = new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true };
            var ready = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".ready", access, out bool createdReady);
            EventWaitHandle? stop = null; CursorGuard? result = null;
            try
            {
                stop = new(false, EventResetMode.ManualReset, prefix + ".stop", access, out bool createdStop);
                if (!createdReady || !createdStop) throw new IOException("The cursor recovery session name is already in use.");
                result = new(ready, stop);
                using var current = System.Diagnostics.Process.GetCurrentProcess();
                var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("The current CoffeeStrap executable path is unavailable.")) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in new[] { "--coffeestrap-upscale-guard", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), current.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture),
                    original.Left.ToString(CultureInfo.InvariantCulture), original.Top.ToString(CultureInfo.InvariantCulture), original.Right.ToString(CultureInfo.InvariantCulture), original.Bottom.ToString(CultureInfo.InvariantCulture),
                    owned.Left.ToString(CultureInfo.InvariantCulture), owned.Top.ToString(CultureInfo.InvariantCulture), owned.Right.ToString(CultureInfo.InvariantCulture), owned.Bottom.ToString(CultureInfo.InvariantCulture), prefix + ".ready", prefix + ".stop" }) info.ArgumentList.Add(argument);
                result._process = System.Diagnostics.Process.Start(info) ?? throw new IOException("Windows did not start the cursor recovery helper.");
                if (!ready.WaitOne(TimeSpan.FromSeconds(3)) || result._process.HasExited) throw new IOException("The cursor recovery helper did not become ready. No system cursor was hidden.");
                return result;
            }
            catch
            {
                if (result != null) result.Dispose(); else { stop?.Dispose(); ready.Dispose(); }
                throw;
            }
        }
        internal void Stop()
        {
            if (_disposed) return;
            _stop.Set();
            if (_process != null && !_process.WaitForExit(3000)) throw new IOException("The cursor recovery helper is still finishing. Keep CoffeeStrap open and stop the upscaler again.");
            if (_process != null && _process.ExitCode != 0) throw new IOException("The cursor recovery helper reported a Windows error (" + _process.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _stop.Set();
            _process?.Dispose(); _ready.Dispose(); _stop.Dispose(); _disposed = true;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint GuardVersion();
    }
    [StructLayout(LayoutKind.Sequential)] internal readonly record struct Point(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] internal readonly record struct Rectangle(int Left, int Top, int Right, int Bottom)
    { internal int Width => Right - Left; internal int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal Rectangle Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct ThumbnailProperties { internal uint Flags; internal Rectangle Destination, Source; internal byte Opacity; [MarshalAs(UnmanagedType.Bool)] internal bool Visible, ClientOnly; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { internal uint Size, Flags; internal nint Cursor; internal Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] internal bool IsIcon; internal uint HotX, HotY; internal nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap { internal int Type, Width, Height, WidthBytes; internal ushort Planes, BitsPerPixel; internal nint Bits; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClassDefinition { internal uint Size, Style; internal WindowProcedure Procedure; internal int ClassExtra, WindowExtra; internal nint Instance, Icon, Cursor, Background; internal string? Menu, Name; internal nint SmallIcon; }
    [StructLayout(LayoutKind.Sequential)] private struct CreateStructure { internal nint Parameter, Instance, Menu, Parent; internal int Height, Width, Y, X, Style; internal nint Name, Class; internal uint ExStyle; }
    [StructLayout(LayoutKind.Sequential)] private struct Message { internal nint Window; internal uint Id; internal nuint WParam; internal nint LParam; internal uint Time; internal Point Position; internal uint Private; }
    [StructLayout(LayoutKind.Sequential)] private struct Paint { internal nint Dc; internal int Erase; internal Rectangle Rectangle; internal int Restore, Incremental; internal long Reserved1, Reserved2, Reserved3, Reserved4; }
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    private delegate void WinEventProcedure(nint hook, uint eventId, nint window, int objectId, int childId, uint eventThread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out Rectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint window, out Rectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClassDefinition definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)] private static extern nint CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int show);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint MsgWaitForMultipleObjects(uint count, nint[] handles, bool all, uint milliseconds, uint wake);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProcedure callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClipCursor(out Rectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClipCursor(ref Rectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out Paint paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint window, ref Paint paint);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint window, nint rectangle, bool erase);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref Rectangle rectangle, nint brush);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint background, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)] private static extern int GetObject(nint value, int size, out Bitmap bitmap);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)] private static extern nint GetProcAddress(nint module, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(nint process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref ThumbnailProperties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("Magnification.dll", SetLastError = true)] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll", SetLastError = true)] private static extern bool MagShowSystemCursor(bool show);
}
