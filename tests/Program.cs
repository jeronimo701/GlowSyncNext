using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using GlowSync.Capture;
using GlowSync.Config;
using GlowSync.Core;
using GlowSync.Device;

// A bare test command must never write the installed user's configuration.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GLOWSYNC_NEXT_DATA_DIR")))
    Environment.SetEnvironmentVariable("GLOWSYNC_NEXT_DATA_DIR", Path.Combine(Path.GetTempPath(), "GlowSyncNext-tests-" + Guid.NewGuid().ToString("N")));

if (args.Contains("--ui-smoke")) return UiSmoke.Run();
if (args.Contains("--media-probe")) return MediaProbe.Run();

int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    if (!ok) failures++;
}

void Skip(string name, string why) => Console.WriteLine($"SKIP  {name}  — {why}");

// 1. Layout generator reproduces Skydimo's own maps (only if Skydimo is installed here).
string cfgDir = @"C:\Program Files (x86)\Skydimo\controler_config";
bool hasSkydimoFiles = Directory.Exists(cfgDir);
foreach (var (model, l, t, r, b) in new[] { ("SK0127", 17, 31, 17, 0), ("SK0L27", 17, 31, 17, 31), ("SK0201", 20, 20, 20, 0), ("SK0134", 15, 41, 15, 41) })
{
    if (!hasSkydimoFiles)
    {
        Skip($"layout {model}", "Skydimo не установлен");
        continue;
    }
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(cfgDir, model + ".json")));
    var map = doc.RootElement.GetProperty("ledMap").EnumerateArray().OrderBy(e => e.GetProperty("id").GetInt32())
        .Select(e => (e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32())).ToList();
    int sw = doc.RootElement.GetProperty("screenWidth").GetInt32(), sh = doc.RootElement.GetProperty("screenHeight").GetInt32();
    var (w, h, leds) = LayoutBuilder.Build(l, t, r, b, StartCorner.BottomLeft, true, 0);
    var mine = leds.Select(p => (p.X, p.Y)).ToList();
    bool same = mine.SequenceEqual(map);
    bool gridOk = w == sw && h == sh;
    int firstDiff = Enumerable.Range(0, Math.Min(mine.Count, map.Count)).FirstOrDefault(i => mine[i] != map[i], -1);
    Check($"layout {model}", same && gridOk,
        $"n={mine.Count}/{map.Count} grid={w}x{h} vs {sw}x{sh}" + (same ? "" : $" firstDiff={firstDiff}: {mine.ElementAtOrDefault(firstDiff)} vs {map.ElementAtOrDefault(firstDiff)}"));
}

// Counter-clockwise from bottom-left walks the bottom edge first (left to right).
var ccw = LayoutBuilder.Build(17, 31, 17, 31, StartCorner.BottomLeft, false, 0).Leds;
Check("layout ccw starts bottom-left going right", ccw[0].X == 0 && ccw[0].Y == 16 && ccw[1].X == 1 && ccw[1].Y == 16);
var offset = LayoutBuilder.Build(17, 31, 17, 0, StartCorner.BottomLeft, true, 5).Leds;
Check("layout offset rotates", offset[0].Y == 11 && offset[^1].Y == 12);

// 2. Handshake parsing, shaped like a real controller reply: "SK0127," + 7 raw serial bytes + CRLF.
//    The serial bytes deliberately contain 0x0D and 0x0A, which a naive line-based parser would cut.
var reply = Convert.FromHexString("534b303132372c010d0a040506070d0a");
var hello = DeviceManager.ParseHello(reply);
Check("handshake parse", hello?.Model == "SK0127" && hello?.Serial == "010D0A04050607", $"{hello?.Model} {hello?.Serial}");
Check("handshake garbage", DeviceManager.ParseHello(new byte[] { 1, 2, 3 }) == null);

// 3. Qt hotkeys from global.json.
Check("qt key Up", SkydimoImporter.QtKeyToVirtualKey(16777235) == 0x26);
Check("qt key F10", SkydimoImporter.QtKeyToVirtualKey(16777273) == 0x79);

// 4. Importing an existing Skydimo installation, if there is one on this machine.
var cfg = new AppConfig();
if (SkydimoImporter.IsAvailable)
{
    var report = SkydimoImporter.Import(cfg);
    cfg.Normalize();
    foreach (var line in report) Console.WriteLine("      import: " + line);
    Check("import brightness", cfg.Brightness is >= 0 and <= 100);
    Check("import device", cfg.Device.Model is { Length: > 0 } && cfg.Device.LedCount > 0, $"{cfg.Device.Model} {cfg.Device.LedCount} LED");
    Check("import leds", cfg.Layout.Leds.Count > 0 && cfg.Layout.GridWidth > 1 && cfg.Layout.GridHeight > 1,
        $"{cfg.Layout.Leds.Count} шт., {cfg.Layout.GridWidth}x{cfg.Layout.GridHeight}");
    Check("import fps", cfg.Screen.Fps is >= 10 and <= 60, $"{cfg.Screen.Fps}");
    var toggle = cfg.Hotkeys.First(h => h.Action == HotkeyAction.Toggle);
    Check("import hotkey toggle", toggle.Key != 0 && toggle.Modifiers != 0, $"{toggle.Modifiers}+{toggle.Key}");
}
else
{
    Skip("import", "настроек Skydimo нет — импортировать нечего");
    cfg.Normalize();
}

// 5. Sampler vs Skydimo's real output for the gradient test pattern (R = x, G = y, B = distance to edge),
//    reduced to 320x180 like the GPU mip level, then gamma 2.2 as the engine does.
int W = 320, H = 180;
var px = new byte[W * H * 4];
for (int y = 0; y < H; y++)
{
    for (int x = 0; x < W; x++)
    {
        double sx = (x + 0.5) * 8, sy = (y + 0.5) * 8;
        double dist = Math.Min(Math.Min(sx, 2560 - sx), Math.Min(sy, 1440 - sy));
        int i = (y * W + x) * 4;
        px[i + 2] = (byte)Math.Round(sx / 2560 * 255);
        px[i + 1] = (byte)Math.Round(sy / 1440 * 255);
        px[i] = (byte)Math.Round(Math.Clamp(dist / 720, 0, 1) * 255);
    }
}
var reference = LayoutBuilder.Build(17, 31, 17, 0, StartCorner.BottomLeft, true, 0).Leds; // SK0127
var sampler = new ScreenSampler();
sampler.Ingest(Fake(px, W, H), false);
var colors = new Vector3[65];
sampler.Sample(reference, 31, 17, 1.0, Vortice.DXGI.ModeRotation.Identity, colors);
int[,] skydimo =
{
    {0,239,0},{0,207,0},{0,178,0},{0,154,0},{0,128,0},{0,107,0},{0,87,0},{0,71,0},{0,55,0},{0,41,0},{0,30,0},{0,21,0},{0,13,0},{0,7,0},{0,3,0},{0,1,0},{0,0,0},
    {0,0,0},{0,0,0},{0,0,0},{2,0,0},{3,0,0},{5,0,0},{8,0,0},{11,0,0},{14,0,0},{18,0,0},{23,0,0},{29,0,0},{34,0,0},{40,0,0},{47,0,0},{55,0,0},{63,0,0},{72,0,0},{81,0,0},{91,0,0},{103,0,0},{112,0,0},{125,0,0},{138,0,0},{151,0,0},{164,0,0},{180,0,0},{195,0,0},{211,0,0},{227,0,0},{246,0,0},
    {246,0,0},{246,1,0},{246,3,0},{246,7,0},{246,13,0},{246,21,0},{246,30,0},{246,41,0},{246,55,0},{246,71,0},{246,87,0},{246,107,0},{246,128,0},{246,154,0},{246,178,0},{246,207,0},{246,239,0},
};
int maxErr = 0, worst = -1;
for (int i = 0; i < 65; i++)
{
    int[] mine = { Gamma(colors[i].X), Gamma(colors[i].Y), Gamma(colors[i].Z) };
    for (int k = 0; k < 3; k++)
    {
        int e = Math.Abs(mine[k] - skydimo[i, k]);
        if (e > maxErr)
        {
            maxErr = e;
            worst = i;
        }
    }
}
Check("sampler matches Skydimo output", maxErr <= 4, $"max channel error {maxErr} at LED {worst + 1}");

// 6. Black bars: a 2.39:1 movie letterboxed on 16:9 is detected symmetrically.
var lb = new byte[W * H * 4];
int bar = (int)Math.Round((H - W / 2.39) / 2);
for (int y = bar; y < H - bar; y++)
{
    for (int x = 0; x < W; x++)
    {
        int i = (y * W + x) * 4;
        lb[i] = 200;
        lb[i + 1] = 100;
        lb[i + 2] = 50;
    }
}
var bars = new ScreenSampler();
var lbCap = Fake(lb, W, H);
for (int f = 0; f < 40; f++) bars.Ingest(lbCap, true);
Check("black bars detected", Math.Abs(bars.Bars.T - bar) <= 1 && bars.Bars.T == bars.Bars.B && bars.Bars.L == 0, $"{bars.Bars} expected {bar}");

// 7. Config JSON round-trip keeps everything.
var clone = ConfigStore.Clone(cfg);
Check("config round-trip", clone.Layout.Leds.Count == cfg.Layout.Leds.Count && clone.Hotkeys.Count == 5 && clone.Device.Serial == cfg.Device.Serial && clone.Screen.Gamma == cfg.Screen.Gamma);

// 8. Night schedule across midnight.
var night = new NightConfig { Enabled = true, From = "23:00", To = "07:00" };
Check("night window", night.IsActive(new DateTime(2026, 1, 1, 23, 30, 0)) && night.IsActive(new DateTime(2026, 1, 1, 6, 59, 0)) && !night.IsActive(new DateTime(2026, 1, 1, 12, 0, 0)));

// 9. Real desktop capture through the whole GPU path (duplication, mips, staging map) and sampling.
if (args.Contains("--no-desktop")) Skip("desktop capture", "explicit headless test mode");
else using (var capture = new DesktopCapture())
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    CaptureResult last = CaptureResult.Unavailable;
    int frames = 0;
    while (sw.ElapsedMilliseconds < 3000)
    {
        last = capture.Update("");
        if (last == CaptureResult.NewFrame) frames++;
        if (frames >= 3) break;
        System.Threading.Thread.Sleep(33);
    }
    bool gotImage = capture.HasFrame && capture.Width > 0 && capture.Pixels.Any(b => b != 0);
    Check("desktop capture", gotImage, $"frames={frames} last={last} reduced={capture.Width}x{capture.Height} monitor={capture.CurrentMonitor}");
    if (gotImage)
    {
        var live = new ScreenSampler();
        live.Ingest(capture, true);
        var liveColors = new Vector3[65];
        live.Sample(reference, 31, 17, 1.0, capture.Rotation, liveColors);
        Console.WriteLine("      live LED 1/33/65: " + string.Join(" | ", new[] { 0, 32, 64 }.Select(i => ColorMath.ToHex(liveColors[i]))));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int n = 0;
        while (timer.ElapsedMilliseconds < 1000)
        {
            if (capture.Update("") == CaptureResult.NewFrame) { live.Ingest(capture, true); n++; }
            live.Sample(reference, 31, 17, 1.0, capture.Rotation, liveColors);
        }
        Console.WriteLine($"      busy-loop: {n} new frames/s (screen changes only)");
    }
}

// Real format negotiation with optional HDR enabled; SDR must remain capturable.
if (!args.Contains("--no-desktop"))
{
    using var hdrCapture = new DesktopCapture();
    hdrCapture.ConfigureHdr(true, 203, 1000);
    var hdrWatch = System.Diagnostics.Stopwatch.StartNew();
    while (!hdrCapture.HasFrame && hdrWatch.ElapsedMilliseconds < 2500) { hdrCapture.Update(""); System.Threading.Thread.Sleep(20); }
    Check("optional HDR format negotiation captures desktop", hdrCapture.HasFrame && hdrCapture.PixelFormat is "B8G8R8A8_UNorm" or "R16G16B16A16_Float", $"HDR output={hdrCapture.IsHdrOutput}; actual format={hdrCapture.PixelFormat}");
}

// 10. Monitor power tracker: a display that never answers DDC/CI must never blank the strip,
//     a single dropped reply must not either, and a real "off" must be caught.
{
    var quiet = new GlowSync.Platform.MonitorPowerWatcher.Tracker();
    for (int i = 0; i < 10; i++) quiet.Report(null);
    Check("ddc: no answer ever = unknown", quiet.IsOff == null && !quiet.EverAnswered);

    var t = new GlowSync.Platform.MonitorPowerWatcher.Tracker();
    t.Report(1);
    bool on = t.IsOff == false;
    t.Report(null);
    t.Report(null);
    bool stillOn = t.IsOff == false;
    t.Report(null);
    bool offAfterSilence = t.IsOff == true;
    t.Report(1);
    bool backOn = t.IsOff == false;
    t.Report(4);
    bool offReported = t.IsOff == true;
    t.Report(2);
    bool standbyIsOff = t.IsOff == true;
    Check("ddc: on/off state machine", on && stillOn && offAfterSilence && backOn && offReported && standbyIsOff,
        $"on={on} stillOn={stillOn} offAfterSilence={offAfterSilence} backOn={backOn} offReported={offReported} standby={standbyIsOff}");
}

// Next regressions: a hand-edited side count must rebuild the old persisted coordinate list.
{
    var stale = new LayoutConfig { Left = 20, Top = 41, Right = 20, Bottom = 0 };
    stale.Leds = LayoutBuilder.Build(17, 31, 17, 0, StartCorner.BottomLeft, true, 0).Leds;
    stale.Normalize();
    Check("Next: stale 65 map repaired to 81", stale.Leds.Count == 81 && stale.GridWidth == 41 && stale.GridHeight == 20);
    using var store = new ConfigStore();
    store.Config.Layout.Left = 20; store.Config.Layout.Top = 41; store.Config.Layout.Right = 20;
    Check("Next: layout rebuilt with no window", store.Config.Layout.Leds.Count == 81);
    int revision = store.Revision;
    var rule = new ProfileRule { Process = "game.exe" };
    store.Config.Profiles.Rules.Add(rule);
    rule.Process = "other.exe";
    Check("Next: new rules tracked", store.Revision >= revision + 2);
    store.Config.Profiles.Rules.Remove(rule);
    revision = store.Revision;
    rule.Process = "detached.exe";
    Check("Next: deleted rules detached", store.Revision == revision);
    store.Config.Profiles.Game.Screen.SmoothingMs = 37;
    var nextClone = ConfigStore.Clone(store.Config);
    nextClone.Normalize();
    Check("Next: profile persistence round trip", nextClone.Profiles.Game.Screen.SmoothingMs == 37 && nextClone.Layout.Leds.Count == 81);
}
{
    var p = new ProfilesConfig();
    void Classify(string name, string process, string title, ProfileKind expected, string path = "", string? url = null) =>
        Check("Next: " + name, ProfileClassifier.Classify(new(process, title, path, url), p).Profile == expected);
    Classify("ordinary browser", "chrome", "Search", ProfileKind.Desktop);
    Classify("YouTube URL no implicit match", "msedge", "Video", ProfileKind.Desktop, url: "https://www.youtube.com/watch?v=123");
    Classify("YouTube title no implicit match", "chrome", "Clip - YouTube - Google Chrome", ProfileKind.Desktop);
    Classify("Firefox no implicit match", "firefox", "Clip - YouTube — Mozilla Firefox", ProfileKind.Desktop);
    Classify("Music URL no implicit match", "chrome", "Song - YouTube Music - Google Chrome", ProfileKind.Desktop, url: "https://music.youtube.com/watch?v=123");
    Classify("short URL no implicit match", "brave", "Video", ProfileKind.Desktop, url: "youtu.be/123");
    Classify("fake YouTube domain", "chrome", "Example", ProfileKind.Desktop, url: "https://youtube.com.evil.example/watch");
    Classify("YouTube text on ordinary page", "chrome", "How to use YouTube - Search - Google Chrome", ProfileKind.Desktop);
    Classify("player no implicit match", "mpc-hc64.exe", "Movie", ProfileKind.Desktop);
    Classify("music player no implicit match", "foobar2000", "Track", ProfileKind.Desktop);
    Classify("Steam path no implicit match", "mygame", "Game", ProfileKind.Desktop, @"D:\SteamLibrary\steamapps\common\MyGame\game.exe");
    Classify("Epic path no implicit match", "mygame", "Game", ProfileKind.Desktop, @"D:\Epic Games\MyGame\game.exe");
    Classify("unknown app", "notepad", "Document", ProfileKind.Desktop);
    p.Rules.Add(new() { Process = "customgame.exe", Profile = ProfileKind.Game });
    Classify("custom game rule", "customgame", "Game", ProfileKind.Game);
    p.Rules.Insert(0, new() { Process = "chrome.exe", TitleContains = "YouTube", Profile = ProfileKind.Desktop });
    Classify("rule overrides builtin", "chrome", "Clip - YouTube", ProfileKind.Desktop, url: "https://youtube.com");
    p.Rules[0].Enabled = false;
    Classify("disabled rule has no hidden fallback", "chrome", "Clip - YouTube", ProfileKind.Desktop);
    p.DetectBrowserVideo = false;
    Classify("browser detection disabled", "chrome", "Clip - YouTube", ProfileKind.Desktop);
    p.DetectGamesByPath = false;
    Classify("game detection disabled", "mygame", "Game", ProfileKind.Desktop, @"D:\SteamLibrary\steamapps\common\Game\game.exe");
    var debounce = new ProfileDebouncer();
    Check("Next: debounce holds initial profile", !debounce.Update(ProfileKind.Game, 0, 800));
    Check("Next: debounce accepts stable profile", debounce.Update(ProfileKind.Game, 800, 800) && debounce.Current == ProfileKind.Game);
    debounce.Update(ProfileKind.Video, 900, 800);
    Check("Next: short Alt+Tab ignored", !debounce.Update(ProfileKind.Game, 1000, 800) && debounce.Current == ProfileKind.Game);
    debounce.Update(ProfileKind.Video, 2000, 800);
    Check("Next: stable player takes over", debounce.Update(ProfileKind.Video, 2800, 800) && debounce.Current == ProfileKind.Video);
}
{
    byte[] packet = Array.Empty<byte>();
    int size = FrameEncoding.Build(DeviceProtocol.Skydimo, ChannelOrder.RGB, 81, new byte[] { 10, 20, 30 }, ref packet);
    Check("Next: SKA127 frame header", size == 249 && packet.AsSpan(0, 6).SequenceEqual(new byte[] { 65, 100, 97, 0, 0, 81 }));
    Check("Next: missing LEDs padded black", packet.AsSpan(9, size - 9).ToArray().All(x => x == 0));
    size = FrameEncoding.Build(DeviceProtocol.Adalight, ChannelOrder.RGB, 300, new byte[] { 1, 2, 3 }, ref packet);
    Check("Next: Adalight big-endian count/checksum", size == 906 && packet[3] == 1 && packet[4] == 43 && packet[5] == (1 ^ 43 ^ 0x55));
    foreach (var order in Enum.GetValues<ChannelOrder>())
    {
        size = FrameEncoding.Build(DeviceProtocol.Adalight, order, 1, new byte[] { 10, 20, 30, 99, 99, 99 }, ref packet);
        var expected = order.ToString().Select(c => (byte)(c == 'R' ? 10 : c == 'G' ? 20 : 30)).ToArray();
        Check("Next: channel order " + order, size == 9 && packet.AsSpan(6, 3).SequenceEqual(expected));
    }
    double limit = FrameEncoding.LimitFps(60, DeviceProtocol.Skydimo, 115200, 81);
    Check("Next: UART frame rate budget", limit > 41 && limit < 42);
    Check("Next: DDP has no UART limit", FrameEncoding.LimitFps(60, DeviceProtocol.WledDdp, 115200, 1000) == 60);
    // Exercise the actual UDP transport against a local receiver, including fragmentation and PUSH.
    using var listener = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
    listener.Client.ReceiveTimeout = 1000;
    int port = ((System.Net.IPEndPoint)listener.Client.LocalEndPoint!).Port;
    using var transport = new DdpFrameTransport("127.0.0.1", port);
    var rgb = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
    transport.Write(rgb);
    var rebuilt = new byte[3000];
    bool headers = true;
    for (int chunk = 0; chunk < 3; chunk++)
    {
        var remote = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
        var received = listener.Receive(ref remote);
        int offsetBytes = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(received.AsSpan(4));
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(received.AsSpan(8));
        headers &= received[0] == (chunk == 2 ? 0x41 : 0x40) && received[1] == 1 && received[2] == 0x0b && received[3] == 1 && count + 10 == received.Length && offsetBytes == chunk * 1440;
        received.AsSpan(10).CopyTo(rebuilt.AsSpan(offsetBytes));
    }
    Check("Next: DDP network fragmentation and push", headers && rebuilt.SequenceEqual(rgb));
}

ExtendedTests.Run((name, ok, detail) => Check(name, ok, detail));
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures;

static int Gamma(float v) => (int)Math.Round(Math.Pow(Math.Clamp(v, 0, 1), 2.2) * 255);

static DesktopCapture Fake(byte[] px, int w, int h)
{
    var capture = new DesktopCapture();
    typeof(DesktopCapture).GetProperty("Pixels")!.SetValue(capture, px);
    typeof(DesktopCapture).GetProperty("Width")!.SetValue(capture, w);
    typeof(DesktopCapture).GetProperty("Height")!.SetValue(capture, h);
    return capture;
}
