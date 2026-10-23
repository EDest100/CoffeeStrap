// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security;

namespace Froststrap.Services;

internal static class HardwareTuning
{
    internal sealed record Panel(string Name, string Path);
    internal sealed record Snapshot(string Cpu, string Gpu, IReadOnlyList<Panel> Panels, string Warning = "");
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint State;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice result, uint flags);
    public static Snapshot Detect()
    {
        if (!OperatingSystem.IsWindows()) return new("System CPU", "System graphics", [], "Graphics shortcuts are available on Windows only.");
        string cpu = "Unknown CPU", warning = "";
        try
        {
            using var processor = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = (processor?.GetValue("ProcessorNameString") as string)?.Trim() is { Length: > 0 } name ? name : cpu;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { App.Logger.Warn(error, "Could not identify the CPU."); warning = "CPU information is unavailable. Windows graphics preferences can still be used."; }
        var adapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (uint i = 0; i < 32; i++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.State & 8) == 0 && !string.IsNullOrWhiteSpace(device.Description)) adapters.Add(device.Description.Trim());
        }
        string gpu = adapters.Count > 0 ? string.Join(" · ", adapters) : "No display adapter reported";
        return new(cpu, gpu, FindDriverPanels(gpu, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), File.Exists), warning);
    }
    internal static IReadOnlyList<Panel> FindDriverPanels(string gpu, string programFiles, Func<string, bool> exists)
    {
        var panels = new List<Panel>();
        void Add(string name, params string[] parts)
        {
            string path = Path.Combine([programFiles, .. parts]);
            if (exists(path)) panels.Add(new(name, path));
        }
        if (gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            Add("NVIDIA app", "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe");
            Add("NVIDIA Control Panel", "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe");
        }
        if (gpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            Add("AMD Software", "AMD", "CNext", "CNext", "RadeonSoftware.exe");
        return panels;
    }

    internal static string? PlayerExecutable()
    {
        try
        {
            if (!App.PlayerState.IsSaved) return null;
            var player = new AppData.RobloxPlayerData();
            if (string.IsNullOrWhiteSpace(player.DistributionState.VersionGuid)) return null;
            string path = Path.GetFullPath(player.ExecutablePath);
            string root = Path.GetFullPath(Paths.Versions) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
        { App.Logger.Warn(error, "Could not locate the installed Roblox executable."); return null; }
    }
}
