using System;
using Shasta.Services;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shasta
{
    // See wp-apps/03-design-visual-e-navegacao.md#tratamento-de-erro-visível-na-ui
    // and wp-apps/04-convencoes-de-codigo.md#tratamento-de-erro-global.
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;

            // Release (.NET Native) builds sometimes fail silently in
            // scenarios that work fine in Debug. Without this handler, a
            // broken sideload just "opens and closes", impossible to
            // diagnose without a Visual Studio attached.
            UnhandledException += OnUnhandledException;
        }

        private void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            if (Window.Current?.Content is Frame rootFrame &&
                rootFrame.Content is MainPage mainPage)
            {
                mainPage.ShowFatalError(e.Message);
            }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                Window.Current.Activate();
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load page " + e.SourcePageType.FullName);
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            // LocalDataStore writes to disk on every operation, so there's
            // no other pending in-memory state to persist here.
            // PlaybackService's MediaPlayer must NOT be torn down or
            // paused in this handler — an active MediaPlayer is what
            // keeps the process alive through lock/suspend under UWP's
            // single-process background-audio model on 14393+. Only flush
            // playback progress to the server, as a safety net in case
            // suspension does happen mid-book (e.g. a device's Battery
            // Saver overriding the background-audio exemption).
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                await PlaybackService.FlushProgressOnSuspendAsync();
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
