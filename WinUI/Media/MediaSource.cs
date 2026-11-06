using Windows.Media.Control;
using Windows.Storage.Streams;

namespace VNotch.WinUI.Media;

/// <summary>
/// The current Windows media session (Spotify, browser, …) through the system media transport controls.
/// Events arrive on background threads; the window marshals them to the UI thread.
/// </summary>
internal sealed class MediaSource
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    public event Action? TrackChanged;
    public event Action? PlaybackChanged;

    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public IRandomAccessStreamReference? Thumbnail { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool HasTrack => Title.Length > 0;
    private TimeSpan _position, _duration;
    private DateTimeOffset _positionAt;

    public TimeSpan Duration => _duration;

    public TimeSpan Position
    {
        get
        {
            var p = IsPlaying ? _position + (DateTimeOffset.Now - _positionAt) : _position;
            return p < TimeSpan.Zero ? TimeSpan.Zero : (_duration > TimeSpan.Zero && p > _duration ? _duration : p);
        }
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => _ = AttachAsync();
        await AttachAsync();
    }

    private async Task AttachAsync()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _session = _manager?.GetCurrentSession();
        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelineChanged;
        }
        ReadPlayback();
        ReadTimeline();
        await ReadPropertiesAsync();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => _ = ReadPropertiesAsync();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
    {
        ReadPlayback();
        ReadTimeline();
        PlaybackChanged?.Invoke();
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
    {
        ReadTimeline();
        PlaybackChanged?.Invoke();
    }

    private async Task ReadPropertiesAsync()
    {
        string title = "", artist = "";
        IRandomAccessStreamReference? thumbnail = null;
        try
        {
            if (_session?.TryGetMediaPropertiesAsync() is { } pending)
            {
                var props = await pending;
                title = props?.Title ?? "";
                artist = props?.Artist ?? "";
                thumbnail = props?.Thumbnail;
            }
        }
        catch (Exception)
        {
            // The session can vanish between the event and the read; show nothing until the next event.
        }
        if (title == Title && artist == Artist && ReferenceEquals(thumbnail, Thumbnail)) return;
        Title = title;
        Artist = artist;
        Thumbnail = thumbnail;
        TrackChanged?.Invoke();
    }

    private void ReadPlayback()
    {
        try
        {
            IsPlaying = _session?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception) { IsPlaying = false; }
    }

    private void ReadTimeline()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) { _position = _duration = TimeSpan.Zero; return; }
            _duration = t.EndTime - t.StartTime;
            _position = t.Position - t.StartTime;
            _positionAt = t.LastUpdatedTime.Year > 2000 ? t.LastUpdatedTime : DateTimeOffset.Now;
        }
        catch (Exception) { _position = _duration = TimeSpan.Zero; }
    }

    public void TogglePlayPause() => _ = _session?.TryTogglePlayPauseAsync();
    public void Next() => _ = _session?.TrySkipNextAsync();
    public void Previous() => _ = _session?.TrySkipPreviousAsync();
}
