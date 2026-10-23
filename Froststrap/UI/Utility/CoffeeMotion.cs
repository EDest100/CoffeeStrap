// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;

namespace Froststrap.UI.Utility;

/// <summary>A finite, interruptible UI animation. No timer runs while idle.</summary>
internal sealed class CoffeeMotion(Visual owner, Action<double> apply, Action? completed = null)
{
    // The preview assembly can supply a controlled clock. Production always uses
    // the system monotonic timestamp provider and the same frame callback path.
    internal static TimeProvider TimeSource { get; set; } = TimeProvider.System;
    private readonly TimeProvider _time = TimeSource;
    private long _started;
    private double _start;
    private double _duration;
    private bool _spring;
    private bool _framePending;
    private int _generation;
    public double Value { get; private set; }
    public double Target { get; private set; }
    public bool IsRunning { get; private set; }
    public int TickCount { get; private set; }
    internal object Diagnostics => new { clock = _time.GetType().Name, elapsed = _time.GetElapsedTime(_started).TotalMilliseconds,
        Value, Target, IsRunning, TickCount, pending = _framePending, generation = _generation };
    public static bool Enabled => !App.Settings.Prop.DisableAnimations;

    public void Jump(double value)
    {
        Stop();
        Value = Target = value;
        apply(value);
        completed?.Invoke();
    }

    public void To(double target, double milliseconds = 190, bool spring = false)
    {
        if (!Enabled || milliseconds <= 0) { Jump(target); return; }
        if (IsRunning && Math.Abs(Target - target) < .01) return;
        if (Math.Abs(Value - target) < .01) { Jump(target); return; }
        _start = Value;
        Target = target;
        _duration = milliseconds;
        _spring = spring;
        _started = _time.GetTimestamp();
        IsRunning = true;
        QueueFrame();
    }

    private void QueueFrame()
    {
        if (_framePending || !IsRunning) return;
        if (TopLevel.GetTopLevel(owner) is not { } root) { Jump(Target); return; }
        _framePending = true;
        int generation = _generation;
        // A segment's first sample may equal the displayed value. Keep the render
        // loop awake while active even when that sample did not change a property.
        owner.InvalidateVisual();
        root.RequestAnimationFrame(_ =>
        {
            if (generation != _generation) return;
            _framePending = false;
            Tick();
        });
    }

    private void Tick()
    {
        if (!IsRunning) return;
        TickCount++;
        if (!Enabled) { Jump(Target); return; }
        double t = Math.Clamp(_time.GetElapsedTime(_started).TotalMilliseconds / _duration, 0, 1);
        double progress = _spring ? 1 - Math.Exp(-8 * t) * Math.Cos(10 * t) : 1 - Math.Pow(1 - t, 3);
        Value = t >= 1 ? Target : _start + (Target - _start) * progress;
        apply(Value);
        if (t >= 1) { Stop(); completed?.Invoke(); }
        else QueueFrame();
    }

    public void Stop() { IsRunning = false; _framePending = false; _generation++; }
}
