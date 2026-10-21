using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using GlowSync.Config;
using GlowSync.Platform;
using GlowSync.Core;

namespace GlowSync.Device;

internal enum DeviceState { Searching, Connected, NotFound, PortBusy, Suspended }

internal sealed record DeviceStatus(DeviceState State, string? Port = null, string? Model = null, string? Serial = null, int LedCount = 0)
{
    public string Describe() => State switch
    {
        DeviceState.Connected => $"{Model ?? "Adalight"} · {Port} · {LedCount} LED",
        DeviceState.Searching => "Поиск устройства…",
        DeviceState.PortBusy => $"Порт {Port} занят другой программой" + (Process.GetProcessesByName("SkyDimo").Length > 0 ? " (запущен Skydimo)" : ""),
        DeviceState.Suspended => "Пауза (сон)",
        _ => "Устройство не найдено — проверьте USB-кабель",
    };
}

/// <summary>
/// Owns the serial connection on its own thread: finds the controller, sends the latest frame,
/// keeps the link alive and silently reconnects after unplug, sleep or driver hiccups.
/// </summary>
internal sealed class DeviceManager : IDisposable
{
    private static readonly byte[] Handshake = Encoding.ASCII.GetBytes("Moni-A");

    private readonly Func<DeviceConfig> _getConfig;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly FrameTimer _pacer;
    private readonly object _frameLock = new();
    private readonly object _portLock = new();

    private IFrameTransport? _port;
    private string _connectedKey = "";
    private byte[] _pending = new byte[65 * 3];
    private bool _pendingDirty;
    private byte[] _packet = Array.Empty<byte>();
    private int _packetLength;
    private volatile bool _stop;
    private volatile bool _suspended;
    private volatile bool _rescan = true;
    private long _nextAttemptTicks;
    private int _failedAttempts;
    private long _lastWriteTicks;
    private long _nextWriteAt;
    private long _sendPeriodTicks;
    private DeviceProtocol _connectedProtocol;
    private ChannelOrder _connectedChannelOrder;
    private int _connectedBaud;
    private int _connectedOpcChannel;
    private int _disposeCalled;
    private long _submittedAt, _overwritten, _writeErrors, _connections;
    public FrameMetric Sending { get; } = new();
    public double LastQueueMs { get; private set; }
    public double EstimatedWireMs => FrameEncoding.IsNetwork(_connectedProtocol) || _connectedBaud <= 0 ? 0 : _packetLength * 10_000.0 / _connectedBaud;
    public long OverwrittenFrames => Interlocked.Read(ref _overwritten);
    public long WriteErrors => Interlocked.Read(ref _writeErrors);
    public long Connections => Interlocked.Read(ref _connections);
    public string LastError { get; private set; } = "";

    public DeviceStatus Status { get; private set; } = new(DeviceState.Searching);

    /// <summary>Raised from the device thread.</summary>
    public event Action<DeviceStatus>? StatusChanged;

    public DeviceManager(ConfigStore store, Func<DeviceConfig>? configSource = null)
    {
        _getConfig = configSource ?? (() => store.Config.Device);
        _pacer = new FrameTimer(_wake);
        _thread = new Thread(Run) { IsBackground = true, Name = "GlowSync.Device" };
    }

    public void Start() => _thread.Start();

    /// <summary>LED count the connected controller expects (0 when unknown).</summary>
    public int DeviceLedCount => Status.State == DeviceState.Connected ? Status.LedCount : 0;

    /// <summary>Queues an RGB frame (3 bytes per LED). Only the newest frame is ever sent.</summary>
    public void Submit(ReadOnlySpan<byte> rgb)
    {
        lock (_frameLock)
        {
            if (_pendingDirty) Interlocked.Increment(ref _overwritten);
            _submittedAt = Stopwatch.GetTimestamp();
            if (_pending.Length != rgb.Length) _pending = new byte[rgb.Length];
            rgb.CopyTo(_pending);
            _pendingDirty = true;
        }
        _wake.Set();
    }

    public void RequestRescan()
    {
        _rescan = true;
        _failedAttempts = 0;
        _wake.Set();
    }

    /// <summary>Synchronously turns the LEDs off (used on shutdown, logoff and sleep).</summary>
    public void BlackoutNow()
    {
        lock (_portLock)
        {
            if (_port == null) return;
            try
            {
                byte[] packet = Array.Empty<byte>();
                int length = FrameEncoding.Build(_connectedProtocol, _connectedChannelOrder, Math.Max(1, Status.LedCount), ReadOnlySpan<byte>.Empty, ref packet, _connectedOpcChannel);
                for (int i = 0; i < 2; i++) _port.Write(packet.AsSpan(0, length));
            }
            catch
            {
                // Device already gone — nothing to turn off.
            }
        }
    }

    public void Suspend()
    {
        _suspended = true;
        BlackoutNow();
        Disconnect("system suspend", DeviceState.Suspended);
    }

    public void Resume()
    {
        _suspended = false;
        SetStatus(new DeviceStatus(DeviceState.Searching));
        RequestRescan();
    }

    private void Run()
    {
        while (!_stop)
        {
            try
            {
                Step();
            }
            catch (Exception ex)
            {
                Log.Error("Device loop error", ex);
                Disconnect("loop error", DeviceState.NotFound);
                Thread.Sleep(1000);
            }
        }
        BlackoutNow();
        Disconnect("stopped", DeviceState.NotFound);
    }

    private void Step()
    {
        if (_suspended)
        {
            _wake.WaitOne(1000);
            return;
        }

        var cfg = _getConfig();
        if (_port != null && ConfigKey(cfg) != _connectedKey)
        {
            Disconnect("settings changed", DeviceState.Searching);
            _rescan = true;
        }

        if (_port == null)
        {
            long now = Environment.TickCount64;
            if (!_rescan && now < Volatile.Read(ref _nextAttemptTicks))
            {
                _wake.WaitOne(500);
                return;
            }
            _rescan = false;
            TryConnect(cfg);
            if (_port == null)
            {
                // Back off to 30 s while nothing is plugged in; USB arrival triggers an immediate rescan anyway.
                int delay = Math.Min(30_000, 3000 * (1 << Math.Min(_failedAttempts, 4)));
                _failedAttempts++;
                Volatile.Write(ref _nextAttemptTicks, Environment.TickCount64 + delay);
                return;
            }
            _failedAttempts = 0;
        }

        _wake.WaitOne(1000);
        // Pace before taking the newest pending frame, so a UART never accumulates stale frames.
        double frameMs = 1000.0 / FrameEncoding.LimitFps(60, _connectedProtocol, _connectedBaud, Math.Max(1, Status.LedCount));
        long periodTicks = (long)(frameMs * Stopwatch.Frequency / 1000.0);
        if (_sendPeriodTicks != periodTicks) { _sendPeriodTicks = periodTicks; _nextWriteAt = 0; }
        double remaining;
        // Start-to-start cadence: blocking write time is already part of the frame period.
        // High-resolution waits avoid rounding 16.67 ms to 17+ ms on every frame.
        while (!_stop && !_suspended && ConfigKey(cfg) == _connectedKey &&
               _nextWriteAt != 0 && (remaining = (_nextWriteAt - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency) > 0.01)
            _pacer.Sleep(remaining / 1000.0);
        if (_stop || _suspended || ConfigKey(cfg) != _connectedKey) return;

        bool send;
        long queuedAt = 0;
        lock (_frameLock)
        {
            send = _pendingDirty;
            if (send)
            {
                queuedAt = _submittedAt;
                int leds = Status.LedCount > 0 ? Status.LedCount : _pending.Length / 3;
                _packetLength = FrameEncoding.Build(_connectedProtocol, _connectedChannelOrder, leds, _pending, ref _packet, _connectedOpcChannel);
                _pendingDirty = false;
            }
        }
        // Keep-alive: some controllers drop to an idle animation when the stream stops.
        if (!send && _packetLength > 0 && Environment.TickCount64 - _lastWriteTicks > 1000) send = true;
        if (!send) return;

        lock (_portLock)
        {
            if (_port == null) return;
            try
            {
                long writeStart = Stopwatch.GetTimestamp();
                LastQueueMs = queuedAt == 0 ? 0 : FrameMetric.ElapsedMs(queuedAt);
                _port.Write(_packet.AsSpan(0, _packetLength));
                // Advance the scheduled deadline, not the overslept actual start.
                // Missed periods are discarded rather than replayed as a burst.
                _nextWriteAt = Math.Max((_nextWriteAt == 0 ? writeStart : _nextWriteAt) + periodTicks, Stopwatch.GetTimestamp());
                if (!FrameEncoding.IsNetwork(_connectedProtocol) && _connectedBaud > 0)
                {
                    long wireTicks = (long)(_packetLength * 10.0 / _connectedBaud * Stopwatch.Frequency);
                    _nextWriteAt = Math.Max(_nextWriteAt, writeStart + wireTicks);
                }
                Sending.Observe(FrameMetric.ElapsedMs(writeStart));
                _lastWriteTicks = Environment.TickCount64;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _writeErrors);
                LastError = ex.GetType().Name;
                Log.Warn($"Write to {_port.Name} failed ({ex.Message}); reconnecting");
                DisconnectLocked(DeviceState.Searching);
                Volatile.Write(ref _nextAttemptTicks, Environment.TickCount64 + 1000);
            }
        }
    }

    private void TryConnect(DeviceConfig cfg)
    {
        if (FrameEncoding.IsNetwork(cfg.Protocol))
        {
            try
            {
                IFrameTransport transport = cfg.Protocol == DeviceProtocol.OpcTcp
                    ? new OpcFrameTransport(cfg.Host, cfg.UdpPort)
                    : new DdpFrameTransport(cfg.Host, cfg.UdpPort, cfg.Protocol == DeviceProtocol.Tpm2Net);
                Connected(transport, cfg, new(DeviceState.Connected, transport.Name, cfg.Protocol.ToString(), null, cfg.LedCount));
            }
            catch (Exception ex)
            {
                Log.Warn("DDP connection: " + ex.Message);
                LastError = ex.GetType().Name;
                SetStatus(new(DeviceState.NotFound, cfg.Host));
            }
            return;
        }
        bool auto = string.Equals(cfg.Port, DeviceConfig.AutoPort, StringComparison.OrdinalIgnoreCase);
        string[] candidates;
        if (auto)
        {
            var ports = PortEnumerator.GetPorts().Where(p => p.IsUsb && !p.IsBluetooth).ToList();
            candidates = ports
                .OrderByDescending(p => string.Equals(p.Name, cfg.LastPort, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(p => p.IsCh34x)
                .ThenBy(p => p.Number)
                .Select(p => p.Name)
                .ToArray();
        }
        else
        {
            candidates = new[] { cfg.Port.ToUpperInvariant() };
        }

        string? busyPort = null;
        foreach (var name in candidates)
        {
            if (_stop || _suspended) return;
            NativeSerialPort port;
            try
            {
                port = NativeSerialPort.Open(name, cfg.BaudRate, dtr: false);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == NativeSerialPort.ErrorAccessDenied) busyPort ??= name;
                continue;
            }

            var hello = cfg.Protocol == DeviceProtocol.Skydimo || auto ? TryHandshake(port) : null;
            if (hello != null)
            {
                var model = SkydimoModels.Find(hello.Value.Model);
                int count = model?.LedCount ?? cfg.LedCount;
                Connected(new SerialFrameTransport(port), cfg, new DeviceStatus(DeviceState.Connected, name, hello.Value.Model, hello.Value.Serial, count));
                return;
            }
            if (!auto)
            {
                // Explicit port: trust the user even if the controller does not answer the Skydimo handshake.
                Connected(new SerialFrameTransport(port), cfg, new DeviceStatus(DeviceState.Connected, name, cfg.Protocol == DeviceProtocol.Skydimo ? "Skydimo?" : null, null, cfg.LedCount));
                return;
            }
            port.Dispose();
        }

        var status = busyPort != null ? new DeviceStatus(DeviceState.PortBusy, busyPort) : new DeviceStatus(DeviceState.NotFound);
        if (status != Status)
        {
            Log.Info("Device: " + status.Describe());
            SetStatus(status);
        }
    }

    private void Connected(IFrameTransport port, DeviceConfig cfg, DeviceStatus status)
    {
        lock (_portLock)
        {
            if (_stop || _suspended) { port.Dispose(); return; }
            _port = port;
            _connectedKey = ConfigKey(cfg);
            _connectedProtocol = cfg.Protocol;
            _connectedChannelOrder = cfg.ChannelOrder;
            _connectedBaud = cfg.BaudRate;
            _connectedOpcChannel = cfg.OpcChannel;
        }
        _lastWriteTicks = 0;
        _nextWriteAt = 0;
        Interlocked.Increment(ref _connections);
        Log.Info($"Device connected: {status.Model} S/N {status.Serial} on {status.Port}, {status.LedCount} LEDs");
        if (!string.Equals(cfg.LastPort, status.Port, StringComparison.OrdinalIgnoreCase)) cfg.LastPort = status.Port;
        SetStatus(status);
        lock (_frameLock) _pendingDirty = true;
    }

    private (string Model, string? Serial)? TryHandshake(NativeSerialPort port)
    {
        try
        {
            port.Write(Handshake);
            var buf = new byte[128];
            int len = 0;
            var sw = Stopwatch.StartNew();
            long lastData = 0;
            while (!_stop && !_suspended && sw.ElapsedMilliseconds < 800 && len < buf.Length)
            {
                int n = port.Read(buf.AsSpan(len));
                if (n > 0)
                {
                    len += n;
                    lastData = sw.ElapsedMilliseconds;
                }
                else if (len >= 2 && buf[len - 2] == '\r' && buf[len - 1] == '\n' && sw.ElapsedMilliseconds - lastData > 40)
                {
                    break;
                }
                Thread.Sleep(10);
            }
            return ParseHello(buf.AsSpan(0, len));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reply looks like "SK0127," + 7 raw serial bytes + "\r\n" (serial bytes may contain any value).</summary>
    internal static (string Model, string? Serial)? ParseHello(ReadOnlySpan<byte> data)
    {
        int start = data.IndexOf("SK"u8);
        if (start < 0) return null;
        data = data[start..];
        int comma = data.IndexOf((byte)',');
        if (comma < 3 || comma > 12) return null;
        var model = Encoding.ASCII.GetString(data[..comma]);
        var rest = data[(comma + 1)..];
        if (rest.Length >= 2 && rest[^2] == '\r' && rest[^1] == '\n') rest = rest[..^2];
        string? serial = rest.Length > 0 ? Convert.ToHexString(rest) : null;
        return (model, serial);
    }

    private static string ConfigKey(DeviceConfig cfg) => $"{cfg.Port}|{cfg.Protocol}|{cfg.BaudRate}|{cfg.LedCount}|{cfg.Host}|{cfg.UdpPort}|{cfg.ChannelOrder}|{cfg.OpcChannel}";

    private void Disconnect(string reason, DeviceState state)
    {
        lock (_portLock)
        {
            if (_port != null) Log.Info($"Device disconnected ({reason})");
            DisconnectLocked(state);
        }
    }

    private void DisconnectLocked(DeviceState state)
    {
        _port?.Dispose();
        _port = null;
        _connectedKey = "";
        _packetLength = 0;
        SetStatus(new DeviceStatus(state));
    }

    private void SetStatus(DeviceStatus status)
    {
        if (status == Status) return;
        Status = status;
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            Log.Error("StatusChanged handler failed", ex);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeCalled, 1) != 0) return;
        _stop = true;
        _wake.Set();
        if (_thread.IsAlive && !_thread.Join(2000)) { Log.Warn("Device thread still stopping; retaining its wake handle"); return; }
        _pacer.Dispose();
        _wake.Dispose();
    }
}
