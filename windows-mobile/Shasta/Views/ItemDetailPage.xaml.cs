using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
            _ = LibraryService.LoadCoverAsync(CoverImage, item.Id, 300);
            TitleText.Text = item.GetTitle();
            AuthorText.Text = item.GetAuthorDisplay();
            MetaAuthorText.Text = item.GetAuthorDisplay();
            MetaDurationText.Text = FormatDuration(item.GetDurationSeconds());

            string seriesName = item.GetSeriesName();
            if (!string.IsNullOrEmpty(seriesName))
            {
                SeriesText.Text = seriesName;
                SeriesText.Visibility = Visibility.Visible;
                MetaSeriesText.Text = seriesName;
                MetaSeriesRow.Visibility = Visibility.Visible;
            }

            DescriptionText.Text = item.GetDescription();

            List<AbsChapter> chapters = item.GetChapters();
            if (chapters.Count > 0)
            {
                ChaptersPanel.Children.Clear();
                int number = 1;
                foreach (AbsChapter chapter in chapters)
                {
                    Grid row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{number}. {chapter.Title}",
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                        TextWrapping = TextWrapping.Wrap,
                    });
                    TextBlock durationText = new TextBlock
                    {
                        Text = FormatTime(Math.Max(0, chapter.End - chapter.Start)),
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Opacity = 0.6,
                        Margin = new Thickness(12, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(durationText, 1);
                    row.Children.Add(durationText);
                    ChaptersPanel.Children.Add(row);
                    number++;
                }
                ChaptersTitle.Visibility = Visibility.Visible;
                ChaptersPanel.Visibility = Visibility.Visible;
            }

            _ = RefreshProgressAsync(item.Id);
        }

        // Best-effort — a missing/never-started progress record is a
        // perfectly normal state for an item the user hasn't opened yet,
        // not an error worth surfacing.
        private async Task RefreshProgressAsync(string itemId)
        {
            try
            {
                MediaProgress progress = await ProgressService.GetProgressAsync(itemId);
                if (progress != null && progress.Duration > 0 && !progress.IsFinished && progress.Progress > 0)
                {
                    ProgressStatusText.Text = $"{progress.Progress * 100:0}% listened";
                    ProgressStatusText.Visibility = Visibility.Visible;
                }
                else if (progress != null && progress.IsFinished)
                {
                    ProgressStatusText.Text = "Finished";
                    ProgressStatusText.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                // No progress yet, or the server has nothing for this item.
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
                MarkDownloaded();
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
                MarkDownloaded();
            }
        }

        // Swaps the download-icon button to a disabled checkmark once the
        // item is available offline — DownloadButton is icon-only now (see
        // ItemDetailPage.xaml), so there's no text label to flip.
        private void MarkDownloaded()
        {
            DownloadButtonIcon.Glyph = "";
            DownloadButton.IsEnabled = false;
        }

        private static string FormatTime(double totalSeconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
            return span.Hours > 0
                ? $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{span.Minutes}:{span.Seconds:D2}";
        }

        private static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds <= 0)
            {
                return "--";
            }
            TimeSpan span = TimeSpan.FromSeconds(totalSeconds);
            return span.Hours > 0
                ? $"{span.Hours} hr {span.Minutes} min"
                : $"{span.Minutes} min";
        }
    }
}
