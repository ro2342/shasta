using System;
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

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            // Nothing to persist here: LocalDataStore writes to disk on
            // every operation (see wp-apps/05-dados-locais-e-conteudo.md),
            // so there's no pending in-memory state. Once PlaybackService
            // exists (Phase 4), it must NOT be torn down here either — an
            // active MediaPlayer is what keeps the process alive through
            // lock/suspend under UWP's single-process background-audio
            // model on 14393+.
        }
    }
}
