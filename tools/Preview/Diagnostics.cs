// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Froststrap.UI.Elements.Controls;

internal static partial class Program
{
    private static readonly ConcurrentQueue<object> Diagnostics = new();
    private sealed class PreviewDiagnosticsTarget : NLog.Targets.Target
    {
        protected override void Write(NLog.LogEventInfo entry)
        {
            // Some upstream callers pass exceptions as formatting arguments rather than
            // LogEventInfo.Exception. Preserve these details in the isolated review log.
            var exception = entry.Exception ?? entry.Parameters?.OfType<Exception>().FirstOrDefault();
            Console.WriteLine(entry.FormattedMessage + " " + exception);
            Diagnostics.Enqueue(new { message = entry.FormattedMessage, exceptionType = exception?.GetType().FullName,
                exception = exception?.ToString() });
        }
    }
    private static void SaveDiagnostics() => File.WriteAllText(Path.Combine(_output, "diagnostics.json"),
        JsonSerializer.Serialize(Diagnostics.ToArray(), new JsonSerializerOptions { WriteIndented = true }));

    private static void VerifyCardPanels(Window window, string name)
    {
        var panels = window.GetVisualDescendants().OfType<SettingsCardPanel>()
            .Where(p => p.IsEffectivelyVisible && p.Bounds.Width > 1 && p.Bounds.Height > 1 &&
                !p.GetVisualAncestors().OfType<Border>().Any(b => b.ClipToBounds && b.Bounds.Height < 1)).ToArray();
        bool valid = true;
        foreach (var panel in panels)
        {
            var children = panel.Children.Where(c => c.IsVisible).ToArray();
            valid &= children.All(c => c.Bounds.X >= -1 && c.Bounds.Right <= panel.Bounds.Width + 1);
            valid &= children.SelectMany((a, i) => children.Skip(i + 1).Select(b => (a, b)))
                .All(pair => !pair.a.Bounds.Deflate(1).Intersects(pair.b.Bounds.Deflate(1)));
        }
        Check(valid, "rendered card panels fit without overlap: " + name, new { panels = panels.Length });
    }
}
