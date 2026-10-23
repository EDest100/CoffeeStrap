#pragma warning disable CA1001 // Avalonia owns window lifetime; disposable search state is released in OnClosed.
// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Settings;
using LucideAvalonia.Enum;
using System.ComponentModel;

namespace Froststrap.UI.Elements.Settings
{
    internal partial class MainWindow : Base.AvaloniaWindow
    {
        protected override bool ApplyTopPadding => false;
        public static MainWindow? Instance { get; private set; }

        public static WindowNotificationManager? NotificationManager { get; private set; }

        private static Models.Persistable.WindowState State => App.State.Prop.SettingsWindow;
        private readonly MainWindowViewModel? _viewModel;

        public NextAction CloseAction => _viewModel?.CloseAction ?? NextAction.Terminate;
        private readonly HashSet<string> _indexedPageTags = [];
        private readonly SearchIndexBuilder _searchIndexBuilder = new();
        private bool _isIndexingAllPagesStarted;
        private readonly CancellationTokenSource _indexLifetime = new();
        private Task _indexTask = Task.CompletedTask;
        private bool _closed;

        private const double NotificationHeight = 80;
        private const double NotificationSpacing = 15;
        private const double NotificationSlideDistance = 500;
        private const int MaxVisibleNotifications = 3;
        private readonly List<NotificationEntry> _notifications = [];
        private sealed class NotificationEntry
        {
            public required Border Element { get; init; }
            public required TranslateTransform Transform { get; init; }
            public CancellationTokenSource? TimeoutCts { get; set; }
            public bool SaveNotice { get; init; }
        }

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            foreach (var route in NavigationRegistry.Pages.Where(route => route.Sidebar))
                this.FindControl<CoffeeNavigationView>("NavView")!.MenuItems.Add(new CoffeeNavigationItem { Content = route.Title, Tag = route.Tag, Icon = new LucideAvalonia.Lucide { Icon = route.Icon } });
            WindowDecorations = WindowDecorations.BorderOnly;
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.K && e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
                { this.FindControl<InlineSettingsSearch>("SettingsSearch")?.FocusSearch(); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 52;
        }

        public MainWindow(bool showAlreadyRunningWarning) : this()
        {
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
            Activated += (_, _) => { if (_viewModel.CurrentPage is HomeViewModel home) home.RefreshLastPlace(); };

            _viewModel.RequestSaveNoticeEvent += (_, _) => ShowSaveNotification();
            _viewModel.RequestCloseWindowEvent += (_, _) => Close();
            _viewModel.SearchBar.SearchResultSelected += (_, item) => OnSearchResultSelected(item);

            var search = this.FindControl<InlineSettingsSearch>("SettingsSearch");
            if (search != null) search.IndexRequested += (_, _) => StartIndexingAllPagesIfNeeded();

            App.Logger.Debug("Initializing settings window");

            if (showAlreadyRunningWarning)
                ShowAlreadyRunningNotification();

            LoadState();


            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;

            UpdatePageView(_viewModel.CurrentPage);

            Dispatcher.UIThread.Post(() =>
            {
                UpdateSelectedNavigationViewItem(_viewModel.SelectedPage);
            }, DispatcherPriority.Loaded);
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true; _indexLifetime.Cancel(); _indexLifetime.Dispose();
            foreach (var entry in _notifications) entry.TimeoutCts?.Cancel();
            _notifications.Clear(); this.FindControl<Panel>("NotificationPanel")?.Children.Clear();
            if (_viewModel != null) _viewModel.SearchBar.IsIndexing = false;
            _searchRevealCancellation?.Cancel(); _searchRevealCancellation?.Dispose();
            (_viewModel?.CurrentPage as IDisposable)?.Dispose(); _viewModel?.SearchBar.Dispose();
            base.OnClosed(e);
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            NotificationManager = new WindowNotificationManager(TopLevel.GetTopLevel(this))
            {
                Position = NotificationPosition.TopRight,
                MaxItems = 3
            };
        }

        public void StartIndexingAllPagesIfNeeded()
        {
            if (_closed || _isIndexingAllPagesStarted) return;
            _isIndexingAllPagesStarted = true;
            if (_viewModel != null) _viewModel.SearchBar.IndexingIncomplete = false;
            _indexTask = IndexMissingPagesAsync(_indexLifetime.Token);
        }
        internal Task SearchIndexingTask => _indexTask;

        private void TitleBarGrid_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            this.BeginMoveDrag(e);
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                UpdatePageView(_viewModel.CurrentPage);
                UpdateSelectedNavigationViewItem(_viewModel.BreadcrumbItems.FirstOrDefault()?.Tag ?? _viewModel.SelectedPage);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.SelectedPage))
            {
                UpdateSelectedNavigationViewItem(_viewModel.SelectedPage);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.HasSaveError) && _viewModel.HasSaveError)
                ClearSaveNotifications();
        }

        private SearchBarItem? _pendingSearchScrollItem;

        private void OnSearchResultSelected(SearchBarItem item)
        {
            this.FindControl<SearchDialog>("SearchOverlay")?.Hide();

            _pendingSearchScrollItem = item;

            if (_viewModel?.SelectedPage != item.PageTag)
            {
                SaveCurrentPage();

                var action = GetNavigationAction(item.PageTag ?? "");
                action?.Invoke();
            }
            else
            {
                _pendingSearchScrollItem = null;
                ScrollToSearchItem(item);
            }
        }

        private Action? GetNavigationAction(string pageTag) => NavigationRegistry.Find(pageTag) != null
            ? () => _viewModel?.NavigateRegistered(pageTag) : null;

        private readonly Dictionary<string, (string Title, LucideIconNames Icon)> _pageInfo =
            NavigationRegistry.Pages.ToDictionary(page => page.Tag, page => (page.Title, page.Icon));

        private void UpdatePageView(object? viewModel)
        {
            SaveCurrentPage();

            var pageControl = this.FindControl<TransitioningContentControl>("PageContentControl");
            if (pageControl == null || viewModel == null) return;

            string pageTag = _viewModel?.SelectedPage ?? "";
            Control? view = ResolveViewForViewModel(viewModel);
            if (view == null) return;

            if (view.DataContext == viewModel) return;

            view.DataContext = viewModel;
            pageControl.Content = view;

            Dispatcher.UIThread.Post(() =>
            {
                if (_closed) return;
                if (!string.IsNullOrEmpty(pageTag) && _pageInfo.TryGetValue(pageTag, out var info)
                    && !_indexedPageTags.Contains(pageTag))
                {
                    if (IndexPage(view, pageTag, info.Title, info.Icon)) _indexedPageTags.Add(pageTag);
                }

                if (_pendingSearchScrollItem is { } pending && _viewModel?.SelectedPage == pending.PageTag)
                {
                    _pendingSearchScrollItem = null;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ReferenceEquals(pageControl.Content, view)) ScrollToSearchItem(pending);
                    }, DispatcherPriority.Render);
                }
            }, DispatcherPriority.Background);
        }

        private void NavView_ItemInvoked(object? sender, CoffeeNavigationInvokedEventArgs e)
        {
            if (e.InvokedItemContainer is CoffeeNavigationItem navItem && navItem.Tag is string tag)
            {
                if (tag == "about")
                {
                    _viewModel?.OpenAboutCommand.Execute(null);
                    return;
                }

                if (_viewModel != null && _viewModel.SelectedPage == tag)
                {
                    return;
                }

                SaveCurrentPage();

                var action = GetNavigationAction(tag);
                action?.Invoke();
            }
        }

        private void OpenSearchOverlay(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            StartIndexingAllPagesIfNeeded();

            this.FindControl<SearchDialog>("SearchOverlay")?.Show();
        }

        private void UpdateSelectedNavigationViewItem(string selectedPage)
        {
            var navView = this.FindControl<CoffeeNavigationView>("NavView");
            if (navView == null) return;

            foreach (var item in navView.MenuItems)
            {
                if (item is CoffeeNavigationItem navItem && navItem.Tag is string tag)
                {
                    if (tag == selectedPage)
                    {
                        navView.SelectedItem = navItem;
                        return;
                    }
                }
            }
            foreach (var item in navView.FooterMenuItems)
            {
                if (item is CoffeeNavigationItem navItem && navItem.Tag is string tag)
                {
                    if (tag == selectedPage)
                    {
                        navView.SelectedItem = navItem;
                        return;
                    }
                }
            }
        }

        private static Control? ResolveViewForViewModel(object viewModel) => NavigationRegistry.CreateView(viewModel);

        public void LoadState()
        {
            var screen = Screens.Primary?.Bounds;
            if (screen != null)
            {
                if (State.Left > screen.Value.Width) State.Left = 0;
                if (State.Top > screen.Value.Height) State.Top = 0;
            }

            if (State.Width > 0) this.Width = State.Width;
            if (State.Height > 0) this.Height = State.Height;

            if (State.Left > 0 && State.Top > 0)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Position = new PixelPoint((int)State.Left, (int)State.Top);
            }
        }

        private void ShowSaveNotification()
        {
            ClearSaveNotifications();
            ShowNotification(
                Strings.Menu_SettingsSaved_Title,
                Strings.Menu_SettingsSaved_Message,
                FAInfoBarSeverity.Success,
                3000, saveNotice: true);
        }

        private void ClearSaveNotifications()
        {
            var panel = this.FindControl<Panel>("NotificationPanel");
            foreach (var entry in _notifications.Where(item => item.SaveNotice).ToArray())
            {
                entry.TimeoutCts?.Cancel(); _notifications.Remove(entry); panel?.Children.Remove(entry.Element);
            }
            RepositionNotifications();
        }

        private async void ShowAlreadyRunningNotification()
        {
            await Task.Delay(500);
            ShowNotification(
                Strings.Menu_AlreadyRunning_Title,
                Strings.Menu_AlreadyRunning_Caption,
                FAInfoBarSeverity.Warning,
                5000);
        }

        public static void ShowGlobalNotification(string title, string subtitle, FAInfoBarSeverity type, int timeout = 3000, LucideIconNames? icon = null)
        {
            Dispatcher.UIThread.Post(() => Instance?.ShowNotification(title, subtitle, type, timeout, icon));
        }

        public void ShowNotification(string title, string subtitle, FAInfoBarSeverity type, int timeout, LucideIconNames? customIcon = null, bool saveNotice = false)
        {
            if (_closed) return;
            var notificationPanel = this.FindControl<Panel>("NotificationPanel");
            if (notificationPanel == null) return;

            var iconSymbol = customIcon ?? (type == FAInfoBarSeverity.Success
                ? LucideIconNames.CircleCheck
                : LucideIconNames.TriangleAlert);

            var contentGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Margin = new Thickness(0)
            };
            var icon = new LucideAvalonia.Lucide
            {
                Icon = type == FAInfoBarSeverity.Success ? LucideIconNames.Check : LucideIconNames.X,
                StrokeBrush = new SolidColorBrush(
                    type == FAInfoBarSeverity.Success
                        ? Color.Parse("#00D084")
                        : Color.Parse("#FFB900")
                ),
                Margin = new Thickness(25, 15, 20, 15),
                FontSize = 20,
                Width = 20,
                Height = 20,
            };
            Grid.SetColumn(icon, 0);
            contentGrid.Children.Add(icon);

            var textPanel = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Spacing = 2 };
            var titleText = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(0, 2) };
            titleText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextFillColorPrimaryBrush"));
            var subtitleText = new TextBlock { Text = subtitle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2) };
            subtitleText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextFillColorSecondaryBrush"));
            textPanel.Children.Add(titleText);
            textPanel.Children.Add(subtitleText);
            Grid.SetColumn(textPanel, 1);
            contentGrid.Children.Add(textPanel);

            var closeButton = new IconButton
            {
                Icon = LucideIconNames.X,
                IconSize = 12,
                CornerRadius = new CornerRadius(0, 10, 10, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(20, 0, 0, 0),
                Width = 50,
            };
            closeButton.Bind(IconButton.ForegroundProperty, new DynamicResourceExtension("TextFillColorSecondaryBrush"));
            Grid.SetColumn(closeButton, 2);
            contentGrid.Children.Add(closeButton);

            var transform = new TranslateTransform(NotificationSlideDistance, 0)
            {
                Transitions =
                [
                    new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(350), Easing = new QuarticEaseOut() },
                    new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new QuarticEaseOut() }
                ]
            };

            var notification = new Border
            {
                Margin = new Thickness(0, 15, 15, 0),
                MinWidth = 350,
                Height = NotificationHeight,
                CornerRadius = new CornerRadius(10),
                RenderTransform = transform,
                Child = contentGrid,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, OffsetY = 4, Color = Color.Parse("#40000000") }),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };
            notification.Bind(Border.BackgroundProperty, new DynamicResourceExtension("NotificationBackgroundColor"));

            var entry = new NotificationEntry { Element = notification, Transform = transform, SaveNotice = saveNotice };

            void Dismiss() => DismissNotification(entry);

            closeButton.Click += (s, e) => { e.Handled = true; Dismiss(); };
            notification.PointerPressed += (s, e) => { if (e.Source is IconButton) return; Dismiss(); };

            _notifications.Insert(0, entry);
            notificationPanel.Children.Add(notification);

            while (_notifications.Count > MaxVisibleNotifications)
                DismissNotification(_notifications[^1]);

            RepositionNotifications();

            var cts = new CancellationTokenSource();
            entry.TimeoutCts = cts;

            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(50);
                if (_closed || cts.IsCancellationRequested) return;
                transform.X = 0;

                await Task.Delay(timeout);
                if (!cts.IsCancellationRequested)
                    Dismiss();
            });
        }

        private async void DismissNotification(NotificationEntry entry)
        {
            if (!_notifications.Remove(entry)) return;

            entry.TimeoutCts?.Cancel();
            RepositionNotifications();

            entry.Transform.X = NotificationSlideDistance;
            await Task.Delay(350);
            if (_closed) return;

            var notificationPanel = this.FindControl<Panel>("NotificationPanel");
            if (notificationPanel != null && notificationPanel.Children.Contains(entry.Element))
                notificationPanel.Children.Remove(entry.Element);
        }

        private void RepositionNotifications()
        {
            for (var i = 0; i < _notifications.Count; i++)
                _notifications[i].Transform.Y = i * (NotificationHeight + NotificationSpacing);
        }

        public void ShowLoading(string message = "Loading...")
        {
            var loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
            var loadingText = this.FindControl<TextBlock>("LoadingOverlayText");

            if (loadingOverlay != null && loadingText != null)
            {
                loadingText.Text = message;
                loadingOverlay.IsVisible = true;
            }
        }

        public void HideLoading()
        {
            var loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
            loadingOverlay?.IsVisible = false;
        }

        private async Task IndexMissingPagesAsync(CancellationToken cancellation)
        {
            if (_viewModel == null) return;

            var allPageTags = NavigationRegistry.Pages.Where(page => page.Searchable && (page.Tag != "globalsettings" || _viewModel.GBSEnabled)).Select(page => page.Tag).ToList();

            var pagesToIndex = allPageTags.Where(tag => !_indexedPageTags.Contains(tag)).ToList();
            if (pagesToIndex.Count == 0) return;

            _viewModel.SearchBar.IsIndexing = true;

            var stagingArea = this.FindControl<Border>("OffscreenIndexingCanvas");
            if (stagingArea == null)
            {
                App.Logger.Error("OffscreenIndexingCanvas not found");
                _viewModel.SearchBar.IsIndexing = false;
                _viewModel.SearchBar.IndexingIncomplete = true; _isIndexingAllPagesStarted = false;
                return;
            }

            stagingArea.IsVisible = true;
            bool failed = false;

            try
            {
                foreach (var pageTag in pagesToIndex)
                {
                    cancellation.ThrowIfCancellationRequested();
                    object? vm = null;
                    Control? view = null;
                    Border? container = null;
                    try
                    {
                        vm = NavigationRegistry.Find(pageTag)?.CreateModel(_viewModel);

                        if (vm == null) continue;

                        view = ResolveViewForViewModel(vm);
                        if (view == null) continue;

                        view.DataContext = vm;

                        container = new Border { Child = view };
                        stagingArea.Child = container;

                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render, cancellation);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, cancellation);
                        cancellation.ThrowIfCancellationRequested();

                        var (title, icon) = _pageInfo[pageTag];
                        if (IndexPage(view, pageTag, title, icon)) _indexedPageTags.Add(pageTag);
                        else failed = true;

                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        failed = true;
                        App.Logger.Error($"Error indexing {pageTag}: {ex.Message}");
                    }
                    finally
                    {
                        if (container != null) container.Child = null;
                        stagingArea.Child = null;
                        if (view != null) view.DataContext = null;
                        if (vm is IDisposable disposable) disposable.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error) { failed = true; App.Logger.Error(error, "Search indexing could not finish."); }
            finally
            {
                stagingArea.IsVisible = false;
                if (!_closed)
                {
                    _isIndexingAllPagesStarted = !failed;
                    _viewModel.SearchBar.IndexingIncomplete = failed;
                    _viewModel.SearchBar.IsIndexing = false;
                    _viewModel.SearchBar.RefreshSearchResults();
                }
            }
        }

        private bool IndexPage(Control pageView, string pageTag, string pageTitle, LucideIconNames pageIcon)
        {
            if (_viewModel == null || _closed) return false;

            try
            {
                var addedItems = _searchIndexBuilder.ScanRenderedPageForElements(pageView, pageTag);

                if (addedItems.Count > 0)
                {
                    var navAction = GetNavigationAction(pageTag);
                    foreach (var item in addedItems)
                    {
                        item.PageName = pageTitle;
                        item.IconSymbol = pageIcon;
                        if (navAction != null)
                            item.NavigateAction = navAction;
                    }

                    _viewModel.SearchBar.AddSearchItems(addedItems);
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.Error($"Failed to scan page {pageTag}: {ex.Message}");
                _searchIndexBuilder.InvalidatePageCache(pageTag);
                return false;
            }
        }

        private CancellationTokenSource? _searchRevealCancellation;
        internal Control? LastSearchFocusTarget { get; private set; }
        private async void ScrollToSearchItem(SearchBarItem item)
        {
            if (_closed) return;
            _searchRevealCancellation?.Cancel();
            _searchRevealCancellation?.Dispose();
            var cancellation = _searchRevealCancellation = new CancellationTokenSource();
            try
            {
                var page = this.FindControl<TransitioningContentControl>("PageContentControl")?.Content as Control;
                if (page == null) return;
                bool Matches(Control control)
                {
                    bool title = control switch
                    {
                        OptionControl option => item.Category == "Setting" && option.Header == item.DisplayName,
                        CardExpander group => item.Category == "Section" && Equals(group.Header, item.DisplayName),
                        CardAction action => item.Category == "Action" && (action.Header == item.DisplayName || Equals(action.Content, item.DisplayName)),
                        SquareCard card => item.Category == "Card" && Equals(card.Header, item.DisplayName),
                        _ => false
                    };
                    return title && (item.ParentSectionName == null || Equals(control.GetVisualAncestors().OfType<CardExpander>().FirstOrDefault()?.Header, item.ParentSectionName));
                }
                Control? target = page.GetVisualDescendants().OfType<Control>().FirstOrDefault(Matches);
                if (target == null) return;
                if (target is CardExpander section) section.SetCurrentValue(CardExpander.IsExpandedProperty, true);
                foreach (var group in target.GetVisualAncestors().OfType<CardExpander>()) group.SetCurrentValue(CardExpander.IsExpandedProperty, true);
                if (CoffeeMotion.Enabled) await Task.Delay(250, cancellation.Token);
                if (cancellation.IsCancellationRequested || !ReferenceEquals(page, this.FindControl<TransitioningContentControl>("PageContentControl")?.Content)) return;
                target.BringIntoView();
                Control? focus = target;
                if (target is OptionControl option)
                    focus = option.Toggle != null ? option : OptionControl.GetEditorControls(option.InnerContent)
                        .FirstOrDefault(control => control.Focusable && control.IsEffectivelyEnabled);
                else if (!target.Focusable)
                    focus = target.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Focusable && control.IsEffectivelyEnabled);
                focus ??= target; focus.Focusable = true;
                focus.Focus(NavigationMethod.Tab);
                LastSearchFocusTarget = focus;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { App.Logger.Error(error, "Could not reveal a settings search result."); }
        }

        private void SaveCurrentPage()
        {
            if (_viewModel?.CurrentPage != null)
            {
                App.State.Prop.LastPage = _viewModel.CurrentPage.GetType().FullName;
                App.State.SaveSetting("LastPage");
            }
        }
        #region Event Handlers

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (MainWindowViewModel.HasUnsavedChanges)
            {
                e.Cancel = true;

                var result = await Frontend.ShowMessageBox(
                    Strings.Menu_UnsavedChangesPrompt,
                    MessageBoxImage.Warning,
                    MessageBoxButton.YesNoCancel
                );

                if (_viewModel?.CanCloseAfterPrompt(result) != true)
                    return;

                this.Closing -= MainWindow_Closing;
                this.Close();
                return;
            }

            State.Width = this.Width;
            State.Height = this.Height;
            State.Left = this.Position.X;
            State.Top = this.Position.Y;

            SaveCurrentPage();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            NotificationManager = null;
            Instance = null;
            App.SettingsOpen = false;

            App.Logger.Info("Settings window closed");
        }

        #endregion
    }
}
