// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Text.RegularExpressions;
namespace Froststrap.Services;

/// <summary>Recoverable retention of identified CoffeeStrap logs, without recursive traversal or deletion.</summary>
internal static partial class AppLogRetention
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:\.\d+)?\.log$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveName();
    public static void ArchiveExpired() => ArchiveExpired(Paths.Logs, DateTime.UtcNow);
    internal static int ArchiveExpired(string root, DateTime now)
    {
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return 0;
        string fullRoot = Path.GetFullPath(root);
        string archive = Path.Combine(fullRoot, "Retained");
        if (Directory.Exists(archive) && (File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0) return 0;
        int moved = 0;
        foreach (string file in Directory.EnumerateFiles(fullRoot, "*.log", SearchOption.TopDirectoryOnly).Take(200))
        {
            try
            {
                if (!ArchiveName().IsMatch(Path.GetFileName(file)) ||
                    (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || File.GetLastWriteTimeUtc(file) >= now.AddDays(-1)) continue;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    char[] header = new char[8192]; int length = reader.ReadBlock(header, 0, header.Length);
                    if (!new string(header, 0, length).Contains("Starting CoffeeStrap ", StringComparison.Ordinal)) continue;
                }
                Directory.CreateDirectory(archive);
                string destination = Path.Combine(archive, Path.GetFileName(file));
                if (File.Exists(destination)) destination = Path.Combine(archive, Path.GetFileNameWithoutExtension(file) + "-" + Guid.NewGuid().ToString("N") + ".log");
                File.Move(file, destination); moved++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return moved;
    }
}
