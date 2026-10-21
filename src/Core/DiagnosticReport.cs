using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using GlowSync.Config;

namespace GlowSync.Core;

internal static class DiagnosticReport
{
    // Deliberately omit window titles, URLs, serial numbers, paths and network addresses.
    public static object Create(AppController app) => new
    {
        CreatedUtc = DateTimeOffset.UtcNow,
        Version = typeof(AppController).Assembly.GetName().Version?.ToString(3),
        OS = RuntimeInformation.OSDescription,
        Runtime = RuntimeInformation.FrameworkDescription,
        WorkingSetMb = Math.Round(Environment.WorkingSet / (1024.0 * 1024), 1),
        Selection = app.Store.Config.Profiles.Selection.ToString(),
        Mode = app.Engine.ActiveMode.ToString(),
        TargetFps = app.Engine.TargetFps,
        RequestedFps = (app.Engine.ActiveProfile?.Screen ?? app.Store.Config.Screen).Fps,
        Rendering = app.Engine.Rendering.Snapshot(),
        Capturing = app.Engine.Capturing.Snapshot(),
        CaptureFormat = app.Engine.CaptureFormat,
        CaptureSize = app.Engine.CaptureSize,
        HdrOutput = app.Engine.HdrOutput,
        HdrToneMappingEnabled = app.Store.Config.Screen.HdrToneMapping,
        CaptureUnavailablePolls = app.Engine.CaptureFailures,
        EngineLastErrorType = app.Engine.LastError,
        MediaSessionStatus = app.Profiles.MediaStatus,
        LayoutLedCount = app.Store.Config.Layout.Leds.Count,
        Outputs = app.Device.Snapshot().Select((route, index) => new
        {
            Index = index,
            Enabled = route.Config.OutputEnabled,
            Protocol = route.Config.Protocol.ToString(),
            State = route.Manager.Status.State.ToString(),
            ConfiguredLeds = route.Config.LedCount,
            BaudRate = GlowSync.Device.FrameEncoding.IsNetwork(route.Config.Protocol) ? (int?)null : route.Config.BaudRate,
            SafeFpsLimit = GlowSync.Device.FrameEncoding.LimitFps(60, route.Config.Protocol, route.Config.BaudRate,
                route.Manager.DeviceLedCount > 0 ? route.Manager.DeviceLedCount : route.Config.LedCount),
            DeviceLeds = route.Manager.DeviceLedCount,
            StartLed = route.Config.StartLed,
            Sending = route.Manager.Sending.Snapshot(),
            QueueMs = route.Manager.LastQueueMs,
            EstimatedSerialWireMs = route.Manager.EstimatedWireMs,
            ReplacedPendingFrames = route.Manager.OverwrittenFrames,
            WriteErrors = route.Manager.WriteErrors,
            Connections = route.Manager.Connections,
            LastErrorType = route.Manager.LastError,
        }).ToArray(),
        TimingNote = "Software timings only. Driver write completion is not LED presentation; serial wire time is an estimate. No end-to-end photon latency measurement.",
    };
}
