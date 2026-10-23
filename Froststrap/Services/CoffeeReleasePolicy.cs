// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal sealed record CoffeeRelease(Version Version, string Product, string Platform, Uri Destination, bool Compatible);

internal static class CoffeeReleasePolicy
{
    // Automatic updates remain disabled until a compatible CoffeeStrap feed is verified.
    // An upstream Froststrap release is never a CoffeeStrap update.
    public static Uri? VerifiedDestination => null;
    public static CoffeeRelease? KnownRelease => null;
    public static bool CanShow(Version current, CoffeeRelease? release, Uri? verifiedDestination) =>
        release is { Compatible: true, Product: "CoffeeStrap", Platform: "win-x64" } &&
        release.Version > current && verifiedDestination is { Scheme: "https" } &&
        release.Destination == verifiedDestination;
}
