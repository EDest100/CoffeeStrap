// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Froststrap.Integrations;
using Froststrap.UI.Converters;
namespace Froststrap.Services;

internal static class GameArtworkService
{
    public static async Task LoadAsync(List<QuickPlayGameItem> games, CancellationToken cancellation = default)
    {
        var eligible = games.Where(game => game.UniverseId > 0).ToList();
        if (eligible.Count == 0 || cancellation.IsCancellationRequested) return;
        try
        {
            var requests = eligible.Select(game => new ThumbnailRequest { TargetId = (ulong)game.UniverseId,
                Type = ThumbnailType.GameIcon, Size = ThumbnailSize.Detailed, Format = ThumbnailFormat.Png }).ToList();
            var urls = await Thumbnails.GetThumbnailUrlsAsync(requests, cancellation);
            await Task.WhenAll(eligible.Select(async (game, i) =>
            {
                string? url = urls.ElementAtOrDefault(i);
                if (string.IsNullOrWhiteSpace(url) || cancellation.IsCancellationRequested) return;
                var bitmap = await UrlToBitmapConverter.GetBitmapFromCacheOrDownloadAsync(url);
                if (cancellation.IsCancellationRequested) return;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { game.ThumbnailUrl = url; game.ThumbnailBitmap = bitmap; });
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { App.Logger.Warn(error, "Game artwork unavailable."); }
    }
}
