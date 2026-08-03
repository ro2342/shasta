using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class LibraryPage : Page
    {
        // Plain display-projection type for the ListView's DataTemplate —
        // populated once per load, not a live ViewModel. No
        // INotifyPropertyChanged and no two-way sync back to AbsLibraryItem.
        private sealed class LibraryItemRow
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
        }

        private AbsLibrary _library;

        public LibraryPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _library = e.Parameter as AbsLibrary;
            LibraryTitleText.Text = _library?.Name ?? "Library";
            if (_library == null)
            {
                StatusText.Text = "No library selected.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            await RememberLastOpenedLibraryAsync(_library.Id);
            await LoadItemsAsync();
        }

        private static async Task RememberLastOpenedLibraryAsync(string libraryId)
        {
            AppSettings settings = await LocalDataStore.GetSettingsAsync();
            settings.LastOpenedLibraryId = libraryId;
            await LocalDataStore.SetSettingsAsync(settings);
        }

        private async Task LoadItemsAsync()
        {
            StatusText.Text = "Loading…";
            StatusText.Visibility = Visibility.Visible;
            ItemsListView.Visibility = Visibility.Collapsed;

            try
            {
                List<AbsLibraryItem> items = await LibraryService.GetLibraryItemsAsync(_library.Id);
                if (items.Count == 0)
                {
                    StatusText.Text = "This library is empty.";
                    return;
                }

                // Alphabetical by title — no series/author grouping yet
                // (out of MVP scope), but a stable, predictable order beats
                // whatever raw order the server happens to return.
                List<LibraryItemRow> rows = items
                    .Select(item => new LibraryItemRow
                    {
                        Id = item.Id,
                        Title = item.GetTitle(),
                        Author = item.GetAuthorDisplay(),
                    })
                    .OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                ItemsListView.ItemsSource = rows;
                ItemsListView.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load this library: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        // Standard UWP pattern for async images inside a virtualized
        // ListView: RegisterUpdateCallback so the fetch only starts once
        // the container is actually about to be shown (and is naturally
        // skipped/cancelled-in-spirit for recycled/scrolled-past
        // containers), rather than firing for every row up front.
        private void ItemsListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }
            LibraryItemRow row = args.Item as LibraryItemRow;
            if (row == null)
            {
                return;
            }
            args.RegisterUpdateCallback(async (s, e) =>
            {
                Image image = (e.ItemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("CoverImage") as Image;
                if (image != null)
                {
                    // Recycled containers can still be showing a previous
                    // row's cover — clear it before the new fetch so a
                    // stale image never flashes for the wrong book.
                    image.Source = null;
                    await LibraryService.LoadCoverAsync(image, row.Id, 100);
                }
            });
        }

        private void ItemsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            LibraryItemRow row = (LibraryItemRow)e.ClickedItem;
            MainPage.Current.ContentFrame.Navigate(typeof(ItemDetailPage), row.Id);
        }
    }
}
