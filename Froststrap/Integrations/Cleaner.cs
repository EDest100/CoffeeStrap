// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Integrations;
internal static class Cleaner
{
    public static readonly IReadOnlyDictionary<string, string?> Directories = new Dictionary<string, string?> { ["FroststrapLogs"] = Paths.Logs };
    public static void DoCleaning() => Services.AppLogRetention.ArchiveExpired();
}
