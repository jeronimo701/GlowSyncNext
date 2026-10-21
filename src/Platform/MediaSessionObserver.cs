using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlowSync.Core;
using Windows.Media;
using Windows.Media.Control;

namespace GlowSync.Platform;

/// <summary>Read-only SMTC snapshots. No media control requests are sent.</summary>
internal sealed class MediaSessionObserver
{
    private Task? _poll;
    private long _requestedAt;
    private long _updatedAt;
    private PlaybackEvidence[] _snapshot = Array.Empty<PlaybackEvidence>();
    private string _status = "Ожидание";
    public string Status => _poll is { IsCompleted: false } && Environment.TickCount64 - Volatile.Read(ref _requestedAt) > 3500
        ? "Медиапровайдер не ответил; используется приложение/сайт" : Volatile.Read(ref _status);

    // Called from a single foreground thread; never waits for an unresponsive media provider.
    public PlaybackEvidence? Get(ForegroundContext context)
    {
        long now = Environment.TickCount64;
        if ((_poll == null || _poll.IsCompleted) && now - _requestedAt >= 1000)
        {
            _requestedAt = now;
            _poll = Task.Run(PollAsync);
        }
        return now - Volatile.Read(ref _updatedAt) < 3500 ? PlaybackMatcher.Match(context, Volatile.Read(ref _snapshot)) : null;
    }

    private async Task PollAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var result = new List<PlaybackEvidence>();
            int count = 0;
            foreach (var session in manager.GetSessions())
            {
                if (count++ >= 24) break;
                var info = session.GetPlaybackInfo();
                var properties = await session.TryGetMediaPropertiesAsync();
                var state = info.PlaybackStatus switch
                {
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackState.Stopped,
                    _ => PlaybackState.Unknown,
                };
                var kind = properties.PlaybackType switch { MediaPlaybackType.Music => MediaKind.Music, MediaPlaybackType.Video => MediaKind.Video, _ => MediaKind.Unknown };
                result.Add(new(session.SourceAppUserModelId, properties.Title ?? "", state, kind));
            }
            Volatile.Write(ref _snapshot, result.ToArray());
            Volatile.Write(ref _updatedAt, Environment.TickCount64);
            Volatile.Write(ref _status, $"Доступно медиасессий: {result.Count}");
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _snapshot, Array.Empty<PlaybackEvidence>());
            Volatile.Write(ref _status, "Медиасессии недоступны: " + ex.GetType().Name);
        }
    }
}
