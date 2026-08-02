using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class ItemDetailPage : Page
    {
        private AbsLibraryItem _item;

        public ItemDetailPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string itemId = e.Parameter as string;
            if (string.IsNullOrEmpty(itemId))
            {
                StatusText.Text = "No item selected.";
                StatusText.Visibility = Visibility.Visible;
                return;
            }

            StatusText.Text = "Loading…";
            StatusText.Visibility = Visibility.Visible;

            try
            {
                AbsLibraryItem item = await LibraryService.GetItemDetailAsync(itemId, expanded: true);
                Populate(item);
                await RefreshDownloadButtonAsync(item.Id);
                StatusText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't load this item: " + ex.Message;
                StatusText.Visibility = Visibility.Visible;
            }
        }

        private void Populate(AbsLibraryItem item)
        {
            _item = item;
            CoverImage.Source = new BitmapImage(LibraryService.GetCoverUri(item.Id, 300));
            TitleText.Text = item.GetTitle();
            AuthorText.Text = item.GetAuthorDisplay();

            string seriesName = item.GetSeriesName();
            if (!string.IsNullOrEmpty(seriesName))
            {
                SeriesText.Text = seriesName;
                SeriesText.Visibility = Visibility.Visible;
            }

            DescriptionText.Text = item.GetDescription();

            List<AbsChapter> chapters = item.GetChapters();
            if (chapters.Count > 0)
            {
                ChaptersPanel.Children.Clear();
                foreach (AbsChapter chapter in chapters)
                {
                    ChaptersPanel.Children.Add(new TextBlock
                    {
                        Text = $"{FormatTime(chapter.Start)}  {chapter.Title}",
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                        Margin = new Thickness(0, 0, 0, 8),
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
                ChaptersTitle.Visibility = Visibility.Visible;
                ChaptersPanel.Visibility = Visibility.Visible;
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_item != null)
            {
                MainPage.Current.ContentFrame.Navigate(typeof(PlayerPage), _item);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_item == null)
            {
                return;
            }

            DownloadButton.IsEnabled = false;
            DownloadStatusText.Visibility = Visibility.Visible;
            DownloadStatusText.Text = "Downloading… 0%";

            Progress<double> progress = new Progress<double>(percent =>
            {
                DownloadStatusText.Text = $"Downloading… {percent:0}%";
            });

            try
            {
                await DownloadService.DownloadItemAsync(_item, progress, CancellationToken.None);
                DownloadStatusText.Text = "Downloaded for offline playback.";
                DownloadButton.Content = "Downloaded";
            }
            catch (Exception ex)
            {
                DownloadStatusText.Text = "Download failed: " + ex.Message;
                DownloadButton.IsEnabled = true;
            }
        }

        private async Task RefreshDownloadButtonAsync(string itemId)
        {
            bool downloaded = await DownloadService.IsDownloadedAsync(itemId);
            if (downloaded)
            {
                DownloadButton.Content = "Downloaded";
                DownloadButton.IsEnabled = false;
            }
        }

        private static string FormatTime(double totalSeconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
            return span.Hours > 0
                ? $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{span.Minutes}:{span.Seconds:D2}";
        }
    }
}
