using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using GlowSync.Capture;
using GlowSync.Config;
using GlowSync.Core;
using GlowSync.Device;

internal static class ExtendedTests
{
    private sealed class DelayedTransport : IFrameTransport
    {
        public string Name => "Simulated blocking transport";
        public System.Collections.Generic.List<long> Starts { get; } = new();
        public void Write(ReadOnlySpan<byte> packet) { Starts.Add(System.Diagnostics.Stopwatch.GetTimestamp()); Thread.Sleep(8); }
        public void Dispose() { }
    }
    public static void Run(Action<string, bool, string> check)
    {
        void Test(string name, bool ok) => check("0.3: " + name, ok, "");
        foreach (bool changing in new[] { false, true })
        {
            var breathing = new Effects();
            var settings = new EffectsConfig { BreathingSpeed = 100, BreathingRainbow = changing, BreathingColor = "#3366CC" };
            var pixels = new Vector3[2];
            breathing.Advance(0.5, settings); breathing.Breathing(pixels, settings);
            var first = pixels[0];
            breathing.Advance(1, settings); breathing.Breathing(pixels, settings);
            Test(changing ? "breathing changes hue between breaths when checked" : "breathing retains selected color when unchecked",
                changing ? Vector3.Distance(first, pixels[0]) > 0.1f : Vector3.Distance(first, pixels[0]) < 0.00001f);
            Test("breathing fills the whole rendered strip", pixels[0] == pixels[1]);
        }
        var engineAssembly = typeof(Engine).Assembly;
        Test("engine is a separate assembly without WPF references", engineAssembly.GetName().Name == "GlowSync.Engine" && engineAssembly.GetReferencedAssemblies().All(a => a.Name is not ("PresentationFramework" or "PresentationCore" or "WindowsBase")));
        var profiles = new ProfilesConfig();
        var playing = new PlaybackEvidence("chrome.exe", "Example song", PlaybackState.Playing, MediaKind.Music);
        var context = new ForegroundContext("chrome", "Example song - YouTube - Google Chrome", BrowserUrl: "https://youtube.com/watch?v=test");
        Test("matching active browser session", PlaybackMatcher.Match(context, new[] { playing }) == playing);
        Test("background browser tab ignored", PlaybackMatcher.Match(context with { Title = "Other video - YouTube - Google Chrome" }, new[] { playing }) == null);
        Test("search title cannot impersonate media title", PlaybackMatcher.Match(context with { Title = "How to play Example song - Search" }, new[] { playing }) == null);
        Test("ambiguous sessions ignored", PlaybackMatcher.Match(context, new[] { playing, playing }) == null);
        Test("other application ignored", PlaybackMatcher.Match(context, new[] { playing with { Application = "spotify.exe" } }) == null);
        var withMedia = context with { Playback = playing };
        Test("media metadata cannot select a profile", ProfileClassifier.Classify(withMedia, profiles).Profile == ProfileKind.Desktop);
        profiles.Normalize();
        Test("examples populated and disabled by default", profiles.Rules.Count >= 15 && profiles.Rules.All(r => !r.Enabled));
        var youtube = profiles.Rules.First(r => r.Process == "vivaldi.exe" && r.TitleContains == "YouTube");
        youtube.Enabled = true;
        foreach (string title in new[] { "Clip - YouTube - Vivaldi", "Clip – YouTube — Vivaldi", "YouTube — Vivaldi", "Clip — YouTube – Vivaldi", "Clip - YouTube" })
            Test("Vivaldi explicit title rule: " + title, ProfileClassifier.Classify(new("vivaldi.exe", title), profiles).Profile == ProfileKind.Video);
        Test("Vivaldi ordinary tab stays desktop", ProfileClassifier.Classify(new("vivaldi", "Example page - Vivaldi"), profiles).Profile == ProfileKind.Desktop);
        profiles.Rules.First(r => r.Process == "vivaldi.exe" && r.TitleContains == "YouTube Music").Enabled = true;
        Test("Vivaldi music site", ProfileClassifier.Classify(new("vivaldi", "Song – YouTube Music — Vivaldi"), profiles).Profile == ProfileKind.Music);
        youtube.Enabled = false;
        Test("unchecking YouTube really disables it", ProfileClassifier.Classify(new("vivaldi", "Clip - YouTube"), profiles).Profile == ProfileKind.Desktop);
        int exampleCount = profiles.Rules.Count;
        profiles.AddRuleExamples(); profiles.Normalize();
        Test("examples not duplicated or re-enabled", profiles.Rules.Count == exampleCount && !youtube.Enabled);
        profiles.Rules.Clear(); profiles.Normalize();
        Test("deleted examples do not reappear", profiles.Rules.Count == 0);
        var retired = new ProfilesConfig { DetectBrowserVideo = false, DetectGamesByPath = false, DetectPlayback = false, DesktopWhenPaused = true };
        retired.Normalize();
        Test("old detection switches no longer drive classification", ProfileClassifier.Classify(withMedia, retired).Profile == ProfileKind.Desktop);
        Test("81 LED UART 115200 cannot deliver 60 FPS", Math.Abs(FrameEncoding.LimitFps(60, DeviceProtocol.Skydimo, 115200, 81) - 41.63855) < 0.001);
        Test("81 LED UART 230400 has bandwidth for 60 FPS", FrameEncoding.LimitFps(60, DeviceProtocol.Skydimo, 230400, 81) == 60);
        var debounce = new ProfileDebouncer(); debounce.Update(ProfileKind.Game, 0, 200); debounce.Update(ProfileKind.Game, 200, 200); debounce.Reset();
        Test("return to auto does not reuse stale candidate", debounce.Current == null && !debounce.Update(ProfileKind.Video, 1000, 200));
        profiles.DesktopWhenPaused = false;
        Test("pause cannot trigger hidden selection", ProfileClassifier.Classify(withMedia with { Playback = playing with { State = PlaybackState.Paused } }, profiles).Profile == ProfileKind.Desktop);
        profiles.DetectPlayback = false;
        Test("SMTC disabled does not enable site heuristics", ProfileClassifier.Classify(withMedia, profiles).Profile == ProfileKind.Desktop);
        profiles.DetectPlayback = true;
        profiles.Rules.Add(new() { Process = "chrome.exe", Profile = ProfileKind.Game });
        Test("explicit rule wins over media", ProfileClassifier.Classify(withMedia, profiles).Profile == ProfileKind.Game);
        profiles.Rules.Clear();
        using (var pacingStore = new ConfigStore())
        using (var pacingManager = new DeviceManager(pacingStore))
        {
            pacingStore.Config.Device.Protocol = DeviceProtocol.Adalight;
            pacingStore.Config.Device.BaudRate = 230400;
            pacingStore.Config.Device.LedCount = 81;
            using var transport = new DelayedTransport();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(DeviceManager).GetMethod("Connected", flags)!.Invoke(pacingManager, new object[] { transport, pacingStore.Config.Device, new DeviceStatus(DeviceState.Connected, "TEST", LedCount: 81) });
            pacingManager.RequestRescan(); // A connected rescan hint must not bypass pacing.
            var step = typeof(DeviceManager).GetMethod("Step", flags)!;
            for (int i = 0; i < 24; i++) { pacingManager.Submit(new byte[243]); step.Invoke(pacingManager, null); }
            double averageMs = (transport.Starts.Last() - transport.Starts.First()) * 1000.0 / System.Diagnostics.Stopwatch.Frequency / (transport.Starts.Count - 1);
            Test("blocking write is included in 60 FPS period (not added twice)", averageMs is >= 16 and < 23);
            Console.WriteLine($"INFO  simulated 8ms write, 60 FPS target: start interval {averageMs:0.00} ms ({1000 / averageMs:0.0} FPS), not a hardware test");
        }
        Test("game directory and soundtrack ignored without rules", ProfileClassifier.Classify(new("game", "Game", @"D:\Steam\steamapps\common\Game\game.exe", Playback: playing), profiles).Profile == ProfileKind.Desktop);
        Test("launcher is not the game", ProfileClassifier.Classify(new("EpicGamesLauncher", "Epic Games", @"C:\Epic Games\Launcher\EpicGamesLauncher.exe"), profiles).Profile == ProfileKind.Desktop);
        Test("process names preserve dots", ProfileClassifier.ProcessName("Microsoft.Media.Player.exe") == "Microsoft.Media.Player");
        var uwp = new PlaybackEvidence("Microsoft.ZuneMusic_test!App", "Track", PlaybackState.Playing, MediaKind.Music);
        Test("UWP player app identity matched", PlaybackMatcher.Match(new("Music.UI", "Track"), new[] { uwp }) == uwp);

        var metric = new FrameMetric();
        metric.Observe(1, 0); metric.Observe(2, 10); metric.Observe(30, 20);
        var sample = metric.Snapshot(999);
        Test("telemetry count/mean/p95", sample.Total == 3 && sample.LastSecond == 3 && sample.MeanMs == 11 && sample.P95Ms == 30);
        Test("telemetry rate expires while idle", metric.Snapshot(1020).LastSecond == 0 && metric.Snapshot(1020).Total == 3);
        metric.Observe(double.NaN, 2000);
        Test("telemetry nonfinite input sanitized", metric.Snapshot(2000).LastMs == 0);

        Test("HDR black preserved", HdrToneMapper.Map(Vector3.Zero, 203, 1000) == Vector3.Zero);
        var gray = HdrToneMapper.Map(new Vector3(0.5f), 80, 80);
        Test("HDR SDR transfer reference", Math.Abs(gray.X - 0.7353569) < 0.00001 && gray.X == gray.Y && gray.Y == gray.Z);
        float previous = -1; bool monotonic = true;
        for (int i = 0; i <= 100; i++)
        {
            var result = HdrToneMapper.Map(new Vector3(i * 0.2f), 203, 1000);
            monotonic &= result.X >= previous && result.X is >= 0 and <= 1;
            previous = result.X;
        }
        Test("HDR luminance ramp monotonic/bounded", monotonic);
        var finite = HdrToneMapper.Map(new(float.NaN, float.PositiveInfinity, -10), 203, 1000);
        Test("HDR negative/nonfinite channels safe", float.IsFinite(finite.X) && float.IsFinite(finite.Y) && float.IsFinite(finite.Z) && finite.Z == 0);
        var hdrBytes = new byte[4];
        HdrToneMapper.ConvertRow(new Half[] { (Half)1, (Half)0, (Half)0, (Half)1 }, hdrBytes, 80, 80);
        Test("HDR FP16 RGBA to BGRA byte order", hdrBytes.SequenceEqual(new byte[] { 0, 0, 255, 255 }));

        var custom = new LayoutConfig { UseCustomMap = true, GridWidth = 10, GridHeight = 10, Leds = new() { new(2, 3), new(7, 8) } };
        custom.Normalize();
        Test("custom coordinates preserved", custom.UseCustomMap && custom.Leds.Count == 2 && custom.Leds[0].X == 2);
        custom.Leds[0].X = -1; custom.Normalize();
        Test("invalid custom coordinates fall back", !custom.UseCustomMap && custom.Leds.Count == 65);
        var rgb = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        Test("output route selects segment", DeviceHub.Slice(rgb, 20, 41).SequenceEqual(rgb.AsSpan(60, 123)));
        Test("route outside map is empty for zero padding", DeviceHub.Slice(rgb.AsSpan(0, 30), 20, 41).IsEmpty);
        using (var store = new ConfigStore())
        {
            store.Config.Outputs.Clear();
            var output = new DeviceConfig { Protocol = DeviceProtocol.Tpm2Net, UdpPort = 65506 };
            int revision = store.Revision; store.Config.Outputs.Add(output); output.StartLed = 20;
            Test("secondary output changes tracked", store.Revision >= revision + 2);
            var clone = ConfigStore.Clone(store.Config); clone.Normalize();
            Test("secondary output round trip", clone.Outputs.Count == 1 && clone.Outputs[0].StartLed == 20 && clone.Outputs[0].Protocol == DeviceProtocol.Tpm2Net);
            store.Config.Outputs.Remove(output); revision = store.Revision; output.StartLed = 1;
            Test("removed output detached", store.Revision == revision);
        }
        using (var legacy = new ConfigStore()) { legacy.Config.Version = 1; legacy.SaveNow(); }
        using (var upgraded = new ConfigStore())
        {
            string backup = GlowSync.Platform.Paths.ConfigFile + ".v1-before-v3.bak";
            Test("v1 configuration upgrade retains versioned backup", upgraded.Config.Version == 3 && System.IO.File.Exists(backup));
            using var old = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(backup));
            Test("versioned backup preserves original schema version", old.RootElement.GetProperty("version").GetInt32() == 1);
        }
        using (var renderStore = new ConfigStore())
        using (var renderHub = new DeviceHub(renderStore))
        using (var engine = new Engine(renderStore, renderHub))
        {
            renderStore.Config.Enabled = true; renderStore.Config.Mode = EffectMode.Static;
            renderStore.Config.Layout.Left = 17; renderStore.Config.Layout.Top = 31; renderStore.Config.Layout.Right = 17; renderStore.Config.Layout.Bottom = 0;
            var tick = typeof(Engine).GetMethod("Tick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            tick.Invoke(engine, new object[] { renderStore.Config, 0.25 });
            byte[] preview = Array.Empty<byte>(); int initial = engine.CopyPreview(ref preview);
            renderStore.Config.Layout.Left = renderStore.Config.Layout.Top = renderStore.Config.Layout.Right = 0;
            tick.Invoke(engine, new object[] { renderStore.Config, 0.25 });
            Test("empty layout clears previous rendered map", initial == 65 && engine.CopyPreview(ref preview) == 0);
        }

        try
        {
            using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.Client.ReceiveTimeout = 1500;
            using var sender = new DdpFrameTransport("127.0.0.1", ((IPEndPoint)receiver.Client.LocalEndPoint!).Port, tpm: true);
            sender.Write(rgb);
            var rebuilt = new byte[4320]; bool valid = true;
            for (int part = 0; part < 3; part++)
            {
                var remote = new IPEndPoint(IPAddress.Any, 0); var bytes = receiver.Receive(ref remote);
                valid &= bytes.Length == 1447 && bytes[0] == 0x9c && bytes[1] == 0xda && bytes[2] == 5 && bytes[3] == 160 && bytes[4] == part + 1 && bytes[5] == 3 && bytes[^1] == 0x36;
                bytes.AsSpan(6, 1440).CopyTo(rebuilt.AsSpan(part * 1440));
            }
            Test("TPM2 network fragmentation/equal padding", valid && rebuilt.AsSpan(0, rgb.Length).SequenceEqual(rgb) && rebuilt.AsSpan(rgb.Length).ToArray().All(x => x == 0));
        }
        catch (Exception ex) { check("0.3: TPM2 loopback", false, ex.Message); }
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            try
            {
                using var sender = new OpcFrameTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
                using var accepted = listener.AcceptSocket(); accepted.ReceiveTimeout = 1500;
                byte[] packet = Array.Empty<byte>();
                int length = FrameEncoding.Build(DeviceProtocol.OpcTcp, ChannelOrder.RGB, 1000, rgb, ref packet, 7);
                sender.Write(packet.AsSpan(0, length));
                var received = new byte[length]; int position = 0;
                while (position < length) { int read = accepted.Receive(received.AsSpan(position)); if (read == 0) break; position += read; }
                Test("OPC TCP full frame header/payload", position == 3004 && received[0] == 7 && received[1] == 0 && received[2] == 11 && received[3] == 184 && received.AsSpan(4).SequenceEqual(rgb));
            }
            finally { listener.Stop(); }
        }
        catch (Exception ex) { check("0.3: OPC loopback", false, ex.Message); }

        try { TestRoutes(Test); }
        catch (Exception ex) { check("0.3: multi-output integration", false, ex.Message); }
    }

    private static void TestRoutes(Action<string, bool> check)
    {
        using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        first.Client.ReceiveTimeout = second.Client.ReceiveTimeout = 1500;
        using var store = new ConfigStore();
        store.Config.Outputs.Clear(); store.Config.Device.OutputEnabled = false;
        int Port(UdpClient socket) => ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        store.Config.Outputs.Add(new() { Protocol = DeviceProtocol.WledDdp, Host = "127.0.0.1", UdpPort = Port(first), LedCount = 2, StartLed = 0 });
        store.Config.Outputs.Add(new() { Protocol = DeviceProtocol.WledDdp, Host = "127.0.0.1", UdpPort = Port(second), LedCount = 2, StartLed = 2 });
        using var hub = new DeviceHub(store); hub.Start();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (hub.Snapshot().Skip(1).Any(r => r.Manager.Status.State != DeviceState.Connected) && watch.ElapsedMilliseconds < 1500) Thread.Sleep(10);
        byte[] rgb = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
        hub.Submit(rgb);
        bool Receive(UdpClient socket, ReadOnlySpan<byte> expected)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 1400)
            {
                var remote = new IPEndPoint(IPAddress.Any, 0); var packet = socket.Receive(ref remote);
                if (packet.AsSpan(10).SequenceEqual(expected)) return true;
            }
            return false;
        }
        check("independent live DDP outputs route correct segments", Receive(first, rgb.AsSpan(0, 6)) && Receive(second, rgb.AsSpan(6, 6)));
        store.Config.Outputs.Add(new() { Protocol = DeviceProtocol.WledDdp, Host = "127.0.0.1", UdpPort = Port(first) });
        check("duplicate endpoint refused", hub.Snapshot().Length == 3 && hub.RoutingWarning.Length > 0);
        check("network output not capped by disabled UART", hub.LimitFps(60) == 60);
    }
}
