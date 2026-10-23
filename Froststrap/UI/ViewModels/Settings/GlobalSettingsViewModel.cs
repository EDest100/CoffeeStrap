// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Enums.GBSPresets;
using System.Windows.Input;
using System.Xml.Linq;

namespace Froststrap.UI.ViewModels.Settings
{
    internal interface IDialogServiceGlobal
    {
        Task OpenGlobalSettingsEditorAsync();
    }

    internal class DefaultGlobalDialogService : IDialogServiceGlobal
    {
        public Task OpenGlobalSettingsEditorAsync()
        {
            return Task.CompletedTask;
        }
    }

    internal class GlobalSettingsViewModel(IDialogServiceGlobal dialogService) : NotifyPropertyChangedViewModel
    {
        private readonly IDialogServiceGlobal _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        public GlobalSettingsViewModel() : this(new DefaultGlobalDialogService())
        {
        }

        public ICommand OpenGlobalSettingsEditorCommand => new AsyncRelayCommand(async () =>
        {
            App.Logger.Info("Opening Global Settings Editor...");
            await _dialogService.OpenGlobalSettingsEditorAsync();
        });

        public static ICommand OpenRobloxFolderCommand => new RelayCommand(() =>
        {
            string targetPath;

            if (OperatingSystem.IsMacOS())
                targetPath = Path.Combine(Paths.UserProfile, "Library", "Roblox");
            else
                targetPath = Paths.Roblox;

            Froststrap.Utility.Threading.ShellExecute(targetPath);
        });
        public static IAsyncRelayCommand ImportCommand => new AsyncRelayCommand<Control>(async c => await ImportSettings(c));
        public static IAsyncRelayCommand ExportCommand => new AsyncRelayCommand<Control>(async c => await ExportSettings(c));

        private static async Task ExportSettings(Control? control)
        {
            App.Logger.Info("Exporting settings");
            if (control is null) return;

            var topLevel = TopLevel.GetTopLevel(control);
            if (topLevel is not Window parentWindow) return;
            var storageProvider = parentWindow.StorageProvider;

            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export GBS Settings",
                SuggestedFileName = "GlobalBasicSettings_13.xml",
                DefaultExtension = ".xml",
                FileTypeChoices =
                [new FilePickerFileType("GBS Settings File") { Patterns = ["*.xml"] }]
            });

            if (file != null)
            {
                string localPath = file.Path.LocalPath;
                bool success = GBSEditor.ExportSettings(localPath);

                if (success)
                {
                    _ = Frontend.ShowMessageBox(string.Format(CultureInfo.InvariantCulture, Strings.Menu_GlobalSettings_Export_Success, localPath), MessageBoxImage.Information);
                }
                else
                {
                    _ = Frontend.ShowMessageBox(Strings.Menu_GlobalSettings_Export_Fail, MessageBoxImage.Error);
                }
            }
        }

        private static async Task ImportSettings(Control? control)
        {
            App.Logger.Info("Importing settings");
            if (control is null) return;

            var topLevel = TopLevel.GetTopLevel(control);
            if (topLevel is not Window parentWindow) return;
            var storageProvider = parentWindow.StorageProvider;

            var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import GBS Settings",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("GBS Settings File") { Patterns = ["*.xml"] }]
            });

            if (result.Count == 0) return;
            var selectedFile = result[0];

            string localPath = selectedFile.Path.LocalPath;

            try
            {
                var doc = XDocument.Load(localPath);
                if (doc.Root?.Name != "roblox")
                {
                    _ = Frontend.ShowMessageBox(Strings.Menu_GlobalSettings_Import_NotGBS, MessageBoxImage.Warning);
                    return;
                }
            }
            catch
            {
                _ = Frontend.ShowMessageBox(Strings.Menu_GlobalSettings_Import_NotXML, MessageBoxImage.Warning);
                return;
            }

            var confirm = await Frontend.ShowMessageBox(
                Strings.Menu_GlobalSettings_Import_Confirmation,
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo);

            if (confirm == MessageBoxResult.Yes)
            {
                bool success = App.GlobalSettings.ImportSettings(localPath);
                if (success)
                {
                    App.GlobalSettings.Load();
                    _ = Frontend.ShowMessageBox(Strings.Menu_GlobalSettings_Import_Success, MessageBoxImage.Information);
                }
                else
                {
                    _ = Frontend.ShowMessageBox(Strings.Menu_GlobalSettings_Import_Fail, MessageBoxImage.Error);
                }
            }
        }

        public bool IsWarningVisible
        {
            get
            {
                return !ReadOnly && (Fullscreen || MaxQualityEnabled || VignetteEnabled || FramerateCap != -1);
            }
        }

        public string WarningTooltipText
        {
            get
            {
                if (ReadOnly) return string.Empty;

                var activeSettings = new List<string>();

                if (Fullscreen) activeSettings.Add("Fullscreen");
                if (MaxQualityEnabled) activeSettings.Add("Max Quality");
                if (VignetteEnabled) activeSettings.Add("Vignette");
                if (FramerateCap != -1) activeSettings.Add("Framerate Cap");

                if (activeSettings.Count == 0) return string.Empty;

                string settingsList = activeSettings.Count > 1
                    ? string.Join(", ", activeSettings.Take(activeSettings.Count - 1)) + " and " + activeSettings.Last()
                    : activeSettings[0];

                return $"Roblox may overwrite {settingsList} while Prevent Override is off.";
            }
        }

        private void UpdateWarningState()
        {
            OnPropertyChanged(nameof(IsWarningVisible));
            OnPropertyChanged(nameof(WarningTooltipText));
        }

        public bool ReadOnly
        {
            get => GBSEditor.GetReadOnly();
            set
            {
                if (value)
                    _ = Frontend.ShowMessageBox(Strings.Menu_GlobalSettings_ReadonlyMessage, MessageBoxImage.Warning);

                App.GlobalSettings.SetReadOnly(value);
                OnPropertyChanged(nameof(ReadOnly));
                UpdateWarningState();
            }
        }

        public int FramerateCap
        {
            get
            {
                if (int.TryParse(App.GlobalSettings.GetPreset("Rendering.FramerateCap"), out int framerate))
                {
                    return framerate;
                }
                else
                    return 60;
            }
            set
            {
                if (value < 1)
                    value = -1;

                App.GlobalSettings.SetPreset("Rendering.FramerateCap", value);
                OnPropertyChanged(nameof(FramerateCap));
                UpdateWarningState();
            }
        }

        public int GraphicsQuality
        {
            get => int.TryParse(App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
            set
            {
                App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel",
                    (LockMaximumRenderDistance ? 10 : Math.Clamp(value, 0, 10)).ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged(nameof(GraphicsQuality));
                GraphicsChanged?.Invoke();
            }
        }

        public bool RenderDistanceOverride
        {
            get => App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") != null;
            set
            {
                if (!value && LockMaximumRenderDistance) LockMaximumRenderDistance = false;
                App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", value ? RenderDistance.ToString(CultureInfo.InvariantCulture) : null);
                OnPropertyChanged(); OnPropertyChanged(nameof(RenderDistance));
                GraphicsChanged?.Invoke();
            }
        }

        public void RefreshGraphics()
        {
            OnPropertyChanged(nameof(LockMaximumRenderDistance));
            OnPropertyChanged(nameof(RenderDistanceOverride)); OnPropertyChanged(nameof(RenderDistance));
            OnPropertyChanged(nameof(TextureQualityOverride)); OnPropertyChanged(nameof(TextureQuality)); OnPropertyChanged(nameof(GraphicsQuality));
            OnPropertyChanged(nameof(MaxQualityEnabled)); OnPropertyChanged(nameof(VignetteEnabled));
        }

        public event Action? GraphicsChanged;
        public bool LockMaximumRenderDistance
        {
            get => App.Settings.Prop.LockMaximumRenderDistance;
            set
            {
                App.Settings.Prop.LockMaximumRenderDistance = value;
                App.Settings.SaveSetting(nameof(App.Settings.Prop.LockMaximumRenderDistance));
                if (value && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == null)
                    App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "1");
                App.GlobalSettings.ApplyDrawDistancePreference();
                RefreshGraphics(); GraphicsChanged?.Invoke();
            }
        }
        public bool TextureQualityOverride
        {
            get => App.FastFlags.GetValue("DFFlagTextureQualityOverrideEnabled") == "True";
            set
            {
                App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", value ? "True" : null);
                App.FastFlags.SetValue("DFIntTextureQualityOverride", value ? TextureQuality.ToString(CultureInfo.InvariantCulture) : null);
                RefreshGraphics(); GraphicsChanged?.Invoke();
            }
        }
        public int TextureQuality
        {
            get => int.TryParse(App.FastFlags.GetValue("DFIntTextureQualityOverride"), out int level) ? Math.Clamp(level, 0, 3) : 3;
            set
            {
                App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", "True");
                App.FastFlags.SetValue("DFIntTextureQualityOverride", Math.Clamp(value, 0, 3).ToString(CultureInfo.InvariantCulture));
                RefreshGraphics(); GraphicsChanged?.Invoke();
            }
        }
        public ICommand PlasticMaximumCommand => new RelayCommand(() =>
        {
            MaximumDistanceLowEffectsCommand.Execute(null); TextureQuality = 3;
            MaxQualityEnabled = false; VignetteEnabled = false;
            App.Settings.Prop.SmoothPlastic = true;
            App.Settings.SaveSetting(nameof(App.Settings.Prop.SmoothPlastic));
            App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", null);
            RefreshGraphics(); GraphicsChanged?.Invoke();
        });

        public ICommand MaximumDistanceLowEffectsCommand => new RelayCommand(() =>
        {
            LockMaximumRenderDistance = true; RenderDistance = 1;
            MaxQualityEnabled = false;
            RefreshGraphics(); GraphicsChanged?.Invoke();
        });

        public ICommand TextureCompatibilityCommand => new RelayCommand(() =>
        {
            App.FastFlags.SetPresetEnum("Rendering.Mode", "D3D11", "True");
            App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", null);
            TextureQuality = 3;
            RefreshGraphics(); GraphicsChanged?.Invoke();
        });

        public int RenderDistance
        {
            get => int.TryParse(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride"), out int value) ? Math.Clamp(value, 1, 21) : 1;
            set
            {
                App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", Math.Clamp(value, 1, 21).ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged(); OnPropertyChanged(nameof(RenderDistanceOverride));
                GraphicsChanged?.Invoke();
            }
        }

        public bool Fullscreen
        {
            get => App.GlobalSettings.GetPreset("Rendering.Fullscreen")?.ToUpperInvariant() == "TRUE";
            set
            {
                App.GlobalSettings.SetPreset("Rendering.Fullscreen", value);
                OnPropertyChanged(nameof(Fullscreen));
                UpdateWarningState();
            }
        }

        public bool MaxQualityEnabled
        {
            get => App.GlobalSettings.GetPreset("Rendering.MaxQualityEnabled")?.ToUpperInvariant() == "TRUE";
            set
            {
                App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", value);
                OnPropertyChanged(nameof(MaxQualityEnabled));
                UpdateWarningState();
                GraphicsChanged?.Invoke();
            }
        }

        public bool VignetteEnabled
        {
            get
            {
                bool setting1 = App.GlobalSettings.GetPreset("Rendering.VignetteEnabled")?.ToUpperInvariant() == "TRUE";
                bool setting2 = App.GlobalSettings.GetPreset("Rendering.VignetteEnableOption")?.ToUpperInvariant() == "TRUE";

                return setting1 && setting2;
            }
            set
            {
                string val = value.ToString().ToUpperInvariant();

                App.GlobalSettings.SetPreset("Rendering.VignetteEnabled", val);
                App.GlobalSettings.SetPreset("Rendering.VignetteEnableOption", val);

                OnPropertyChanged(nameof(VignetteEnabled));
                UpdateWarningState();
            }
        }

        public static double MasterVolume
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("Audio.MasterVolume"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0;
            set => App.GlobalSettings.SetPreset("Audio.MasterVolume",
                value.ToString(CultureInfo.InvariantCulture));
        }
        public static double MasterVolumeStudio
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("Audio.MasterVolumeStudio"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0;
            set => App.GlobalSettings.SetPreset("Audio.MasterVolumeStudio",
                value.ToString(CultureInfo.InvariantCulture));
        }
        public static double PartyVoiceVolume
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("Audio.PartyChatVolume"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0;
            set => App.GlobalSettings.SetPreset("Audio.PartyChatVolume",
                value.ToString(CultureInfo.InvariantCulture));
        }
        public static double VoiceChatVolume
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("Audio.VoiceChatVolume"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0;
            set => App.GlobalSettings.SetPreset("Audio.VoiceChatVolume",
                value.ToString(CultureInfo.InvariantCulture));
        }

        public static double MouseSensitivity
        {
            get => double.TryParse(App.GlobalSettings.GetPreset("User.MouseSensitivity"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1.0;
            set => App.GlobalSettings.SetPreset("User.MouseSensitivity",
                value.ToString(CultureInfo.InvariantCulture));
        }

        public static bool ShiftLock
        {
            get => App.GlobalSettings.GetPreset("User.ShiftLock") == "1";
            set => App.GlobalSettings.SetPreset("User.ShiftLock", value ? "1" : "0");
        }

        public string MouseSensitivityFirstPersonX
        {
            get => App.GlobalSettings.GetVectorValue("User.MouseSensitivityFirstPerson", "X");
            set
            {
                App.GlobalSettings.SetVectorValue("User.MouseSensitivityFirstPerson", "X", value);
                OnPropertyChanged(nameof(MouseSensitivityFirstPersonX));
            }
        }

        public string MouseSensitivityFirstPersonY
        {
            get => App.GlobalSettings.GetVectorValue("User.MouseSensitivityFirstPerson", "Y");
            set
            {
                App.GlobalSettings.SetVectorValue("User.MouseSensitivityFirstPerson", "Y", value);
                OnPropertyChanged(nameof(MouseSensitivityFirstPersonY));
            }
        }
        public string MouseSensitivityThirdPersonX
        {
            get => App.GlobalSettings.GetVectorValue("User.MouseSensitivityThirdPerson", "X");
            set
            {
                App.GlobalSettings.SetVectorValue("User.MouseSensitivityThirdPerson", "X", value);
                OnPropertyChanged(nameof(MouseSensitivityThirdPersonX));
            }
        }

        public string MouseSensitivityThirdPersonY
        {
            get => App.GlobalSettings.GetVectorValue("User.MouseSensitivityThirdPerson", "Y");
            set
            {
                App.GlobalSettings.SetVectorValue("User.MouseSensitivityThirdPerson", "Y", value);
                OnPropertyChanged(nameof(MouseSensitivityThirdPersonY));
            }
        }

        public static bool CameraYInverted
        {
            get => App.GlobalSettings.GetPreset("User.CameraYInverted")?.ToUpperInvariant() == "TRUE";
            set => App.GlobalSettings.SetPreset("User.CameraYInverted", value);
        }

        public static int HapticStrength
        {
            get => int.TryParse(App.GlobalSettings.GetPreset("User.HapticStrength"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
            set => App.GlobalSettings.SetPreset("User.HapticStrength",
                value.ToString(CultureInfo.InvariantCulture));
        }

        public int UITransparency
        {

            get => int.TryParse(App.GlobalSettings.GetPreset("UI.Transparency"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
            set
            {
                App.GlobalSettings.SetPreset("UI.Transparency",
                    value.ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged(nameof(UITransparency));
            }
        }

        public static bool ReducedMotion
        {
            get => App.GlobalSettings.GetPreset("UI.ReducedMotion")?.ToUpperInvariant() == "TRUE";
            set => App.GlobalSettings.SetPreset("UI.ReducedMotion", value);
        }

        public static IReadOnlyDictionary<FontSize, string?> FontSizes => GBSEditor.FontSizes;
        public static FontSize SelectedFontSize
        {
            get => FontSizes.FirstOrDefault(x => x.Value == App.GlobalSettings.GetPreset("UI.FontSize")).Key;
            set => App.GlobalSettings.SetPreset("UI.FontSize", FontSizes[value]);
        }

        public static IReadOnlyDictionary<PlayerListLayOut, string?> PlayerListLayOuts => GBSEditor.PlayerListLayOuts;
        public static PlayerListLayOut SelectedPlayerListLayOut
        {
            get => PlayerListLayOuts.FirstOrDefault(x => x.Value == App.GlobalSettings.GetPreset("UI.PlayerListLayOut")).Key;
            set => App.GlobalSettings.SetPreset("UI.PlayerListLayOut", PlayerListLayOuts[value]);
        }

        public static bool PerformanceStatsVisible
        {
            get => App.GlobalSettings.GetPreset("Misc.PerformanceStatsVisible")?.ToUpperInvariant() == "TRUE";
            set => App.GlobalSettings.SetPreset("Misc.PerformanceStatsVisible", value);
        }

        public static bool ChatTranslationEnabled
        {
            get => App.GlobalSettings.GetPreset("Misc.ChatTranslationEnabled")?.ToUpperInvariant() == "TRUE";
            set => App.GlobalSettings.SetPreset("Misc.ChatTranslationEnabled", value);
        }

        public static bool ChatTranslationFTUXShown
        {
            get => App.GlobalSettings.GetPreset("Misc.ChatTranslationFTUXShown")?.ToUpperInvariant() == "TRUE";
            set => App.GlobalSettings.SetPreset("Misc.ChatTranslationFTUXShown", value);
        }

        public static bool VREnabled
        {
            get => App.GlobalSettings.GetPreset("User.VREnabled")?.ToUpperInvariant() == "TRUE";
            set => App.GlobalSettings.SetPreset("User.VREnabled", value);
        }
    }
}
