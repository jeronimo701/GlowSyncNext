using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GlowSync.Config;

public enum ProfileKind { Desktop, Game, Video, Music }
public enum ProfileSelection { Auto, Custom, Desktop, Game, Video, Music }

public sealed partial class ProfileRule : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _process = "";
    [ObservableProperty] private string _titleContains = "";
    [ObservableProperty] private ProfileKind _profile = ProfileKind.Game;
}

public sealed partial class ProfilePreset : ObservableObject
{
    [ObservableProperty] private EffectMode _mode;
    [ObservableProperty] private int _brightness = 70;
    [ObservableProperty] private string _staticColor = "#FFFFFF";
    public ScreenConfig Screen { get; set; } = new();

    public void Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = EffectMode.Screen;
        Brightness = Math.Clamp(Brightness, 0, 100);
        Screen ??= new();
        Screen.Fps = Math.Clamp(Screen.Fps, 10, 60);
        Screen.SmoothingMs = Math.Clamp(Screen.SmoothingMs, 0, 1000);
        Screen.Saturation = Math.Clamp(Screen.Saturation, -100, 100);
        Screen.Gamma = Math.Clamp(Screen.Gamma, 1, 3);
        Screen.Depth = Math.Clamp(Screen.Depth, 0.5, 6);
        Screen.SpatialBlur = Math.Clamp(Screen.SpatialBlur, 0, 100);
        Screen.BlackThreshold = Math.Clamp(Screen.BlackThreshold, 0, 30);
    }
}

public sealed partial class ProfilesConfig : ObservableObject
{
    [ObservableProperty] private ProfileSelection _selection = ProfileSelection.Auto;
    [ObservableProperty] private int _switchDelayMs = 800;
    [ObservableProperty] private bool _detectBrowserVideo = true;
    [ObservableProperty] private bool _detectGamesByPath = true;
    [ObservableProperty] private bool _detectPlayback = true;
    [ObservableProperty] private bool _desktopWhenPaused;
    public ProfilePreset Desktop { get; set; } = new() { Mode = EffectMode.Static, Brightness = 25 };
    public ProfilePreset Game { get; set; } = new()
    {
        Mode = EffectMode.Screen, Brightness = 80,
        Screen = new() { Fps = 60, SmoothingMs = 20, BlackThreshold = 1 },
    };
    public ProfilePreset Video { get; set; } = new()
    {
        Mode = EffectMode.Screen, Brightness = 65,
        Screen = new() { Fps = 30, SmoothingMs = 100, SpatialBlur = 20, RemoveBlackBars = true },
    };
    public ProfilePreset Music { get; set; } = new() { Mode = EffectMode.Music, Brightness = 65 };
    public ObservableCollection<ProfileRule> Rules { get; set; } = new();
    public int RuleExamplesVersion { get; set; }

    public void AddRuleExamples()
    {
        void Add(string process, string title, ProfileKind profile)
        {
            foreach (var existing in Rules)
                if (string.Equals(existing.Process, process, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.TitleContains, title, StringComparison.OrdinalIgnoreCase)) return;
            Rules.Add(new() { Process = process, TitleContains = title, Profile = profile, Enabled = false });
        }
        foreach (string browser in new[] { "vivaldi.exe", "chrome.exe", "msedge.exe", "firefox.exe" })
        {
            Add(browser, "YouTube Music", ProfileKind.Music);
            Add(browser, "YouTube", ProfileKind.Video);
        }
        Add("vivaldi.exe", "Twitch", ProfileKind.Video);
        foreach (string player in new[] { "vlc.exe", "mpv.exe", "mpc-hc64.exe", "mpc-be64.exe", "PotPlayerMini64.exe" }) Add(player, "", ProfileKind.Video);
        foreach (string player in new[] { "aimp.exe", "foobar2000.exe", "Spotify.exe" }) Add(player, "", ProfileKind.Music);
        Add("game.exe", "", ProfileKind.Game);
        RuleExamplesVersion = 1;
    }

    public ProfilePreset Get(ProfileKind kind) => kind switch
    {
        ProfileKind.Game => Game, ProfileKind.Video => Video, ProfileKind.Music => Music, _ => Desktop,
    };

    public void Normalize()
    {
        if (!Enum.IsDefined(Selection)) Selection = ProfileSelection.Auto;
        // Legacy detection flags remain deserializable, but no longer drive classification.
        SwitchDelayMs = Math.Clamp(SwitchDelayMs, 200, 5000);
        Desktop ??= new() { Mode = EffectMode.Static, Brightness = 25 };
        Game ??= new() { Mode = EffectMode.Screen };
        Video ??= new() { Mode = EffectMode.Screen };
        Music ??= new() { Mode = EffectMode.Music };
        Rules ??= new();
        foreach (var preset in new[] { Desktop, Game, Video, Music }) preset.Normalize();
        for (int i = Rules.Count - 1; i >= 0; i--)
            if (Rules[i] == null) Rules.RemoveAt(i);
        if (RuleExamplesVersion < 1) AddRuleExamples();
    }
}
