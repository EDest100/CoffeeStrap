// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.Elements.Settings.Pages;
using Froststrap.UI.ViewModels.Settings.FastFlags;
using Froststrap.UI.ViewModels.Settings.GlobalSettings;
using Froststrap.UI.ViewModels.Settings.Mods;
using System.Collections.ObjectModel;
using LucideAvalonia.Enum;
using System.Collections.Specialized;
using System.Windows.Input;

namespace Froststrap.UI.ViewModels.Settings
{
    internal class BreadcrumbItemModel
    {
        public string Content { get; set; } = string.Empty;
        public string? Tag { get; set; }
        public bool IsLast { get; set; }
    }

    internal class MainWindowViewModel : ObservableObject
    {
        private bool _isNotLinux = !OperatingSystem.IsLinux();
        public bool IsNotLinux { get => _isNotLinux; }

        private object? _currentPage;
        public object? CurrentPage
        {
            get => _currentPage;
            set => SetProperty(ref _currentPage, value);
        }

        private LucideIconNames _currentPageIcon;
        public LucideIconNames CurrentPageIcon
        {
            get => _currentPageIcon;
            set => SetProperty(ref _currentPageIcon, value);
        }

        public static bool HasUnsavedChanges =>
            App.Settings.HasUnsavedChanges ||
            App.State.HasUnsavedChanges ||
            App.FastFlags.HasUnsavedChanges ||
            (OperatingSystem.IsLinux() && App.SoberSettings.HasUnsavedChanges) ||
            App.GlobalSettings.HasUnsavedChanges ||
            App.PendingSettingTasks.Count > 0;

        private LaunchMode _selectedLaunchMode = LaunchMode.Player;

        private string _selectedPage = "integrations";
        public string SelectedPage { get => _selectedPage; set { if (SetProperty(ref _selectedPage, value)) OnPropertyChanged(nameof(IsHome)); } }
        public bool IsHome => SelectedPage == "home";

        private string _currentPageTitle = Strings.Menu_Integrations_Title;
        public string CurrentPageTitle { get => _currentPageTitle; set => SetProperty(ref _currentPageTitle, value); }

        private string _currentPageDescription = "";
        public string CurrentPageDescription { get => _currentPageDescription; set => SetProperty(ref _currentPageDescription, value); }

        public SearchBarViewModel SearchBar { get; }

        private ObservableCollection<BreadcrumbItemModel> _breadcrumbItems = [];
        public ObservableCollection<BreadcrumbItemModel> BreadcrumbItems
        {
            get => _breadcrumbItems;
            set
            {
                _breadcrumbItems?.CollectionChanged -= OnBreadcrumbsChanged;

                SetProperty(ref _breadcrumbItems, value);

                _breadcrumbItems?.CollectionChanged += OnBreadcrumbsChanged;

                UpdateBreadcrumbVisibility();
            }
        }

        private void OnBreadcrumbsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateBreadcrumbVisibility();

        private void UpdateBreadcrumbVisibility()
        {
            OnPropertyChanged(nameof(HasBreadcrumbs));
            OnPropertyChanged(nameof(ShowPageTitle));
        }

        public bool HasBreadcrumbs => BreadcrumbItems.Count > 0;
        public bool ShowPageTitle => !HasBreadcrumbs;

        public ICommand SetLaunchModeCommand { get; }

        public IRelayCommand NavigateToIntegrationsCommand { get; }
        public IRelayCommand NavigateToBehaviourCommand { get; }
        public IRelayCommand NavigateToLinuxSettingsCommand { get; }
        public IRelayCommand NavigateToMyModsCommand { get; }
        public IRelayCommand NavigateToFastFlagsCommand { get; }
        public IRelayCommand NavigateToFastFlagEditorCommand { get; }
        public IRelayCommand NavigateToAppearanceCommand { get; }
        public IRelayCommand NavigateToRegionSelectorCommand { get; }
        public IRelayCommand NavigateToGlobalSettingsCommand { get; }
        public IRelayCommand NavigateToGlobalSettingsEditorCommand { get; }
        public IRelayCommand NavigateToShortcutsCommand { get; }
        public IRelayCommand NavigateToQuickPlayCommand { get; }
        public IRelayCommand NavigateToChannelsCommand { get; }
        public IRelayCommand NavigateToCommunityModsCommand { get; }
        public IRelayCommand NavigateToPresetModsCommand { get; }
        public IRelayCommand NavigateToModGeneratorCommand { get; }

        public ICommand OpenAboutCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand SaveAndLaunchSettingsCommand { get; }
        public ICommand RestartAppCommand { get; }
        public ICommand CloseWindowCommand { get; }
        public ICommand BreadcrumbItemClickedCommand { get; }

        public EventHandler? RequestSaveNoticeEvent;
        private string _saveError = "";
        public string SaveError
        {
            get => _saveError;
            private set { if (SetProperty(ref _saveError, value)) OnPropertyChanged(nameof(HasSaveError)); }
        }
        public bool HasSaveError => SaveError.Length > 0;
        public EventHandler? RequestCloseWindowEvent;
        public bool GBSEnabled = App.GlobalSettings.Loaded;

        public NextAction CloseAction { get; private set; } = NextAction.Terminate;

        public MainWindowViewModel()
        {
            _breadcrumbItems.CollectionChanged += OnBreadcrumbsChanged;

            SetLaunchModeCommand = new RelayCommand<LaunchMode>(mode => SelectedLaunchMode = mode);

            OpenAboutCommand = new RelayCommand(OpenAbout);
            SaveSettingsCommand = new RelayCommand(() => SaveSettings());
            SaveAndLaunchSettingsCommand = new RelayCommand(SaveAndLaunchSettings);
            RestartAppCommand = new RelayCommand(RestartApp);
            CloseWindowCommand = new RelayCommand(CloseWindow);
            BreadcrumbItemClickedCommand = new RelayCommand<BreadcrumbItemModel>(HandleBreadcrumbItemClicked);
            SearchBar = new();
            SearchBar.SetSearchIndex(Froststrap.UI.Utility.NavigationRegistry.Pages.Where(page => page.Sidebar && (page.Tag != "globalsettings" || GBSEnabled))
                .Select(page => new SearchBarItem { DisplayName = page.Title, PageName = page.Title, PageTag = page.Tag, Category = "Navigation" }).ToList());

            NavigateToIntegrationsCommand = new RelayCommand(() => NavigateRegistered("home"));
            NavigateToBehaviourCommand = new RelayCommand(() => NavigateRegistered("behaviour"));
            NavigateToLinuxSettingsCommand = new RelayCommand(() => NavigateRegistered("shortcuts"));
            NavigateToPresetModsCommand = new RelayCommand(() => NavigateRegistered("mods"));
            NavigateToFastFlagsCommand = new RelayCommand(() => NavigateRegistered("fastflags"));
            NavigateToAppearanceCommand = new RelayCommand(() => NavigateRegistered("shortcuts"));
            NavigateToRegionSelectorCommand = new RelayCommand(() => NavigateRegistered("home"));
            NavigateToGlobalSettingsCommand = new RelayCommand(() => NavigateRegistered("fastflags"));
            NavigateToShortcutsCommand = new RelayCommand(() => NavigateRegistered("shortcuts"));
            NavigateToQuickPlayCommand = new RelayCommand(() => NavigateRegistered("quickplay"));
            NavigateToChannelsCommand = new RelayCommand(() => NavigateRegistered("shortcuts"));
            NavigateToGlobalSettingsEditorCommand = new RelayCommand(() => NavigateRegistered("globalsettingseditor"));
            NavigateToFastFlagEditorCommand = new RelayCommand(() => NavigateRegistered("fastflags"));
            NavigateToCommunityModsCommand = new RelayCommand(() => NavigateRegistered("mods"));
            NavigateToModGeneratorCommand = new RelayCommand(() => NavigateRegistered("modgenerator"));
            NavigateToMyModsCommand = new RelayCommand(() => NavigateRegistered("custommods"));
            NavigateRegistered("home");

            SelectedLaunchMode = App.State.Prop.LastLaunchMode;
        }

        private void Navigate(string pageId, string title, string description, object viewModel, ObservableCollection<BreadcrumbItemModel>? customBreadcrumbs = null)
        {
            try
            {
                SelectedPage = pageId;
                CurrentPageTitle = title;
                CurrentPageDescription = description;
                CurrentPageIcon = Froststrap.UI.Utility.NavigationRegistry.Find(pageId)?.Icon ?? LucideIconNames.CircleQuestionMark;
                BreadcrumbItems = customBreadcrumbs ?? [];
                CurrentPage = viewModel;
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
            }
        }

        public void NavigateRegistered(string tag)
        {
            var route = Froststrap.UI.Utility.NavigationRegistry.Find(tag);
            if (route == null) return;
            var model = route.CreateModel(this);
            if (model is ModsPresetsViewModel mods)
            {
                mods.OpenModsEvent += (_, _) => NavigateRegistered("custommods");
                mods.OpenModGeneratorEvent += (_, _) => NavigateRegistered("modgenerator");
            }
            var old = CurrentPage;
            Navigate(route.Tag, route.Title, "", model);
            if (old is IDisposable disposable) disposable.Dispose();
        }

        private void OpenAbout()
        {
            if (App.AboutOpen) return;

            App.FrostRPC?.SetDialog("About");
            new Elements.About.MainWindow().Show();
            App.FrostRPC?.ClearDialog();

            App.AboutOpen = true;
        }

        private void CloseWindow() => RequestCloseWindowEvent?.Invoke(this, EventArgs.Empty);

        public bool SaveSettings()
        {
            if (CurrentPage != null)
            {
                App.State.Prop.LastPage = CurrentPage.GetType().FullName;
            }

            var failed = new List<string>();
            void SaveJson<T>(JsonManager<T> manager, string label) where T : class, new()
            {
                try
                {
                    if (!manager.Save(false) && (manager.HasUnsavedChanges || !manager.IsSaved)) failed.Add(label);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    App.Logger.Warn(error, "Could not prepare settings for saving."); failed.Add(label);
                }
            }
            SaveJson(App.Settings, "CoffeeStrap preferences");
            SaveJson(App.State, "launcher state");
            SaveJson(App.FastFlags, "Game Tweaks");
            if (!App.GlobalSettings.Save() && (App.GlobalSettings.HasUnsavedChanges || (App.GlobalSettings.Loaded && !File.Exists(GBSEditor.FileLocation))))
                failed.Add("Roblox saved settings");

            if (OperatingSystem.IsLinux())
                SaveJson(App.SoberSettings, "Sober settings");

            if (failed.Count > 0)
            {
                SaveError = "Could not save " + string.Join(", ", failed) + ". Other files may already be saved. Remaining edits are kept. Check file access and choose Retry save.";
                return false;
            }
            try
            {
                Services.RetiredPresets.RemoveAvatarBackground();
                foreach (var pair in App.PendingSettingTasks.ToArray())
                {
                    var task = pair.Value;
                    if (task.Changed)
                    {
                        App.Logger.Info($"Executing pending task '{task}'");
                        task.Execute();
                    }
                    App.PendingSettingTasks.Remove(pair.Key);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
            {
                App.Logger.Warn(error, "Could not complete pending settings file changes.");
                SaveError = "Settings files were saved, but a file change could not finish. Remaining changes are kept. Check file access and choose Retry save. " + error.Message;
                return false;
            }
            SaveError = "";
            RequestSaveNoticeEvent?.Invoke(this, EventArgs.Empty);
            return true;
        }

        internal bool CanCloseAfterPrompt(MessageBoxResult result) => result == MessageBoxResult.No || (result == MessageBoxResult.Yes && SaveSettings());

        public void SaveAndLaunchSettings()
        {
            if (!SaveSettings()) return;

            if (!App.LaunchSettings.TestModeFlag.Active)
            {
                LaunchEntry.LaunchInChildProcess(SelectedLaunchMode);
            }
            else
            {
                CloseAction = LaunchEntry.ToNextAction(SelectedLaunchMode);
                CloseWindow();
            }
        }

        public LaunchMode SelectedLaunchMode
        {
            get => _selectedLaunchMode;
            set
            {
                if (SetProperty(ref _selectedLaunchMode, value))
                {
                    App.State.Prop.LastLaunchMode = value;
                    App.State.SaveSetting("LastLaunchMode");
                    OnPropertyChanged(nameof(LaunchButtonText));
                }
            }
        }

        public static bool IsPlayerInstalled
        {
            get
            {
                if (OperatingSystem.IsLinux())
                    return App.LinuxPlayerInstalled;
                else
                    return App.IsPlayerInstalled;
            }
        }

        public static bool IsStudioInstalled => App.IsStudioInstalled;
        public static string PlayerMenuItemText => OperatingSystem.IsLinux() ? Strings.Common_Sober : Strings.Common_Player;

        public string LaunchButtonText
        {
            get
            {
                if (SelectedLaunchMode == LaunchMode.Player)
                {
                    string modeName = OperatingSystem.IsLinux() ? Strings.Common_Sober : Strings.Common_Player;
                    return IsPlayerInstalled
                        ? $"{Strings.Common_SaveAndLaunch} {modeName}"
                        : $"{Strings.Common_SaveAndInstall} {modeName}";
                }
                else
                {
                    return IsStudioInstalled
                        ? $"{Strings.Common_SaveAndLaunch} {Strings.Common_Studio}"
                        : $"{Strings.Common_SaveAndInstall} {Strings.Common_Studio}";
                }
            }
        }

        public void RestartApp()
        {
            if (!SaveSettings()) return;

            CloseAction = NextAction.LaunchSettings;
            CloseWindow();
        }

        private void HandleBreadcrumbItemClicked(BreadcrumbItemModel? item)
        {
            if (item?.Tag == null || item.IsLast) return;

            switch (item.Tag)
            {
                case "mods":
                    NavigateToPresetModsCommand.Execute(null);
                    break;
                case "fastflags":
                    NavigateToFastFlagsCommand.Execute(null);
                    break;
                case "globalsettings":
                    NavigateToGlobalSettingsCommand.Execute(null);
                    break;
            }
        }

        public void SetSearchIndex(List<SearchBarItem> searchIndex)
        {
            SearchBar.SetSearchIndex(searchIndex);
        }

        public List<SearchBarItem> GetSearchIndex()
        {
            return SearchBar.GetSearchIndex();
        }
    }
}
