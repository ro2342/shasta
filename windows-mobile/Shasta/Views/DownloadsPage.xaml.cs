using System;
using System.Collections.Generic;
using System.Linq;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class DownloadsPage : Page
    {
        public DownloadsPage()
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
            DownloadsPanel.Children.Clear();
            StatusText.Text = "Loading…";
            StatusText.Visibility = Visibility.Visible;

            List<DownloadRecord> records = await DownloadService.GetDownloadsAsync();
            var groups = records
                .Where(r => r.Status == DownloadRecord.Statuses.Completed)
                .GroupBy(r => r.LibraryItemId)
                .ToList();

            if (groups.Count == 0)
            {
                StatusText.Text = "No downloads yet. Download a book from its detail page to listen offline.";
                return;
            }

            StatusText.Visibility = Visibility.Collapsed;
            foreach (var group in groups)
            {
                DownloadsPanel.Children.Add(BuildCard(group.Key, group.ToList()));
            }
        }

        private Border BuildCard(string libraryItemId, List<DownloadRecord> files)
        {
            long totalBytes = files.Sum(f => f.TotalBytes);
            string sizeText = $"{totalBytes / 1024.0 / 1024.0:0.0} MB • {files.Count} file{(files.Count == 1 ? "" : "s")}";

            Border card = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = files[0].Title,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = sizeText,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.7,
            });
            Grid.SetColumn(text, 0);

            Button deleteButton = new Button { Content = "Delete", VerticalAlignment = VerticalAlignment.Center };
            deleteButton.Click += async (sender, e) => await DeleteAsync(libraryItemId);
            Grid.SetColumn(deleteButton, 1);

            grid.Children.Add(text);
            grid.Children.Add(deleteButton);
            card.Child = grid;
            return card;
        }

        private async System.Threading.Tasks.Task DeleteAsync(string libraryItemId)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = "Delete download?",
                Content = "This removes the offline copy. You can download it again later.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
            };
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await DownloadService.DeleteAllDownloadsForItemAsync(libraryItemId);
            await LoadAsync();
        }
    }
}
