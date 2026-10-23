from pathlib import Path
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
p='Froststrap/Models/APIs/Config/CommunityMod.cs';s=read(p)
s=s.replace('        private bool _isDownloading;','''        private bool _installed;
        private string _downloadError = "";
        [JsonIgnore] public bool IsInstalled { get => _installed; set { if (SetProperty(ref _installed, value)) NotifyDownloadState(); } }
        [JsonIgnore] public string DownloadError { get => _downloadError; set { if (SetProperty(ref _downloadError, value)) NotifyDownloadState(); } }
        [JsonIgnore] public bool CanDownload => !IsInstalled && !IsDownloading;
        [JsonIgnore] public string DownloadLabel => IsInstalled ? "Installed" : IsDownloading ? "Downloading…" : string.IsNullOrEmpty(DownloadError) ? "Download" : "Retry";
        private void NotifyDownloadState() { OnPropertyChanged(nameof(CanDownload)); OnPropertyChanged(nameof(DownloadLabel)); }
        private bool _isDownloading;''')
s=s.replace('set => SetProperty(ref _isDownloading, value);','set { if (SetProperty(ref _isDownloading, value)) NotifyDownloadState(); }')
s=s.replace('return Path.Combine(cacheDir, $"{Id}.png");','return Path.Combine(cacheDir, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Id ?? ThumbnailUrl))) + ".png");')
write(p,s)
p='Froststrap/Models/ModConfig.cs';s=read(p).replace('        private string _folderName', '        public string? CommunityModId { get; set; }\n        private string _folderName',1);write(p,s)
p='Froststrap/UI/ViewModels/Settings/Mods/CommunityModsViewModel.cs';s=read(p)
s=s.replace('        private bool _disposed;','''        private bool _disposed;
        private readonly EventHandler _dataLoaded;
        public bool IsEmpty => !IsLoading && Mods.Count == 0;
        internal static string SafeFolderName(string name)
        {
            string safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\\\' ? '_' : ch)).Trim().Trim('.');
            if (string.IsNullOrWhiteSpace(safe)) throw new InvalidDataException("The mod has no valid installation name.");
            return safe.Length > 100 ? safe[..100] : safe;
        }
        private static bool Installed(CommunityMod mod)
        {
            var entry = App.State.Prop.Mods.FirstOrDefault(item => item.CommunityModId == mod.Id || item.FolderName == mod.Name);
            string folder = mod.IsCustomTheme ? Paths.CustomThemes : Paths.Modifications;
            return mod.IsCustomTheme ? Directory.Exists(Path.Combine(folder, SafeFolderName(mod.Name)))
                : entry != null && Directory.Exists(Path.Combine(folder, SafeFolderName(entry.FolderName)));
        }''')
s=s.replace('''            App.RemoteData.Subscribe(async (_, _) => await RefreshModsAsync());''','''            _dataLoaded = async (_, _) => { if (!_disposed) await RefreshModsAsync(); };
            App.RemoteData.Subscribe(_dataLoaded);
            Mods.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));''')
s=s.replace('set => SetProperty(ref _isLoading, value);','set { if (SetProperty(ref _isLoading, value)) OnPropertyChanged(nameof(IsEmpty)); }')
s=s.replace('''                _allMods = App.RemoteData.Prop.CommunityMods ?? [];''','''                if (_disposed) return;
                if (App.RemoteData.LoadedState != GenericTriState.Successful) throw new IOException("Community mods are unavailable. Check your connection and retry.");
                _allMods = App.RemoteData.Prop.CommunityMods ?? [];''')
s=s.replace('''            foreach (var mod in filtered)
                mod.DownloadCommand = DownloadModCommand;''','''            foreach (var mod in filtered)
            {
                mod.DownloadCommand = DownloadModCommand;
                try { mod.IsInstalled = Installed(mod); } catch { mod.IsInstalled = false; }
            }
            filtered = [.. filtered.OrderByDescending(mod => mod.IsInstalled).ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)];''')
s=s.replace('''            await _searchCts!.CancelAsync();''','''            if (_searchCts != null) { await _searchCts.CancelAsync(); _searchCts.Dispose(); }''')
s=s.replace('''                ApplyFilters();
            }
            catch (OperationCanceledException)''','''                if (!_disposed) await Dispatcher.UIThread.InvokeAsync(ApplyFilters);
            }
            catch (OperationCanceledException)''')
s=s.replace('private static async Task DownloadModAsync(CommunityMod mod)','private async Task DownloadModAsync(CommunityMod mod)')
s=s.replace('if (mod == null || mod.IsDownloading) return;','if (mod == null || !mod.CanDownload) return;')
s=s.replace('''                mod.IsDownloading = true;''','''                mod.DownloadError = "";
                if (!Uri.TryCreate(mod.DownloadUrl, UriKind.Absolute, out var download) || download.Scheme != "https") throw new InvalidDataException("A secure download URL is required.");
                mod.IsDownloading = true;''',1)
s=s.replace('string baseName = mod.Name;','string baseName = SafeFolderName(mod.Name);',1)
s=s.replace('''                            FolderName = finalName,''','''                            FolderName = finalName,
                            CommunityModId = mod.Id,''',1)
s=s.replace('''                        existingMod.Enabled = true;''','''                        existingMod.Enabled = true;
                        existingMod.CommunityModId = mod.Id;''',1)
s=s.replace('''                _ = Frontend.ShowMessageBox(ex.Message, MessageBoxImage.Error);
                App.Logger.Error(ex);''','''                mod.DownloadError = "Download failed. Try again.";
                App.Logger.Error(ex);''',1)
s=s.replace('''                mod.IsDownloading = false;
                if (File.Exists(tempFile)) File.Delete(tempFile);''','''                mod.IsDownloading = false;
                try { mod.IsInstalled = Installed(mod); } catch { mod.IsInstalled = false; }
                if (!_disposed) ApplyFilters();
                if (File.Exists(tempFile)) File.Delete(tempFile);''',1)
s=s.replace('''                _searchCts?.Cancel();''','''                App.RemoteData.DataLoaded -= _dataLoaded;
                _searchCts?.Cancel();''',1)
write(p,s)
# Trim the merged community view to a centered search and actual catalog cards.
p='Froststrap/UI/Elements/Settings/Pages/Mods/CommunityModsPage.axaml'
write(p,'''<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
 SPDX-License-Identifier: MPL-2.0 -->
<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
 xmlns:controls="using:Froststrap.UI.Elements.Controls" xmlns:models="using:Froststrap.Models.APIs.Config" xmlns:vm="using:Froststrap.UI.ViewModels.Settings.Mods"
 xmlns:ic="clr-namespace:LucideAvalonia;assembly=LucideAvalonia" x:Class="Froststrap.UI.Elements.Settings.Pages.Mods.CommunityModsPage" x:DataType="vm:CommunityModsViewModel">
<StackPanel Spacing="18">
<TextBox Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}" PlaceholderText="Search community mods" HorizontalAlignment="Center" MaxWidth="560" MinWidth="250" MinHeight="44" />
<ProgressBar IsIndeterminate="True" IsVisible="{Binding IsLoading}" Height="3" />
<StackPanel IsVisible="{Binding HasError}" Spacing="10" HorizontalAlignment="Center"><TextBlock Text="{Binding ErrorMessage}" TextWrapping="Wrap" /><Button Content="Retry" Command="{Binding RefreshModsCommand}" HorizontalAlignment="Center" /></StackPanel>
<TextBlock Text="No matching mods." IsVisible="{Binding IsEmpty}" HorizontalAlignment="Center" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
<ItemsControl ItemsSource="{Binding Mods}"><ItemsControl.ItemsPanel><ItemsPanelTemplate><controls:ResponsiveCardGrid MinimumCardWidth="250" MaximumColumns="3" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
<ItemsControl.ItemTemplate><DataTemplate x:DataType="models:CommunityMod">
<controls:Card Padding="0" CornerRadius="20" ClipToBounds="True" Height="350"><Grid RowDefinitions="140,Auto,*,Auto" >
<Border Background="{DynamicResource CoffeeGroupBrush}"><Grid><ic:Lucide Icon="Image" Width="32" Height="32" HorizontalAlignment="Center" VerticalAlignment="Center"/><Image Source="{Binding Thumbnail}" Stretch="UniformToFill" RenderOptions.BitmapInterpolationMode="HighQuality" /></Grid></Border>
<TextBlock Grid.Row="1" Text="{Binding Name}" TextAlignment="Center" TextWrapping="Wrap" FontSize="16" FontWeight="Bold" Margin="14,14,14,8" />
<TextBlock Grid.Row="2" Text="{Binding Description}" TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis" MaxHeight="58" Margin="16,0" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
<StackPanel Grid.Row="3" Margin="12" Spacing="6"><TextBlock Text="{Binding DownloadError}" TextWrapping="Wrap" FontSize="11" Foreground="{DynamicResource TextFillColorSecondaryBrush}" /><ProgressBar Value="{Binding DownloadProgress}" IsVisible="{Binding IsDownloading}" Height="3"/><Button Content="{Binding DownloadLabel}" Command="{Binding DownloadCommand}" CommandParameter="{Binding}" IsEnabled="{Binding CanDownload}" HorizontalAlignment="Stretch" Height="40" CornerRadius="16" /></StackPanel>
</Grid></controls:Card></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>
</StackPanel></UserControl>''')
# PlaceholderText is the actual Avalonia property, used by both controls.
p='Froststrap/UI/Elements/Settings/Pages/QuickPlayPage.axaml';s=read(p)
s=s.replace(' PlaceholderText="{x:Static resources:Strings.Menu_RegionSelector_FindGame}" Watermark="Search games or paste a Roblox link"',' PlaceholderText="Search games or paste a Roblox link"').replace(' Watermark="Server ID or private link"',' PlaceholderText="Server ID or private link"');write(p,s)
print('Community catalog: installed-first ordering, truthful download state, safe paths and failure recovery.')
