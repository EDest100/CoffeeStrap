// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal static class ModFilePolicy
{
    private static readonly HashSet<string> ShaderExtensions = new(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe", ".asi", ".fx", ".fxh", ".hlsl", ".cso", ".spv" };
    public static bool IsAllowed(string relativePath) => !ShaderExtensions.Contains(Path.GetExtension(relativePath)) &&
        !relativePath.Split(['/', '\\']).Any(part => part.Equals("shaders", StringComparison.OrdinalIgnoreCase) || part.Equals("reshade-shaders", StringComparison.OrdinalIgnoreCase));

    public static string InstalledPath(string root, string folder)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, folder));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || Path.GetRelativePath(root, path).Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The installed mod is outside the mod library.");
        return path;
    }
}
