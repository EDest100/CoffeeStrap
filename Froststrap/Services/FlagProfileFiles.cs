// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal sealed record ProfileApplication(bool Matched, bool Applied, string Name, int Count, string Message);
internal static class FlagProfileFiles
{
    internal const int MaximumBytes = 1048576;
    internal const int MaximumProfiles = 256;
    internal const int MaximumPlaces = 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 16 };
    internal static string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.StartsWith('.') || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':', '<', '>', '|', '?', '"', '*']) >= 0 ||
            Regex.IsMatch(Path.GetFileNameWithoutExtension(name), @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Use a profile name of 1–100 characters without reserved names or path characters.");
        string path = Path.Combine(Paths.SavedFlagProfiles, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked profile files are not supported.");
        return path;
    }
    internal static bool TryPlaceId(string? value, out long id)
    {
        id = 0;
        return value is { Length: > 0 and <= 19 } && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
    internal static IEnumerable<string> Names() => Directory.Exists(Paths.SavedFlagProfiles)
        ? Directory.EnumerateFiles(Paths.SavedFlagProfiles).Select(path => Path.GetFileName(path)).Where(name => !name.StartsWith('.')) : [];
    internal static bool AssignmentsWithinLimits(IReadOnlyDictionary<string, List<string>> assignments)
        => assignments.Count <= MaximumProfiles && assignments.All(pair => pair.Value != null && pair.Value.Count <= MaximumPlaces);
    internal static async Task<Dictionary<string, object>> ReadAsync(string name, CancellationToken cancellationToken)
    {
        string path = Resolve(name);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await ReadStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }
    internal static async Task<Dictionary<string, object>> ReadPresetAsync(string name, CancellationToken cancellationToken)
    {
        using var stream = typeof(FlagProfileFiles).Assembly.GetManifestResourceStream("Froststrap.Resources.PresetFlags." + name)
            ?? throw new FileNotFoundException("The selected built-in profile is unavailable.");
        return await ReadStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }
    private static async Task<Dictionary<string, object>> ReadStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        long length = stream.Length;
        if (length > MaximumBytes) throw new InvalidDataException("Profile exceeds the 1 MiB limit.");
        byte[] bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var flags = JsonSerializer.Deserialize<Dictionary<string, object>>(bytes, Options) ?? throw new InvalidDataException("Profile is empty.");
        if (flags.Count is 0 or > 4096) throw new InvalidDataException("Profile must contain 1–4096 settings.");
        return flags;
    }
    internal static void SaveNew(string name, IReadOnlyDictionary<string, object> flags)
        => Save(name, flags, false);
    internal static void Update(string name, IReadOnlyDictionary<string, object> flags)
        => Save(name, flags, true);
    private static void Save(string name, IReadOnlyDictionary<string, object> flags, bool overwrite)
    {
        string path = Resolve(name);
        if (overwrite && !File.Exists(path)) throw new FileNotFoundException("The selected profile no longer exists.");
        if (!overwrite)
        {
            if (File.Exists(path)) throw new IOException("A snapshot already uses that name. Choose a new name, or use Replace snapshot.");
            if (Names().Take(MaximumProfiles).Count() >= MaximumProfiles)
                throw new InvalidDataException("The library has reached 256 snapshots. Delete an unused snapshot before saving another. Existing snapshots and assignments are unchanged.");
        }
        var accepted = CuratedFlagRegistry.Filter(flags, out _);
        if (accepted.Count == 0) throw new InvalidDataException("Select at least one supported Game Tweak before saving a profile.");
        AtomicFile.WriteText(path, JsonSerializer.Serialize(accepted, Options), overwrite);
    }
    internal static string Serialize(IReadOnlyDictionary<string, object> flags) => JsonSerializer.Serialize(flags, Options);
    internal static void Rename(string name, string newName)
    {
        string source = Resolve(name), destination = Resolve(newName);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected profile no longer exists.");
        if (name == newName) return;
        if (File.Exists(destination) || Directory.Exists(destination) || App.Settings.Prop.ProfilePlaceIds.ContainsKey(newName))
            throw new IOException("That name already has a profile or experience assignments. Choose another name.");
        ChangeFile(name, newName, source, destination);
    }
    internal static void Delete(string name)
    {
        string source = Resolve(name);
        ChangeFile(name, null, source, Path.Combine(Paths.SavedFlagProfiles, "." + Guid.NewGuid().ToString("N") + ".deleted"));
    }
    private static void ChangeFile(string name, string? newName, string source, string destination)
    {
        var previous = App.Settings.Prop.ProfilePlaceIds;
        var next = previous.ToDictionary(pair => pair.Key, pair => pair.Value);
        bool mapped = next.Remove(name, out var places);
        if (mapped && newName != null) next.Add(newName, places!);
        File.Move(source, destination);
        bool saved = false;
        try
        {
            if (mapped)
            {
                App.Settings.Prop.ProfilePlaceIds = next;
                if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.ProfilePlaceIds)))
                    throw new IOException("Could not save experience assignments. Close other file tools and retry.");
                saved = true;
            }
            if (newName == null) File.Delete(destination);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Move(destination, source); }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
            {
                if (!saved) App.Settings.Prop.ProfilePlaceIds = previous;
                throw new IOException($"Could not restore the profile file. Recover it from {destination}. Original operation: {error.Message}", rollback);
            }
            App.Settings.Prop.ProfilePlaceIds = previous;
            if (saved && !App.Settings.TrySaveSetting(nameof(App.Settings.Prop.ProfilePlaceIds)))
            {
                App.Settings.Prop.ProfilePlaceIds = next;
                throw new IOException("The profile file was restored, but its assignments could not be restored. Reassign it after closing other file tools.", error);
            }
            throw;
        }
    }
    internal static async Task<ProfileApplication> ApplyForPlaceAsync(long placeId, string destination, CancellationToken cancellationToken)
    {
        if (placeId <= 0) return new(false, false, "", 0, "No experience was selected.");
        string selected = "Experience profiles";
        try
        {
            var bindings = App.Settings.Prop.ProfilePlaceIds ?? [];
            if (!AssignmentsWithinLimits(bindings)) throw new InvalidDataException("Experience profile assignments exceed their supported bounds. Use the assignment recovery controls in Game Tweaks; using global flags.");
            string[] matches = bindings.Where(pair => pair.Value.Any(value => TryPlaceId(value, out long id) && id == placeId)).Select(pair => pair.Key).Take(2).ToArray();
            if (matches.Length == 0) return new(false, false, "", 0, "No assigned profile; using global flags.");
            if (matches.Length > 1) throw new InvalidDataException("Multiple profiles are assigned to this experience. Remove the duplicate assignment; using global flags.");
            selected = matches[0];
            var source = await ReadAsync(selected, cancellationToken).ConfigureAwait(false);
            var accepted = CuratedFlagRegistry.Filter(source, out var held);
            if (accepted.Count == 0) throw new InvalidDataException("Profile contains no supported settings; using global flags.");
            CuratedFlagRegistry.ApplySelectedOverrides(accepted);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFile.WriteText(destination, JsonSerializer.Serialize(accepted, Options));
            return new(true, true, selected, accepted.Count, $"Prepared {accepted.Count} supported flags; {held.Count} unsupported values excluded. Selected global quality and renderer preferences keep priority.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
        { App.Logger.Warn(error, "Experience profile was not applied; using global flags."); return new(true, false, selected, 0, error.Message); }
    }
}
