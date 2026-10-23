// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;

namespace Froststrap.Services;

internal sealed record RunningPlayerWindow(int ProcessId, long StartedUtcTicks, nint Handle, string Title)
{
    public override string ToString() => $"PID {ProcessId} · {Title}";
}

internal static class RunningPlayerWindows
{
    internal static IReadOnlyList<RunningPlayerWindow> Find()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var result = new List<RunningPlayerWindow>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName)))
        {
            using (process)
            {
                try
                {
                    if (result.Count >= 32 || process.HasExited || process.MainWindowHandle == 0) continue;
                    string title = new(process.MainWindowTitle.Where(character => !char.IsControl(character)).Take(160).ToArray());
                    result.Add(new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.MainWindowHandle, title.Length > 0 ? title : "Roblox player"));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException) { }
            }
        }
        return result.OrderBy(window => window.ProcessId).ToArray();
    }
    internal static bool IsCurrent(RunningPlayerWindow window)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var process = Process.GetProcessById(window.ProcessId);
            return !process.HasExited && process.ProcessName.Equals(Path.GetFileNameWithoutExtension(App.RobloxPlayerAppName), StringComparison.OrdinalIgnoreCase)
                && process.StartTime.ToUniversalTime().Ticks == window.StartedUtcTicks && process.MainWindowHandle == window.Handle;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or ArgumentException) { return false; }
    }
}
