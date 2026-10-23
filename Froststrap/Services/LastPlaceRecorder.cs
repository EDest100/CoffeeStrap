// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Text.RegularExpressions;
namespace Froststrap.Services;

internal static partial class LastPlaceRecorder
{
    [GeneratedRegex(@"! Joining game '[^']*' place (\d+)\b", RegexOptions.CultureInvariant, 100)]
    private static partial Regex JoinLine();
    public static long? ParsePlace(string line) => JoinLine().Match(line) is { Success: true } match &&
        long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long place) && place > 0 ? place : null;

    internal static async Task<int> ReadAvailableAsync(TextReader reader, Action<long> record, CancellationToken cancellation = default)
    {
        int count = 0;
        while (await reader.ReadLineAsync(cancellation) is { } line)
            if (ParsePlace(line) is { } place) { record(place); count++; }
        return count;
    }

    public static async Task ObserveAsync(string? log, CancellationToken cancellation, Action<string>? observeLine = null)
    {
        if (string.IsNullOrEmpty(log)) return;
        try
        {
            for (int attempt = 0; !File.Exists(log) && attempt < 60; attempt++) await Task.Delay(500, cancellation);
            if (!File.Exists(log)) { App.Logger.Warn("The player log did not appear; using the launch destination for the title."); return; }
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (!cancellation.IsCancellationRequested)
            {
                if (stream.Length < stream.Position) { stream.Position = 0; reader.DiscardBufferedData(); }
                int lines = 0;
                while (lines++ < 2000 && await reader.ReadLineAsync(cancellation) is { } line)
                {
                    if (ParsePlace(line) is { } place) GameLaunchService.RecordPlace(place);
                    observeLine?.Invoke(line);
                }
                await Task.Delay(500, cancellation);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException error) { App.Logger.Warn(error, "Last-place log became unavailable."); }
        catch (UnauthorizedAccessException error) { App.Logger.Warn(error, "Last-place log is not readable."); }
    }
}
