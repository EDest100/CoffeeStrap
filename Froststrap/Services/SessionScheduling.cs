// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Froststrap.Services;

[StructLayout(LayoutKind.Sequential)]
internal record struct PowerPolicy(uint Version, uint ControlMask, uint StateMask);

internal interface ISchedulingTarget : IDisposable
{
    uint Cpu { get; set; }
    int Gpu { get; set; }
    PowerPolicy Power { get; set; }
    bool Exited { get; }
}

internal sealed class WindowsSchedulingTarget : ISchedulingTarget
{
    private readonly SafeProcessHandle _handle;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint rights, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetPriorityClass(SafeProcessHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetPriorityClass(SafeProcessHandle handle, uint priority);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint code);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryImage(SafeProcessHandle handle, uint flags, [Out] char[] path, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessInformation(SafeProcessHandle handle, int kind, ref PowerPolicy policy, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessInformation(SafeProcessHandle handle, int kind, ref PowerPolicy policy, uint size);
    [DllImport("gdi32.dll")] private static extern int D3DKMTGetProcessSchedulingPriorityClass(SafeProcessHandle handle, out int priority);
    [DllImport("gdi32.dll")] private static extern int D3DKMTSetProcessSchedulingPriorityClass(SafeProcessHandle handle, int priority);
    public WindowsSchedulingTarget(int pid)
    {
        _handle = OpenProcess(0x1600, false, pid);
        if (_handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); _handle.Dispose(); throw new Win32Exception(error); }
    }
    public string Image
    {
        get { var path = new char[32768]; uint length = (uint)path.Length; Ensure(QueryImage(_handle, 0, path, ref length)); return new string(path, 0, checked((int)length)); }
    }
    private static void Ensure(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static void EnsureGpu(int status) { if (status < 0) throw new IOException($"Windows GPU scheduling returned 0x{status:X8}."); }
    public uint Cpu { get { uint value = GetPriorityClass(_handle); Ensure(value != 0); return value; } set => Ensure(SetPriorityClass(_handle, value)); }
    public int Gpu { get { EnsureGpu(D3DKMTGetProcessSchedulingPriorityClass(_handle, out int value)); return value; } set => EnsureGpu(D3DKMTSetProcessSchedulingPriorityClass(_handle, value)); }
    public PowerPolicy Power
    {
        get { var policy = new PowerPolicy(1, 0, 0); Ensure(GetProcessInformation(_handle, 4, ref policy, 12)); return policy; }
        set { Ensure(SetProcessInformation(_handle, 4, ref value, 12)); }
    }
    public bool Exited { get { Ensure(GetExitCodeProcess(_handle, out uint code)); return code != 259; } }
    public void Dispose() => _handle.Dispose();
}

internal sealed class SessionScheduling : IDisposable
{
    private readonly ISchedulingTarget _target;
    private uint? _cpu;
    private int? _gpu;
    private PowerPolicy? _power;
    private uint _ownedPower;
    private bool _disposed;
    private bool _cpuSet, _gpuSet;
    internal string Result { get; private set; } = "";
    internal SessionScheduling(ISchedulingTarget target, bool cpu, bool gpu, bool highQos, bool timers)
    {
        _target = target;
        var results = new List<string>();
        void Apply(string name, Func<bool> action)
        {
            try { results.Add(name + (action() ? ": applied" : ": existing setting retained")); }
            catch (Exception error) when (error is Win32Exception or IOException or EntryPointNotFoundException or DllNotFoundException)
            { results.Add(name + ": unavailable — " + error.Message); App.Logger.Warn(error, name + " could not be applied."); }
        }
        if (cpu) Apply("CPU priority", () => { _cpu = target.Cpu; if (_cpu is 0x80 or 0x100 or 0x8000) return false; target.Cpu = 0x8000; _cpuSet = true; if (target.Cpu != 0x8000) throw new IOException("The requested priority was not retained."); return true; });
        if (gpu) Apply("GPU priority", () => { _gpu = target.Gpu; if (_gpu >= 3) return false; target.Gpu = 3; _gpuSet = true; if (target.Gpu != 3) throw new IOException("The requested GPU priority was not retained."); return true; });
        void ApplyPower(string name, uint mechanism) => Apply(name, () =>
        {
            var current = target.Power;
            _power ??= current;
            if ((current.ControlMask & mechanism) == mechanism && (current.StateMask & mechanism) == 0) return false;
            target.Power = new(1, current.ControlMask | mechanism, current.StateMask & ~mechanism);
            _ownedPower |= mechanism;
            var applied = target.Power;
            if ((applied.ControlMask & mechanism) != mechanism || (applied.StateMask & mechanism) != 0) throw new IOException("Windows did not retain the requested power policy.");
            return true;
        });
        if (highQos) ApplyPower("Performance QoS", 1);
        if (timers) ApplyPower("Timer policy", 4);
        Result = string.Join(" · ", results);
    }
    internal static SessionScheduling? ForPlayer(int pid)
    {
        var settings = App.Settings.Prop;
        if (!OperatingSystem.IsWindows() || !(settings.AboveNormalCpuPriority || settings.AboveNormalGpuPriority || settings.PreventEcoQoS || settings.HonorTimerRequests)) return null;
        WindowsSchedulingTarget? target = null;
        SessionScheduling? session = null;
        try
        {
            target = new(pid);
            if (!Path.GetFileName(target.Image).Equals(App.RobloxPlayerAppName, StringComparison.OrdinalIgnoreCase) || target.Exited)
                throw new IOException("The launched Roblox player is no longer available.");
            session = new SessionScheduling(target, settings.AboveNormalCpuPriority, settings.AboveNormalGpuPriority, settings.PreventEcoQoS, settings.HonorTimerRequests);
            App.State.Prop.LastSchedulingResult = $"Player {pid}: " + session.Result;
            App.State.SaveSetting(nameof(App.State.Prop.LastSchedulingResult));
            return session;
        }
        catch (Exception error) when (error is Win32Exception or IOException or EntryPointNotFoundException or DllNotFoundException)
        {
            if (session != null) session.Dispose(); else target?.Dispose(); App.Logger.Warn(error, "Player scheduling is unavailable.");
            App.State.Prop.LastSchedulingResult = "Unavailable: " + error.Message;
            App.State.SaveSetting(nameof(App.State.Prop.LastSchedulingResult));
            return null;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_target.Exited) return;
            void Restore(Action action)
            {
                try { action(); }
                catch (Exception error) when (error is Win32Exception or IOException or EntryPointNotFoundException or DllNotFoundException)
                { App.Logger.Warn(error, "A player scheduling setting could not be restored."); }
            }
            if (_cpuSet && _cpu.HasValue) Restore(() => { if (_target.Cpu == 0x8000) _target.Cpu = _cpu.Value; });
            if (_gpuSet && _gpu.HasValue) Restore(() => { if (_target.Gpu == 3) _target.Gpu = _gpu.Value; });
            if (_ownedPower != 0 && _power.HasValue) Restore(() =>
            {
                var current = _target.Power;
                uint unchanged = _ownedPower & current.ControlMask & ~current.StateMask;
                if (unchanged != 0)
                    _target.Power = new(1, (current.ControlMask & ~unchanged) | (_power.Value.ControlMask & unchanged),
                        (current.StateMask & ~unchanged) | (_power.Value.StateMask & unchanged));
            });
        }
        catch (Win32Exception error) { App.Logger.Warn(error, "Player exited before scheduling cleanup."); }
        finally { _target.Dispose(); }
    }
}
