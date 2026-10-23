// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Models;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Elements.Dialogs;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision5()
    {
        VerifyRecoverableLogs();
        var artworkGame = new QuickPlayGameItem { PlaceId = 70001, Name = "Input fixture" };
        int plays = 0;
        var artwork = new GameArtworkCard { Game = artworkGame, Width = 180, Height = 180,
            PlayCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => plays++) };
        var artworkWindow = new Window { Width = 300, Height = 260, Content = artwork }; artworkWindow.Show(); RunFor(80);
        var pinToggle = artwork.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single();
        Click(artworkWindow, At(pinToggle, artworkWindow));
        Check(artworkGame.IsPinned && plays == 0, "artwork star click pins once without launching its parent tile");
        Click(artworkWindow, At(artwork, artworkWindow, .25, .65));
        Check(plays == 1, "artwork background click invokes the injected play command once");
        artworkWindow.Hide();
        Check(!new Froststrap.Models.Persistable.Settings().AllowCookieAccess, "sensitive account-cookie access stays opt-in while confirmation is pending");
        Check(!NavigationRegistry.Pages.Any(page => page.Tag == "globalsettings") && NavigationRegistry.Find("fastflags")!.ModelType == typeof(CuratedFastFlagsViewModel),
            "Global Settings and legacy mesh slider are not reachable through the current route registry");
        App.State.Prop.HasSeededJjsPin = false; HomeViewModel.SeedDefaultPin(); HomeViewModel.SeedDefaultPin();
        Check(App.State.Prop.QuickLaunchPins.Count(pin => pin.PlaceId == 9391468976 && pin.UniverseId == 3508322461) == 1, "verified Jujutsu Shenanigans identity is seeded exactly once");
        GameLaunchService.SetPinned(new QuickPlayGameItem { PlaceId = 9391468976 }, false); HomeViewModel.SeedDefaultPin();
        Check(!App.State.Prop.QuickLaunchPins.Any(pin => pin.PlaceId == 9391468976), "unpinning the seeded game is respected on later Home visits");
        for (int i = 1; i <= 8; i++) GameLaunchService.SetPinned(new QuickPlayGameItem { PlaceId = 80000 + i, Name = "Pinned fixture " + i }, true);
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime;
        App.Settings.Prop.DisableAnimations = false; App.RemoveAnimationSettings();
        var compact = new LaunchMenuDialog(); compact.Show(); RunFor(80);
        Check(!typeof(SettingsWindow).IsAssignableFrom(compact.GetType()) && compact.Bounds.Width == 390 && compact.Bounds.Height == 520,
            "compact startup menu remains a separate 390-wide window with space for bottom pinned games");
        var play = compact.FindControl<Button>("PlayButton")!; var glow = compact.FindControl<Border>("PlayGlow")!;
        compact.MouseMove(At(play, compact), RawInputModifiers.None); var glows = new List<double>();
        for (int i = 0; i < 8; i++) { StepFrame(16); glows.Add(glow.Opacity); RecordFrame(compact, "launcher-glow", i); }
        Check(glows.Distinct().Count() > 3 && glows.Any(value => value > 0 && value < 1), "compact Play hover glow has actual intermediate opacity frames");
        for (int i = 0; i < 8; i++) { compact.MouseMove(i % 2 == 0 ? new Point(8, 60) : At(play, compact), RawInputModifiers.None); StepFrame(20); }
        compact.MouseMove(new Point(8, 60), RawInputModifiers.None); RunFor(350);
        Check(glow.Opacity < .01, "rapid Play hover reversals settle without a stuck glow");
        ((Froststrap.UI.ViewModels.Dialogs.LaunchMenuViewModel)compact.DataContext!).LaunchSettingsCommand.Execute(null);
        Check(compact.CloseAction == NextAction.LaunchSettings, "compact Settings command requests a separate settings window without launching Roblox");

        var shell = new SettingsWindow(false); shell.Show(); RunFor(350);
        var owner = (MainWindowViewModel)shell.DataContext!; var home = (HomeViewModel)owner.CurrentPage!;
        WaitFor(() => !home.Games.IsLoading, 4000);
        home.Games.RecentGames.Clear(); home.Games.RecommendedGames.Clear();
        home.Games.RecentGames.Add(new QuickPlayGameItem { PlaceId = 90001, Name = "Continue fixture" });
        home.Games.RecommendedGames.Add(new QuickPlayGameItem { PlaceId = 90002, Name = "Recommended fixture" }); RunFor(80);
        Check(home.ContinueGames.Count == 1 && home.RecommendedGames.Count == 1, "Home keeps Continue and Recommended in separate observable lists");
        home.SearchQuery = "Recommended";
        Check(home.ContinueGames.Count == 0 && home.RecommendedGames.Count == 1, "centered Home search filters both game lists without mixing them");
        home.SearchQuery = ""; RunFor(80);
        GameLaunchService.SetPinned(home.Games.RecentGames[0], true);
        Check(home.PinnedGames.Any(game => game.PlaceId == 90001), "Home stars refresh the pinned artwork row immediately");
        home.PinnedGames.Single(game => game.PlaceId == 90001).IsPinned = false; RunFor(60);
        Check(!home.PinnedGames.Any(game => game.PlaceId == 90001), "Home unpin removes the artwork tile immediately");
        var continueTile = shell.GetVisualDescendants().OfType<GameArtworkCard>().First(card => ReferenceEquals(card.Game, home.Games.RecentGames[0]));
        Check(continueTile.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single().IsChecked == false,
            "unpinning a shortcut updates the corresponding Continue card star");
        var pins = shell.FindControl<TransitioningContentControl>("PageContentControl")!.GetVisualDescendants().OfType<ArtworkScrollViewer>().Single();
        var outer = shell.FindControl<TransitioningContentControl>("PageContentControl")!.GetVisualDescendants().OfType<SmoothScrollViewer>().First();
        pins.BringIntoView(); RunFor(80); double outerBefore = outer.Offset.Y;
        shell.MouseWheel(At(pins, shell), new Vector(0, -2), RawInputModifiers.None); RunFor(350);
        Check(pins.ScrollBarMaximum.X > 0 && pins.Offset.X > 0 && outer.Offset.Y == outerBefore, "wheel scrolling over more than three pins moves only the artwork row");
        var tile = shell.GetVisualDescendants().OfType<GameArtworkCard>().First(card => card.Game?.PlaceId == 90001);
        tile.BringIntoView(); RunFor(80);
        Check(tile.PlayCommand != null && tile.FindControl<Button>("PlayTile")!.Bounds.Size == tile.Bounds.Size,
            "actual game artwork fills its tile and binds to the shared play command");
        using var bright = new RenderTargetBitmap(new PixelSize(32, 32));
        using (var drawing = bright.CreateDrawingContext()) drawing.DrawRectangle(Avalonia.Media.Brushes.White, null, new Rect(0, 0, 32, 32));
        tile.Game!.ThumbnailBitmap = bright; RunFor(80);
        Check(ReferenceEquals(tile.FindControl<Image>("GameImage")!.Source, bright), "late game artwork updates the actual image binding");
        Capture(shell, Froststrap.Enums.Theme.Dark, "home-bright-artwork");
        shell.MouseMove(At(tile, shell), RawInputModifiers.None); var reveals = new List<double>();
        for (int i = 0; i < 8; i++) { StepFrame(16); reveals.Add(tile.RevealOpacity); RecordFrame(shell, "artwork-hover", i); }
        Check(reveals.Distinct().Count() > 3 && reveals.Any(value => value > 0 && value < 1), "game artwork reveals Play over actual intermediate frames");
        for (int i = 0; i < 8; i++) { shell.MouseMove(i % 2 == 0 ? new Point(4, 55) : At(tile, shell), RawInputModifiers.None); StepFrame(20); }
        shell.MouseMove(new Point(4, 55), RawInputModifiers.None); RunFor(350);
        Check(tile.RevealOpacity < .01, "rapid game hover reversals settle without a visible stale Play layer");
        shell.Hide();
    }
    private static void VerifyRecoverableLogs()
    {
        string root = Path.Combine(_output, "fixture-data", "log-retention-" + Guid.NewGuid().ToString("N"), "CoffeeStrap", "Logs");
        Directory.CreateDirectory(root); DateTime now = DateTime.UtcNow;
        void Write(string name, string body, double age)
        { string file = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, body); File.SetLastWriteTimeUtc(file, now.AddHours(-age)); }
        Write("2026-01-01_01-01-01.log", "DEBUG Starting CoffeeStrap 0.5.0\nfixture", 25);
        Write("2026-01-02_01-01-01.log", "DEBUG Starting CoffeeStrap 0.5.0", 23);
        Write("latest.log", "DEBUG Starting CoffeeStrap 0.5.0", 48);
        Write("2026-01-03_01-01-01.log", "Starting Roblox", 48);
        Write("notes.log", "unrelated", 48); Write("nested/2026-01-04_01-01-01.log", "Starting CoffeeStrap 0.5.0", 48);
        int moved = AppLogRetention.ArchiveExpired(root, now);
        Check(moved == 1 && File.ReadAllText(Path.Combine(root, "Retained", "2026-01-01_01-01-01.log")).Contains("fixture"), "one-day retention moves identified expired app logs intact to a recoverable local archive");
        Check(new[] { "2026-01-02_01-01-01.log", "latest.log", "2026-01-03_01-01-01.log", "notes.log", "nested/2026-01-04_01-01-01.log" }.All(name => File.Exists(Path.Combine(root, name))),
            "retention preserves recent, active, unrelated, Roblox-named content and nested files");
        Check(AppLogRetention.ArchiveExpired(root, now) == 0, "repeat retention is idempotent and does not purge recovered logs");
    }
}
