// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Froststrap.Services;

internal sealed class PlayerWindowSizing : IDisposable
{
    internal static IReadOnlyList<string> Choices { get; } = ["Roblox default", "1920 × 1080", "1600 × 900", "1280 × 720", "960 × 540"];
    internal static (int Width, int Height) Size(int choice) => choice switch { 1 => (1920, 1080), 2 => (1600, 900), 3 => (1280, 720), 4 => (960, 540), _ => (0, 0) };
    [StructLayout(LayoutKind.Sequential)] internal record struct Rectangle(int Left, int Top, int Right, int Bottom)
    { internal int Width => Right - Left; internal int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal Rectangle Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rectangle rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rectangle rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint GetWindowStyle(nint window, int index);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    private readonly nint _window;
    private readonly uint _pid;
    private readonly Rectangle _original;
    private Rectangle _requested;
    private bool _disposed;
    internal Rectangle ClientSize { get { EnsureOwned(); Ensure(GetClientRect(_window, out var rect)); return rect; } }
    private static void Ensure(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private void EnsureOwned()
    {
        if (GetWindowThreadProcessId(_window, out uint pid) == 0 || pid != _pid) throw new IOException("The original player window is no longer available.");
    }
    private static bool Windowed(nint window) => !IsZoomed(window) && !IsIconic(window) && (GetWindowStyle(window, -16) & 0xC00000) == 0xC00000;
    internal static Rectangle Fit(Rectangle work, int borderWidth, int borderHeight, int width, int height)
    {
        if (width < 1 || height < 1 || borderWidth < 0 || borderHeight < 0 || work.Width <= borderWidth || work.Height <= borderHeight)
            throw new IOException("The available monitor work area is invalid.");
        double scale = Math.Min(1, Math.Min((double)(work.Width - borderWidth) / width, (double)(work.Height - borderHeight) / height));
        int w = (int)Math.Floor(width * scale), h = (int)Math.Floor(height * scale);
        if (w < 640 || h < 360) throw new IOException("The monitor cannot fit a readable 640 × 360 game window.");
        w += borderWidth; h += borderHeight;
        int x = work.Left + (work.Width - w) / 2, y = work.Top + (work.Height - h) / 2;
        return new(x, y, x + w, y + h);
    }
    internal PlayerWindowSizing(nint window, uint pid, int width, int height)
    {
        _window = window; _pid = pid; EnsureOwned();
        if (!Windowed(window)) throw new IOException("Fullscreen, maximized or minimized window retained. Start Roblox in windowed mode to use this option.");
        Ensure(GetWindowRect(window, out _original));
        ApplySize(width, height);
    }
    internal void ApplySize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureOwned();
        if (!Windowed(_window)) throw new IOException("Use a windowed, restored player before changing its size.");
        Ensure(GetWindowRect(_window, out var current)); Ensure(GetClientRect(_window, out var client));
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        Ensure(GetMonitorInfo(MonitorFromWindow(_window, 2), ref monitor));
        var requested = Fit(monitor.Work, current.Width - client.Width, current.Height - client.Height, width, height);
        Ensure(SetWindowPos(_window, 0, requested.Left, requested.Top, requested.Width, requested.Height, 0x4014));
        _requested = requested;
    }
    internal bool Confirmed => GetWindowThreadProcessId(_window, out uint pid) != 0 && pid == _pid && GetWindowRect(_window, out var current) && current == _requested;
    internal bool OriginalRestored => GetWindowThreadProcessId(_window, out uint pid) != 0 && pid == _pid && GetWindowRect(_window, out var current) && current == _original;
    internal void KeepSize() => _disposed = true;
    internal bool Restore()
    {
        if (_disposed || !Confirmed || !Windowed(_window)) return false;
        Ensure(SetWindowPos(_window, 0, _original.Left, _original.Top, _original.Width, _original.Height, 0x4014));
        return true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { Restore(); }
        catch (Win32Exception error) { App.Logger.Warn(error, "The original player window could not be restored."); }
        finally { _disposed = true; }
    }
    internal static async Task RunForPlayerAsync(int pid, int choice, CancellationToken cancellation)
    {
        var (width, height) = Size(choice);
        if (!OperatingSystem.IsWindows() || width == 0) return;
        PlayerWindowSizing? sizing = null;
        void Report(string message)
        {
            App.State.Prop.LastWindowSizeResult = message;
            App.State.SaveSetting(nameof(App.State.Prop.LastWindowSizeResult));
        }
        try
        {
            Report("Waiting for the launched player's window…");
            using var process = Process.GetProcessById(pid);
            if (!process.ProcessName.Equals(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The launched process is not the Roblox player.");
            nint handle = 0;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                cancellation.ThrowIfCancellationRequested(); process.Refresh();
                if (process.HasExited) throw new IOException("The player exited before its window was available.");
                handle = process.MainWindowHandle;
                if (handle != 0) break;
                await Task.Delay(750, cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            if (handle == 0) throw new IOException("No player window appeared within 30 seconds.");
            sizing = new(handle, (uint)pid, width, height);
            for (int attempt = 0; attempt < 20 && !sizing.Confirmed; attempt++) await Task.Delay(50, cancellation).ConfigureAwait(false);
            if (!sizing.Confirmed) throw new IOException("Windows did not confirm the requested window size. The game may have changed it.");
            var actual = sizing.ClientSize;
            Report($"Player window: {actual.Width} × {actual.Height} client pixels. Applied once; your later resizing is respected.");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException or EntryPointNotFoundException)
        { App.Logger.Warn(error, "Player window size unavailable."); Report("Window size unavailable: " + error.Message); }
        finally { sizing?.Dispose(); }
    }
}
