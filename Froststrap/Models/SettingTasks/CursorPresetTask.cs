// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Models.SettingTasks;

/// <summary>A custom file selection supersedes pending built-in presets rather than being overwritten on Save.</summary>
internal sealed class CursorPresetTask : EnumModPresetTask<CursorType>
{
    private readonly string[] _paths;
    private static string CursorFolder => Path.Combine(Paths.Modifications, "content", "textures", "Cursors", "KeyboardMouse");
    private static bool HasFiles => Directory.Exists(CursorFolder) && Directory.GetFiles(CursorFolder, "*.png").Length > 0;
    public new IEnumerable<CursorType> Selections => base.Selections.Where(value => value != CursorType.Custom || HasFiles);
    public CursorPresetTask(string name, Dictionary<CursorType, Dictionary<string, string>> map) : base(name, map)
    { _paths = map.Values.SelectMany(value => value.Keys).Distinct().ToArray(); if (OriginalState == CursorType.Default && HasFiles) AcceptCustomFiles(); }
    public void AcceptCustomFiles()
    {
        if (!HasFiles) return;
        OriginalState = CursorType.Custom;
        NewState = CursorType.Custom;
    }
    public override void Execute()
    {
        if (NewState == CursorType.Custom) return;
        if (NewState != CursorType.Default) { base.Execute(); return; }
        foreach (string relative in _paths)
        {
            string file = Path.Combine(Paths.Modifications, relative.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(file)) { Filesystem.AssertReadOnly(file); File.Delete(file); }
        }
        OriginalState = CursorType.Default;
    }
}
