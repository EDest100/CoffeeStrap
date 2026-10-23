// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.UI.Elements.Dialogs;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;

namespace Froststrap.UI.ViewModels.Settings.Mods
{
    internal partial class CommunityModsViewModel : NotifyPropertyChangedViewModel, IDisposable
    {
        private List<CommunityMod> _allMods = [];
        private readonly CommunityMod _coffeeMod = Services.CoffeeCommunityMod.Create();
        private CancellationTokenSource? _searchCts;
        private bool _disposed;
        private readonly EventHandler _dataLoaded;
        public bool IsEmpty => !IsLoading && Mods.Count == 0;
        internal static string SafeFolderName(string name)
        {
            string safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' ? '_' : ch)).Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(safe)) throw new InvalidDataException("The mod has no valid installation name.");
            return safe.Length > 100 ? safe[..100] : safe;
        }
        private static string ThemeFolder(CommunityMod mod) => App.Settings.Prop.CommunityThemeFolders.GetValueOrDefault(mod.Id) ?? SafeFolderName(mod.Name);
        private static bool Installed(CommunityMod mod)
        {
            var entry = App.State.Prop.Mods.FirstOrDefault(item => item.CommunityModId == mod.Id || item.FolderName == mod.Name);
            string folder = mod.IsCustomTheme ? Paths.CustomThemes : Paths.Modifications;
            return mod.IsCustomTheme ? Directory.Exists(Services.ModFilePolicy.InstalledPath(folder, ThemeFolder(mod)))
                : entry != null && Directory.Exists(Path.Combine(folder, SafeFolderName(entry.FolderName)));
        }

        public event EventHandler? OpenModsEvent;
        public event EventHandler? OpenModGeneratorEvent;
        public event EventHandler? OpenPresetModsEvent;

        private ObservableCollection<CommunityMod> _mods = [];
        public ObservableCollection<CommunityMod> Mods
        {
            get => _mods;
            set => SetProperty(ref _mods, value);
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set { if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty)); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    _ = SearchModsAsync();
                }
            }
        }

        private ModType? _activeFilter;
        public ModType? ActiveFilter
        {
            get => _activeFilter;
            set => SetProperty(ref _activeFilter, value);
        }

        private readonly Func<string, string, IProgress<double>, Task> _download;
        public CommunityModsViewModel() : this(DownloadFileAsync) { }
        internal CommunityModsViewModel(Func<string, string, IProgress<double>, Task> download)
        {
            _download = download;
            _dataLoaded = async (_, _) => { if (!_disposed) await RefreshModsAsync(); };
            App.RemoteData.Subscribe(_dataLoaded);
            Mods.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        }

        [RelayCommand] private void OpenMods() => OpenModsEvent?.Invoke(this, EventArgs.Empty);
        [RelayCommand] private void OpenPresetMods() => OpenPresetModsEvent?.Invoke(this, EventArgs.Empty);
        [RelayCommand] private void OpenModGenerator() => OpenModGeneratorEvent?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void SetFilter(object? parameter)
        {
            if (parameter is null)
            {
                ActiveFilter = null;
            }
            else if (parameter is ModType newFilter)
            {
                ActiveFilter = ActiveFilter == newFilter ? null : newFilter;
            }

            ApplyFilters();
        }

        [RelayCommand]
        public async Task RefreshModsAsync()
        {
            try
            {
                IsLoading = true;
                HasError = false;

                if (App.RemoteData.LoadedState == GenericTriState.Unknown)
                    await App.RemoteData.WaitUntilDataFetched();

                if (_disposed) return;
                if (App.RemoteData.LoadedState != GenericTriState.Successful) throw new IOException("Community mods are unavailable. Check your connection and retry.");
                _allMods = Services.CoffeeCommunityMod.Merge(App.RemoteData.Prop.CommunityMods ?? [], _coffeeMod);
                AddInstalledCards();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Other community mods couldn't be loaded. CoffeeStrap Espresso and installed mods are still available. Check your connection and retry.";
                App.Logger.Error($"Unhandled exception: {ex}");
                _allMods = [_coffeeMod]; AddInstalledCards(); ApplyFilters();
            }
            finally { IsLoading = false; }
        }

        private void ApplyFilters()
        {
            var query = SearchQuery.ToUpperInvariant().Trim();

            var filtered = _allMods.Where(mod =>
                (!mod.IsLocalOnly || Installed(mod)) && (ActiveFilter == null || mod.ModType == ActiveFilter) &&
                (string.IsNullOrEmpty(query) ||
                 mod.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 mod.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList();

            foreach (var mod in filtered)
            {
                mod.DownloadCommand = DownloadModCommand;
                mod.ToggleCommand = ToggleModCommand;
                mod.PreviewCommand = OpenModInfoDialogCommand;
                mod.UninstallCommand = UninstallModCommand;
                mod.IsEnabled = mod.IsCustomTheme ? App.Settings.Prop.SelectedCustomTheme == ThemeFolder(mod) && App.Settings.Prop.BootstrapperStyle == BootstrapperStyle.CustomDialog
                    : App.State.Prop.Mods.FirstOrDefault(item => item.CommunityModId == mod.Id || item.FolderName == mod.Name)?.Enabled == true;
                try { mod.IsInstalled = Installed(mod); } catch { mod.IsInstalled = false; }
            }
            filtered = [.. filtered.OrderByDescending(mod => mod.IsInstalled).ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)];

            Dispatcher.UIThread.Invoke(() =>
            {
                Mods.Clear();
                foreach (var mod in filtered)
                {
                    Mods.Add(mod);
                    _ = mod.LoadThumbnailAsync();
                }
            });
        }

        [RelayCommand]
        private async Task SearchModsAsync()
        {
            if (_searchCts != null) { await _searchCts.CancelAsync(); _searchCts.Dispose(); }
            _searchCts = new CancellationTokenSource();
            try
            {
                await Task.Delay(300, _searchCts.Token);
                if (!_disposed) await Dispatcher.UIThread.InvokeAsync(ApplyFilters);
            }
            catch (OperationCanceledException) { }
        }

        private void AddInstalledCards()
        {
            _allMods = [.. _allMods];
            foreach (var entry in App.State.Prop.Mods)
            {
                if (_allMods.Any(mod => mod.Id == entry.CommunityModId || mod.Name == entry.FolderName)) continue;
                if (!Directory.Exists(Services.ModFilePolicy.InstalledPath(Paths.Modifications, entry.FolderName))) continue;
                _allMods.Add(new CommunityMod { Id = entry.CommunityModId ?? "installed:" + entry.FolderName, Name = entry.FolderName, Description = "Installed content mod", ModType = ModType.MiscMod, IsLocalOnly = true, ThumbnailUrl = "", DownloadUrl = "" });
            }
        }

        [RelayCommand]
        internal void ToggleMod(CommunityMod mod)
        {
            if (!mod.IsInstalled || mod.IsDownloading) return;
            if (mod.IsCustomTheme)
            {
                App.Settings.Prop.SelectedCustomTheme = mod.IsEnabled ? null : ThemeFolder(mod);
                App.Settings.Prop.BootstrapperStyle = mod.IsEnabled ? BootstrapperStyle.FluentAeroDialog : BootstrapperStyle.CustomDialog;
                App.Settings.Save();
            }
            else
            {
                var entry = App.State.Prop.Mods.FirstOrDefault(item => item.CommunityModId == mod.Id || item.FolderName == mod.Name);
                if (entry == null) return;
                entry.Enabled = !entry.Enabled;
                App.State.SaveSetting("Mods");
            }
            ApplyFilters();
        }

        [RelayCommand]
        internal void UninstallMod(CommunityMod mod)
        {
            if (!mod.IsInstalled || mod.IsDownloading) return;
            try
            {
                var entry = App.State.Prop.Mods.FirstOrDefault(item => item.CommunityModId == mod.Id || item.FolderName == mod.Name);
                string source = Services.ModFilePolicy.InstalledPath(mod.IsCustomTheme ? Paths.CustomThemes : Paths.Modifications, mod.IsCustomTheme ? ThemeFolder(mod) : entry?.FolderName ?? throw new InvalidDataException("Installed mod record is missing."));
                string trash = Path.Combine(Paths.Base, "ModTrash");
                Directory.CreateDirectory(trash);
                Directory.Move(source, Path.Combine(trash, Guid.NewGuid().ToString("N")));
                if (mod.IsCustomTheme)
                {
                    if (App.Settings.Prop.SelectedCustomTheme == ThemeFolder(mod)) { App.Settings.Prop.SelectedCustomTheme = null; App.Settings.Prop.BootstrapperStyle = BootstrapperStyle.FluentAeroDialog; }
                    App.Settings.Prop.CommunityThemeFolders.Remove(mod.Id); App.Settings.Save();
                }
                if (entry != null) { App.State.Prop.Mods.Remove(entry); App.State.SaveSetting("Mods"); }
                mod.IsInstalled = false; mod.IsEnabled = false; mod.DownloadError = "";
                ApplyFilters();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { mod.DownloadError = "Uninstall failed: " + error.Message; App.Logger.Error(error); }
        }

        [RelayCommand]
        internal async Task DownloadModAsync(CommunityMod mod)
        {
            if (mod == null || !mod.CanDownload) return;

            string tempFile = Path.Combine(Path.GetTempPath(), App.ProjectName, $"{Guid.NewGuid()}.zip");
            try
            {
                mod.DownloadError = "";
                if (!Uri.TryCreate(mod.DownloadUrl, UriKind.Absolute, out var download) || download.Scheme != "https") throw new InvalidDataException("A secure download URL is required.");
                mod.IsDownloading = true;
                Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);

                var progress = new Progress<double>(p => mod.DownloadProgress = p);
                await _download(mod.DownloadUrl, tempFile, progress);

                string baseName = SafeFolderName(mod.Name);
                string finalName = baseName;

                if (mod.IsCustomTheme)
                {
                    string themePath = Path.Combine(Paths.CustomThemes, finalName);

                    if (Directory.Exists(themePath))
                    {
                        var result = await Frontend.ShowMessageBox(
                            string.Format(CultureInfo.InvariantCulture, Strings.Menu_CommunityMods_Overwrite, baseName),
                            MessageBoxImage.Question,
                            MessageBoxButton.YesNo);

                        if (result == MessageBoxResult.Yes)
                        {
                            // The archive installer atomically replaces this folder after validation.
                        }
                        else
                        {
                            int counter = 1;
                            while (Directory.Exists(Path.Combine(Paths.CustomThemes, $"{baseName} {counter}")))
                                counter++;
                            finalName = $"{baseName} {counter}";
                            themePath = Path.Combine(Paths.CustomThemes, finalName);
                        }
                    }

                    await Services.ModArchiveInstaller.InstallAsync(tempFile, themePath);

                    App.Settings.Prop.SelectedCustomTheme = finalName;
                    App.Settings.Prop.CommunityThemeFolders[mod.Id] = finalName;
                    App.Settings.Prop.BootstrapperStyle = BootstrapperStyle.CustomDialog;
                    App.Settings.Save();


                }
                else
                {
                    string installPath = Path.Combine(Paths.Modifications, finalName);

                    if (Directory.Exists(installPath))
                    {
                        var result = await Frontend.ShowMessageBox(
                            string.Format(CultureInfo.InvariantCulture, Strings.Menu_CommunityMods_Overwrite, baseName),
                            MessageBoxImage.Question,
                            MessageBoxButton.YesNo);

                        if (result == MessageBoxResult.Yes)
                        {
                            // The archive installer atomically replaces this folder after validation.
                        }
                        else
                        {
                            int counter = 1;
                            while (Directory.Exists(Path.Combine(Paths.Modifications, $"{baseName} {counter}")))
                                counter++;
                            finalName = $"{baseName} {counter}";
                            installPath = Path.Combine(Paths.Modifications, finalName);
                        }
                    }

                    await Services.ModArchiveInstaller.InstallAsync(tempFile, installPath);

                    var existingMod = App.State.Prop.Mods.FirstOrDefault(m =>
                        string.Equals(m.FolderName, finalName, StringComparison.OrdinalIgnoreCase));

                    if (existingMod != null)
                    {
                        existingMod.Enabled = true;
                        existingMod.CommunityModId = mod.Id;
                        App.Logger.Info($"Enabled existing mod '{finalName}'.");
                    }
                    else
                    {
                        int maxPriority = App.State.Prop.Mods.Count > 0 ? App.State.Prop.Mods.Max(m => m.Priority) : 0;
                        var newMod = new ModConfig
                        {
                            FolderName = finalName,
                            CommunityModId = mod.Id,
                            Enabled = true,
                            Priority = maxPriority + 1,
                            Target = ModTarget.Both
                        };
                        App.State.Prop.Mods.Add(newMod);
                        App.Logger.Info($"Added mod '{finalName}' to state.");
                    }

                    App.State.SaveSetting("Mods");

                }
            }
            catch (Exception ex)
            {
                mod.DownloadError = ex is InvalidDataException ? ex.Message : "Download failed. Try again.";
                App.Logger.Error(ex);
            }
            finally
            {
                mod.IsDownloading = false;
                try { mod.IsInstalled = Installed(mod); } catch { mod.IsInstalled = false; }
                if (!_disposed) ApplyFilters();
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [RelayCommand]
        private static async Task OpenModInfoDialog(Control? control)
        {
            if (control?.DataContext is not CommunityMod mod) return;

            var topLevel = TopLevel.GetTopLevel(control);
            if (topLevel is not Window parentWindow) return;

            App.FrostRPC?.SetDialog($"Viewing {mod.Name}");

            try
            {
                var dialog = new CommunityModInfoDialog(mod);
                await dialog.ShowDialog(parentWindow);
            }
            catch (Exception ex)
            {
                App.Logger.Error($"Unhandled exception: {ex.Message}");
            }
            finally
            {
                App.FrostRPC?.ClearDialog();
            }
        }

        private static async Task DownloadFileAsync(string url, string path, IProgress<double> progress)
        {
            using var response = await App.HttpClient.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var downloadStream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await downloadStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                if (totalBytes != -1) progress.Report((double)totalRead / totalBytes * 100);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                App.RemoteData.DataLoaded -= _dataLoaded;
                _searchCts?.Cancel();
                _searchCts?.Dispose();
                _searchCts = null;
            }

            _disposed = true;
        }
    }
}
