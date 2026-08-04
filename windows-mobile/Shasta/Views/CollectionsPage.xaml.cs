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
    public sealed partial class CollectionsPage : Page
    {
        private sealed class CollectionRow
        {
            public string Name { get; set; }
            public string BookCountText { get; set; }
            public string FirstBookId { get; set; }
            public AbsCollection Collection { get; set; }
        }

        public CollectionsPage()
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
            CollectionsGridView.Visibility = Visibility.Collapsed;

            try
            {
                AbsLibrary library = await LibraryService.ResolveDefaultLibraryAsync();
                if (library == null)
                {
                    StatusText.Text = "No libraries found on this server.";
                    return;
                }

                List<AbsCollection> collections = await LibraryService.GetCollectionsAsync(library.Id);
                if (collections.Count == 0)
                {
                    StatusText.Text = "No collections in this library.";
                    return;
                }

                List<CollectionRow> rows = new List<CollectionRow>();
                foreach (AbsCollection c in collections)
                {
                    rows.Add(new CollectionRow
                    {
                        Name = c.Name,
                        BookCountText = c.Books.Count == 1 ? "1 book" : $"{c.Books.Count} books",
                        FirstBookId = c.Books.Count > 0 ? c.Books[0].Id : null,
                        Collection = c,
                    });
                }
                CollectionsGridView.ItemsSource = rows;
                CollectionsGridView.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load collections: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void CollectionsGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }
            CollectionRow row = args.Item as CollectionRow;
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

        private void CollectionsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            CollectionRow row = (CollectionRow)e.ClickedItem;
            MainPage.Current.ContentFrame.Navigate(typeof(ItemGroupPage), new ItemGroupNavArgs(row.Name, row.Collection.Books));
        }
    }
}
