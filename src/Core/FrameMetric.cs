using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GlowSync.Core;

internal sealed record MetricSnapshot(long Total, int LastSecond, double LastMs, double MeanMs, double P95Ms);

/// <summary>Bounded rolling one-second window; snapshots become zero-rate when the pipeline idles.</summary>
internal sealed class FrameMetric
{
    private readonly object _sync = new();
    private readonly Queue<(long Time, double Ms)> _samples = new(128);
    private long _total;
    private double _last;
    public void Observe(double milliseconds = 0, long? now = null)
    {
        lock (_sync)
        {
            long stamp = now ?? Environment.TickCount64;
            Trim(stamp);
            if (_samples.Count >= 256) _samples.Dequeue();
            _last = double.IsFinite(milliseconds) ? Math.Max(0, milliseconds) : 0;
            _samples.Enqueue((stamp, _last));
            _total++;
        }
    }
    private void Trim(long now)
    {
        while (_samples.TryPeek(out var value) && now - value.Time >= 1000) _samples.Dequeue();
    }
    public MetricSnapshot Snapshot(long? now = null)
    {
        lock (_sync)
        {
            Trim(now ?? Environment.TickCount64);
            var ms = _samples.Select(s => s.Ms).OrderBy(x => x).ToArray();
            return new(_total, ms.Length, _last, ms.Length == 0 ? 0 : ms.Average(), ms.Length == 0 ? 0 : ms[(int)Math.Ceiling(ms.Length * 0.95) - 1]);
        }
    }
    public static double ElapsedMs(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
}
