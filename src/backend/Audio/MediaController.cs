// C# 12, fichier MediaController.cs, cible Windows 11 / .NET 8, sessions SMTC de Windows
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PanneauxGlass;

internal sealed record MediaSnapshot(
    bool Available,
    string App,
    string Title,
    string Artist,
    bool Playing,
    double Position,
    double Duration,
    bool CanToggle,
    bool CanNext,
    bool CanPrevious,
    bool CanSeek,
    BitmapSource? Artwork,
    string Album = "",
    string Aumid = "",
    string ExeStem = "",
    double Rate = 1,
    bool DurationKnown = true
);

internal sealed class MediaController : IDisposable
{
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    bool active,
        dirty = true,
        reading;
    int generation;
    int rereadsLeft;
    DispatcherTimer? rereadTimer;
    string artKey = "",
        title = "",
        artist = "",
        album = "";
    BitmapSource? artwork;
    internal event Action? Changed;
    static readonly ConcurrentDictionary<string, MediaSource> resolvedSources = new(StringComparer.Ordinal);

    // Les suppositions (hash non résolu, navigateurs ambigus) sont gardées brièvement pour qu'un tick ne reconstruise pas un instantané de
    // processus.
    static readonly ConcurrentDictionary<string, (MediaSource Source, long Expires)> guessedSources = new(
        StringComparer.Ordinal
    );
    const long GuessTtlMs = 5000;

    // Résout un AUMID en nom d'affichage et en radical d'exécutable. Les résultats stables sont mis en cache indéfiniment.
    internal static MediaSource Resolve(string aumid)
    {
        if (resolvedSources.TryGetValue(aumid, out var cached))
        {
            return cached;
        }
        long now = Environment.TickCount64;
        if (guessedSources.TryGetValue(aumid, out var guess) && now < guess.Expires)
        {
            return guess.Source;
        }
        var source = ProcessSnapshot.CreateIdentity().Resolve(aumid, out bool stable);
        if (stable)
        {
            resolvedSources[aumid] = source;
            guessedSources.TryRemove(aumid, out _);
        }
        else
        {
            guessedSources[aumid] = (source, now + GuessTtlMs);
        }
        return source;
    }

    internal static string AppName(string id) => Resolve(id).DisplayName;

    internal static GlobalSystemMediaTransportControlsSession? Select(
        GlobalSystemMediaTransportControlsSessionManager manager,
        string preferred
    )
    {
        var sessions = manager.GetSessions();
        if (!string.IsNullOrEmpty(preferred))
        {
            var found = sessions.FirstOrDefault(s => s.SourceAppUserModelId == preferred);
            if (found != null)
                return found;
        }
        var music = sessions
            .Where(s =>
                !s.SourceAppUserModelId.Contains("desktophut", StringComparison.OrdinalIgnoreCase)
                && !s.SourceAppUserModelId.Contains("wallpaper", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        var current = manager.GetCurrentSession();
        // Une session qui joue réellement passe avant un Spotify en pause : sinon un lecteur arrêté masquerait une vidéo
        // en cours, et la carte afficherait « aucun signal » alors que du son sort.
        const GlobalSystemMediaTransportControlsSessionPlaybackStatus Playing =
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return music.FirstOrDefault(s =>
                s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase)
                && s.GetPlaybackInfo().PlaybackStatus == Playing
            )
            ?? music.FirstOrDefault(s => s.GetPlaybackInfo().PlaybackStatus == Playing)
            ?? music.FirstOrDefault(s => s.SourceAppUserModelId.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            ?? music.FirstOrDefault(s => s.SourceAppUserModelId == current?.SourceAppUserModelId)
            ?? music.FirstOrDefault();
    }

    internal async Task StartAsync()
    {
        if (active)
            return;
        active = true;
        int expected = ++generation;
        if (manager == null)
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (!active || generation != expected)
            return;
        manager.SessionsChanged += SessionsChanged;
        manager.CurrentSessionChanged += CurrentChanged;
        dirty = true;
    }

    void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        Notify();

    void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        Notify();

    void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        dirty = true;
        Notify();
        // Firefox déclenche cet événement avant d'avoir publié les nouvelles métadonnées : planifie des relectures différées.
        dispatcher.BeginInvoke(
            (Action)(
                () =>
                {
                    rereadsLeft = 2;
                    ScheduleReread();
                }
            )
        );
    }

    void ScheduleReread()
    {
        if (!active || rereadsLeft <= 0)
        {
            return;
        }
        if (rereadTimer == null)
        {
            rereadTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(300),
            };
            rereadTimer.Tick += (_, _) =>
            {
                rereadTimer.Stop();
                if (!active || rereadsLeft <= 0)
                {
                    return;
                }
                rereadsLeft--;
                dirty = true;
                Changed?.Invoke();
            };
        }
        if (!rereadTimer.IsEnabled)
        {
            rereadTimer.Start();
        }
    }

    // Appelé après chaque lecture des métadonnées : réessaie une fois de plus si le titre n'a pas changé, s'arrête s'il a changé.
    void AfterPropertiesRead(bool titleChanged)
    {
        if (titleChanged)
        {
            rereadsLeft = Math.Min(rereadsLeft, 1);
        }
        else if (rereadsLeft > 0)
        {
            ScheduleReread();
        }
    }

    void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        Notify();

    void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        Notify();

    void Notify()
    {
        if (active)
            dispatcher.BeginInvoke(
                (Action)(
                    () =>
                    {
                        if (active)
                            Changed?.Invoke();
                    }
                )
            );
    }

    // Les sessions sont comparées par identité d'objet : Firefox recrée des objets de session qui gardent le même AUMID, et
    // les gestionnaires attachés à l'objet mort ne se déclencheraient plus jamais.
    void SetSession(GlobalSystemMediaTransportControlsSession? next)
    {
        if (!MediaTimeline.NeedsResubscribe(session, next))
        {
            return;
        }
        string previousAumid = session?.SourceAppUserModelId ?? "";
        bool sameApplicationObject = next != null && session != null && previousAumid == next.SourceAppUserModelId;
        if (session != null)
        {
            session.MediaPropertiesChanged -= PropertiesChanged;
            session.PlaybackInfoChanged -= PlaybackChanged;
            session.TimelinePropertiesChanged -= TimelineChanged;
        }
        session = next;
        // Même AUMID derrière une autre référence (peut-être la même session native enveloppée deux fois) : on se réabonne seulement,
        // sans relecture forcée ; les événements de métadonnées des nouveaux gestionnaires et les ticks du minuteur gardent les données à
        // jour.
        bool sameApplication = next != null && previousAumid == next.SourceAppUserModelId;
        if (!sameApplicationObject)
        {
            dirty = true;
        }
        if (!sameApplication)
        {
            title = artist = "";
            artwork = null;
            artKey = "";
        }
        if (session != null)
        {
            session.MediaPropertiesChanged += PropertiesChanged;
            session.PlaybackInfoChanged += PlaybackChanged;
            session.TimelinePropertiesChanged += TimelineChanged;
        }
    }

    internal async Task<MediaSnapshot?> ReadAsync()
    {
        if (!active || manager == null || reading)
            return null;
        reading = true;
        int expected = generation;
        try
        {
            SetSession(Select(manager, Store.State.Controls.MediaApp));
            var selected = session;
            if (selected == null)
                return new(
                    false,
                    "",
                    "Aucune lecture",
                    "Lance Spotify ou une autre application audio",
                    false,
                    0,
                    0,
                    false,
                    false,
                    false,
                    false,
                    null
                );
            if (dirty)
            {
                dirty = false;
                var props = await selected.TryGetMediaPropertiesAsync();
                if (!active || expected != generation || selected != session)
                    return null;
                bool titleChanged = props.Title != title;
                title = props.Title;
                album = props.AlbumTitle;
                AfterPropertiesRead(titleChanged);
                artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist;
                string nextKey = title + "|" + artist + "|" + props.AlbumTitle;
                if (artKey != nextKey)
                {
                    artKey = nextKey;
                    artwork = null;
                    if (props.Thumbnail != null)
                        try
                        {
                            using var stream = await props.Thumbnail.OpenReadAsync();
                            if (stream.Size is > 0 and <= 4194304)
                            {
                                using var reader = new DataReader(stream.GetInputStreamAt(0));
                                uint length = (uint)stream.Size;
                                await reader.LoadAsync(length);
                                byte[] data = new byte[length];
                                reader.ReadBytes(data);
                                using var memory = new MemoryStream(data);
                                var image = new BitmapImage();
                                image.BeginInit();
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.DecodePixelWidth = 192;
                                image.StreamSource = memory;
                                image.EndInit();
                                image.Freeze();
                                artwork = image;
                            }
                        }
                        catch (Exception ex)
                        {
                            Store.Log("Pochette: " + ex.Message);
                        }
                }
            }
            if (!active || expected != generation)
                return null;
            var playback = selected.GetPlaybackInfo();
            var timeline = selected.GetTimelineProperties();
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool durationKnown = MediaTimeline.IsDurationKnown(timeline.StartTime, timeline.EndTime);
            double duration = durationKnown ? (timeline.EndTime - timeline.StartTime).TotalSeconds : 0;
            double rate = playback.PlaybackRate ?? 1;
            double position = MediaTimeline.ExtrapolatePosition(
                (timeline.Position - timeline.StartTime).TotalSeconds,
                timeline.LastUpdatedTime,
                DateTimeOffset.UtcNow,
                rate,
                playing,
                duration
            );
            var source = Resolve(selected.SourceAppUserModelId);
            var controls = playback.Controls;
            return new(
                true,
                source.DisplayName,
                string.IsNullOrWhiteSpace(title) ? "Lecture en cours" : title,
                artist,
                playing,
                position,
                duration,
                controls.IsPlayPauseToggleEnabled,
                controls.IsNextEnabled,
                controls.IsPreviousEnabled,
                controls.IsPlaybackPositionEnabled,
                artwork,
                album,
                source.Aumid,
                source.ExeStem,
                rate,
                durationKnown
            );
        }
        catch
        {
            SetSession(null);
            throw;
        }
        finally
        {
            reading = false;
        }
    }

    internal (string id, string name)[] Sessions() =>
        manager?.GetSessions().Select(s => (s.SourceAppUserModelId, AppName(s.SourceAppUserModelId))).ToArray()
        ?? Array.Empty<(string, string)>();

    internal async Task<bool> CommandAsync(string action, double position = 0)
    {
        var selected = session;
        if (selected == null)
            return false;
        return action switch
        {
            "toggle" => await selected.TryTogglePlayPauseAsync(),
            "next" => await selected.TrySkipNextAsync(),
            "previous" => await selected.TrySkipPreviousAsync(),
            "seek" => await selected.TryChangePlaybackPositionAsync(
                selected.GetTimelineProperties().StartTime.Ticks + TimeSpan.FromSeconds(position).Ticks
            ),
            _ => false,
        };
    }

    internal void Stop()
    {
        active = false;
        generation++;
        rereadsLeft = 0;
        rereadTimer?.Stop();
        if (manager != null)
        {
            manager.SessionsChanged -= SessionsChanged;
            manager.CurrentSessionChanged -= CurrentChanged;
        }
        SetSession(null);
    }

    public void Dispose()
    {
        Stop();
        manager = null;
    }
}
