using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GlowSync.Config;
using GlowSync.Core;
using GlowSync.Platform;

namespace GlowSync.UI;

public partial class MainWindow
{
    private TextBox? _diagnosticText;
    private TextBlock? _diagnosticSaved;
    private long _lastDiagnosticsAt;
    private void BuildDiagnostics()
    {
        DiagnosticsPanel.Children.Clear();
        DiagnosticsPanel.Children.Add(Hint("Захват — новые кадры экрана. Обработка — рассчитанные кадры подсветки. Отправка — успешные записи в драйвер/сокет. На статичном экране частота новых кадров может быть ниже лимита."));
        _diagnosticText = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"), FontSize = 13, MinHeight = 360,
            Background = ProfileInputBackground, Foreground = Brushes.White, Padding = new Thickness(12),
            BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        DiagnosticsPanel.Children.Add(_diagnosticText);
        DiagnosticsPanel.Children.Add(Hint("Ожидание — возраст выбранного последнего кадра до вызова записи. Запись — время вызова драйвера/сокета, а не момент свечения. Время UART рассчитано по числу байт и baud. Полная задержка до LED требует физического измерения."));
        var export = new Wpf.Ui.Controls.Button { Content = "Сохранить диагностический JSON", Margin = new Thickness(0, 8, 0, 4) };
        export.Click += (_, _) =>
        {
            try
            {
                string folder = Path.Combine(Paths.DataDir, "diagnostics");
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json");
                File.WriteAllText(file, JsonSerializer.Serialize(DiagnosticReport.Create(_app), new JsonSerializerOptions { WriteIndented = true }));
                _diagnosticSaved!.Text = "Сохранено: " + file;
            }
            catch (Exception ex) { _diagnosticSaved!.Text = "Не удалось сохранить: " + ex.Message; }
        };
        DiagnosticsPanel.Children.Add(export);
        _diagnosticSaved = Hint("Отчёт не содержит адресов сайтов, заголовков окон, IP, COM-портов, серийных номеров и путей к приложениям.");
        DiagnosticsPanel.Children.Add(_diagnosticSaved);
    }

    internal void UpdateDiagnostics()
    {
        if (_diagnosticText == null || Environment.TickCount64 - _lastDiagnosticsAt < 500) return;
        _lastDiagnosticsAt = Environment.TickCount64;
        var render = _app.Engine.Rendering.Snapshot();
        var capture = _app.Engine.Capturing.Snapshot();
        var text = new StringBuilder();
        text.AppendLine($"GlowSync Next {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}");
        text.AppendLine($"Профиль: {_app.Profiles.Status}");
        text.AppendLine($"Медиа: {_app.Profiles.MediaStatus}");
        text.AppendLine($"Карта: {_app.Store.Config.Layout.Leds.Count} LED | захват: {_app.Engine.CaptureSize} {_app.Engine.CaptureFormat}");
        text.AppendLine($"HDR-выход: {_app.Engine.HdrOutput} | scRGB включён: {_app.Store.Config.Screen.HdrToneMapping}");
        text.AppendLine($"Лимит: {_app.Engine.TargetFps:0.0} FPS | обработка: {render.LastSecond} FPS | новые кадры: {capture.LastSecond} FPS");
        var primary = _app.Store.Config.Device;
        int primaryLeds = _app.Device.DeviceLedCount > 0 ? _app.Device.DeviceLedCount : primary.LedCount;
        if (!GlowSync.Device.FrameEncoding.IsNetwork(primary.Protocol))
        {
            double serialLimit = GlowSync.Device.FrameEncoding.LimitFps(60, primary.Protocol, primary.BaudRate, primaryLeds);
            text.AppendLine($"Основной UART: {primary.BaudRate} бод, {primaryLeds} LED → безопасный предел {serialLimit:0.0} FPS.");
            if (serialLimit < 60) text.AppendLine("Для 60 FPS нужна более высокая скорость, поддерживаемая контроллером. Смена baud только в программе не меняет скорость контроллера.");
        }
        text.AppendLine($"Обработка: среднее {render.MeanMs:0.00} мс, p95 {render.P95Ms:0.00} мс");
        text.AppendLine($"Захват/GPU readback: среднее {capture.MeanMs:0.00} мс, p95 {capture.P95Ms:0.00} мс");
        text.AppendLine($"Недоступность захвата (опросы): {_app.Engine.CaptureFailures} | ошибка: {_app.Engine.LastError}");
        foreach (var route in _app.Device.Snapshot())
        {
            var sent = route.Manager.Sending.Snapshot();
            text.AppendLine();
            text.AppendLine($"{route.Config.Name}: {route.Config.Protocol} | {route.Manager.Status.State} | {sent.LastSecond} FPS | всего {sent.Total}");
            text.AppendLine($"LED: {route.Manager.DeviceLedCount}/{route.Config.LedCount}, начало: {route.Config.StartLed}");
            text.AppendLine($"Ожидание: {route.Manager.LastQueueMs:0.00} мс | запись p95: {sent.P95Ms:0.00} мс | UART ~{route.Manager.EstimatedWireMs:0.00} мс");
            text.AppendLine($"Заменено устаревших кадров: {route.Manager.OverwrittenFrames} | ошибки: {route.Manager.WriteErrors} | подключений: {route.Manager.Connections}");
        }
        if (_app.Device.RoutingWarning.Length > 0) text.AppendLine("Выходы: " + _app.Device.RoutingWarning);
        _diagnosticText.Text = text.ToString();
    }
}
