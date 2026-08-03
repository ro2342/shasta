using System;
using Shasta.Models;
using Shasta.Services;
using Shasta.Views;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Shasta
{
    // Native navigation shell: a Frame for content + a fixed header at the
    // top (hamburger + current section title, side by side) that opens a
    // SplitView sliding over the content, in the same spirit as Microsoft's
    // own native apps (News/Forecast/Settings: "☰ Section name"). Switching
    // destination closes the pane, updates the header title, and doesn't
    // push history (they're peers at the same level); navigating to a
    // detail page pushes normally onto the Frame's back stack instead,
    // without changing the header title.
    public sealed partial class MainPage : Page
    {
        public static MainPage Current { get; private set; }

        // Only used to reapply the accent color when it changes live (see
        // UiSettings_ColorValuesChanged) — always the last top-level
        // destination (Home/Library/Downloads/Settings), never a "detail"
        // page that happens to be pushed on the Frame.
        private Type _currentTabPageType;
        private readonly UISettings _uiSettings = new UISettings();

        public MainPage()
        {
            try
            {
                this.InitializeComponent();
                Current = this;
                ApplyNavLabels();
                StyleMenuButton();
                this.Loaded += MainPage_Loaded;
                ContentFrame.Navigated += ContentFrame_Navigated;
                SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;

                // SystemAccentColor is already a {ThemeResource} everywhere
                // we don't copy it manually, so it updates itself — but the
                // MenuButton and the active pane item use a SolidColorBrush
                // copied once (ThemeHelper.AccentBrush), which doesn't
                // follow a live change. UISettings.ColorValuesChanged fires
                // as soon as the user changes the system accent color (even
                // without restarting the app).
                _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
                // Deliberately not subscribed here — see MainPage_Loaded.
                // The constructor runs during App.OnLaunched, before the
                // window is activated; PlaybackService is touched for the
                // first time from Loaded instead, once the page is
                // actually on screen.
            }
            catch (Exception ex)
            {
                ShowFatalError("Error starting the page: " + ex.Message);
            }
        }

        private async void PlaybackService_StateChanged(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, UpdateMiniPlayer);
        }

        // StateChanged fires often (every playback position tick) — track
        // which item the cover was last loaded for so a fresh network
        // fetch only happens when the playing item actually changes, not
        // on every single tick.
        private string _miniPlayerCoverItemId;

        private async void UpdateMiniPlayer()
        {
            bool isOnPlayerPage = ContentFrame.CurrentSourcePageType == typeof(PlayerPage);
            if (!PlaybackService.HasActiveItem || isOnPlayerPage)
            {
                MiniPlayerBar.Visibility = Visibility.Collapsed;
                return;
            }

            AbsLibraryItem item = PlaybackService.CurrentItem;
            MiniPlayerTitleText.Text = item.GetTitle();
            MiniPlayerAuthorText.Text = item.GetAuthorDisplay();
            // Segoe MDL2 Assets glyphs: Pause is codepoint E769, Play is E768.
            MiniPlayerPlayPauseIcon.Glyph = PlaybackService.IsPlaying ? "" : "";
            MiniPlayerBar.Visibility = Visibility.Visible;

            if (_miniPlayerCoverItemId != item.Id)
            {
                _miniPlayerCoverItemId = item.Id;
                await LibraryService.LoadCoverAsync(MiniPlayerCover, item.Id, 100);
            }
        }

        private void MiniPlayerBar_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(PlayerPage));
        }

        private void MiniPlayerPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackService.IsPlaying)
            {
                PlaybackService.Pause();
            }
            else
            {
                PlaybackService.Resume();
            }
        }

        private async void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            // Fires on a background thread — must hop back to the UI thread
            // before touching any visual element.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                StyleMenuButton();
                if (_currentTabPageType != null)
                {
                    UpdateActiveTab(_currentTabPageType);
                }
            });
        }

        private void ApplyNavLabels()
        {
            NavHomeLabel.Text = "Home";
            NavLibraryLabel.Text = "Library";
            NavDownloadsLabel.Text = "Downloads";
            NavSettingsLabel.Text = "Settings";
        }

        // Solid square in the accent color, like News' menu button — has to
        // be applied in code because the system accent color doesn't change
        // with the theme.
        private void StyleMenuButton()
        {
            SolidColorBrush accent = ThemeHelper.AccentBrush();
            MenuButton.Background = accent;
            MenuButton.Foreground = new SolidColorBrush(Windows.UI.Colors.White);
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                PlaybackService.StateChanged += PlaybackService_StateChanged;

                bool hasSession = await AbsAuthService.RestoreSessionAsync();
                if (hasSession)
                {
                    HeaderBar.Visibility = Visibility.Visible;
                    NavigateToTab(typeof(HomePage));
                }
                else
                {
                    BeginOnboarding();
                }
            }
            catch (Exception ex)
            {
                ShowFatalError("Error loading the app: " + ex.Message);
            }
        }

        public void BeginOnboarding()
        {
            HeaderBar.Visibility = Visibility.Collapsed;
            NavSplitView.IsPaneOpen = false;
            ContentFrame.Navigate(typeof(OnboardingPage));
            ContentFrame.BackStack.Clear();
        }

        public void CompleteOnboarding()
        {
            HeaderBar.Visibility = Visibility.Visible;
            NavigateToTab(typeof(HomePage));
        }

        // — navigation pane —

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            SetPaneOpen(!NavSplitView.IsPaneOpen);
        }

        private void PaneDismissOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SetPaneOpen(false);
        }

        private void SetPaneOpen(bool open)
        {
            NavSplitView.IsPaneOpen = open;
            PaneDismissOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NavItem_Click(object sender, RoutedEventArgs e)
        {
            string tag = (string)((FrameworkElement)sender).Tag;
            Type pageType;
            switch (tag)
            {
                case "Home":
                    pageType = typeof(HomePage);
                    break;
                case "Library":
                    pageType = typeof(LibraryPage);
                    break;
                case "Downloads":
                    pageType = typeof(DownloadsPage);
                    break;
                case "Settings":
                    pageType = typeof(SettingsPage);
                    break;
                default:
                    return;
            }
            NavigateToTab(pageType);
            SetPaneOpen(false);
        }

        public void NavigateToTab(Type pageType, object parameter = null)
        {
            ContentFrame.Navigate(pageType, parameter);
            ContentFrame.BackStack.Clear();
            _currentTabPageType = pageType;
            UpdateActiveTab(pageType);
        }

        private void UpdateActiveTab(Type pageType)
        {
            // Never hand-compute a "default" brush here: a lookup via
            // Application.Current.Resources[...] doesn't follow a live
            // theme change (RequestedTheme changed by ThemeModeService), it
            // always resolves to whatever theme the app launched with.
            // Instead, clear the local value (ClearValue) to inherit the
            // Button's default Foreground, which IS truly theme-aware via
            // {ThemeResource}.
            SolidColorBrush accent = ThemeHelper.AccentBrush();

            bool isHome = pageType == typeof(HomePage);
            bool isLibrary = pageType == typeof(LibraryPage);
            bool isDownloads = pageType == typeof(DownloadsPage);
            bool isSettings = pageType == typeof(SettingsPage);

            SetTabForeground(NavHomeLabel, NavHomeIcon, isHome, accent);
            SetTabForeground(NavLibraryLabel, NavLibraryIcon, isLibrary, accent);
            SetTabForeground(NavDownloadsLabel, NavDownloadsIcon, isDownloads, accent);
            SetTabForeground(NavSettingsLabel, NavSettingsIcon, isSettings, accent);

            if (isHome) HeaderTitleText.Text = "Home";
            else if (isLibrary) HeaderTitleText.Text = "Library";
            else if (isDownloads) HeaderTitleText.Text = "Downloads";
            else if (isSettings) HeaderTitleText.Text = "Settings";
        }

        // IconElement (not SymbolIcon) because NavDownloadsIcon is a
        // FontIcon (raw glyph) — both inherit from IconElement, which
        // already has the Foreground property used here.
        private static void SetTabForeground(TextBlock label, IconElement icon, bool active, Brush accent)
        {
            if (active)
            {
                label.Foreground = accent;
                icon.Foreground = accent;
            }
            else
            {
                label.ClearValue(TextBlock.ForegroundProperty);
                icon.ClearValue(IconElement.ForegroundProperty);
            }
        }

        // — navigation/back —

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                ContentFrame.CanGoBack ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;
            UpdateMiniPlayer();
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (NavSplitView.IsPaneOpen)
            {
                e.Handled = true;
                SetPaneOpen(false);
                return;
            }
            if (ContentFrame.CanGoBack)
            {
                e.Handled = true;
                ContentFrame.GoBack();
            }
        }

        // — fatal error —

        public void ShowFatalError(string message)
        {
            if (ErrorText != null && ErrorPanel != null)
            {
                ContentFrame.Visibility = Visibility.Collapsed;
                ErrorText.Text = message;
                ErrorPanel.Visibility = Visibility.Visible;
            }
        }
    }
}
