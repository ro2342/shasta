using System;
using System.Collections.Generic;
using System.Linq;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            StatusText.Text = "Loading…";
            StatusText.Visibility = Visibility.Visible;
            ContinueListeningTitle.Visibility = Visibility.Collapsed;
            ContinueListeningPanel.Visibility = Visibility.Collapsed;
            LibrariesTitle.Visibility = Visibility.Collapsed;
            LibrariesPanel.Visibility = Visibility.Collapsed;

            try
            {
                List<AbsLibrary> libraries = await LibraryService.GetLibrariesAsync();
                if (libraries.Count == 0)
                {
                    StatusText.Text = "No libraries found on this server.";
                    return;
                }

                BuildLibraryCards(libraries);
                LibrariesTitle.Visibility = Visibility.Visible;
                LibrariesPanel.Visibility = Visibility.Visible;

                // Continue Listening is scoped to a single library for v1
                // (whichever was opened last, or the first one) rather
                // than aggregating shelves across every library — keeps
                // Home to one extra request instead of one per library.
                AppSettings settings = await LocalDataStore.GetSettingsAsync();
                AbsLibrary target = libraries.FirstOrDefault(l => l.Id == settings.LastOpenedLibraryId) ?? libraries[0];

                List<AbsLibraryItem> continueItems = await LibraryService.GetContinueListeningAsync(target.Id);
                if (continueItems.Count > 0)
                {
                    BuildContinueListeningCards(continueItems);
                    ContinueListeningTitle.Visibility = Visibility.Visible;
                    ContinueListeningPanel.Visibility = Visibility.Visible;
                }

                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load your libraries: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void BuildLibraryCards(List<AbsLibrary> libraries)
        {
            LibrariesPanel.Children.Clear();
            foreach (AbsLibrary library in libraries)
            {
                Border card = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
                StackPanel content = new StackPanel();
                content.Children.Add(new TextBlock
                {
                    Text = library.Name,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                });
                content.Children.Add(new TextBlock
                {
                    Text = library.MediaType,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.7,
                });
                card.Child = content;
                card.Tapped += (sender, e) => NavigateToLibrary(library);
                LibrariesPanel.Children.Add(card);
            }
        }

        private void BuildContinueListeningCards(List<AbsLibraryItem> items)
        {
            ContinueListeningPanel.Children.Clear();
            foreach (AbsLibraryItem item in items)
            {
                Border card = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Image cover = new Image
                {
                    Width = 56,
                    Height = 56,
                    Stretch = Stretch.UniformToFill,
                };
                _ = LibraryService.LoadCoverAsync(cover, item.Id, 100);
                Grid.SetColumn(cover, 0);

                StackPanel text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = item.GetTitle(),
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                text.Children.Add(new TextBlock
                {
                    Text = item.GetAuthorDisplay(),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.7,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                Grid.SetColumn(text, 1);

                grid.Children.Add(cover);
                grid.Children.Add(text);
                card.Child = grid;

                string itemId = item.Id;
                card.Tapped += (sender, e) => MainPage.Current.ContentFrame.Navigate(typeof(ItemDetailPage), itemId);
                ContinueListeningPanel.Children.Add(card);
            }
        }

        private void NavigateToLibrary(AbsLibrary library)
        {
            MainPage.Current.ContentFrame.Navigate(typeof(LibraryPage), library);
        }
    }
}
