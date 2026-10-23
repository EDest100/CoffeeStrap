// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Security.Cryptography;

namespace Froststrap.Services;

internal sealed class ModBackupStore(string versionRoot)
{
    private readonly string _root = Path.GetFullPath(versionRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private readonly string _backup = Path.Combine(Paths.Base, "ModBackups", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(versionRoot)))));
    private (string Target, string Original, string Absent) PathsFor(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("A relative mod path is required.");
        string target = Path.GetFullPath(Path.Combine(_root, relative));
        if (!target.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The mod file escapes the player directory.");
        string original = Path.Combine(_backup, relative + ".original");
        return (target, original, original + ".absent");
    }
    public void Capture(string relative)
    {
        var paths = PathsFor(relative);
        if (File.Exists(paths.Original) || File.Exists(paths.Absent)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Original)!);
        if (File.Exists(paths.Target)) File.Copy(paths.Target, paths.Original, false);
        else File.WriteAllText(paths.Absent, "");
    }
    public string? OriginalSource(string relative, bool previouslyModified)
    {
        var paths = PathsFor(relative);
        if (File.Exists(paths.Original)) return paths.Original;
        if (previouslyModified || File.Exists(paths.Absent)) return null;
        return File.Exists(paths.Target) ? paths.Target : null;
    }
    public bool Restore(string relative)
    {
        var paths = PathsFor(relative);
        if (!File.Exists(paths.Original) && !File.Exists(paths.Absent)) return false;
        Utility.Filesystem.AssertReadOnly(paths.Target);
        if (File.Exists(paths.Original)) { Directory.CreateDirectory(Path.GetDirectoryName(paths.Target)!); File.Copy(paths.Original, paths.Target, true); File.Delete(paths.Original); }
        else { File.Delete(paths.Target); File.Delete(paths.Absent); }
        return true;
    }
}
