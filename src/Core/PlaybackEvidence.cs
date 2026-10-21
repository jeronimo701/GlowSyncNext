using System;
using System.Collections.Generic;
using System.Linq;

namespace GlowSync.Core;

internal enum PlaybackState { Unknown, Playing, Paused, Stopped }
internal enum MediaKind { Unknown, Music, Video }
internal sealed record PlaybackEvidence(string Application, string Title, PlaybackState State, MediaKind Kind);

internal static class PlaybackMatcher
{
    public static PlaybackEvidence? Match(ForegroundContext context, IEnumerable<PlaybackEvidence> sessions)
    {
        string process = ProfileClassifier.ProcessName(context.Process);
        bool browser = ProfileClassifier.IsBrowser(process);
        var matches = sessions.Where(s => SourceMatches(s.Application, process)).ToArray();
        if (browser)
        {
            // Browser sessions refer to tabs, not just processes. Never use another tab's playback state.
            string title = context.Title.Trim();
            if (title.StartsWith('(') && title.IndexOf(')') is > 1 and < 8)
            {
                int close = title.IndexOf(')');
                if (int.TryParse(title.AsSpan(1, close - 1), out _)) title = title[(close + 1)..].TrimStart();
            }
            matches = matches.Where(s =>
            {
                string mediaTitle = s.Title.Trim();
                if (mediaTitle.Length < 4 || !title.StartsWith(mediaTitle, StringComparison.OrdinalIgnoreCase)) return false;
                string suffix = title[mediaTitle.Length..];
                return suffix.Length == 0 || suffix.StartsWith(" - ") || suffix.StartsWith(" — ");
            }).ToArray();
        }
        // Ambiguous tabs/sessions must fall back to app/URL classification.
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool SourceMatches(string source, string process)
    {
        if (string.Equals(ProfileClassifier.ProcessName(source), process, StringComparison.OrdinalIgnoreCase)) return true;
        return (process.Equals("music.ui", StringComparison.OrdinalIgnoreCase) && source.StartsWith("Microsoft.ZuneMusic_", StringComparison.OrdinalIgnoreCase)) ||
            (process.Equals("video.ui", StringComparison.OrdinalIgnoreCase) && source.StartsWith("Microsoft.ZuneVideo_", StringComparison.OrdinalIgnoreCase)) ||
            (process.Equals("microsoft.media.player", StringComparison.OrdinalIgnoreCase) && source.StartsWith("Microsoft.ZuneMusic_", StringComparison.OrdinalIgnoreCase));
    }
}
