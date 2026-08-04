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
    public sealed partial class SeriesPage : Page
    {
        // Plain display-projection row — carries the series' first book id
        // (for the stand-in cover) alongside the full AbsSeries so
        // ItemClick can hand its already-fetched Books straight to
        // ItemGroupPage with no extra request.
        private sealed class SeriesRow
        {
            public string Name { get; set; }
            public string BookCountText { get; set; }
            public string FirstBookId { get; set; }
            public AbsSeries Series { get; set; }
        }

        public SeriesPage()
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
            SeriesGridView.Visibility = Visibility.Collapsed;

            try
            {
                AbsLibrary library = await LibraryService.ResolveDefaultLibraryAsync();
                if (library == null)
                {
                    StatusText.Text = "No libraries found on this server.";
                    return;
                }

                List<AbsSeries> series = await LibraryService.GetSeriesAsync(library.Id);
                if (series.Count == 0)
                {
                    StatusText.Text = "No series in this library.";
                    return;
                }

                List<SeriesRow> rows = new List<SeriesRow>();
                foreach (AbsSeries s in series)
                {
                    rows.Add(new SeriesRow
                    {
                        Name = s.Name,
                        BookCountText = s.Books.Count == 1 ? "1 book" : $"{s.Books.Count} books",
                        FirstBookId = s.Books.Count > 0 ? s.Books[0].Id : null,
                        Series = s,
                    });
                }
                SeriesGridView.ItemsSource = rows;
                SeriesGridView.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load series: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void SeriesGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }
            SeriesRow row = args.Item as SeriesRow;
            if (row == null || row.FirstBookId == null)
            {
                return;
            }
            args.RegisterUpdateCallback(async (s, e) =>
            {
                Border cover = (e.ItemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("CoverImage") as Border;
                if (cover != null)
                {
                    cover.Background = (Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"];
                    await LibraryService.LoadCoverAsync(cover, row.FirstBookId, 100);
                }
            });
        }

        private void SeriesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            SeriesRow row = (SeriesRow)e.ClickedItem;
            MainPage.Current.ContentFrame.Navigate(typeof(ItemGroupPage), new ItemGroupNavArgs(row.Name, row.Series.Books));
        }
    }
}
