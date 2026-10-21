using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GlowSync.Core;

namespace GlowSync.Platform;

/// <summary>Read-only foreground inspection. A stalled browser accessibility provider never blocks rendering or the UI.</summary>
internal sealed class ForegroundObserver : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEvent _stop = new(false);
    private Task<string?>? _addressTask;
    private (IntPtr Handle, string Title) _addressKey;
    private long _addressAt;
    private readonly MediaSessionObserver _media = new();
    public volatile bool InspectPlayback = true;
    public string MediaStatus => _media.Status;
    public volatile bool InspectBrowser = true;
    public event Action<ForegroundContext>? Observed;
    public ForegroundObserver() => _thread = new Thread(Run) { IsBackground = true, Name = "GlowSync.Foreground" };
    public void Start() => _thread.Start();

    private void Run()
    {
        while (!_stop.WaitOne(400))
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) continue;
                GetWindowThreadProcessId(window, out uint pid);
                if (pid == Environment.ProcessId) continue; // Keep profile while editing GlowSync.
                using var process = Process.GetProcessById((int)pid);
                if (process.ProcessName.Equals("glowsync", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Equals("glowsync.next", StringComparison.OrdinalIgnoreCase)) continue;
                var title = new StringBuilder(1024);
                GetWindowTextW(window, title, title.Capacity);
                string path = "";
                try { path = process.MainModule?.FileName ?? ""; } catch { }
                string? url = null;
                var key = (window, title.ToString());
                if (InspectBrowser && ProfileClassifier.IsBrowser(process.ProcessName))
                {
                    if (_addressTask?.IsCompleted == true && _addressKey == key && Environment.TickCount64 - _addressAt < 2500)
                        url = _addressTask.GetAwaiter().GetResult();
                    if ((_addressTask == null || _addressTask.IsCompleted) && (_addressKey != key || Environment.TickCount64 - _addressAt >= 2000))
                    {
                        _addressKey = key;
                        _addressAt = Environment.TickCount64;
                        _addressTask = Task.Run(() => ReadAddress(window));
                    }
                }
                var context = new ForegroundContext(process.ProcessName, title.ToString(), path, url);
                if (InspectPlayback) context = context with { Playback = _media.Get(context) };
                if (GetForegroundWindow() == window) Observed?.Invoke(context);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* Foreground process may exit at any time. */ }
        }
    }

    private static string? ReadAddress(IntPtr window)
    {
        try
        {
            var root = AutomationElement.FromHandle(window);
            var walker = TreeWalker.ControlViewWalker;
            var queue = new System.Collections.Generic.Queue<(AutomationElement Element, int Depth)>();
            queue.Enqueue((root, 0));
            var watch = Stopwatch.StartNew();
            int visited = 0;
            while (queue.Count > 0 && visited++ < 250 && watch.ElapsedMilliseconds < 250)
            {
                var (element, depth) = queue.Dequeue();
                if (element.Current.ControlType == ControlType.Edit)
                {
                    string id = element.Current.AutomationId ?? "";
                    string name = element.Current.Name ?? "";
                    bool address = id.Contains("urlbar", StringComparison.OrdinalIgnoreCase) ||
                        id.Contains("omnibox", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("address", StringComparison.OrdinalIgnoreCase) || name.Contains("адрес", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("search or enter", StringComparison.OrdinalIgnoreCase);
                    if (address && element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                        return ((ValuePattern)pattern).Current.Value;
                }
                // Never traverse page content or read form fields.
                if (depth >= 9 || element.Current.ControlType == ControlType.Document) continue;
                var child = walker.GetFirstChild(element);
                for (int i = 0; child != null && i < 50; i++, child = walker.GetNextSibling(child)) queue.Enqueue((child, depth + 1));
            }
        }
        catch { /* Browser may not expose its address bar, especially in fullscreen. */ }
        return null;
    }

    public void Dispose()
    {
        _stop.Set();
        if (_thread.IsAlive && !_thread.Join(1200)) return;
        _stop.Dispose();
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int count);
}
