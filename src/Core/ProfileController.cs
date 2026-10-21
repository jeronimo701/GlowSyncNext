using System;
using GlowSync.Config;
using GlowSync.Platform;

namespace GlowSync.Core;

internal sealed class ProfileController : IDisposable
{
    private readonly ConfigStore _store;
    private readonly Engine _engine;
    private readonly ForegroundObserver _observer = new();
    private readonly ProfileDebouncer _debouncer = new();
    private ForegroundContext? _context;
    private bool _disposed;
    public string Status { get; private set; } = "По правилам: ожидание активного приложения";
    public string MediaStatus => "Отключено: только явные правила, без URL и медиасессий";
    public event Action? Changed;

    public ProfileController(ConfigStore store, Engine engine)
    {
        _store = store;
        _engine = engine;
        _observer.Observed += context => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Observe(context));
        _store.Changed += ConfigChanged;
        _store.Replaced += Refresh;
    }
    public void Start() { Refresh(); _observer.Start(); }
    private void Observe(ForegroundContext context) { if (_disposed) return; _context = context; Refresh(); }
    private void ConfigChanged(object sender, string property)
    {
        // Changing an original mode explicitly selects manual operation. Auto never writes Mode.
        if (sender is AppConfig && property == nameof(AppConfig.Mode)) _store.Config.Profiles.Selection = ProfileSelection.Custom;
        if (sender is ProfilesConfig && property == nameof(ProfilesConfig.Selection)) _debouncer.Reset();
        if (sender is ProfilesConfig or ProfilePreset or ProfileRule || sender is ScreenConfig || sender is AppConfig)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess()) Refresh(); else dispatcher.BeginInvoke(Refresh);
        }
    }
    public void Refresh()
    {
        if (_disposed) return;
        var cfg = _store.Config.Profiles;
        _observer.InspectBrowser = false;
        _observer.InspectPlayback = false;
        if (cfg.Selection == ProfileSelection.Custom)
        {
            _engine.SetProfile(null);
            SetStatus("Вручную — настройки Главной и Экрана");
            return;
        }
        ProfileKind? kind;
        string reason;
        if (cfg.Selection == ProfileSelection.Auto)
        {
            if (_context == null)
            {
                _engine.SetProfile(cfg.Desktop);
                SetStatus("По правилам: включено · пока обычная подсветка. Перейдите в нужное приложение.");
                return;
            }
            var decision = ProfileClassifier.Classify(_context, cfg);
            _debouncer.Update(decision.Profile, Environment.TickCount64, cfg.SwitchDelayMs);
            kind = _debouncer.Current;
            if (kind == null)
            {
                _engine.SetProfile(cfg.Desktop);
                SetStatus($"По правилам: включено · ожидание {Name(decision.Profile)} · {_context.Process}.exe");
                return;
            }
            reason = kind == decision.Profile ? decision.Reason : "Ожидание переключения";
        }
        else { kind = (ProfileKind)((int)cfg.Selection - (int)ProfileSelection.Desktop); reason = "Закреплён вручную"; }
        _engine.SetProfile(cfg.Get(kind.Value));
        SetStatus((cfg.Selection == ProfileSelection.Auto ? "По правилам: " : "") + $"{Name(kind.Value)} · {reason}" + (cfg.Selection == ProfileSelection.Auto && _context != null ? $" · {_context.Process}.exe" : ""));
    }
    public static string Name(ProfileKind kind) => kind switch { ProfileKind.Game => "Игра", ProfileKind.Video => "Кино", ProfileKind.Music => "Музыка", _ => "Обычная подсветка" };
    private void SetStatus(string status)
    {
        if (Status == status) return;
        Status = status;
        Changed?.Invoke();
    }
    public void Dispose()
    {
        _disposed = true;
        _store.Changed -= ConfigChanged;
        _store.Replaced -= Refresh;
        _observer.Dispose();
    }
}
