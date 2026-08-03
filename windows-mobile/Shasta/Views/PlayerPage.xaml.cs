using System;
using System.Collections.Generic;
using System.Globalization;
using Shasta.Models;
using Shasta.Services;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace Shasta.Views
{
    public sealed partial class PlayerPage : Page
    {
        private bool _suppressSeek;

        public PlayerPage()
        {
            this.InitializeComponent();
            // Set in code rather than IsSelected="True" in the XAML — after
            // OnboardingPage's IsChecked="True" failed to parse at runtime
            // on real 14393 hardware (compiled fine against the newer SDK),
            // any boolean-ish XAML attribute on a selection-style control
            // gets set here instead rather than trusted un-tested.
            SpeedComboBox.SelectedIndex = 1;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            PlaybackService.StateChanged += PlaybackService_StateChanged;

            if (e.Parameter is AbsLibraryItem item &&
                (PlaybackService.CurrentItem == null || PlaybackService.CurrentItem.Id != item.Id))
            {
                StatusText.Text = "Loading…";
                StatusText.Visibility = Visibility.Visible;
                try
                {
                    await PlaybackService.PlayItemAsync(item);
                    StatusText.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Couldn't start playback: " + ex.Message;
                    StatusText.Visibility = Visibility.Visible;
                    return;
                }
            }

            PopulateStaticInfo();
            RefreshUi();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            PlaybackService.StateChanged -= PlaybackService_StateChanged;
        }

        private async void PlaybackService_StateChanged(object sender, EventArgs e)
        {
            // StateChanged can fire on a background thread (SMTC button
            // presses, MediaPlayer session events) — hop to the UI thread
            // before touching any XAML element, same pattern MainPage uses
            // for UISettings.ColorValuesChanged.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, RefreshUi);
        }

        private void PopulateStaticInfo()
        {
            AbsLibraryItem item = PlaybackService.CurrentItem;
            if (item == null)
            {
                return;
            }

            _ = LibraryService.LoadCoverAsync(CoverImage, item.Id, 300);
            TitleText.Text = item.GetTitle();
            AuthorText.Text = item.GetAuthorDisplay();

            List<AbsChapter> chapters = PlaybackService.CurrentChapters;
            if (chapters.Count > 0)
            {
                ChaptersPanel.Children.Clear();
                foreach (AbsChapter chapter in chapters)
                {
                    Button chapterButton = new Button
                    {
                        Content = $"{FormatTime(chapter.Start)}  {chapter.Title}",
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 4),
                    };
                    AbsChapter capturedChapter = chapter;
                    chapterButton.Click += (s, e) => PlaybackService.SeekToChapter(capturedChapter);
                    ChaptersPanel.Children.Add(chapterButton);
                }
                ChaptersTitle.Visibility = Visibility.Visible;
                ChaptersPanel.Visibility = Visibility.Visible;
            }
        }

        private void RefreshUi()
        {
            double duration = PlaybackService.DurationSeconds;
            double position = PlaybackService.PositionSeconds;

            PositionSlider.Maximum = Math.Max(duration, 1);
            _suppressSeek = true;
            PositionSlider.Value = Math.Min(position, PositionSlider.Maximum);
            _suppressSeek = false;

            CurrentTimeText.Text = FormatTime(position);
            DurationText.Text = FormatTime(duration);
            // Segoe MDL2 Assets glyphs: Pause is codepoint E769, Play is E768.
            PlayPauseIcon.Glyph = PlaybackService.IsPlaying ? "" : "";
        }

        private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressSeek)
            {
                return;
            }
            PlaybackService.SeekTo(TimeSpan.FromSeconds(e.NewValue));
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaybackService.IsPlaying)
            {
                PlaybackService.Pause();
            }
            else
            {
                PlaybackService.Resume();
            }
        }

        private void SkipBackButton_Click(object sender, RoutedEventArgs e) => PlaybackService.SkipBackward();

        private void SkipForwardButton_Click(object sender, RoutedEventArgs e) => PlaybackService.SkipForward();

        private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // NumberStyles.Float + InvariantCulture regardless of the
            // device's region setting — a device set to a comma-decimal
            // locale must not misparse "1.25" from the XAML-authored Tag.
            if (SpeedComboBox.SelectedItem is ComboBoxItem item &&
                double.TryParse((string)item.Tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            {
                PlaybackService.SetPlaybackRate(rate);
            }
        }

        private static string FormatTime(double totalSeconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
            return span.Hours > 0
                ? $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{span.Minutes}:{span.Seconds:D2}";
        }
    }
}
