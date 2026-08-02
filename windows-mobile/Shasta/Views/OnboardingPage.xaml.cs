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
            // These can run before InitializeComponent finishes wiring up
            // every field (RadioButton.IsChecked="True" in the XAML fires
            // Checked during load) — guard against the panels not existing
            // yet.
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
