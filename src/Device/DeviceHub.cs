using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GlowSync.Config;

namespace GlowSync.Device;

/// <summary>Independent latest-frame mailboxes for each output: a slow USB controller cannot stall a network strip.</summary>
internal sealed class DeviceHub : IDisposable
{
    private readonly ConfigStore _store;
    private readonly DeviceManager _primary;
    private readonly object _sync = new();
    private (DeviceConfig Config, DeviceManager Manager)[] _routes = Array.Empty<(DeviceConfig, DeviceManager)>();
    private bool _started, _suspended;
    public string RoutingWarning { get; private set; } = "";
    public DeviceStatus Status => _primary.Status;
    public int DeviceLedCount => _primary.DeviceLedCount;
    public DeviceManager Primary => _primary;
    public event Action<DeviceStatus>? StatusChanged;

    public DeviceHub(ConfigStore store)
    {
        _store = store;
        _primary = new(store);
        _primary.StatusChanged += status => StatusChanged?.Invoke(status);
        _store.Changed += Changed;
        _store.Replaced += Refresh;
    }
    public void Start()
    {
        _started = true;
        if (!_store.Config.Device.OutputEnabled) _primary.Suspend();
        _primary.Start();
        Refresh();
    }
    private void Changed(object sender, string property)
    {
        if (_started && (sender is DeviceConfig || sender is AppConfig && property == nameof(AppConfig.Outputs))) Refresh();
    }
    private static string Endpoint(DeviceConfig cfg) => FrameEncoding.IsNetwork(cfg.Protocol)
        ? $"{cfg.Host.Trim().ToLowerInvariant()}:{cfg.UdpPort}" : cfg.Port.Trim().ToUpperInvariant();
    public void Refresh()
    {
        if (!_started) return;
        lock (_sync)
        {
            var next = new List<(DeviceConfig Config, DeviceManager Manager)>();
            var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_store.Config.Device.OutputEnabled) endpoints.Add(Endpoint(_store.Config.Device));
            if (_store.Config.Device.OutputEnabled && !FrameEncoding.IsNetwork(_store.Config.Device.Protocol) && _primary.Status.Port is { } actualPort)
                endpoints.Add(actualPort);
            var errors = new List<string>();
            foreach (var cfg in _store.Config.Outputs.Take(8))
            {
                if (!cfg.OutputEnabled) continue;
                if (!FrameEncoding.IsNetwork(cfg.Protocol) && _store.Config.Device.OutputEnabled &&
                    !FrameEncoding.IsNetwork(_store.Config.Device.Protocol) && _store.Config.Device.Port == DeviceConfig.AutoPort)
                { errors.Add($"{cfg.Name}: при нескольких USB-выходах укажите COM основного вручную"); continue; }
                if (!FrameEncoding.IsNetwork(cfg.Protocol) && cfg.Port == DeviceConfig.AutoPort)
                { errors.Add($"{cfg.Name}: укажите COM вручную"); continue; }
                if (!endpoints.Add(Endpoint(cfg))) { errors.Add($"{cfg.Name}: адрес/COM уже занят другим выходом"); continue; }
                var existing = _routes.FirstOrDefault(r => ReferenceEquals(r.Config, cfg));
                var manager = existing.Manager ?? new DeviceManager(_store, () => cfg);
                if (existing.Manager == null) { if (_suspended) manager.Suspend(); manager.Start(); }
                next.Add((cfg, manager));
            }
            foreach (var route in _routes)
                if (!next.Any(n => ReferenceEquals(n.Manager, route.Manager))) route.Manager.Dispose();
            Volatile.Write(ref _routes, next.ToArray());
            RoutingWarning = string.Join("; ", errors);
            if (!_store.Config.Device.OutputEnabled) _primary.Suspend();
            else if (!_suspended && _primary.Status.State == DeviceState.Suspended) _primary.Resume();
        }
    }

    internal static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> rgb, int startLed, int count)
    {
        int start = Math.Min(rgb.Length, Math.Clamp(startLed, 0, 1000) * 3);
        return rgb.Slice(start, Math.Min(rgb.Length - start, Math.Clamp(count, 1, 1000) * 3));
    }
    public void Submit(ReadOnlySpan<byte> rgb)
    {
        lock (_sync)
        {
            if (_store.Config.Device.OutputEnabled) _primary.Submit(rgb);
            foreach (var route in _routes) route.Manager.Submit(Slice(rgb, route.Config.StartLed, route.Config.LedCount));
        }
    }
    public double LimitFps(int requested)
    {
        double Limit(DeviceConfig cfg, DeviceManager manager) => FrameEncoding.LimitFps(requested, cfg.Protocol, cfg.BaudRate, manager.DeviceLedCount > 0 ? manager.DeviceLedCount : cfg.LedCount);
        double result = _store.Config.Device.OutputEnabled ? Limit(_store.Config.Device, _primary) : 1;
        foreach (var route in Volatile.Read(ref _routes)) result = Math.Max(result, Limit(route.Config, route.Manager));
        return result;
    }
    public (DeviceConfig Config, DeviceManager Manager)[] Snapshot() =>
        new[] { (_store.Config.Device, _primary) }.Concat(Volatile.Read(ref _routes)).ToArray();
    public void RequestRescan() { _primary.RequestRescan(); foreach (var route in Volatile.Read(ref _routes)) route.Manager.RequestRescan(); }
    public void Suspend()
    {
        lock (_sync) { _suspended = true; _primary.Suspend(); foreach (var route in _routes) route.Manager.Suspend(); }
    }
    public void Resume()
    {
        lock (_sync) { _suspended = false; if (_store.Config.Device.OutputEnabled) _primary.Resume(); foreach (var route in _routes) route.Manager.Resume(); }
    }
    public void Dispose()
    {
        _store.Changed -= Changed; _store.Replaced -= Refresh;
        lock (_sync) { _primary.Dispose(); foreach (var route in _routes) route.Manager.Dispose(); _routes = Array.Empty<(DeviceConfig, DeviceManager)>(); }
    }
}
