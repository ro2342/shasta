using System;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Shasta.Views
{
    public sealed partial class OnboardingPage : Page
    {
        public OnboardingPage()
        {
            this.InitializeComponent();
            // Set in code, not as IsChecked="True" in the XAML — that
            // attribute failed to parse at runtime on real 14393 hardware
            // ("Failed to assign to property ...ToggleButton.IsChecked",
            // even though it compiled fine against the newer build SDK).
            // ToggleButton.IsChecked is a nullable bool; a direct C#
            // assignment isn't subject to whatever XAML markup-conversion
            // path was failing.
            PasswordModeRadio.IsChecked = true;
        }

        private void ServerUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string url = ServerUrlBox.Text.Trim();
            HttpWarningText.Visibility =
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void LoginMode_Checked(object sender, RoutedEventArgs e)
        {
            bool isApiKeyMode = ApiKeyModeRadio.IsChecked == true;
            // Defensive guard: this fires the instant the constructor sets
            // PasswordModeRadio.IsChecked = true, which happens right after
            // InitializeComponent — should always be non-null by then, but
            // costs nothing to check.
            if (PasswordPanel == null || ApiKeyPanel == null)
            {
                return;
            }
            PasswordPanel.Visibility = isApiKeyMode ? Visibility.Collapsed : Visibility.Visible;
            ApiKeyPanel.Visibility = isApiKeyMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string serverUrl = ServerUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(serverUrl))
            {
                ShowStatus("Enter a server address.");
                return;
            }

            LoginButton.IsEnabled = false;
            LoginButton.Content = "Logging in…";
            HideStatus();

            try
            {
                AbsAuthResult result = ApiKeyModeRadio.IsChecked == true
                    ? await AbsAuthService.LoginWithApiKeyAsync(serverUrl, ApiKeyBox.Password)
                    : await AbsAuthService.LoginAsync(serverUrl, UsernameBox.Text.Trim(), PasswordBox.Password);

                if (result.Success)
                {
                    MainPage.Current.CompleteOnboarding();
                }
                else
                {
                    ShowStatus(result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message);
            }
            finally
            {
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Log In";
            }
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }

        private void HideStatus()
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }
}
