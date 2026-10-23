// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Utility;

internal static class AtomicFile
{
    internal static void WriteText(string path, string text, bool overwrite = true)
        => Write(path, temporary => File.WriteAllText(temporary, text), overwrite);

    internal static void Write(string path, Action<string> writeTemporary, bool overwrite = true)
    {
        string folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        string temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            writeTemporary(temporary);
            File.Move(temporary, path, overwrite);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { App.Logger.Warn(error, "Could not remove a temporary settings file."); }
        }
    }
}
