using System;
using System.Linq;
using System.Threading.Tasks;
using Shasta.Models;
using Shasta.Services;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class SettingsPage : Page
    {
        private AppSettings _settings;
        private StorageFile _downloadedUpdateFile;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            AbsSession session = AbsAuthService.CurrentSession;
            ServerUrlText.Text = session?.ServerUrl ?? "";
            UsernameText.Text = session?.User?.Username ?? "";

            _settings = await LocalDataStore.GetSettingsAsync();
            UpdateThemeButtonHighlight(_settings.ThemeMode);

            UpdateStatusText.Text = $"You're on version {UpdateCheckService.GetInstalledVersion()}.";
        }

        // — Server —

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            await AbsAuthService.LogoutAsync();
            MainPage.Current.BeginOnboarding();
        }

        // — Appearance —

        private async void ThemeMode_Click(object sender, RoutedEventArgs e)
        {
            string mode = (string)((FrameworkElement)sender).Tag;
            _settings.ThemeMode = mode;
            ThemeModeService.Apply(mode);
            await LocalDataStore.SetSettingsAsync(_settings);
            UpdateThemeButtonHighlight(mode);
        }

        private void UpdateThemeButtonHighlight(string mode)
        {
            SolidColorBrush accent = ThemeHelper.AccentBrush();
            SetThemeButtonAccent(ThemeLightButton, mode == "light", accent);
            SetThemeButtonAccent(ThemeDarkButton, mode == "dark", accent);
            SetThemeButtonAccent(ThemeAutoButton, mode != "light" && mode != "dark", accent);
        }

        private static void SetThemeButtonAccent(Button button, bool active, SolidColorBrush accent)
        {
            if (active)
            {
                button.BorderBrush = accent;
                button.BorderThickness = new Thickness(2);
            }
            else
            {
                button.ClearValue(Button.BorderBrushProperty);
                button.ClearValue(Button.BorderThicknessProperty);
            }
        }

        // — Advanced: updates —

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking…";

            UpdateCheckResult result = await UpdateCheckService.CheckAsync();
            if (!result.Success)
            {
                UpdateStatusText.Text = "Couldn't check for updates: " + result.Error;
            }
            else if (result.UpdateAvailable)
            {
                UpdateStatusText.Text = $"New version available: {result.Latest}";
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = "You're on the latest version.";
            }

            CheckUpdateButton.IsEnabled = true;
        }

        private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadUpdateButton.IsEnabled = false;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;

            Progress<double> progress = new Progress<double>(p => UpdateProgressBar.Value = p);
            try
            {
                StorageFile file = await UpdateCheckService.DownloadUpdateAsync(progress);
                if (file != null)
                {
                    _downloadedUpdateFile = file;
                    UpdateStatusText.Text = "Download complete. Tap Install to continue.";
                    InstallUpdateButton.Visibility = Visibility.Visible;
                    DownloadUpdateButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    UpdateStatusText.Text = "Download cancelled.";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Download failed: " + ex.Message;
            }
            finally
            {
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                DownloadUpdateButton.IsEnabled = true;
            }
        }

        private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_downloadedUpdateFile != null)
            {
                // The user still confirms installation on Windows' own
                // native installer screen — a sideloaded app can never
                // install itself silently. See
                // wp-apps/07-build-e-ci-cd.md#o-limite-de-fábrica.
                bool launched = await UpdateCheckService.InstallUpdateAsync(_downloadedUpdateFile);
                if (!launched)
                {
                    UpdateStatusText.Text = "Couldn't open the installer. Try downloading again.";
                }
            }
        }

        // — Advanced: danger zone —

        private async void ClearDownloadsButton_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ConfirmAsync(
                "Clear all downloads?",
                "This removes every offline copy you've downloaded. You can download them again later.");
            if (!confirmed)
            {
                return;
            }
            await ClearAllDownloadsAsync();
        }

        private async void ResetAppButton_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = await ConfirmAsync(
                "Reset app?",
                "This signs you out, erases downloads, and resets local settings.");
            if (!confirmed)
            {
                return;
            }

            await ClearAllDownloadsAsync();
            await LocalDataStore.ResetAllAsync();
            await AbsAuthService.LogoutAsync();
            MainPage.Current.BeginOnboarding();
        }

        private static async Task ClearAllDownloadsAsync()
        {
            var downloads = await DownloadService.GetDownloadsAsync();
            foreach (var group in downloads.GroupBy(d => d.LibraryItemId))
            {
                await DownloadService.DeleteAllDownloadsForItemAsync(group.Key);
            }
        }

        private static async Task<bool> ConfirmAsync(string title, string content)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "Confirm",
                CloseButtonText = "Cancel",
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
    }
}
