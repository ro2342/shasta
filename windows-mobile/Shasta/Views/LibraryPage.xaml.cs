using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
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
        // Raw fetch, re-projected/re-sorted locally on Sort menu taps — no
        // extra server round-trip needed just to change the sort key.
        private List<AbsLibraryItem> _loadedItems = new List<AbsLibraryItem>();
        private bool _sortByAuthor;

        public LibraryPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _library = e.Parameter as AbsLibrary;
            if (_library == null)
            {
                // Reached from the nav-drawer "Library" tab, which has no
                // specific library to hand over (unlike a library card tap
                // on Home) — fall back to whichever library was opened
                // last, or the first one on the server.
                LibraryTitleText.Text = "Library";
                StatusText.Text = "Loading…";
                StatusText.Visibility = Visibility.Visible;
                try
                {
                    _library = await LibraryService.ResolveDefaultLibraryAsync();
                    if (_library == null)
                    {
                        StatusText.Text = "No libraries found on this server.";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Couldn't load your libraries: " + ex.Message;
                    return;
                }
            }

            LibraryTitleText.Text = _library.Name;
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
                _loadedItems = await LibraryService.GetLibraryItemsAsync(_library.Id);
                if (_loadedItems.Count == 0)
                {
                    StatusText.Text = "This library is empty.";
                    return;
                }

                ApplySort();
                ItemsListView.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load this library: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        // Title sort is a flat alphabetical list. Author sort groups by
        // author name with a section header per author (e.g. every C. S.
        // Lewis book clustered under one "C. S. Lewis" heading) instead of
        // just reordering rows with no visual separation between authors.
        private void ApplySort()
        {
            IEnumerable<LibraryItemRow> rows = _loadedItems
                .Select(item => new LibraryItemRow
                {
                    Id = item.Id,
                    Title = item.GetTitle(),
                    Author = item.GetAuthorDisplay(),
                });

            if (_sortByAuthor)
            {
                // IGrouping<TKey,TElement> already has the Key+IEnumerable
                // shape CollectionViewSource(IsSourceGrouped) expects — no
                // extra wrapper type needed. Sort by title BEFORE grouping
                // (GroupBy preserves encounter order), not by calling
                // OrderBy on each group afterward — that would return a
                // plain IOrderedEnumerable and lose the Key property
                // CollectionViewSource needs.
                var grouped = rows
                    .OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)
                    .GroupBy(row => row.Author)
                    .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                CollectionViewSource cvs = new CollectionViewSource { IsSourceGrouped = true, Source = grouped };
                ItemsListView.ItemsSource = cvs.View;
            }
            else
            {
                ItemsListView.ItemsSource = rows.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        private void SortByTitle_Click(object sender, RoutedEventArgs e)
        {
            _sortByAuthor = false;
            SortButtonText.Text = "Title";
            if (_loadedItems.Count > 0)
            {
                ApplySort();
            }
        }

        private void SortByAuthor_Click(object sender, RoutedEventArgs e)
        {
            _sortByAuthor = true;
            SortButtonText.Text = "Author";
            if (_loadedItems.Count > 0)
            {
                ApplySort();
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
                Border cover = (e.ItemContainer.ContentTemplateRoot as FrameworkElement)?.FindName("CoverImage") as Border;
                if (cover != null)
                {
                    // Recycled containers can still be showing a previous
                    // row's cover — reset to the placeholder brush before
                    // the new fetch so a stale image never flashes for the
                    // wrong book.
                    cover.Background = (Brush)Application.Current.Resources["SystemControlBackgroundBaseLowBrush"];
                    await LibraryService.LoadCoverAsync(cover, row.Id, 100);
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
