// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Froststrap.Services;

internal static class BackgroundMinimizeWatcher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }
    private delegate void WindowEvent(nint hook, uint kind, nint window, int objectId, int childId, uint eventThread, uint time);
    private delegate bool WindowEnumerator(nint window, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint minimum, uint maximum, nint module, WindowEvent callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] private static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessage(out Message message, nint window, uint minimum, uint maximum, uint action);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int exitCode);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(WindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);

    internal static Task RunForPlayerAsync(int pid, SessionScheduling? scheduling, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows() || scheduling?.BackgroundEnabled != true || cancellation.IsCancellationRequested) return Task.CompletedTask;
        return RunAsync(pid, scheduling, SaveResult, cancellation);
    }
    internal static Task RunAsync(int pid, SessionScheduling scheduling, Action<string> report, CancellationToken cancellation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pid);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            nint hook = 0, visibilityHook = 0;
            GCHandle callbackRoot = default;
            Exception? unexpected = null;
            string? failure = null;
            void Publish(string result)
            {
                try { report(result); }
                catch (Exception error) { unexpected ??= error; PostQuitMessage(1); }
            }
            try
            {
                PeekMessage(out _, 0, 0, 0, 0);
                uint threadId = GetCurrentThreadId();
                using var cancel = cancellation.Register(() => PostThreadMessage(threadId, 0x12, 0, 0));
                if (cancellation.IsCancellationRequested) return;
                var knownWindows = new HashSet<nint>();
                WindowEvent callback = (_, kind, window, objectId, childId, _, _) =>
                {
                    if (cancellation.IsCancellationRequested || window == 0 || objectId != 0 || childId != 0) return;
                    try
                    {
                        bool minimizeEvent = kind is 0x16 or 0x17;
                        if (kind == 0x8001)
                        { if (!knownWindows.Contains(window)) return; }
                        else if (GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)pid || GetAncestor(window, 2) != window) return;
                        if (scheduling.SetMinimized(AllVisibleWindowsMinimized(pid, minimizeEvent ? window : 0, kind == 0x16, knownWindows))) Publish(scheduling.BackgroundResult);
                    }
                    catch (Exception error)
                    {
                        unexpected = error;
                        PostQuitMessage(1);
                    }
                };
                callbackRoot = GCHandle.Alloc(callback);
                hook = SetWinEventHook(0x16, 0x17, 0, callback, (uint)pid, 0, 0);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                visibilityHook = SetWinEventHook(0x8001, 0x8003, 0, callback, (uint)pid, 0, 0);
                if (visibilityHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                scheduling.SetMinimized(AllVisibleWindowsMinimized(pid, knownWindows: knownWindows));
                Publish(scheduling.BackgroundResult);
                while (!cancellation.IsCancellationRequested)
                {
                    int state = GetMessage(out var message, 0, 0, 0);
                    if (state == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (state == 0) break;
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                if (unexpected != null) throw new InvalidOperationException("The minimized-player event callback failed.", unexpected);
            }
            catch (Exception error) when (error is Win32Exception or IOException or EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
            {
                App.Logger.Warn(error, "Background resource saver is unavailable.");
                failure = "Unavailable: " + error.Message;
                Publish(failure);
            }
            catch (Exception error) { unexpected = error; }
            finally
            {
                if (visibilityHook != 0 && !UnhookWinEvent(visibilityHook)) App.Logger.Warn("The background saver visibility hook could not be removed: " + Marshal.GetLastWin32Error());
                if (hook != 0 && !UnhookWinEvent(hook)) App.Logger.Warn("The background saver minimize hook could not be removed: " + Marshal.GetLastWin32Error());
                if (callbackRoot.IsAllocated) callbackRoot.Free();
                try { if (scheduling.SetMinimized(false)) Publish((failure == null ? "Stopped: " : failure + " · Cleanup: ") + scheduling.BackgroundResult); }
                catch (Exception error) { unexpected ??= error; }
                if (unexpected != null) completion.TrySetException(unexpected); else completion.TrySetResult();
            }
        }) { IsBackground = true, Name = "CoffeeStrap minimized player events" };
        thread.Start();
        return completion.Task;
    }
    private static void SaveResult(string result)
    {
        try
        {
            App.State.Prop.LastBackgroundSaverResult = result;
            App.State.SaveSetting(nameof(App.State.Prop.LastBackgroundSaverResult));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { App.Logger.Warn(error, "Could not save the background resource saver result."); }
    }
    internal static bool AllVisibleWindowsMinimized(int pid, nint changingWindow = 0, bool minimizing = false, ISet<nint>? knownWindows = null)
    {
        int visited = 0, found = 0;
        bool allMinimized = true, limited = false;
        knownWindows?.Clear();
        WindowEnumerator callback = (window, _) =>
        {
            if (++visited > 4096) { limited = true; return false; }
            if (GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)pid || !IsWindowVisible(window)) return true;
            found++;
            knownWindows?.Add(window);
            allMinimized &= window == changingWindow ? minimizing : IsIconic(window);
            return true;
        };
        if (!EnumWindows(callback, 0) && !limited) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (limited) throw new IOException("Too many desktop windows to safely apply the minimized-player policy.");
        return found > 0 && allMinimized;
    }
}
