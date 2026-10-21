using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using GlowSync.Platform;

namespace GlowSync.Config;

/// <summary>
/// Owns the live <see cref="AppConfig"/>: loads it, tracks every property change,
/// and persists it with a debounced atomic write (temp file + replace, with a .bak copy).
/// </summary>
internal sealed class ConfigStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Timer _saveTimer;
    private readonly object _saveLock = new();
    private int _revision;
    private AppConfig _config;
    private readonly HashSet<INotifyPropertyChanged> _subscriptions = new();

    /// <summary>Raised on the thread that made the change. Sender is the object whose property changed.</summary>
    public event Action<object, string>? Changed;

    /// <summary>Raised after the whole config object was swapped (e.g. import).</summary>
    public event Action? Replaced;

    public AppConfig Config => _config;

    /// <summary>Increments on every change; background threads poll it to rebuild caches.</summary>
    public int Revision => Volatile.Read(ref _revision);

    public bool IsFirstRun { get; }

    public ConfigStore()
    {
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        var loaded = TryLoad(Paths.ConfigFile) ?? TryLoad(Paths.ConfigFile + ".bak");
        if (loaded == null && !File.Exists(Paths.ConfigFile) && Environment.GetEnvironmentVariable("GLOWSYNC_NEXT_DATA_DIR") == null)
        {
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlowSync", "config.json");
            loaded = TryLoad(legacy);
            if (loaded != null)
            {
                loaded.Behavior ??= new();
                loaded.Behavior.Autostart = false;
                Log.Info("Copied original GlowSync configuration into Next; original file is untouched");
            }
        }
        IsFirstRun = loaded == null && !File.Exists(Paths.ConfigFile);
        if (loaded is { Version: < 3 } && File.Exists(Paths.ConfigFile))
        {
            try
            {
                string backup = Paths.ConfigFile + $".v{loaded.Version}-before-v3.bak";
                if (!File.Exists(backup)) File.Copy(Paths.ConfigFile, backup);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Versioned configuration backup failed: " + ex.GetType().Name); }
        }
        _config = loaded ?? new AppConfig();
        _config.Version = 3;
        _config.Normalize();
        Attach(_config);
    }

    public void Replace(AppConfig config)
    {
        Detach(_config);
        config.Normalize();
        _config = config;
        Attach(config);
        Interlocked.Increment(ref _revision);
        ScheduleSave();
        Replaced?.Invoke();
    }

    public void ScheduleSave() => _saveTimer.Change(800, Timeout.Infinite);

    public void SaveNow()
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                var json = JsonSerializer.Serialize(_config, JsonOptions);
                var tmp = Paths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(Paths.ConfigFile))
                {
                    File.Replace(tmp, Paths.ConfigFile, Paths.ConfigFile + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmp, Paths.ConfigFile);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Config save failed", ex);
            }
        }
    }

    public static AppConfig Clone(AppConfig source) =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;

    private static AppConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error($"Config load failed ({Path.GetFileName(path)})", ex);
            return null;
        }
    }

    private void Attach(AppConfig c)
    {
        foreach (var node in Nodes(c))
            if (_subscriptions.Add(node)) node.PropertyChanged += OnNodeChanged;
        c.Profiles.Rules.CollectionChanged += RulesChanged;
        c.Outputs.CollectionChanged += OutputsChanged;
    }

    private void Detach(AppConfig c)
    {
        foreach (var node in _subscriptions) node.PropertyChanged -= OnNodeChanged;
        _subscriptions.Clear();
        c.Profiles.Rules.CollectionChanged -= RulesChanged;
        c.Outputs.CollectionChanged -= OutputsChanged;
    }

    private void RulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Detach(_config);
        Attach(_config);
        OnNodeChanged(_config.Profiles, new PropertyChangedEventArgs(nameof(ProfilesConfig.Rules)));
    }

    private void OutputsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Detach(_config);
        Attach(_config);
        OnNodeChanged(_config, new PropertyChangedEventArgs(nameof(AppConfig.Outputs)));
    }

    private static System.Collections.Generic.IEnumerable<INotifyPropertyChanged> Nodes(AppConfig c)
    {
        yield return c;
        yield return c.Device;
        foreach (var output in c.Outputs) yield return output;
        yield return c.Layout;
        yield return c.Screen;
        yield return c.Color;
        yield return c.Effects;
        yield return c.Behavior;
        yield return c.Night;
        yield return c.Profiles;
        foreach (var preset in new[] { c.Profiles.Desktop, c.Profiles.Game, c.Profiles.Video, c.Profiles.Music })
        {
            yield return preset;
            yield return preset.Screen;
        }
        foreach (var rule in c.Profiles.Rules) yield return rule;
        foreach (var h in c.Hotkeys) yield return h;
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is LayoutConfig layout && !layout.UseCustomMap && e.PropertyName is nameof(LayoutConfig.Left) or nameof(LayoutConfig.Top) or nameof(LayoutConfig.Right) or nameof(LayoutConfig.Bottom) or nameof(LayoutConfig.Start) or nameof(LayoutConfig.Clockwise) or nameof(LayoutConfig.Offset) or nameof(LayoutConfig.UseCustomMap))
            Core.LayoutBuilder.Apply(layout);
        Interlocked.Increment(ref _revision);
        ScheduleSave();
        Changed?.Invoke(sender!, e.PropertyName ?? "");
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        SaveNow();
        Detach(_config);
    }
}
