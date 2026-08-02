using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shasta.Models;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;

namespace Shasta.Services
{
    // Owns one MediaPlayer for the app's entire lifetime — never recreated
    // per item, never torn down in App.Suspending. On
    // TargetPlatformMinVersion=10.0.14393.0 (Anniversary Update), this
    // single-process MediaPlayer model is exactly what keeps audio playing
    // through lock/suspend; the older dual-process BackgroundMediaPlayer
    // approach only applied to versions below this one and isn't needed
    // here.
    //
    // StateChanged fires on whatever thread the underlying MediaPlayer/SMTC
    // event fired on (often a background thread) — subscribers must
    // marshal to the UI thread themselves before touching XAML, same
    // pattern MainPage already uses for UISettings.ColorValuesChanged.
    public static class PlaybackService
    {
        private static readonly MediaPlayer _player = CreatePlayer();
        private static MediaPlaybackList _playbackList;
        private static List<AudioTrack> _tracks = new List<AudioTrack>();
        private static DispatcherTimer _syncTimer;
        private static DateTimeOffset _lastSyncAtUtc;
        private static TimeSpan? _pendingSeek;

        public static event EventHandler StateChanged;

        public static AbsLibraryItem CurrentItem { get; private set; }
        public static PlaySession CurrentSession { get; private set; }

        public static bool HasActiveItem => CurrentItem != null;
        public static bool IsPlaying => _player.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
        public static double PlaybackRate => _player.PlaybackSession?.PlaybackRate ?? 1.0;

        public static double PositionSeconds
        {
            get
            {
                if (_playbackList == null || _player.PlaybackSession == null)
                {
                    return 0;
                }
                int index = (int)_playbackList.CurrentItemIndex;
                double baseOffset = (index >= 0 && index < _tracks.Count) ? _tracks[index].StartOffset : 0;
                return baseOffset + _player.PlaybackSession.Position.TotalSeconds;
            }
        }

        public static double DurationSeconds =>
            _tracks.Count > 0 ? _tracks[_tracks.Count - 1].StartOffset + _tracks[_tracks.Count - 1].Duration : 0;

        public static List<AbsChapter> CurrentChapters => CurrentItem?.GetChapters() ?? new List<AbsChapter>();

        public static async Task PlayItemAsync(AbsLibraryItem item, double resumeFromSeconds = 0)
        {
            if (CurrentSession != null)
            {
                await StopAsync();
            }

            CurrentItem = item;
            CurrentSession = await ProgressService.StartSessionAsync(item.Id);
            _tracks = CurrentSession.AudioTracks.OrderBy(t => t.Index).ToList();

            double seekTarget = resumeFromSeconds > 0 ? resumeFromSeconds : CurrentSession.CurrentTime;
            int startIndex = FindTrackIndexForOffset(seekTarget);
            double offsetWithinTrack = Math.Max(0, seekTarget - (startIndex < _tracks.Count ? _tracks[startIndex].StartOffset : 0));

            _playbackList = new MediaPlaybackList();
            foreach (AudioTrack track in _tracks)
            {
                // Prefer a downloaded file over streaming when one exists
                // — saves bandwidth and survives a flaky connection
                // mid-book. Still requires ProgressService.StartSessionAsync
                // above to have succeeded; playing with zero connectivity
                // at all isn't supported (see DownloadService's scope note).
                StorageFile localFile = await DownloadService.GetLocalFileForTrackAsync(item, (int)track.Index);
                MediaSource source = localFile != null
                    ? MediaSource.CreateFromStorageFile(localFile)
                    : MediaSource.CreateFromUri(AbsApiClient.BuildStreamUri(track));
                _playbackList.Items.Add(new MediaPlaybackItem(source));
            }
            if (startIndex > 0 && startIndex < _playbackList.Items.Count)
            {
                _playbackList.StartingItem = _playbackList.Items[startIndex];
            }
            _playbackList.CurrentItemChanged += (s, e) => RaiseStateChanged();

            if (offsetWithinTrack > 0.5)
            {
                _pendingSeek = TimeSpan.FromSeconds(offsetWithinTrack);
                _player.MediaOpened += Player_SeekOnceOpened;
            }

            UpdateTransportDisplay(item);
            _player.Source = _playbackList;
            _player.Play();
            StartSyncTimer();
            RaiseStateChanged();
        }

        private static void Player_SeekOnceOpened(MediaPlayer sender, object args)
        {
            sender.MediaOpened -= Player_SeekOnceOpened;
            if (_pendingSeek.HasValue)
            {
                sender.PlaybackSession.Position = _pendingSeek.Value;
                _pendingSeek = null;
            }
        }

        public static void Pause() => _player.Pause();

        public static void Resume() => _player.Play();

        public static void SeekTo(TimeSpan overallPosition)
        {
            if (_playbackList == null || _player.PlaybackSession == null)
            {
                return;
            }
            double target = Math.Max(0, overallPosition.TotalSeconds);
            int index = FindTrackIndexForOffset(target);
            if (index != (int)_playbackList.CurrentItemIndex)
            {
                _playbackList.MoveTo((uint)index);
            }
            double offsetWithinTrack = target - (index < _tracks.Count ? _tracks[index].StartOffset : 0);
            _player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, offsetWithinTrack));
            RaiseStateChanged();
        }

        public static void SeekToChapter(AbsChapter chapter) => SeekTo(TimeSpan.FromSeconds(chapter.Start));

        public static void SkipForward(double seconds = 30) => SeekTo(TimeSpan.FromSeconds(PositionSeconds + seconds));

        public static void SkipBackward(double seconds = 15) => SeekTo(TimeSpan.FromSeconds(PositionSeconds - seconds));

        public static void SetPlaybackRate(double rate)
        {
            if (_player.PlaybackSession != null)
            {
                _player.PlaybackSession.PlaybackRate = rate;
            }
        }

        public static async Task StopAsync()
        {
            StopSyncTimer();
            if (CurrentSession != null)
            {
                double elapsed = Math.Max(0, (DateTimeOffset.UtcNow - _lastSyncAtUtc).TotalSeconds);
                await ProgressService.CloseSessionAsync(CurrentSession.Id, PositionSeconds, elapsed);
            }
            _player.Pause();
            _player.Source = null;
            _playbackList = null;
            _tracks = new List<AudioTrack>();
            CurrentItem = null;
            CurrentSession = null;
            RaiseStateChanged();
        }

        private static int FindTrackIndexForOffset(double offsetSeconds)
        {
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                if (_tracks[i].StartOffset <= offsetSeconds)
                {
                    return i;
                }
            }
            return 0;
        }

        private static void StartSyncTimer()
        {
            StopSyncTimer();
            _lastSyncAtUtc = DateTimeOffset.UtcNow;
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();
        }

        private static void StopSyncTimer()
        {
            if (_syncTimer != null)
            {
                _syncTimer.Stop();
                _syncTimer.Tick -= SyncTimer_Tick;
                _syncTimer = null;
            }
        }

        private static async void SyncTimer_Tick(object sender, object e)
        {
            if (CurrentSession == null || !IsPlaying)
            {
                return;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            double elapsed = (now - _lastSyncAtUtc).TotalSeconds;
            _lastSyncAtUtc = now;
            await ProgressService.SyncSessionAsync(CurrentSession.Id, PositionSeconds, elapsed);
        }

        private static void UpdateTransportDisplay(AbsLibraryItem item)
        {
            SystemMediaTransportControls smtc = _player.SystemMediaTransportControls;
            smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            smtc.DisplayUpdater.MusicProperties.Title = item.GetTitle();
            smtc.DisplayUpdater.MusicProperties.Artist = item.GetAuthorDisplay();
            smtc.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(LibraryService.GetCoverUri(item.Id, 300));
            smtc.DisplayUpdater.Update();
        }

        private static MediaPlayer CreatePlayer()
        {
            MediaPlayer player = new MediaPlayer();

            SystemMediaTransportControls smtc = player.SystemMediaTransportControls;
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.ButtonPressed += Smtc_ButtonPressed;

            player.PlaybackSession.PlaybackStateChanged += (s, e) => RaiseStateChanged();
            player.PlaybackSession.PositionChanged += (s, e) => RaiseStateChanged();

            return player;
        }

        private static void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Resume();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    SkipForward();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    SkipBackward();
                    break;
            }
        }

        private static void RaiseStateChanged() => StateChanged?.Invoke(null, EventArgs.Empty);
    }
}
