using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlowSync.Config;

namespace GlowSync.UI;

public partial class MainWindow
{
    private void BuildOutputs(AppConfig cfg)
    {
        OutputsPanel.Children.Clear();
        OutputsPanel.Children.Add(Toggle("Основной контроллер включён", cfg.Device, nameof(DeviceConfig.OutputEnabled)));
        OutputsPanel.Children.Add(Hint("Основной контроллер настраивается в «Лента и устройство». Дополнительные выходы получают всю карту либо её часть. Начало — индекс LED с нуля; недостаток цветов дополняется чёрным. Каждый выход имеет независимые очередь, переподключение и ограничение скорости."));
        OutputsPanel.Children.Add(Hint("Порты: DDP 4048/UDP, TPM2.NET 65506/UDP, OPC 7890/TCP. Для USB выберите COM вручную. Сначала заполните адрес и настройки, затем включите выход. До 8 дополнительных контроллеров. Не назначайте один адрес/COM разным выходам."));
        var grid = new DataGrid
        {
            ItemsSource = cfg.Outputs, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            MinHeight = 160, MaxHeight = 400, Background = ProfileInputBackground, Foreground = Brushes.White,
            RowBackground = ProfileInputBackground, AlternatingRowBackground = ProfileInputBackground,
        };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Вкл", Binding = new Binding(nameof(DeviceConfig.OutputEnabled)) });
        void Text(string header, string path, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Width = width, Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus } });
        Text("Имя", nameof(DeviceConfig.Name), 110);
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "Протокол", ItemsSource = Enum.GetValues<DeviceProtocol>(), SelectedItemBinding = new Binding(nameof(DeviceConfig.Protocol)) });
        Text("IP", nameof(DeviceConfig.Host), 120); Text("Порт сети", nameof(DeviceConfig.UdpPort), 85);
        Text("COM", nameof(DeviceConfig.Port), 75); Text("LED", nameof(DeviceConfig.LedCount), 65);
        Text("Начало", nameof(DeviceConfig.StartLed), 70); Text("Baud", nameof(DeviceConfig.BaudRate), 90);
        Text("OPC-канал", nameof(DeviceConfig.OpcChannel), 85);
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "RGB", ItemsSource = Enum.GetValues<ChannelOrder>(), SelectedItemBinding = new Binding(nameof(DeviceConfig.ChannelOrder)) });
        OutputsPanel.Children.Add(grid);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var add = new Wpf.Ui.Controls.Button { Content = "Добавить выход" };
        add.Click += (_, _) =>
        {
            if (cfg.Outputs.Count >= 8) return;
            var output = new DeviceConfig { Name = "Выход " + (cfg.Outputs.Count + 1), Protocol = DeviceProtocol.WledDdp, OutputEnabled = false };
            cfg.Outputs.Add(output); grid.SelectedItem = output;
        };
        var remove = new Wpf.Ui.Controls.Button { Content = "Удалить выбранный", Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => { if (grid.SelectedItem is DeviceConfig output) cfg.Outputs.Remove(output); };
        actions.Children.Add(add); actions.Children.Add(remove); OutputsPanel.Children.Add(actions);
        OutputsPanel.Children.Add(Hint("Состояние каждого активного выхода, частота и ошибки показаны в «Диагностика». Один медленный выход не ограничивает частоту остальных. Настройки цвета и физическая карта пока общие; для нестандартной геометрии доступен JSON-режим карты."));
        var maps = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var importMap = new Wpf.Ui.Controls.Button { Content = "Загрузить карту JSON" };
        var exportMap = new Wpf.Ui.Controls.Button { Content = "Сохранить карту JSON", Margin = new Thickness(8, 0, 0, 0) };
        var generated = new Wpf.Ui.Controls.Button { Content = "Карта по сторонам", Margin = new Thickness(8, 0, 0, 0) };
        var mapStatus = Hint(cfg.Layout.UseCustomMap ? "Используется произвольная карта JSON; изменения сторон не влияют на неё." : "Используется карта по сторонам.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        importMap.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Карта GlowSync (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var layout = JsonSerializer.Deserialize<LayoutConfig>(File.ReadAllText(dialog.FileName), options);
                if (layout == null || !layout.CustomMapIsValid()) throw new InvalidDataException("Нужны gridWidth/gridHeight (1..1000) и leds — 1..1000 координат x/y внутри сетки.");
                layout.UseCustomMap = true;
                var clone = ConfigStore.Clone(_app.Store.Config);
                clone.Layout = layout;
                _app.Store.Replace(clone);
            }
            catch (Exception ex) { mapStatus.Text = "Карта не загружена: " + ex.Message; }
        };
        exportMap.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Карта GlowSync (*.json)|*.json", FileName = "led-layout.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(cfg.Layout, options)); mapStatus.Text = "Карта сохранена."; }
            catch (Exception ex) { mapStatus.Text = ex.Message; }
        };
        generated.Click += (_, _) => { cfg.Layout.UseCustomMap = false; mapStatus.Text = "Используется карта по сторонам."; };
        maps.Children.Add(importMap); maps.Children.Add(exportMap); maps.Children.Add(generated);
        OutputsPanel.Children.Add(maps); OutputsPanel.Children.Add(mapStatus);
    }
}
