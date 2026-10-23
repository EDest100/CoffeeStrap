// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.IO.Compression;

namespace Froststrap.Services;

internal static class ModArchiveInstaller
{
    private static readonly HashSet<string> ContentRoots = new(StringComparer.OrdinalIgnoreCase) { "content", "ExtraContent", "PlatformContent" };
    public static Task InstallAsync(string archive, string destination) => Task.Run(async () =>
    {
        string final = Path.GetFullPath(destination);
        string parent = Path.GetDirectoryName(final)!;
        string allowedMods = Path.GetFullPath(Paths.Modifications) + Path.DirectorySeparatorChar;
        string allowedThemes = Path.GetFullPath(Paths.CustomThemes) + Path.DirectorySeparatorChar;
        if (!final.StartsWith(allowedMods, StringComparison.OrdinalIgnoreCase) && !final.StartsWith(allowedThemes, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The mod destination is outside the library.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".download-" + Guid.NewGuid().ToString("N"));
        string unpack = Path.Combine(stage, "unpack"), backup = Path.Combine(stage, "previous");
        Directory.CreateDirectory(stage);
        bool committed = false;
        try
        {
            await ZipFile.ExtractToDirectoryAsync(archive, unpack);
            string[] entries = Directory.GetFileSystemEntries(unpack);
            if (entries.Length == 0) throw new InvalidDataException("The archive is empty.");
            string source = entries.Length == 1 && Directory.Exists(entries[0]) && !ContentRoots.Contains(Path.GetFileName(entries[0])) ? entries[0] : unpack;
            if (final.StartsWith(allowedMods, StringComparison.OrdinalIgnoreCase)) source = ModAssetPreparation.ContentRoot(source);
            if (final.StartsWith(allowedMods, StringComparison.OrdinalIgnoreCase) && Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Any(file => !ModFilePolicy.IsAllowed(Path.GetRelativePath(source, file))))
                throw new InvalidDataException("Shader and executable mods are not supported. Choose a content replacement mod.");
            if (final.StartsWith(allowedMods, StringComparison.OrdinalIgnoreCase) && !ModAssetPreparation.Files(source).Any())
                throw new InvalidDataException("This archive has no supported Roblox content. It must contain content, PlatformContent or ExtraContent folders.");
            if (Directory.Exists(final)) await MoveWithSharingRetryAsync(final, backup);
            try { await MoveWithSharingRetryAsync(source, final); committed = true; }
            catch { if (Directory.Exists(backup) && !Directory.Exists(final)) await MoveWithSharingRetryAsync(backup, final); throw; }
        }
        finally
        {
            // All cleanup targets are generated children of the verified local library root.
            if (committed || !Directory.Exists(backup)) Directory.Delete(stage, true);
        }
    });
    private static async Task MoveWithSharingRetryAsync(string source, string destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException error) when (OperatingSystem.IsWindows() && attempt < 3 && (error.HResult & 0xffff) is 5 or 32 or 33)
            {
                App.Logger.Info("Mod commit encountered a Windows file lock; retrying the directory move.");
                await Task.Delay(50 << attempt);
            }
        }
    }
}
