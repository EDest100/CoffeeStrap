// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
using Froststrap.UI.Elements.Settings.Pages;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.ViewModels.Settings.Mods;
using LucideAvalonia.Enum;

namespace Froststrap.UI.Utility;

internal sealed record CoffeePage(string Tag, string Title, LucideIconNames Icon, Type ModelType,
    Func<MainWindowViewModel, object> CreateModel, Func<Control> CreateView, bool Sidebar = true, bool Searchable = true);

/// <summary>One route registry drives navigation, view construction and settings search.</summary>
internal static class NavigationRegistry
{
    public static IReadOnlyList<CoffeePage> Pages { get; } =
    [
        new("home", "Home", LucideIconNames.House, typeof(HomeViewModel), owner => new HomeViewModel(Services.GameLaunchService.Default, () => { owner.SelectedLaunchMode = LaunchMode.Player; owner.SaveAndLaunchSettings(); },
            () => owner.NavigateRegistered("behaviour"), () => owner.NavigateRegistered("quickplay")), () => new HomePage(), Searchable: false),
        new("quickplay", "Quick Play", LucideIconNames.Gamepad2, typeof(QuickPlayViewModel), _ => new QuickPlayViewModel(), () => new QuickPlayPage(), Searchable: false),
        new("mods", "Texture Editor", LucideIconNames.PaintbrushVertical, typeof(ModsPresetsViewModel), _ => new ModsPresetsViewModel(), () => new ModsPresetsPage()),
        new("fastflags", "Game Tweaks", LucideIconNames.Flag, typeof(CuratedFastFlagsViewModel), _ => new CuratedFastFlagsViewModel(), () => new FastFlagsPage()),
        new("behaviour", "Launcher", LucideIconNames.Play, typeof(BehaviourViewModel), _ => new BehaviourViewModel(), () => new BehaviourPage()),
        new("shortcuts", "Advanced Settings", LucideIconNames.Settings, typeof(AdvancedSettingsViewModel), _ => new AdvancedSettingsViewModel(), () => new ShortcutsPage()),
        new("custommods", "Installed mods", LucideIconNames.Folder, typeof(ModsViewModel), _ => new ModsViewModel(), () => new Elements.Settings.Pages.Mods.ModsPage(), false, false),
        new("modgenerator", "Mod generator", LucideIconNames.PaintbrushVertical, typeof(ModGeneratorViewModel), _ => new ModGeneratorViewModel(), () => new Elements.Settings.Pages.Mods.ModGeneratorPage(), false, false),
        new("globalsettingseditor", "Global Settings editor", LucideIconNames.FilePenLine, typeof(ViewModels.Settings.GlobalSettings.GlobalSettingsEditorViewModel), owner => new ViewModels.Settings.GlobalSettings.GlobalSettingsEditorViewModel(owner),
            () => new Elements.Settings.Pages.GlobalSettings.GlobalSettingsEditor(), false, false)
    ];
    public static CoffeePage? Find(string tag) => Pages.FirstOrDefault(p => p.Tag == tag);
    public static Control? CreateView(object model) => Pages.FirstOrDefault(p => p.ModelType == model.GetType())?.CreateView();
}
