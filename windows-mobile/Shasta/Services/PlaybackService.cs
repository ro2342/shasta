using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shasta.Models;
// Windows.Media.Core also defines an AudioTrack type (a track within a
// MediaSource) — alias ours explicitly so the two don't collide.
using AudioTrack = Shasta.Models.AudioTrack;
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
        // Deliberately NOT a static-readonly-with-eager-initializer field.
        // Constructing MediaPlayer touches SystemMediaTransportControls,
        // which claims a real OS media session — doing that as a class
        // load side effect (i.e. the instant anything references
        // PlaybackService, even just to subscribe to StateChanged) risks
        // running before the app window is fully activated. Lazy-built on
        // first actual playback use instead, via GetOrCreatePlayer().
        private static MediaPlayer _player;
        private static MediaPlaybackList _playbackList;
        private static List<AudioTrack> _tracks = new List<AudioTrack>();
        private static DispatcherTimer _syncTimer;
        private static DateTimeOffset _lastSyncAtUtc;
        private static TimeSpan? _pendingSeek;

        public static event EventHandler StateChanged;

        public static AbsLibraryItem CurrentItem { get; private set; }
        public static PlaySession CurrentSession { get; private set; }

        public static bool HasActiveItem => CurrentItem != null;

        // These read-only status checks use the possibly-null field
        // directly (never GetOrCreatePlayer()) — checking "is anything
        // playing" must never itself be what constructs a MediaPlayer.
        public static bool IsPlaying => _player?.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
        public static double PlaybackRate => _player?.PlaybackSession?.PlaybackRate ?? 1.0;

        public static double PositionSeconds
        {
            get
            {
                if (_playbackList == null || _player?.PlaybackSession == null)
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

            MediaPlayer player = GetOrCreatePlayer();
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
                player.MediaOpened += Player_SeekOnceOpened;
            }

            // Fire-and-forget: title/artist push synchronously inside this
            // call, the cover fetch that follows shouldn't delay playback
            // actually starting.
            _ = UpdateTransportDisplay(player, item);
            player.Source = _playbackList;
            player.Play();
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

        // Pause/Resume/SeekTo/SetPlaybackRate are only ever invoked from UI
        // that's already showing an active session (PlayerPage, the mini-
        // player, SMTC transport buttons) — by the time any of these run,
        // _player is already non-null. GetOrCreatePlayer() is still used
        // rather than the bare field so a stray call is harmless instead
        // of a NullReferenceException, without reintroducing eager
        // construction anywhere near app startup.
        public static void Pause() => GetOrCreatePlayer().Pause();

        public static void Resume() => GetOrCreatePlayer().Play();

        public static void SeekTo(TimeSpan overallPosition)
        {
            if (_playbackList == null)
            {
                return;
            }
            MediaPlayer player = GetOrCreatePlayer();
            if (player.PlaybackSession == null)
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
            player.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, offsetWithinTrack));
            RaiseStateChanged();
        }

        public static void SeekToChapter(AbsChapter chapter) => SeekTo(TimeSpan.FromSeconds(chapter.Start));

        public static void SkipForward(double seconds = 30) => SeekTo(TimeSpan.FromSeconds(PositionSeconds + seconds));

        public static void SkipBackward(double seconds = 15) => SeekTo(TimeSpan.FromSeconds(PositionSeconds - seconds));

        public static void SetPlaybackRate(double rate)
        {
            MediaPlayer player = GetOrCreatePlayer();
            if (player.PlaybackSession != null)
            {
                player.PlaybackSession.PlaybackRate = rate;
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
            if (_player != null)
            {
                _player.Pause();
                _player.Source = null;
            }
            _playbackList = null;
            _tracks = new List<AudioTrack>();
            CurrentItem = null;
            CurrentSession = null;
            RaiseStateChanged();
        }

        // Called from App.xaml.cs's OnSuspending — persists progress to the
        // server without touching playback/the MediaPlayer at all, as a
        // safety net in case suspension happens mid-book despite the
        // background-audio exemption (e.g. a device's Battery Saver
        // overriding it). Does nothing if nothing is playing.
        public static async Task FlushProgressOnSuspendAsync()
        {
            if (CurrentSession == null)
            {
                return;
            }
            double elapsed = Math.Max(0, (DateTimeOffset.UtcNow - _lastSyncAtUtc).TotalSeconds);
            _lastSyncAtUtc = DateTimeOffset.UtcNow;
            await ProgressService.SyncSessionAsync(CurrentSession.Id, PositionSeconds, elapsed);
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

        private static async Task UpdateTransportDisplay(MediaPlayer player, AbsLibraryItem item)
        {
            SystemMediaTransportControls smtc = player.SystemMediaTransportControls;
            smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            smtc.DisplayUpdater.MusicProperties.Title = item.GetTitle();
            smtc.DisplayUpdater.MusicProperties.Artist = item.GetAuthorDisplay();
            // Push title/artist immediately — don't let a slow or failed
            // cover fetch delay the lock-screen text from appearing at all.
            smtc.DisplayUpdater.Update();

            try
            {
                smtc.DisplayUpdater.Thumbnail = await AbsApiClient.GetCoverStreamReferenceAsync(item.Id, 300);
                smtc.DisplayUpdater.Update();
            }
            catch
            {
                // Missing cover art on the lock screen is not fatal.
            }
        }

        // The only place a MediaPlayer gets constructed — first call wins,
        // every call after returns the same instance (matches the "one
        // MediaPlayer for the app's entire lifetime" contract above).
        private static MediaPlayer GetOrCreatePlayer()
        {
            if (_player != null)
            {
                return _player;
            }

            MediaPlayer player = new MediaPlayer
            {
                // Explicit, not left at the default — this is what tells
                // Windows this playback qualifies for the standard
                // background-audio continuation (screen lock, app
                // switched away) instead of leaving that ambiguous. A
                // plausible real cause of "only plays with the app's
                // screen active": nothing in this app explicitly pauses
                // on suspend/background, so if playback was still
                // stopping, an unset AudioCategory not properly
                // signaling "this is media" to the session/power manager
                // is the most concrete lead available without a device
                // attached to a debugger.
                AudioCategory = MediaPlayerAudioCategory.Media,
            };

            SystemMediaTransportControls smtc = player.SystemMediaTransportControls;
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.ButtonPressed += Smtc_ButtonPressed;

            player.PlaybackSession.PlaybackStateChanged += (s, e) => RaiseStateChanged();
            player.PlaybackSession.PositionChanged += (s, e) => RaiseStateChanged();

            _player = player;
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
