// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Web;

namespace Froststrap.Services;

internal static class PreferredRegionMatchmaking
{
    public static bool Applies(GameJoinData join, string command) => App.Settings.Prop.EnableRoValra &&
        !string.IsNullOrWhiteSpace(App.Settings.Prop.PreferredRegion) && App.Settings.Prop.PreferredRegion != "Auto" &&
        join.JoinType == GameJoinType.RequestGame && join.PlaceId is > 0 && string.IsNullOrWhiteSpace(join.JobId) && string.IsNullOrWhiteSpace(join.AccessCode) &&
        join.JoinOrigin is not ("friendServerListJoin" or "placesListInHomePage") &&
        !Regex.IsMatch(command, @"(?:[?&]|%26)(?:gameInstanceId|accessCode|linkCode|userId|reservedServerAccessCode)(?:=|%3d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string WithServer(string command, GameJoinData join, string server)
    {
        if (!Guid.TryParse(server, out var id) || join.PlaceId is not > 0) throw new InvalidDataException("A valid public server instance is required.");
        if (command.StartsWith("roblox://", StringComparison.Ordinal)) return command + (command.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "gameInstanceId=" + id.ToString("D");
        if (string.IsNullOrEmpty(join.PlaceLauncherUrl)) throw new InvalidDataException("The launch URL is unavailable.");
        return command.Replace(join.PlaceLauncherUrl, HttpUtility.UrlEncode(UrlBuilder.BuildPlacelauncherUrl(join.PlaceId.Value, id.ToString("D"))), StringComparison.Ordinal);
    }
}
