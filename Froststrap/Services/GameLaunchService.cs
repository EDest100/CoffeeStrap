// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Froststrap.Models;

namespace Froststrap.Services;

internal sealed class GameLaunchService(Action<Uri> open)
{
    public static event EventHandler? PinsChanged;
    public static GameLaunchService Default { get; } = new(uri =>
        Process.Start(LaunchEntry.CreateStartInfo(LaunchMode.Player, uri.AbsoluteUri)));

    public static Uri BuildUri(long placeId, string? jobId = null, string? accessCode = null, string? linkCode = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(placeId);
        string value = $"roblox://experiences/start?placeId={placeId}";
        // Preserve explicit private/reserved/instance joins; public preference must never replace them.
        if (!string.IsNullOrWhiteSpace(accessCode)) value += "&accessCode=" + Uri.EscapeDataString(accessCode);
        else if (Guid.TryParse(jobId, out var job)) value += "&gameInstanceId=" + job.ToString("D");
        else if (!string.IsNullOrWhiteSpace(linkCode)) value += "&linkCode=" + Uri.EscapeDataString(linkCode);
        return new Uri(value);
    }

    public void Launch(long placeId, string? name = null, string? jobId = null, string? accessCode = null, string? linkCode = null)
    {
        var uri = BuildUri(placeId, jobId, accessCode, linkCode);
        open(uri);
        RecordPlace(placeId, name); // Store no authentication ticket, IP, private code or server access token.
    }

    public static void RecordPlace(long placeId, string? name = null)
    {
        if (placeId <= 0) return;
        bool changed = App.State.Prop.LastPlaceId != placeId;
        App.State.Prop.LastPlaceId = placeId;
        App.State.Prop.LastGameName = !string.IsNullOrWhiteSpace(name) ? name
            : changed || string.IsNullOrWhiteSpace(App.State.Prop.LastGameName) ? $"Place {placeId}" : App.State.Prop.LastGameName;
        App.State.SaveSetting(nameof(App.State.Prop.LastPlaceId));
        App.State.SaveSetting(nameof(App.State.Prop.LastGameName));
    }

    public static void SetPinned(QuickPlayGameItem game, bool pinned)
    {
        var pins = App.State.Prop.QuickLaunchPins;
        pins.RemoveAll(pin => pin.PlaceId == game.PlaceId);
        if (pinned && game.PlaceId > 0)
            pins.Add(new QuickLaunchPin { PlaceId = game.PlaceId, UniverseId = game.UniverseId, Name = game.Name, ThumbnailUrl = game.ThumbnailUrl });
        App.State.SaveSetting(nameof(App.State.Prop.QuickLaunchPins));
        PinsChanged?.Invoke(null, EventArgs.Empty);
    }
}
