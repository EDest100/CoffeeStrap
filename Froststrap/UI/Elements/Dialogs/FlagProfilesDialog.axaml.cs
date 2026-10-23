// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Froststrap.UI.Elements.Base;
using System.Reflection;
using Froststrap.Services;

namespace Froststrap.UI.Elements.Dialogs
{
    internal partial class FlagProfilesDialog : AvaloniaWindow, IDisposable
    {
        protected override bool ApplyTopPadding => false;
        public MessageBoxResult Result = MessageBoxResult.Cancel;

        private const int AssignmentPageSize = 128;
        private List<string>? _displayedIds;
        private int _assignmentOffset;
        private Dictionary<string, List<string>>? _undoAssignments;
        private Dictionary<string, List<string>>? _undoExpected;
        private Dictionary<string, List<string>>? _confirmationMap;
        private string? _clearProfile;
        private bool _clearAll;
        private bool _isUpdatingPlaceIds;
        private readonly bool _experienceMode;
        private readonly CancellationTokenSource _lifetime = new();
        private bool _isBusy;
        private bool _closed;

        public FlagProfilesDialog() : this(false) { }
        public FlagProfilesDialog(bool experienceMode)
        {
            _experienceMode = experienceMode;
            App.Settings.Prop.ProfilePlaceIds ??= [];
            InitializeComponent();
            Resources["ProfileTabColumns"] = experienceMode ? 3 : 2;
            WindowDecorations = WindowDecorations.BorderOnly;
            ExtendClientAreaTitleBarHeightHint = 48;
            Closed += (_, _) => Dispose();
            ProfileIntro.Text = "Save and manage supported flag snapshots. Loading changes pending Game Tweaks; use Save settings in the main window to apply them on the next launch. Selected global quality and renderer preferences keep priority. File edits and assignments save immediately.";
            CancelProfileButton.Content = "Close";
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { if (AssignmentConfirmation.IsExpanded) CloseAssignmentConfirmation(); else Close(); e.Handled = true; } };
            NewPlaceIdTextBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && AddPlaceButton.IsEnabled) { AddPlaceButton_Click(this, new RoutedEventArgs()); e.Handled = true; } };
            SaveProfile.KeyDown += (_, e) => { if (e.Key == Key.Enter && Tabs.SelectedIndex == 2 && OkButton.IsEnabled) { OKButton_Click(this, new RoutedEventArgs()); e.Handled = true; } };


            LoadProfiles();
            LoadPresetProfiles();

            Tabs.SelectionChanged += (s, e) => OnTabSelectionChanged();
            SaveProfile.TextChanged += (s, e) => UpdateUiState();
            RenameTextBox.TextChanged += (s, e) => UpdateUiState();
            RenameTextBox.KeyDown += (_, e) => { if (e.Key == Key.Enter && RenameProfileButton.IsEnabled) { RenameButton_Click(this, new RoutedEventArgs()); e.Handled = true; } };

            LoadProfile.SelectionChanged += (s, e) => UpdateUiState();
            LoadPresetProfile.SelectionChanged += (s, e) => UpdateUiState();
            PlaceProfile.SelectionChanged += (s, e) => UpdatePlaceIdsUiState();
            if (experienceMode)
            {
                ProfileFooter.RowDefinitions = new RowDefinitions("Auto");
                ProfileFooter.ColumnDefinitions = new ColumnDefinitions("*,Auto");
                Grid.SetRow(ProfileFooterActions, 0); Grid.SetColumn(ProfileFooterActions, 1);
                Title = "Experience rendering profiles";
                PresetsTab.IsVisible = false;
                AssignmentsTab.Header = "Assign"; SaveTab.Header = "Save"; LoadTab.Header = "Snapshots";
                ProfileIntro.Text = "Manage snapshots in the Snapshots tab. Profiles are used on the next direct launch of an assigned place ID. Selected global effects, texture and renderer preferences keep priority. Other flags use the saved snapshot. Removing an assignment returns to global flags on the next launch. Assignments save immediately; Close keeps them.";
                CancelProfileButton.Content = "Close";
                RemovePlaceButton.Content = "Remove assignment"; AddPlaceButton.Content = "Assign";
            }
            Tabs.SelectedIndex = experienceMode ? 1 : 2;
            OnTabSelectionChanged();
        }

        private void OnTabSelectionChanged()
        {
            ProfileFeedback.Text = ""; CloseAssignmentConfirmation();
            if (Tabs.SelectedIndex == 1 && PlaceProfile.Items.Count > 0 && PlaceProfile.SelectedItem == null)
            {
                PlaceProfile.SelectedItem = PlaceProfile.Items[0];
            }
            UpdateUiState();
        }

        private void LoadProfiles(string? preferred = null)
        {
            LoadProfile.Items.Clear();
            PlaceProfile.Items.Clear();

            string profilesDirectory = Paths.SavedFlagProfiles;

            try
            {
                Directory.CreateDirectory(profilesDirectory);
                string[] found = FlagProfileFiles.Names().Take(FlagProfileFiles.MaximumProfiles + 1).ToArray();
                var profiles = found.Take(FlagProfileFiles.MaximumProfiles).ToList();
                if (preferred != null && !profiles.Contains(preferred))
                {
                    try
                    {
                        if (File.Exists(FlagProfileFiles.Resolve(preferred)))
                        {
                            if (profiles.Count == FlagProfileFiles.MaximumProfiles) profiles.RemoveAt(profiles.Count - 1);
                            profiles.Add(preferred);
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                    { App.Logger.Warn(error, "Could not retain the selected profile."); }
                }
                foreach (string name in profiles.Order(StringComparer.OrdinalIgnoreCase))
                { LoadProfile.Items.Add(name); PlaceProfile.Items.Add(name); }
                foreach (string name in App.Settings.Prop.ProfilePlaceIds.Keys.Take(FlagProfileFiles.MaximumProfiles).Order(StringComparer.OrdinalIgnoreCase))
                    if (!PlaceProfile.Items.Contains(name)) PlaceProfile.Items.Add(name);
                ProfileLibraryStatus.Text = found.Length > FlagProfileFiles.MaximumProfiles
                    ? "Showing 256 snapshot files. More legacy files exist; deleting unused snapshots reveals more. New creation is paused."
                    : $"{profiles.Count} of 256 snapshot slots used.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { App.Logger.Warn(error, "Could not list flag profiles."); ProfileFeedback.Text = "Could not list profiles. " + error.Message; }

            LoadProfileEmptyText.IsVisible = LoadProfile.Items.Count == 0;

            RenamePanel.IsVisible = LoadProfile.Items.Count > 0;

            RenameTextBox.Text = string.Empty;
            RenameTextBox.IsEnabled = false;
            if (preferred != null)
            {
                if (LoadProfile.Items.Contains(preferred)) LoadProfile.SelectedItem = preferred;
                if (PlaceProfile.Items.Contains(preferred)) PlaceProfile.SelectedItem = preferred;
            }
        }

        private void LoadProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LoadProfile.SelectedItem is string selectedProfile)
            {
                RenameTextBox.Text = selectedProfile;
                RenameTextBox.IsEnabled = true;
            }
            else
            {
                RenameTextBox.Text = string.Empty;
                RenameTextBox.IsEnabled = false;
            }

            UpdateUiState();
        }

        private void LoadPresetProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUiState();
        }

        private void PlaceProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ProfileFeedback.Text = ""; _assignmentOffset = 0; CloseAssignmentConfirmation();
            if (PlaceProfile.SelectedItem is string name)
            {
                try
                {
                    if (File.Exists(FlagProfileFiles.Resolve(name)))
                    {
                        if (!LoadProfile.Items.Contains(name))
                        {
                            if (LoadProfile.ItemCount == FlagProfileFiles.MaximumProfiles) LoadProfile.Items.RemoveAt(LoadProfile.ItemCount - 1);
                            LoadProfile.Items.Add(name);
                        }
                        LoadProfile.SelectedItem = name;
                        RenamePanel.IsVisible = true; LoadProfileEmptyText.IsVisible = false;
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                { App.Logger.Warn(error, "Selected assignment snapshot is unavailable."); }
            }
            UpdatePlaceIdsUiState();
        }

        private void UpdatePlaceIdsUiState()
        {
            if (_isUpdatingPlaceIds || _closed) return;
            _isUpdatingPlaceIds = true;

            try
            {
                string? selectedProfile = PlaceProfile.SelectedItem as string;

                bool hasProfiles = PlaceProfile.Items.Count > 0;
                bool hasProfileSelected = selectedProfile != null;

                PlaceProfileGrid.IsVisible = hasProfiles;
                PlaceIdsContent.IsVisible = hasProfileSelected;
                PlaceProfileEmptyMessage.IsVisible = !hasProfiles || !hasProfileSelected;

                if (!hasProfiles)
                    PlaceProfileEmptyMessage.Text = Strings.Menu_FlagProfiles_NoProfilesFound;
                else if (!hasProfileSelected)
                    PlaceProfileEmptyMessage.Text = Strings.Menu_FlagProfiles_SelectProfileFirst;

                var assignments = App.Settings.Prop.ProfilePlaceIds;
                _displayedIds = selectedProfile != null ? assignments.GetValueOrDefault(selectedProfile) : null;
                int total = _displayedIds?.Count ?? 0;
                _assignmentOffset = Math.Clamp(_assignmentOffset, 0, Math.Max(0, (total - 1) / AssignmentPageSize * AssignmentPageSize));
                var visible = new List<string>(Math.Min(total, AssignmentPageSize));
                for (int i = _assignmentOffset; i < Math.Min(total, _assignmentOffset + AssignmentPageSize); i++)
                {
                    string? value = _displayedIds![i];
                    visible.Add(FlagProfileFiles.TryPlaceId(value, out _) ? value! : "Invalid ID: " + (string.IsNullOrEmpty(value) ? "(empty)" : value[..Math.Min(32, value.Length)]));
                }
                PlaceIdsListBox.ItemsSource = visible;
                PlaceIdsEmpty.IsVisible = total == 0;
                IdPager.IsVisible = total > AssignmentPageSize;
                IdPreviousButton.IsEnabled = _assignmentOffset > 0;
                IdNextButton.IsEnabled = _assignmentOffset + AssignmentPageSize < total;
                IdPageStatus.Text = $"{_assignmentOffset + 1}–{Math.Min(total, _assignmentOffset + AssignmentPageSize)} of {total}";
                bool withinLimits = FlagProfileFiles.AssignmentsWithinLimits(assignments);
                AssignmentLimitsText.IsVisible = !withinLimits;
                AssignmentLimitsText.Text = "Legacy assignments exceed 256 profiles / 1024 IDs per profile, or contain a null list. Launches use global flags until repaired. Remove IDs, clear a profile, or reset all assignments. Snapshot files stay intact.";
                ClearProfileAssignments.IsVisible = selectedProfile != null && assignments.ContainsKey(selectedProfile);
                ResetAllAssignments.IsVisible = !withinLimits && assignments.Count > 0;
                UndoAssignments.IsVisible = _undoAssignments != null;
                UndoAssignments.IsEnabled = _undoAssignments != null && ReferenceEquals(assignments, _undoExpected);
                ToolTip.SetTip(UndoAssignments, UndoAssignments.IsEnabled ? "Restore the previous assignment change. Undo is available until this dialog closes." : "Assignments changed after this operation; the old Undo is no longer available.");
                bool available = false;
                if (selectedProfile != null)
                {
                    try
                    {
                        available = File.Exists(FlagProfileFiles.Resolve(selectedProfile));
                        if (!available) ProfileFeedback.Text = "Snapshot file is missing. Remove its old assignments here, or save a new snapshot with the same name.";
                    }
                    catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
                    { ProfileFeedback.Text = error.Message + " You can still remove its old assignments."; }
                }
                bool hasSlot = total < FlagProfileFiles.MaximumPlaces && (assignments.Count < FlagProfileFiles.MaximumProfiles || selectedProfile != null && assignments.ContainsKey(selectedProfile));
                AddPlaceButton.IsEnabled = hasProfileSelected && available && withinLimits && hasSlot;
                PlaceIdInfoText.Text = "Assignment limit reached. Remove unused IDs or profile assignments before adding more.";
                PlaceIdInfoText.IsVisible = withinLimits && hasProfileSelected && !hasSlot;
                RemovePlaceButton.IsEnabled = false;

            }
            finally
            {
                _isUpdatingPlaceIds = false;
            }
        }

        private void AddPlaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaceProfile.SelectedItem is not string selectedProfile || !AddPlaceButton.IsEnabled) return;
            var assignments = App.Settings.Prop.ProfilePlaceIds;
            var ids = assignments.GetValueOrDefault(selectedProfile);
            if (!FlagProfileFiles.AssignmentsWithinLimits(assignments) || (ids?.Count ?? 0) >= FlagProfileFiles.MaximumPlaces || assignments.Count >= FlagProfileFiles.MaximumProfiles && !assignments.ContainsKey(selectedProfile))
            { ProfileFeedback.Text = "Assignment limit reached. Remove or clear unused assignments first."; return; }
            string input = NewPlaceIdTextBox.Text?.Trim() ?? "";
            if (!FlagProfileFiles.TryPlaceId(input, out long numericId))
            { ProfileFeedback.Text = "Enter a positive place ID using at most 19 digits. Use the place ID from its Roblox game URL."; return; }
            if (ids?.Any(value => FlagProfileFiles.TryPlaceId(value, out long id) && id == numericId) == true)
            { ProfileFeedback.Text = "This experience is already assigned to the selected profile."; return; }
            string? conflict = assignments.FirstOrDefault(pair => pair.Key != selectedProfile && pair.Value.Any(value => FlagProfileFiles.TryPlaceId(value, out long id) && id == numericId)).Key;
            if (conflict != null) { ProfileFeedback.Text = "This experience is assigned to " + conflict + ". Remove that assignment first."; return; }
            var next = assignments.ToDictionary(pair => pair.Key, pair => pair.Value);
            next[selectedProfile] = ids == null ? [] : [.. ids];
            next[selectedProfile].Add(numericId.ToString(CultureInfo.InvariantCulture));
            if (SaveAssignments(next, "Assignment saved. The next direct launch uses this selection; removing it restores global flags on the next launch.")) NewPlaceIdTextBox.Text = "";
        }

        private void RemovePlaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaceProfile.SelectedItem is not string profile || PlaceIdsListBox.SelectedIndex < 0) return;
            var assignments = App.Settings.Prop.ProfilePlaceIds;
            var ids = assignments.GetValueOrDefault(profile);
            int index = _assignmentOffset + PlaceIdsListBox.SelectedIndex;
            if (ids == null || !ReferenceEquals(ids, _displayedIds) || index >= ids.Count)
            { UpdatePlaceIdsUiState(); ProfileFeedback.Text = "Assignments changed. Select the ID again."; return; }
            var next = assignments.ToDictionary(pair => pair.Key, pair => pair.Value);
            var remaining = new List<string>(ids); remaining.RemoveAt(index);
            if (remaining.Count == 0) next.Remove(profile); else next[profile] = remaining;
            SaveAssignments(next, "Assignment removed. Other IDs, including hidden pages, are unchanged. The next direct launch uses this selection.");
        }

        private bool SaveAssignments(Dictionary<string, List<string>> next, string message, bool remember = true)
        {
            var previous = App.Settings.Prop.ProfilePlaceIds;
            try
            {
                if (App.Settings.HasFileOnDiskChanged())
                { ProfileFeedback.Text = "Settings changed outside this dialog. Restart CoffeeStrap to reload them before editing assignments; no changes were saved."; return false; }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { App.Logger.Warn(error, "Could not check assignment ownership."); ProfileFeedback.Text = "Could not save assignments. Close other file tools and retry."; return false; }
            App.Settings.Prop.ProfilePlaceIds = next;
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.ProfilePlaceIds)))
            {
                App.Settings.Prop.ProfilePlaceIds = previous;
                UpdatePlaceIdsUiState(); ProfileFeedback.Text = "Could not save assignments. Your previous selection is unchanged. Close other file tools and retry.";
                return false;
            }
            if (remember) { _undoAssignments = previous; _undoExpected = next; }
            else { _undoAssignments = _undoExpected = null; }
            CloseAssignmentConfirmation(); UpdatePlaceIdsUiState(); ProfileFeedback.Text = message;
            return true;
        }

        private void PlaceIdsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => RemovePlaceButton.IsEnabled = PlaceIdsListBox.SelectedIndex >= 0;

        private void IdPreviousButton_Click(object sender, RoutedEventArgs e)
        { _assignmentOffset = Math.Max(0, _assignmentOffset - AssignmentPageSize); UpdatePlaceIdsUiState(); }
        private void IdNextButton_Click(object sender, RoutedEventArgs e)
        { if (IdNextButton.IsEnabled) _assignmentOffset += AssignmentPageSize; UpdatePlaceIdsUiState(); }
        private void CloseAssignmentConfirmation()
        { AssignmentConfirmation.IsExpanded = false; _confirmationMap = null; _clearProfile = null; }
        private void ClearProfileAssignments_Click(object sender, RoutedEventArgs e) => RequestAssignmentClear(false);
        private void ResetAllAssignments_Click(object sender, RoutedEventArgs e) => RequestAssignmentClear(true);
        private void RequestAssignmentClear(bool all)
        {
            string? selected = PlaceProfile.SelectedItem as string;
            if (!all && (selected == null || !App.Settings.Prop.ProfilePlaceIds.ContainsKey(selected))) return;
            _clearAll = all; _clearProfile = selected; _confirmationMap = App.Settings.Prop.ProfilePlaceIds;
            AssignmentConfirmationText.Text = all ? "Clear assignments from every profile? The next direct launches use global flags. Snapshot files stay intact. Undo is available until this dialog closes."
                : "Clear every ID for the selected profile, including hidden pages? The next direct launches use global flags. Snapshot files stay intact. Undo is available until this dialog closes.";
            AssignmentConfirmation.IsExpanded = true;
        }
        private void CancelAssignmentClear_Click(object sender, RoutedEventArgs e) => CloseAssignmentConfirmation();
        private void ConfirmAssignmentClear_Click(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(_confirmationMap, App.Settings.Prop.ProfilePlaceIds))
            { CloseAssignmentConfirmation(); ProfileFeedback.Text = "Assignments changed. Review the selection before clearing it."; return; }
            var next = _clearAll ? [] : App.Settings.Prop.ProfilePlaceIds.ToDictionary(pair => pair.Key, pair => pair.Value);
            if (!_clearAll && _clearProfile != null) next.Remove(_clearProfile);
            string? selected = _clearProfile;
            if (SaveAssignments(next, "Assignments cleared and saved. Snapshot files are unchanged. Undo is available until this dialog closes.")) { LoadProfiles(selected); UpdatePlaceIdsUiState(); ProfileFeedback.Text = "Assignments cleared and saved. Snapshot files are unchanged. Undo is available until this dialog closes."; }
        }
        private void UndoAssignments_Click(object sender, RoutedEventArgs e)
        {
            if (_undoAssignments == null || !ReferenceEquals(App.Settings.Prop.ProfilePlaceIds, _undoExpected))
            { UpdatePlaceIdsUiState(); ProfileFeedback.Text = "Assignments changed after this operation; the old Undo is no longer available."; return; }
            string? selected = PlaceProfile.SelectedItem as string;
            if (SaveAssignments(_undoAssignments, "Previous assignments restored and saved, including any legacy values. Snapshot files are unchanged.", false)) { LoadProfiles(selected); UpdatePlaceIdsUiState(); ProfileFeedback.Text = "Previous assignments restored and saved, including any legacy values. Snapshot files are unchanged."; }
        }

        private async Task RunOperationAsync(Func<Task> operation)
        {
            if (_isBusy || _closed) return;
            _isBusy = true; Tabs.IsEnabled = false; OkButton.IsEnabled = DeleteButton.IsEnabled = false;
            ProfileFeedback.Text = "Working…";
            try { await operation(); }
            catch (OperationCanceledException) when (_closed) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
            {
                App.Logger.Warn(error, "Profile operation failed.");
                if (!_closed) ProfileFeedback.Text = (Tabs.SelectedIndex == 2 ? "Could not save snapshot: " : "Could not complete this action: ") + error.Message;
            }
            finally
            {
                _isBusy = false;
                if (!_closed) { Tabs.IsEnabled = true; UpdateUiState(); }
            }
        }

        private async void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoadProfile.SelectedItem is not string name) return;
            string replacement = RenameTextBox.Text?.Trim() ?? "";
            await RunOperationAsync(() =>
            {
                FlagProfileFiles.Rename(name, replacement);
                LoadProfiles(replacement);
                ProfileFeedback.Text = "Profile renamed. Existing experience assignments follow the new name.";
                return Task.CompletedTask;
            });
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoadProfile.SelectedItem is not string name) return;
            await RunOperationAsync(async () =>
            {
                var token = _lifetime.Token;
                var flags = await FlagProfileFiles.ReadAsync(name, token);
                token.ThrowIfCancellationRequested();
                var clipboard = Clipboard ?? throw new InvalidOperationException("The clipboard is unavailable in this session.");
                await clipboard.SetTextAsync(FlagProfileFiles.Serialize(flags));
                if (!_closed) ProfileFeedback.Text = "Profile JSON copied, including any excluded values. Only supported values can be loaded.";
            });
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoadProfile.SelectedItem is not string name) return;
            await RunOperationAsync(() =>
            {
                FlagProfileFiles.Update(name, App.FastFlags.Prop);
                ProfileFeedback.Text = "Snapshot replaced with current supported Game Tweaks, including pending edits. Its assignments are unchanged.";
                return Task.CompletedTask;
            });
        }

        private void LoadPresetProfiles()
        {
            LoadPresetProfile.Items.Clear();

            var assembly = Assembly.GetExecutingAssembly();
            string resourcePrefix = "Froststrap.Resources.PresetFlags.";
            var resourceNames = assembly.GetManifestResourceNames();
            var profiles = resourceNames.Where(r => r.StartsWith(resourcePrefix, StringComparison.Ordinal));

            foreach (var resourceName in profiles)
            {
                string profileName = resourceName[resourcePrefix.Length..];
                LoadPresetProfile.Items.Add(profileName);
            }
        }

        private void UpdateUiState()
        {
            if (_closed || Tabs == null || OkButton == null || DeleteButton == null) return;

            int index = Tabs.SelectedIndex;
            OkButton.IsVisible = index != 1;
            if (_experienceMode) { OkButton.IsVisible = index == 2; OkButton.Content = "Save snapshot"; }

            if (!_experienceMode) OkButton.Content = index switch { 0 => "Load preset", 2 => "Save snapshot", 3 => "Load flags", _ => "OK" };
            ClearFlags.IsVisible = false;
            DeleteButton.IsVisible = false;
            RemovePlaceButton.IsVisible = false;

            switch (index)
            {
                case 0:
                    OkButton.IsEnabled = LoadPresetProfile.SelectedItem != null;
                    break;

                case 1:
                    OkButton.IsEnabled = true;
                    RemovePlaceButton.IsVisible = true;
                    UpdatePlaceIdsUiState();
                    RemovePlaceButton.IsEnabled = PlaceIdsListBox?.SelectedItem != null;
                    break;

                case 2:
                    OkButton.IsEnabled = !string.IsNullOrWhiteSpace(SaveProfile.Text);
                    break;

                case 3:
                    bool hasSelection = LoadProfile.SelectedItem != null;
                    OkButton.IsEnabled = hasSelection;
                    DeleteButton.IsEnabled = hasSelection;
                    ClearFlags.IsVisible = !_experienceMode;
                    DeleteButton.IsVisible = true;
                    break;
            }
            if (_isBusy) OkButton.IsEnabled = DeleteButton.IsEnabled = false;
            CopyProfileButton.IsEnabled = UpdateProfileButton.IsEnabled = LoadProfile.SelectedItem != null && !_isBusy;
            RenameProfileButton.IsEnabled = LoadProfile.SelectedItem is string name && !string.IsNullOrWhiteSpace(RenameTextBox.Text) && name != RenameTextBox.Text.Trim() && !_isBusy;
        }

        private async void OKButton_Click(object sender, RoutedEventArgs e)
        {
            int tab = Tabs.SelectedIndex;
            if (tab == 1) return;
            string? selected = tab == 0 ? LoadPresetProfile.SelectedItem as string : LoadProfile.SelectedItem as string;
            await RunOperationAsync(async () =>
            {
                if (tab == 2)
                {
                    string name = SaveProfile.Text?.Trim() ?? "";
                    FlagProfileFiles.SaveNew(name, App.FastFlags.Prop);
                    LoadProfiles(name);
                    Tabs.SelectedIndex = _experienceMode ? 1 : 3;
                    ProfileFeedback.Text = "Snapshot saved without changing global settings. Add the place IDs that should use it.";
                    return;
                }
                if (selected == null) return;
                var token = _lifetime.Token;
                var source = tab == 0 ? await FlagProfileFiles.ReadPresetAsync(selected, token) : await FlagProfileFiles.ReadAsync(selected, token);
                token.ThrowIfCancellationRequested();
                var result = App.FastFlags.ApplyProfile(source, tab == 0 || ClearFlags.IsChecked == true);
                Result = MessageBoxResult.OK;
                ProfileFeedback.Text = $"Loaded {result.Accepted} supported values into pending Game Tweaks; {result.Excluded} excluded. Selected global quality and renderer preferences keep priority. Close this dialog, then Save settings in the main window. Undo is available in the flag editor.";
            });
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoadProfile.SelectedItem is not string name) return;
            await RunOperationAsync(() =>
            {
                FlagProfileFiles.Delete(name);
                LoadProfiles(); ProfileFeedback.Text = "Profile deleted and its experience assignments removed. Those experiences use global flags on the next direct launch.";
                return Task.CompletedTask;
            });
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            this.Result = MessageBoxResult.Cancel;
            this.Close();
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _undoAssignments = _undoExpected = _confirmationMap = null;
            _displayedIds = null;
            PlaceIdsListBox.ItemsSource = null;
        }
    }
}
