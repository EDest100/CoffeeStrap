// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Nodes;

namespace Froststrap
{
    internal class JsonManager<T>(string? className = null) where T : class, new()
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        protected T _prop = new();

        public virtual T Prop
        {
            get => _prop;
            set => _prop = value;
        }

        public string? LastFileHash { get; private set; }

        public bool Loaded { get; protected set; }

        public virtual string ClassName { get; } = string.IsNullOrEmpty(className) ? typeof(T).Name : className;

        public virtual string ProfilesLocation => Path.Combine(Paths.Base, "Profiles.json");

        public virtual string FileName => $"{ClassName}.json";

        public virtual string FileLocation => Path.Combine(Paths.Base, FileName);

        public bool IsSaved => File.Exists(FileLocation);

        protected string? _savedHash;

        protected virtual string ComputeHash(T obj)
        {
            string json = JsonSerializer.Serialize(obj, _jsonOptions);
            return FastHash.FromString(json);
        }

        public bool HasUnsavedChanges
        {
            get
            {
                if (_savedHash == null) return true;
                return ComputeHash(Prop) != _savedHash;
            }
        }

        public virtual bool Load(bool alertFailure = true)
        {
            App.Logger.Info($"Loading from {FileLocation}...");

            try
            {
                if (File.Exists(FileLocation))
                {
                    string contents = File.ReadAllText(FileLocation);

                    T settings = JsonSerializer.Deserialize<T>(contents, _jsonOptions)
                        ?? throw new InvalidOperationException($"{ClassName} deserialization returned null.");

                    _prop = settings;
                    Loaded = true;
                    LastFileHash = FastHash.FromString(contents);
                    _savedHash = ComputeHash(_prop);

                    App.Logger.Info("Loaded successfully!");
                    return true;
                }
                else
                {
                    App.Logger.Error($"Could not find {FileLocation}.");
                    Loaded = true;
                    _savedHash = ComputeHash(_prop);
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger.Error("Failed to load!");
                App.Logger.Error(ex);

                if (alertFailure)
                {
                    string message = ClassName switch
                    {
                        nameof(Settings) => Strings.JsonManager_SettingsLoadFailed,
                        nameof(FastFlagManager) => Strings.JsonManager_FastFlagsLoadFailed,
                        _ => string.Empty
                    };

                    if (!string.IsNullOrEmpty(message))
                        _ = Frontend.ShowMessageBox($"{message}\n\n{ex.Message}", MessageBoxImage.Warning);

                    try
                    {
                        if (File.Exists(FileLocation))
                            File.Copy(FileLocation, FileLocation + ".bak", true);
                    }
                    catch (Exception copyEx)
                    {
                        App.Logger.Error($"Failed to create backup file: {FileLocation}.bak");
                        App.Logger.Error(copyEx);
                    }
                }

                Loaded = true;
                Save();

                return false;
            }
        }

        public virtual bool Save()
            => Save(true);

        public virtual bool Save(bool alertFailure)
        {
            if (!HasUnsavedChanges && File.Exists(FileLocation))
            {
                App.Logger.Info("No changes, skipping save.");
                return false;
            }

            App.Logger.Info($"Saving to {FileLocation}...");

            try
            {
                string contents = JsonSerializer.Serialize(Prop, _jsonOptions);
                string savedHash = ComputeHash(Prop);
                AtomicFile.WriteText(FileLocation, contents);

                LastFileHash = FastHash.FromString(contents);
                _savedHash = savedHash;
                App.Logger.Info("Save complete!");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.Error("Failed to save");
                App.Logger.Error(ex);

                string errorMessage = string.Format(CultureInfo.InvariantCulture, Strings.Bootstrapper_JsonManagerSaveFailed, ClassName, ex.Message);
                if (alertFailure) _ = Frontend.ShowMessageBox(errorMessage, MessageBoxImage.Warning);

                return false;
            }
        }

        public virtual void SaveSetting(string SettingName)
            => TrySaveSetting(SettingName);

        public bool TrySaveSetting(string SettingName)
        {
            if (string.IsNullOrEmpty(SettingName))
            {
                return Save();
            }

            App.Logger.Info($"Saving setting '{SettingName}' to {FileLocation}");

            try
            {
                JsonObject existingJson = [];
                if (File.Exists(FileLocation))
                {
                    string existingContent = File.ReadAllText(FileLocation);
                    if (!string.IsNullOrWhiteSpace(existingContent))
                    {
                        using var doc = JsonDocument.Parse(existingContent);
                        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Settings must be a JSON object.");
                        existingJson = JsonSerializer.SerializeToNode(doc.RootElement)?.AsObject() ?? [];
                    }
                }

                var currentJson = JsonSerializer.SerializeToNode(Prop, _jsonOptions)?.AsObject();
                _ = currentJson ?? throw new InvalidOperationException("Failed to serialize current object.");

                if (currentJson.TryGetPropertyValue(SettingName, out JsonNode? value))
                {
                    existingJson[SettingName] = value?.DeepClone();
                    App.Logger.Info($"Updated Setting '{SettingName}'");
                }
                else
                {
                    App.Logger.Error($"Setting '{SettingName}' not found – aborting save.");
                    return false;
                }

                string contents = existingJson.ToJsonString(_jsonOptions);
                T savedState = JsonSerializer.Deserialize<T>(contents, _jsonOptions) ?? throw new JsonException("Saved settings are empty.");
                string savedHash = ComputeHash(savedState);
                AtomicFile.WriteText(FileLocation, contents);
                LastFileHash = FastHash.FromString(contents);
                _savedHash = savedHash;
                App.Logger.Info("SaveSetting complete!");
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                App.Logger.Error("Failed to save Setting");
                App.Logger.Error(ex);
                return false;
            }
        }

        public virtual void Delete()
        {
            try
            {
                if (File.Exists(FileLocation))
                {
                    File.Delete(FileLocation);

                    Loaded = false;
                    App.Logger.Info("Delete complete!");
                }
                else
                {
                    App.Logger.Error("File does not exist on disk");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                App.Logger.Error("Failed to delete");
                App.Logger.Error(ex);

                // should we notify?
            }
        }

        /// <summary>
        /// Is the file on disk different to the one deserialised during this session?
        /// </summary>
        public bool HasFileOnDiskChanged()
        {
            // file was deleted after being loaded
            if (!File.Exists(FileLocation))
                return !string.IsNullOrEmpty(LastFileHash);

            // check if a file has been created since launch
            if (string.IsNullOrEmpty(LastFileHash) && File.Exists(FileLocation))
                return true;

            return LastFileHash != FastHash.FromFile(FileLocation);
        }
    }

    /// <summary>
    /// <see cref="JsonManager{T}"/> that will automatically load in the JSON if it has not been already
    /// </summary>
    /// <typeparam name="T">Class</typeparam>
    internal class LazyJsonManager<T>(string? className) : JsonManager<T>(className) where T : class, new()
    {
        public override T Prop
        {
            get
            {
                if (!Loaded)
                    Load();

                return _prop;
            }
            set
            {
                _prop = value;
                Loaded = true;
            }
        }
    }
}
