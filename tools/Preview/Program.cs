// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Net;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.UI.Elements.Base;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.Resources;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static string _output = "";
    private static readonly List<object> Captures = [];

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--build-coffee-theme")
        {
            CoffeeThemeBuilder.Build(args[1], args[2], args[3]);
            return 0;
        }
        _output = Path.GetFullPath(args.FirstOrDefault() ?? ".build/verification");
        Directory.CreateDirectory(_output);
        var fixtureRoot = Path.Combine(_output, "fixture-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var temp = Path.Combine(fixtureRoot, "process-temp");
        Directory.CreateDirectory(temp);
        Environment.SetEnvironmentVariable("TEMP", temp);
        Environment.SetEnvironmentVariable("TMP", temp);

        // Process-only isolation. No production entry point, native notifications,
        // installers, protocols, credentials, or live settings are initialized.
        HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("NO_PROXY", "");
        var logging = new NLog.Config.LoggingConfiguration();
        logging.AddRule(NLog.LogLevel.Error, NLog.LogLevel.Fatal,
            new PreviewDiagnosticsTarget { Name = "preview-errors" });
        NLog.LogManager.Configuration = logging;
        InitializeFixturePaths(fixtureRoot);
        App.Settings.Prop.AllowCookieAccess = false;
        App.Settings.Prop.LockMaximumRenderDistance = false;
        App.Settings.Prop.ShowUsingFroststrapRPC = false;
        App.Settings.Prop.UseDiscordRichPresence = false;
        App.Settings.Prop.UpdateChecks = UpdateCheck.Disabled;
        App.Settings.Prop.DisableAnimations = true;
        App.Settings.Prop.SelectedBackdrop = WindowsBackdrops.None;
        App.LaunchSettings = new LaunchSettings(new Froststrap.Cli.FroststrapCliCommand(), []);

        // SetupWithoutStarting has no desktop application lifetime, so App's normal
        // OnFrameworkInitializationCompleted returns before startup side effects.
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        App.ApplyAnimationSettings();
        Froststrap.Locale.Set("en-US");

        try
        {
            if (Array.IndexOf(args, "--coffee-theme-only") is int coffee && coffee >= 0 && args.Length > coffee + 1)
            {
                VerifyCoffeeTheme(Path.GetFullPath(args[coffee + 1]));
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--cloud-volume-only"))
            {
                VerifyRevision26();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--fullscreen-only"))
            {
                VerifyRevision25();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--running-window-probe"))
            {
                var players = Froststrap.Services.RunningPlayerWindows.Find();
                File.WriteAllText(Path.Combine(_output, "running-window-discovery.json"), JsonSerializer.Serialize(new { count = players.Count, validated = players.Count(Froststrap.Services.RunningPlayerWindows.IsCurrent), processIds = players.Select(player => player.ProcessId).ToArray(), metadataOnly = true, windowsChanged = false, memoryAccess = false }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--live-size-only"))
            {
                VerifyRevision23(); VerifyRevision24();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--size-only") || args.Contains("--browser-login-probe"))
            {
                if (args.Contains("--browser-login-probe")) ProbeBrowserLogin(); else VerifySizeChanges();
                RenderRevisionSmoke();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (Array.IndexOf(args, "--asset-probe") is int probe && probe >= 0 && args.Length > probe + 1)
            {
                var assets = Froststrap.Services.ModAssetPreparation.Files(args[probe + 1]).Select(asset => new
                {
                    relative = asset.Relative, prepared = asset.Source, bytes = new FileInfo(asset.Source).Length,
                    header = Convert.ToHexString(File.ReadAllBytes(asset.Source).Take(4).ToArray())
                }).ToArray();
                File.WriteAllText(Path.Combine(_output, "installed-asset-probe.json"), JsonSerializer.Serialize(assets, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Prepared {assets.Length} installed mod assets into the isolated cache; source files were not changed."); return 0;
            }
            if (args.Contains("--live-region-probe"))
            {
                App.Settings.Prop.EnableRoValra = true;
                using var fetcher = new Froststrap.Integrations.RobloxServerFetcher(new HttpClientHandler { UseProxy = false });
                var regionsTask = fetcher.GetPreferredRegionsAsync();
                WaitFor(() => regionsTask.IsCompleted, 20000);
                var regions = regionsTask.GetAwaiter().GetResult();
                var matchTask = fetcher.FindFullestPreferredServerAsync(9391468976, "Atlanta, US");
                WaitFor(() => matchTask.IsCompleted, 60000);
                var server = matchTask.GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(_output, "live-region-probe.json"), JsonSerializer.Serialize(new { timestamp = DateTime.UtcNow, regions = regions.Count, selectedRegion = "Atlanta, US", place = 9391468976L, server, joined = false, credentials = false }));
                Console.WriteLine("Public API probe selected a server in Atlanta; no game was launched."); return 0;
            }
            if (args.Contains("--compact-only"))
            {
                VerifyCompactMenu();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision6-only"))
            {
                VerifyRevision6();
                VerifyCompactMenu();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--resource-audit"))
            {
                var assembly = typeof(App).Assembly;
                Check(!assembly.GetManifestResourceNames().Any(name => name.EndsWith(".cs", StringComparison.Ordinal)), "generated C# source is not embedded as application data");
                Check(!assembly.GetManifestResourceNames().Any(name => name.StartsWith("Froststrap.Resources.Fonts.", StringComparison.Ordinal) || name.StartsWith("Froststrap.Resources.MessageBox.", StringComparison.Ordinal) || name.StartsWith("Froststrap.Resources.BootstrapperStyles.", StringComparison.Ordinal)), "UI-only assets have one copy in Avalonia rather than a duplicate manifest copy");
                var assets = Avalonia.Platform.AssetLoader.GetAssets(new Uri("avares://CoffeeStrap/Resources/"), null).ToArray();
                Check(!assets.Any(uri => uri.AbsolutePath.EndsWith(".resx", StringComparison.Ordinal) || uri.AbsolutePath.EndsWith(".cs", StringComparison.Ordinal)), "UI asset bundle excludes localization XML and generated source copies");
                using (var font = Avalonia.Platform.AssetLoader.Open(new Uri("avares://CoffeeStrap/Resources/Fonts/Rubik-VariableFont_wght.ttf")))
                    Check(font.Length > 0, "the actual application font remains available through its production asset URI");
                using (var mappings = assembly.GetManifestResourceStream("Froststrap.Resources.mappings.json"))
                    Check(mappings is { Length: > 0 }, "native mod mappings remain embedded");
                foreach (string file in Directory.EnumerateFiles("Froststrap/Resources", "Strings*.resx"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    var culture = name == "Strings" ? System.Globalization.CultureInfo.InvariantCulture : new System.Globalization.CultureInfo(name[8..]);
                    var entry = System.Xml.Linq.XDocument.Load(file).Root!.Elements("data").First(element => !string.IsNullOrEmpty(element.Element("value")?.Value));
                    string key = entry.Attribute("name")!.Value, expected = entry.Element("value")!.Value;
                    Check(Strings.ResourceManager.GetString(key, culture) == expected, "compiled translation remains usable: " + (culture.Name.Length == 0 ? "neutral" : culture.Name));
                }
                RenderRevisionSmoke();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--smoke")) { RenderRevisionSmoke(); SaveDiagnostics(); return 0; }
            if (args.Contains("--option-motion-only")) { VerifyOptionMotion(); SaveDiagnostics(); return 0; }
            if (Array.IndexOf(args, "--dds-probe") is int ddsProbe && ddsProbe >= 0 && args.Length > ddsProbe + 1)
            {
                string[] files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[ddsProbe + 1]))!;
                var results = files.Select(file =>
                {
                    string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    string half = Froststrap.Services.DdsMipReducer.Prepare(file, 1), quarter = Froststrap.Services.DdsMipReducer.Prepare(file, 2);
                    timer.Stop();
                    byte[] a = File.ReadAllBytes(half), b = File.ReadAllBytes(quarter);
                    return new { file = Path.GetFileName(file), originalBytes = new FileInfo(file).Length, halfBytes = a.Length, quarterBytes = b.Length, halfPath = half, quarterPath = quarter,
                        halfWidth = DdsValue(a, 16), halfHeight = DdsValue(a, 12), quarterWidth = DdsValue(b, 16), quarterHeight = DdsValue(b, 12),
                        halfDepth = DdsValue(a, 24), quarterDepth = DdsValue(b, 24),
                        preparationElapsedMs = timer.Elapsed.TotalMilliseconds, sourceUnchanged = before == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))) };
                }).ToArray();
                File.WriteAllText(Path.Combine(_output, "texture-budget-inventory.json"), JsonSerializer.Serialize(new { gameTested = false, sourcesReadOnly = true, results }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Read-only DDS inventory prepared " + results.Length + " source textures in the isolated cache."); return 0;
            }
            if (args.Contains("--render-tweaks-only"))
            {
                VerifyRevision23();
                VerifyRevision24();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision21-probe")) { ProbeRevision21(); SaveDiagnostics(); return 0; }
            if (args.Contains("--revision21-only"))
            {
                VerifyRevision21();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision20-probe")) { ProbeRevision20(); SaveDiagnostics(); return 0; }
            if (args.Contains("--revision20-only"))
            {
                VerifyPerformanceProfiles(); VerifyProfileComparison(); VerifyRevision20();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision19-only"))
            {
                VerifyRevision19();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision19-probe")) { ProbeRevision19(); SaveDiagnostics(); return 0; }
            if (args.Contains("--revision18-only"))
            {
                VerifyRevision18();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision17-only"))
            {
                VerifyRevision17();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision16-only"))
            {
                VerifyRevision16();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision15-only"))
            {
                VerifyRevision15();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision14-only"))
            {
                if (args.Contains("--after-revision13")) VerifyRevision13();
                VerifyRevision14();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision13-only"))
            {
                VerifyRevision13();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision12-only"))
            {
                VerifyRevision12();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision11-only"))
            {
                VerifyRevision11();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision10-only"))
            {
                VerifyRevision10();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision9-only"))
            {
                VerifyRevision9();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision8-only"))
            {
                VerifyRevision8();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (args.Contains("--revision7-only"))
            {
                VerifyRevision7();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); return 0;
            }
            if (!args.Contains("--legacy-v3"))
            {
                VerifySimplification();
                VerifyRevision6();
                VerifyRevision7();
                VerifyRevision8();
                VerifyCompactMenu();
                VerifyRevision9();
                VerifyRevision10();
                VerifyRevision11();
                VerifyRevision12();
                VerifyRevision13();
                VerifyRevision14();
                VerifyRevision15();
                VerifyRevision16();
                VerifyRevision17();
                VerifyRevision18();
                VerifyRevision19();
                VerifyRevision20();
                VerifyRevision21();
                VerifyRevision23();
                VerifyRevision24();
                VerifyRevision25();
                VerifyRevision26();
                File.WriteAllText(Path.Combine(_output, "coverage.json"), JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
                SaveDiagnostics(); Console.WriteLine($"Rendered {Captures.Count} revision 0.6 captures."); return 0;
            }
            if (args.Contains("--interactions-only")) { VerifyModernInteractions(); SaveDiagnostics(); return 0; }
            VerifyTemporalRegressions();
            if (args.Contains("--temporal-only")) { SaveDiagnostics(); return 0; }
            VerifyModernInteractions();
            foreach (var theme in new[] { Theme.Dark, Theme.Light })
            {
                App.Settings.Prop.Theme = theme;
                AvaloniaWindow.ApplyTheme();
                if (Application.Current?.Resources.TryGetResource("ApplicationBackgroundColor", null, out var palette) != true)
                    throw new InvalidOperationException("The actual " + theme + " palette did not load.");
                Console.WriteLine("Active palette: " + palette);
                RenderSettings(theme);
                RenderAuxiliary(theme);
            }
            App.Settings.Prop.Theme = Theme.Custom;
            App.Settings.Prop.BackgroundType = BackgroundMode.Gradient;
            AvaloniaWindow.ApplyTheme();
            var custom = new SettingsWindow(false);
            custom.Show();
            ((MainWindowViewModel)custom.DataContext!).NavigateToAppearanceCommand.Execute(null);
            Capture(custom, Theme.Custom, "gradient");
            custom.Hide();
            File.WriteAllText(Path.Combine(_output, "coverage.json"),
                JsonSerializer.Serialize(Captures, new JsonSerializerOptions { WriteIndented = true }));
            SaveDiagnostics();
            Console.WriteLine($"Rendered {Captures.Count} actual application views into {_output}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(_output, "preview-error.txt"), ex.ToString());
            SaveDiagnostics();
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void InitializeFixturePaths(string root)
    {
        // The private setters are only touched by this dedicated test process.
        // Production Paths.Initialize and all migration code are never called.
        foreach (var property in typeof(Paths).GetProperties(BindingFlags.Static | BindingFlags.Public))
        {
            if (property.PropertyType != typeof(string) || property.GetSetMethod(true) == null)
                continue;
            var directory = Path.Combine(root, property.Name);
            Directory.CreateDirectory(directory);
            property.SetValue(null, directory);
        }
    }

    private static void RenderSettings(Theme theme)
    {
        var window = new SettingsWindow(false);
        window.Show();
        var vm = (MainWindowViewModel)window.DataContext!;
        var pages = new (string Name, string Command)[] {
            ("integrations", nameof(vm.NavigateToIntegrationsCommand)),
            ("bootstrapper", nameof(vm.NavigateToBehaviourCommand)),
            ("quick-play", nameof(vm.NavigateToQuickPlayCommand)),
            ("deployment", nameof(vm.NavigateToChannelsCommand)),
            ("preset-mods", nameof(vm.NavigateToPresetModsCommand)),
            ("appearance", nameof(vm.NavigateToAppearanceCommand)),
            ("fastflags", nameof(vm.NavigateToFastFlagsCommand)),
            ("fastflag-editor", nameof(vm.NavigateToFastFlagEditorCommand)),
            ("global-settings", nameof(vm.NavigateToGlobalSettingsCommand)),
            ("global-settings-editor", nameof(vm.NavigateToGlobalSettingsEditorCommand)),
            ("region-selector", nameof(vm.NavigateToRegionSelectorCommand)),
            ("shortcuts", nameof(vm.NavigateToShortcutsCommand)),
            ("installed-mods", nameof(vm.NavigateToMyModsCommand)),
            ("community-mods", nameof(vm.NavigateToCommunityModsCommand)),
            ("mod-generator", nameof(vm.NavigateToModGeneratorCommand))
        };
        foreach (var (name, commandName) in pages)
        {
            var command = (System.Windows.Input.ICommand)vm.GetType().GetProperty(commandName)!.GetValue(vm)!;
            command.Execute(null);
            Pump();
            Capture(window, theme, name);
            VerifyCardPanels(window, theme + " " + name);

            if (name is "integrations" or "bootstrapper" or "deployment" or "preset-mods" or "appearance")
            {
                var content = window.FindControl<TransitioningContentControl>("PageContentControl");
                var scroller = content?.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(s => s.ScrollBarMaximum.Y > 0);
                if (scroller != null)
                {
                    scroller.Offset = new Vector(0, scroller.ScrollBarMaximum.Y);
                    Pump();
                    Capture(window, theme, name + "-bottom");
                    scroller.Offset = default;
                }
            }
        }

        vm.NavigateToAppearanceCommand.Execute(null);
        window.Width = 800;
        window.Height = 650;
        Capture(window, theme, "appearance-narrow");
        window.Width = 1125;
        window.Height = 700;
        foreach (double scaling in new[] { 1.25, 1.5 })
        {
            window.SetRenderScaling(scaling);
            Capture(window, theme, scaling == 1.25 ? "appearance-125-percent" : "appearance-150-percent");
        }
        window.SetRenderScaling(1);

        var themeSelector = window.GetVisualDescendants().OfType<ComboBox>()
            .FirstOrDefault(c => c.SelectedItem is Theme);
        if (themeSelector != null)
        {
            themeSelector.IsDropDownOpen = true;
            Settle();
            CapturePopup(themeSelector, theme, "theme-dropdown");
            themeSelector.IsDropDownOpen = false;
        }

        vm.SearchBar.SetSearchIndex([
            new() { DisplayName = Strings.Menu_Appearance_Custom_Theme_Title, PageName = Strings.Menu_Appearance_Title,
                PageTag = "appearance", Description = "Light, dark, system, or custom appearance." },
            new() { DisplayName = Strings.Menu_Appearance_WindowsBackdrop_Title, PageName = Strings.Menu_Appearance_Title,
                PageTag = "appearance", Description = Strings.Menu_Appearance_WindowsBackdrop_Description }
        ]);
        var search = window.FindControl<Froststrap.UI.Elements.SearchDialog>("SearchOverlay")!;
        search.Show();
        Settle();
        window.KeyTextInput("theme");
        Capture(window, theme, "search-overlay");
        search.Hide();

        vm.NavigateToIntegrationsCommand.Execute(null);
        var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault();
        if (toggle != null)
        {
            var previous = toggle.IsChecked;
            toggle.IsChecked = previous != true;
            Capture(window, theme, "integrations-toggle-state");
            toggle.IsChecked = previous;
        }
        var saveButton = window.GetVisualDescendants().OfType<Button>()
            .First(b => ReferenceEquals(b.Command, vm.SaveSettingsCommand));
        saveButton.Focus(NavigationMethod.Tab);
        Capture(window, theme, "keyboard-focus");
        var point = saveButton.TranslatePoint(new Point(saveButton.Bounds.Width / 2, saveButton.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        Capture(window, theme, "button-hover");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        Capture(window, theme, "button-pressed");
        window.MouseMove(new Point(2, 2), RawInputModifiers.None);
        window.MouseUp(new Point(2, 2), MouseButton.Left, RawInputModifiers.None);
        saveButton.IsEnabled = false;
        Capture(window, theme, "button-disabled");
        saveButton.IsEnabled = true;
        SettingsWindow.ShowGlobalNotification(Strings.Menu_SettingsSaved_Title, Strings.Menu_SettingsSaved_Message,
            FluentAvalonia.UI.Controls.FAInfoBarSeverity.Success);
        Capture(window, theme, "notification");

        var accounts = window.FindControl<Froststrap.UI.Elements.Settings.AccountSelectorView>("AccountSelectorView")!;
        var accountsVm = (AccountSelectorViewModel)accounts.DataContext!;
        accountsVm.IsDropdownOpen = true;
        Settle();
        CapturePopup(accounts, theme, "account-menu");
        accountsVm.IsDropdownOpen = false;
        // Do not close the fully wired settings window: upstream closing commands
        // can save/launch. Hide only, leaving all effects scoped to this process.
        window.Hide();
    }

    private static void RenderAuxiliary(Theme theme)
    {
        var about = new Froststrap.UI.Elements.About.MainWindow();
        about.Show();
        Capture(about, theme, "about");
        var aboutScroll = about.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(s => s.ScrollBarMaximum.Y > 0);
        if (aboutScroll != null)
        {
            aboutScroll.Offset = new Vector(0, aboutScroll.ScrollBarMaximum.Y);
            Capture(about, theme, "about-credits");
            aboutScroll.Offset = default;
        }
        var vm = about.DataContext!;
        foreach (var name in new[] { "NavigateToLicensesCommand", "NavigateToTranslatorsCommand" })
        {
            if (vm.GetType().GetProperty(name)?.GetValue(vm) is System.Windows.Input.ICommand command)
            {
                command.Execute(null);
                Capture(about, theme, name.Contains("Licenses") ? "licenses" : "translations");
            }
        }
        about.Hide();

        var launchMenu = new Froststrap.UI.Elements.Dialogs.LaunchMenuDialog();
        launchMenu.Show();
        Capture(launchMenu, theme, "launch-menu");
        launchMenu.Close();

        var onboarding = new Froststrap.UI.Elements.Onboarding.MainWindow();
        onboarding.Show();
        foreach (var page in new[] { 1, 2, 3, 4, 6, 7 })
        {
            var type = typeof(App).Assembly.GetType($"Froststrap.UI.Elements.Onboarding.Pages.Page{page}")!;
            onboarding.Navigate(type);
            Capture(onboarding, theme, $"onboarding-{page}");
            if (page == 6)
            {
                var first = onboarding.GetVisualDescendants().OfType<Froststrap.UI.Elements.Controls.CardExpander>().First();
                first.IsExpanded = true;
                Capture(onboarding, theme, "onboarding-faq-expanded");
            }
            if (page == 7)
            {
                var discord = onboarding.GetVisualDescendants().OfType<Froststrap.UI.Elements.Controls.DiscordButton>().Single();
                Rect discordBounds = new(discord.TranslatePoint(default, onboarding)!.Value, discord.Bounds.Size);
                var actions = onboarding.GetVisualDescendants().OfType<Froststrap.UI.Elements.Controls.CardAction>();
                Check(actions.All(action => !new Rect(action.TranslatePoint(default, onboarding)!.Value, action.Bounds.Size).Intersects(discordBounds)),
                    "onboarding Discord action does not overlap launch or settings: " + theme);
            }
        }
        onboarding.Hide();

        foreach (bool glass in new[] { false, true })
        {
            var launcher = new Froststrap.UI.Elements.Bootstrapper.FluentDialog(glass);
            launcher.Message = "Starting Roblox...";
            launcher.ProgressMaximum = 100;
            launcher.ProgressValue = 64;
            launcher.CancelEnabled = true;
            launcher.Show();
            Capture(launcher, theme, glass ? "launcher-glass" : "launcher-opaque");
            launcher.Hide();
        }

        foreach (var (name, dialog) in new (string, Window)[] {
            ("color-picker-dialog", new Froststrap.UI.Elements.Dialogs.ColorPickerDialog("#D7AD84")),
            ("launcher-classic", new Froststrap.UI.Elements.Bootstrapper.ClassicFluentDialog()),
            ("launcher-modern", new Froststrap.UI.Elements.Bootstrapper.ModernDialog()),
            ("launcher-2025", new Froststrap.UI.Elements.Bootstrapper.TwentyFiveDialog()),
            ("launcher-byfron", new Froststrap.UI.Elements.Bootstrapper.ByfronDialog())
        })
        {
            if (dialog is Froststrap.UI.Elements.Bootstrapper.Base.AvaloniaDialogBase launcher)
            {
                launcher.Message = "Starting Roblox...";
                launcher.ProgressMaximum = 100;
                launcher.ProgressValue = 64;
                launcher.CancelEnabled = true;
            }
            dialog.Show();
            Capture(dialog, theme, name);
            dialog.Hide();
        }

        Froststrap.Locale.Set("de");
        var localized = new SettingsWindow(false) { Width = 800, Height = 650 };
        localized.Show();
        ((MainWindowViewModel)localized.DataContext!).NavigateToAppearanceCommand.Execute(null);
        Capture(localized, theme, "appearance-german-narrow");
        localized.Hide();
        Froststrap.Locale.Set("en-US");
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Settle()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(420);
        do { Pump(); Thread.Sleep(20); } while (DateTime.UtcNow < deadline);
        Pump();
    }

    private static void CapturePopup(Control owner, Theme theme, string name)
    {
        var popup = owner.GetVisualDescendants().OfType<Popup>().FirstOrDefault(p => p.IsOpen);
        if (popup?.Child is Control child && TopLevel.GetTopLevel(child) is TopLevel root)
            Capture(root, theme, name);
    }

    private static void Capture(TopLevel window, Theme theme, string name)
    {
        Settle();
        var fileName = $"{theme.ToString().ToLowerInvariant()}-{name}.png";
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No rendered frame for " + fileName);
        frame.Save(Path.Combine(_output, fileName), PngBitmapEncoderOptions.Default);
        Captures.Add(new { fileName, theme = theme.ToString(), width = frame.PixelSize.Width,
            height = frame.PixelSize.Height, actualViewType = window.GetType().FullName });
        Console.WriteLine(fileName);
    }
}
