using System;
using System.Collections.Generic;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class SearchPage : Page
    {
        // Plain display-projection type for the GridView's DataTemplate,
        // same pattern as LibraryPage's LibraryItemRow.
        private sealed class SearchResultRow
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
        }

        private AbsLibrary _library;
        // Debounces QueryBox.TextChanged so every keystroke doesn't fire
        // its own request — only a query the user paused on does.
        private readonly DispatcherTimer _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        // Guards against an in-flight search whose result arrives after a
        // newer one — otherwise a slow response for an earlier keystroke
        // could overwrite a faster response for a later one.
        private int _searchGeneration;

        public SearchPage()
        {
            this.InitializeComponent();
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            StatusText.Text = "Loading…";
            StatusText.Visibility = Visibility.Visible;
            try
            {
                _library = await LibraryService.ResolveDefaultLibraryAsync();
                StatusText.Text = _library == null
                    ? "No libraries found on this server."
                    : "Search this library by title.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load your libraries: " + ex.Message;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _debounceTimer.Stop();
        }

        private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _debounceTimer.Stop();
            if (string.IsNullOrWhiteSpace(QueryBox.Text))
            {
                ResultsGridView.Visibility = Visibility.Collapsed;
                StatusText.Text = "Search this library by title.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }
            _debounceTimer.Start();
        }

        private async void DebounceTimer_Tick(object sender, object e)
        {
            _debounceTimer.Stop();
            await RunSearchAsync(QueryBox.Text);
        }

        private async System.Threading.Tasks.Task RunSearchAsync(string query)
        {
            if (_library == null)
            {
                return;
            }

            int generation = ++_searchGeneration;
            StatusText.Text = "Searching…";
            StatusText.Visibility = Visibility.Visible;
            ResultsGridView.Visibility = Visibility.Collapsed;

            try
            {
                List<AbsLibraryItem> items = await LibraryService.SearchBooksAsync(_library.Id, query);
                if (generation != _searchGeneration)
                {
                    return;
                }

                if (items.Count == 0)
                {
                    StatusText.Text = $"No matches for \"{query}\".";
                    StatusText.Visibility = Visibility.Visible;
                    return;
                }

                List<SearchResultRow> rows = new List<SearchResultRow>();
                foreach (AbsLibraryItem item in items)
                {
                    rows.Add(new SearchResultRow
                    {
                        Id = item.Id,
                        Title = item.GetTitle(),
                        Author = item.GetAuthorDisplay(),
                    });
                }
                ResultsGridView.ItemsSource = rows;
                ResultsGridView.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (generation != _searchGeneration)
                {
                    return;
                }
                StatusText.Text = "Search failed: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void ResultsGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }
            SearchResultRow row = args.Item as SearchResultRow;
            if (row == null)
            {
                return;
            }
            args.RegisterUpdateCallback(async (s, e) =>
            {
                Border cover = (e.ItemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("CoverImage") as Border;
                if (cover != null)
                {
                    cover.Background = (Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"];
                    await LibraryService.LoadCoverAsync(cover, row.Id, 100);
                }
            });
        }

        private void ResultsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            SearchResultRow row = (SearchResultRow)e.ClickedItem;
            MainPage.Current.ContentFrame.Navigate(typeof(ItemDetailPage), row.Id);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainPage.Current.ContentFrame.CanGoBack)
            {
                MainPage.Current.ContentFrame.GoBack();
            }
        }
    }
}
