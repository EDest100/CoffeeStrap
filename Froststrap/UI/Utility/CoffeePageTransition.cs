// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Threading;
namespace Froststrap.UI.Utility;

/// <summary>Finite concurrent crossfade with synchronous UI-thread endpoint cleanup.</summary>
internal sealed class CoffeePageTransition : IPageTransition
{
    public Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (to == null || ReferenceEquals(from, to) || cancellationToken.IsCancellationRequested) return Task.CompletedTask;
        if (!CoffeeMotion.Enabled)
        { if (from != null) { from.IsVisible = false; from.Opacity = 1; } to.IsVisible = true; to.Opacity = 1; return Task.CompletedTask; }
        var prior = to.RenderTransform;
        var shift = new TranslateTransform(0, 14);
        to.RenderTransform = shift; to.IsVisible = true; to.Opacity = .3;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoffeeMotion? motion = null;
        CancellationTokenRegistration registration = default;
        bool finished = false;
        void Finish()
        {
            if (finished) return;
            finished = true;
            motion?.Stop(); registration.Unregister();
            if (ReferenceEquals(to.RenderTransform, shift)) { to.RenderTransform = prior; to.Opacity = 1; }
            if (from != null) { from.Opacity = 1; from.IsVisible = false; }
            // Final visual state precedes task completion. No later continuation can
            // resurrect an outgoing page or overwrite a rapidly reversed transition.
            completion.TrySetResult();
        }
        motion = new CoffeeMotion(to, value =>
        {
            to.Opacity = .3 + .7 * value; shift.Y = 14 * (1 - value);
            if (from != null) from.Opacity = 1 - value;
        }, Finish);
        registration = cancellationToken.Register(() =>
        {
            if (Dispatcher.UIThread.CheckAccess()) Finish();
            else Dispatcher.UIThread.Post(Finish, DispatcherPriority.Send);
        });
        if (!finished) motion.To(1, 220);
        return completion.Task;
    }
}
