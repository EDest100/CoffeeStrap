// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Froststrap.Services;

internal sealed class PlayerWindowSizing : IDisposable
{
    private const uint FrameMask = 0x80CF0000, Caption = 0xC00000;
    [StructLayout(LayoutKind.Sequential)] internal record struct Rectangle(int Left, int Top, int Right, int Bottom)
    { internal int Width => Right - Left; internal int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal Rectangle Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Placement { internal uint Length, Flags, Show; internal Point Min, Max; internal Rectangle Normal; }
    private readonly nint _window;
    private readonly uint _pid, _originalStyle;
    private readonly Rectangle _original, _originalClient;
    private readonly Placement _originalPlacement;
    private Placement _confirmedPlacement;
    private readonly bool _automatic, _retainedMaximized;
    private uint _ownedStyle;
    private Rectangle _requested, _observed, _observedClient, _confirmed, _confirmedClient, _work;
    private long _monitorPixels, _stableSince;
    private int _requestedWidth, _requestedHeight;
    private bool _disposed, _changedStyle, _mutationRequested, _recenterRequested, _everConfirmed, _restoreRequested, _restoredMinimized;
    internal Rectangle ClientSize { get { EnsureOwned(); Ensure(GetClientRect(_window, out var rect)); return rect; } }
    internal bool RestorationRequested => _restoreRequested;
    internal string Diagnostics => $"Requested client {_requestedWidth}×{_requestedHeight}; requested outer {Describe(_requested)}; observed outer {Describe(_observed)}; observed client {_observedClient.Width}×{_observedClient.Height}; work {Describe(_work)}; original style 0x{_originalStyle:X8}; prepared style 0x{_ownedStyle:X8}; original show {_originalPlacement.Show}.";
    private static string Describe(Rectangle rect) => $"({rect.Left},{rect.Top}) {rect.Width}×{rect.Height}";
    private static void Ensure(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private bool Owned => GetWindowThreadProcessId(_window, out uint pid) != 0 && pid == _pid;
    private void EnsureOwned() { if (!Owned) throw new IOException("The original player window is no longer available."); }
    private static uint Style(nint window) => unchecked((uint)(long)GetWindowLongPtr(window, -16));
    private static bool Windowed(nint window) => !IsZoomed(window) && !IsIconic(window) && (Style(window) & Caption) == Caption;
    private bool Prepared => !IsIconic(_window) && IsZoomed(_window) == _retainedMaximized && (Style(_window) & Caption) == Caption;
    private void SetStyle(uint style)
    {
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(_window, -16, unchecked((nint)(int)style)) == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        Ensure(SetWindowPos(_window, 0, 0, 0, 0, 0, 0x4037));
    }
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
    internal PlayerWindowSizing(nint window, uint pid, int width, int height, bool automaticPreparation = false)
    {
        _window = window; _pid = pid; _automatic = automaticPreparation; EnsureOwned();
        Ensure(GetWindowRect(window, out _original)); Ensure(GetClientRect(window, out _originalClient));
        var placement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
        Ensure(GetWindowPlacement(window, ref placement)); _originalPlacement = placement;
        _retainedMaximized = _automatic && placement.Show == 3;
        _originalStyle = _ownedStyle = Style(window);
        if (!_automatic && !Windowed(window)) throw new IOException("Fullscreen, maximized or minimized window retained. Start Roblox in windowed mode to use this option.");
        try
        {
            if (_automatic) Prepare();
            ApplySize(width, height);
        }
        catch (Exception error) when (error is Win32Exception or IOException or ArgumentException)
        {
            try
            {
                if (Restore())
                {
                    for (int attempt = 0; attempt < 40 && Owned && !OriginalRestored; attempt++) Thread.Sleep(25);
                    if (Owned && !OriginalRestored) throw new IOException("Windows did not confirm original window restoration after setup failed.");
                }
            }
            catch (Exception restore) when (restore is Win32Exception or IOException)
            { throw new IOException("Window preparation failed: " + error.Message + " Restoration also needs attention: " + restore.Message, error); }
            finally { _disposed = true; }
            throw;
        }
    }
    private void Prepare()
    {
        if (IsIconic(_window)) throw new IOException("The selected player is minimized. Restore and select Roblox before automatic upscaling.");
        if (GetForegroundWindow() != _window) throw new IOException("Automatic upscaling waits until the selected Roblox window is active.");
        if (IsHungAppWindow(_window)) throw new IOException("The selected player is not responding. Its window was retained.");
        if (IsZoomed(_window) != _retainedMaximized) throw new IOException("The selected player's show state changed before automatic upscaling.");
        uint style = Style(_window);
        _ownedStyle = (style & ~0x80000000u) | 0xCF0000u;
        if ((_ownedStyle & FrameMask) != (style & FrameMask))
        {
            _changedStyle = true;
            SetStyle(_ownedStyle);
        }
    }
    internal void ApplySize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); EnsureOwned();
        if (!(_automatic ? Prepared : Windowed(_window))) throw new IOException("The selected player's window state changed before resizing.");
        Ensure(GetWindowRect(_window, out var current)); Ensure(GetClientRect(_window, out var client));
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        Ensure(GetMonitorInfo(MonitorFromWindow(_window, 2), ref monitor));
        _work = monitor.Work; _monitorPixels = (long)monitor.Monitor.Width * monitor.Monitor.Height;
        _requestedWidth = width; _requestedHeight = height;
        _requested = Fit(_work, current.Width - client.Width, current.Height - client.Height, width, height);
        _observed = current; _observedClient = client; _stableSince = Environment.TickCount64;
        _everConfirmed = _recenterRequested = _restoreRequested = false;
        _mutationRequested = true;
        Ensure(SetWindowPos(_window, 0, _requested.Left, _requested.Top, _requested.Width, _requested.Height, 0x4014));
    }
    internal bool Confirmed
    {
        get
        {
            if (!Owned || !GetWindowRect(_window, out var current) || !GetClientRect(_window, out var client)) return false;
            if (!_automatic) return current == _requested;
            if (_everConfirmed) return current == _confirmed && client == _confirmedClient && Prepared;
            if (current != _observed || client != _observedClient) { _observed = current; _observedClient = client; _stableSince = Environment.TickCount64; return false; }
            if (Environment.TickCount64 - _stableSince < 75 || !Prepared || (Style(_window) & FrameMask) != (_ownedStyle & FrameMask)) return false;
            if (client.Width < 640 || client.Height < 360 || (long)client.Width * client.Height > _monitorPixels * 9 / 10) return false;
            if (current.Width > _work.Width || current.Height > _work.Height) return false;
            if (current.Left < _work.Left || current.Top < _work.Top || current.Right > _work.Right || current.Bottom > _work.Bottom)
            {
                if (!_recenterRequested)
                {
                    _recenterRequested = true;
                    int x = _work.Left + (_work.Width - current.Width) / 2, y = _work.Top + (_work.Height - current.Height) / 2;
                    Ensure(SetWindowPos(_window, 0, x, y, 0, 0, 0x4015));
                }
                return false;
            }
            if (current == _original && client == _originalClient && _requested != _original && Environment.TickCount64 - _stableSince < 300) return false;
            _confirmedPlacement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
            Ensure(GetWindowPlacement(_window, ref _confirmedPlacement));
            _confirmed = current; _confirmedClient = client; _everConfirmed = true;
            return true;
        }
    }
    internal bool OriginalRestored
    {
        get
        {
            if (!Owned || !GetWindowRect(_window, out var current)) return false;
            if (!_automatic) return current == _original;
            if ((Style(_window) & FrameMask) != (_originalStyle & FrameMask)) return false;
            if (IsIconic(_window))
            {
                var minimized = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
                return _restoredMinimized && GetWindowPlacement(_window, ref minimized) && minimized.Normal == _originalPlacement.Normal
                    && (minimized.Flags & 2) == (_originalPlacement.Show == 3 ? 2u : 0u);
            }
            if (_retainedMaximized)
            {
                var maximized = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
                return IsZoomed(_window) && current == _original && GetWindowPlacement(_window, ref maximized) && maximized.Normal == _originalPlacement.Normal;
            }
            return !IsZoomed(_window) && current == _original;
        }
    }
    internal void KeepSize() => _disposed = true;
    internal bool Restore()
    {
        if (_disposed || !Owned) return false;
        if (_restoreRequested) return true;
        if (!_automatic)
        {
            if (IsIconic(_window)) return false;
            if (!Confirmed || !Windowed(_window)) return false;
            Ensure(SetWindowPos(_window, 0, _original.Left, _original.Top, _original.Width, _original.Height, 0x4014));
            _restoreRequested = true; return true;
        }
        if (!_mutationRequested && !_changedStyle) return false;
        Ensure(GetWindowRect(_window, out var current));
        bool minimized = IsIconic(_window);
        var currentPlacement = new Placement { Length = (uint)Marshal.SizeOf<Placement>() };
        Ensure(GetWindowPlacement(_window, ref currentPlacement));
        if (_everConfirmed && ((minimized ? currentPlacement.Normal != _confirmedPlacement.Normal : current != _confirmed) || (Style(_window) & FrameMask) != (_ownedStyle & FrameMask))) return false;
        if (_retainedMaximized && (currentPlacement.Normal != _originalPlacement.Normal || (!minimized && !IsZoomed(_window)))) return false;
        if (IsHungAppWindow(_window)) throw new IOException("The player stopped responding before original window restoration.");
        if (_changedStyle)
        {
            uint style = Style(_window);
            if ((style & FrameMask) != (_ownedStyle & FrameMask)) return false;
            SetStyle((style & ~FrameMask) | (_originalStyle & FrameMask));
        }
        if (_retainedMaximized && !minimized)
        {
            Ensure(SetWindowPos(_window, 0, _original.Left, _original.Top, _original.Width, _original.Height, 0x4014));
            _restoreRequested = true; return true;
        }
        var placement = _originalPlacement; placement.Flags |= 4;
        if (minimized)
        {
            placement.Show = 2;
            placement.Flags = (placement.Flags & ~2u) | (_originalPlacement.Show == 3 ? 2u : 0u);
        }
        else if (placement.Show != 3) placement.Show = 4;
        Ensure(SetWindowPlacement(_window, ref placement));
        _restoredMinimized = minimized;
        _restoreRequested = true; return true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { Restore(); }
        catch (Exception error) when (error is Win32Exception or IOException) { App.Logger.Warn(error, "The original player window could not be restored."); }
        finally { _disposed = true; }
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rectangle rect);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rectangle rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsHungAppWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowPlacement(nint window, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPlacement(nint window, ref Placement placement);
}
