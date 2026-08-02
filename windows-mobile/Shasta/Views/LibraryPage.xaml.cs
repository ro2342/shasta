using System;
using System.Collections.Generic;
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
            public string CoverUri { get; set; }
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

                List<LibraryItemRow> rows = new List<LibraryItemRow>();
                foreach (AbsLibraryItem item in items)
                {
                    rows.Add(new LibraryItemRow
                    {
                        Id = item.Id,
                        Title = item.GetTitle(),
                        Author = item.GetAuthorDisplay(),
                        CoverUri = LibraryService.GetCoverUri(item.Id, 100).ToString(),
                    });
                }

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

        private void ItemsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            LibraryItemRow row = (LibraryItemRow)e.ClickedItem;
            MainPage.Current.ContentFrame.Navigate(typeof(ItemDetailPage), row.Id);
        }
    }
}
