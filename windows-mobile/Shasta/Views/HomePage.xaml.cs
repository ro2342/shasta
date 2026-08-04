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
            ContinueListeningScroller.Visibility = Visibility.Collapsed;
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
                    ContinueListeningScroller.Visibility = Visibility.Visible;
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
                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // SymbolIcon Symbol="Library" -- a baseline-safe enum member
                // (see NavLibraryIcon in MainPage.xaml), unlike a raw
                // FontIcon glyph typed by hand, which risks landing on
                // whatever codepoint the editor happens to paste.
                SymbolIcon icon = new SymbolIcon
                {
                    Symbol = Symbol.Library,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.8,
                };
                Grid.SetColumn(icon, 0);

                StackPanel text = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = library.Name,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                });
                text.Children.Add(new TextBlock
                {
                    Text = library.MediaType,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.7,
                });
                Grid.SetColumn(text, 1);

                TextBlock chevron = new TextBlock
                {
                    Text = ">",
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(chevron, 2);

                grid.Children.Add(icon);
                grid.Children.Add(text);
                grid.Children.Add(chevron);
                card.Child = grid;
                card.Tapped += (sender, e) => NavigateToLibrary(library);
                LibrariesPanel.Children.Add(card);
            }
        }

        private const double ContinueListeningCoverSize = 116;

        private void BuildContinueListeningCards(List<AbsLibraryItem> items)
        {
            ContinueListeningPanel.Children.Clear();
            foreach (AbsLibraryItem item in items)
            {
                StackPanel card = new StackPanel
                {
                    Width = ContinueListeningCoverSize,
                    Margin = new Thickness(0, 0, 12, 0),
                };

                // Square, no CornerRadius — flat Groove Music look.
                Border cover = new Border
                {
                    Width = ContinueListeningCoverSize,
                    Height = ContinueListeningCoverSize,
                    Background = (Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"],
                    Margin = new Thickness(0, 0, 0, 8),
                };
                _ = LibraryService.LoadCoverAsync(cover, item.Id, 200);

                TextBlock title = new TextBlock
                {
                    Text = item.GetTitle(),
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 2,
                    TextWrapping = TextWrapping.Wrap,
                };
                TextBlock author = new TextBlock
                {
                    Text = item.GetAuthorDisplay(),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.7,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                card.Children.Add(cover);
                card.Children.Add(title);
                card.Children.Add(author);

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
