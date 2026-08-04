using System.Collections.Generic;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    // Navigation parameter for ItemGroupPage — a name (series/collection
    // name, shown as the page title) plus the already-fetched book list.
    public sealed class ItemGroupNavArgs
    {
        public string Title { get; }
        public List<AbsLibraryItem> Items { get; }

        public ItemGroupNavArgs(string title, List<AbsLibraryItem> items)
        {
            Title = title;
            Items = items;
        }
    }

    public sealed partial class ItemGroupPage : Page
    {
        private sealed class ItemRow
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Author { get; set; }
        }

        public ItemGroupPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            ItemGroupNavArgs args = e.Parameter as ItemGroupNavArgs;
            GroupTitleText.Text = args?.Title ?? "Books";

            List<ItemRow> rows = new List<ItemRow>();
            if (args != null)
            {
                foreach (AbsLibraryItem item in args.Items)
                {
                    rows.Add(new ItemRow
                    {
                        Id = item.Id,
                        Title = item.GetTitle(),
                        Author = item.GetAuthorDisplay(),
                    });
                }
            }
            ItemsGridView.ItemsSource = rows;
        }

        private void ItemsGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }
            ItemRow row = args.Item as ItemRow;
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

        private void ItemsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            ItemRow row = (ItemRow)e.ClickedItem;
            MainPage.Current.ContentFrame.Navigate(typeof(ItemDetailPage), row.Id);
        }
    }
}
