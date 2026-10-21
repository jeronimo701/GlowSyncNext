using System;
using System.Collections.Generic;
using System.IO;
using GlowSync.Config;

namespace GlowSync.Core;

internal sealed record ForegroundContext(string Process, string Title, string Path = "", string? BrowserUrl = null, PlaybackEvidence? Playback = null);
internal sealed record ProfileDecision(ProfileKind Profile, string Reason);

internal static class ProfileClassifier
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "browser", "thorium", "librewolf" };

    public static string ProcessName(string value)
    {
        string name = Path.GetFileName(value.Trim().Replace('\\', '/'));
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
    public static bool IsBrowser(string process) => Browsers.Contains(ProcessName(process));

    public static ProfileDecision Classify(ForegroundContext context, ProfilesConfig config)
    {
        string process = ProcessName(context.Process);
        for (int i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Process)) continue;
            if (!string.Equals(ProcessName(rule.Process), process, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(rule.TitleContains) && !context.Title.Contains(rule.TitleContains, StringComparison.OrdinalIgnoreCase)) continue;
            return new(rule.Profile, $"Правило №{i + 1}");
        }
        return new(ProfileKind.Desktop, "Нет включённого подходящего правила");
    }
}

/// <summary>A new target must remain stable for the whole delay. No oscillation on brief Alt+Tab.</summary>
internal sealed class ProfileDebouncer
{
    private ProfileKind? _candidate;
    private long _since;
    public ProfileKind? Current { get; private set; }
    public void Reset() { _candidate = null; Current = null; _since = 0; }
    public bool Update(ProfileKind target, long now, int delay)
    {
        if (_candidate != target) { _candidate = target; _since = now; }
        if (Current == target || now - _since < delay) return false;
        Current = target;
        return true;
    }
}
