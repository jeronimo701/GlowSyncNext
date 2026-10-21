using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GlowSync.Config;
using GlowSync.Core;

namespace GlowSync.UI;

public partial class MainWindow
{
    private Wpf.Ui.Controls.Button? _rulesModeButton;
    private static readonly Brush ProfileInputBackground = new SolidColorBrush(Color.FromRgb(48, 48, 48));
    private void BuildProfiles(AppConfig cfg)
    {
        var panel = ProfileSettingsPanel;
        panel.Children.Clear();
        var automatic = _rulesModeButton = new Wpf.Ui.Controls.Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        automatic.Click += (_, _) => cfg.Profiles.Selection = cfg.Profiles.Selection == ProfileSelection.Auto ? ProfileSelection.Custom : ProfileSelection.Auto;
        panel.Children.Add(automatic);
        panel.Children.Add(Hint("Полуавтоматика переключает профили только по включённым правилам ниже. Встроенное распознавание YouTube, игр, плееров, URL и воспроизведения отключено. Нет подходящего правила → обычная подсветка. Окно настроек сохраняет предыдущий профиль."));
        panel.Children.Add(Hint("Примеры добавлены выключенными: включите галочку нужной строки. Для Vivaldi/YouTube включите строку vivaldi.exe + YouTube; для YouTube Music — отдельную строку выше. Проверяется только фрагмент заголовка окна, не адрес сайта и не пауза. Полуавтоматика должна быть включена кнопкой сверху."));
        panel.Children.Add(Scale("Задержка переключения, мс", cfg.Profiles, nameof(ProfilesConfig.SwitchDelayMs), 200, 5000, 100));
        var hdr = new StackPanel();
        hdr.Children.Add(Toggle("Включить экспериментальный scRGB → SDR для HDR-экрана", cfg.Screen, nameof(ScreenConfig.HdrToneMapping)));
        hdr.Children.Add(Scale("HDR: белый, нит", cfg.Screen, nameof(ScreenConfig.HdrPaperWhite), 80, 500, 1));
        hdr.Children.Add(Scale("HDR: пик, нит", cfg.Screen, nameof(ScreenConfig.HdrPeak), 500, 4000, 50));
        hdr.Children.Add(Hint("Общие настройки для экранных профилей. На SDR сохраняется 8-битный захват. На HDR запрашивается FP16/scRGB; GPU уменьшает кадр, затем применяется тональная компрессия яркости. Проверка на реальном HDR-мониторе ещё нужна."));
        var hdrExpander = new Expander { Header = "HDR (экспериментально)", Content = hdr, Background = ProfileInputBackground, Foreground = Brushes.White };

        var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 12) };
        foreach (var kind in Enum.GetValues<ProfileKind>())
        {
            var preset = cfg.Profiles.Get(kind);
            var content = new StackPanel { Margin = new Thickness(12) };
            var mode = new ComboBox
            {
                ItemsSource = System.Linq.Enumerable.Select(AppController.ModeOrder, m => new Option<EffectMode>(m, SkydimoImporter.ModeName(m))),
                DisplayMemberPath = "Name", SelectedValuePath = "Value", MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left, Background = ProfileInputBackground, Foreground = Brushes.White,
            };
            Bind(mode, ComboBox.SelectedValueProperty, preset, nameof(ProfilePreset.Mode));
            content.Children.Add(Row("Режим", mode));
            content.Children.Add(Scale("Яркость, %", preset, nameof(ProfilePreset.Brightness), 0, 100, 1));
            var color = new Wpf.Ui.Controls.TextBox { Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Background = ProfileInputBackground, Foreground = Brushes.White };
            Bind(color, TextBox.TextProperty, preset, nameof(ProfilePreset.StaticColor), UpdateSourceTrigger.LostFocus);
            content.Children.Add(Row("Один цвет (#RRGGBB)", color));
            var screen = new StackPanel();
            screen.Children.Add(Scale("Частота, FPS", preset.Screen, nameof(ScreenConfig.Fps), 10, 60, 1));
            screen.Children.Add(Scale("Плавность, мс", preset.Screen, nameof(ScreenConfig.SmoothingMs), 0, 500, 10));
            screen.Children.Add(Scale("Насыщенность", preset.Screen, nameof(ScreenConfig.Saturation), -100, 100, 5));
            screen.Children.Add(Scale("Гамма", preset.Screen, nameof(ScreenConfig.Gamma), 1, 3, 0.1));
            screen.Children.Add(Scale("Глубина захвата", preset.Screen, nameof(ScreenConfig.Depth), 0.5, 6, 0.5));
            screen.Children.Add(Scale("Размытие", preset.Screen, nameof(ScreenConfig.SpatialBlur), 0, 100, 5));
            screen.Children.Add(Scale("Порог чёрного, %", preset.Screen, nameof(ScreenConfig.BlackThreshold), 0, 20, 1));
            screen.Children.Add(Toggle("Убирать чёрные полосы", preset.Screen, nameof(ScreenConfig.RemoveBlackBars)));
            content.Children.Add(new Expander { Header = "Параметры захвата экрана", Content = screen, Background = ProfileInputBackground, Foreground = Brushes.White, IsExpanded = kind is ProfileKind.Game or ProfileKind.Video, Margin = new Thickness(0, 10, 0, 0) });
            content.Children.Add(Hint("Монитор выбирается на странице «Экран» для всех профилей. Баланс белого, ночной режим и настройки музыкальных эффектов тоже общие. Частота автоматически ограничивается пропускной способностью контроллера."));
            tabs.Items.Add(new TabItem { Header = ProfileController.Name(kind), Content = content });
        }
        panel.Children.Add(new TextBlock { Text = "Правила приложений — первое подходящее имеет приоритет", FontSize = 15, Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(Hint("«Добавить правило» открывает выбор запущенного процесса; можно также выбрать EXE с диска или ввести имя. Если задан заголовок, должны совпасть и процесс, и фрагмент заголовка. Специальные правила ставьте выше общих."));
        var grid = new DataGrid
        {
            ItemsSource = cfg.Profiles.Rules, AutoGenerateColumns = false,
            CanUserAddRows = false, CanUserDeleteRows = false, MinHeight = 100,
            MaxHeight = 260, Margin = new Thickness(0, 8, 0, 8),
            Background = ProfileInputBackground, Foreground = Brushes.White,
            RowBackground = ProfileInputBackground, AlternatingRowBackground = ProfileInputBackground,
        };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Вкл.", Width = 52, Binding = new Binding(nameof(ProfileRule.Enabled)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        grid.Columns.Add(new DataGridTextColumn { Header = "Процесс .exe", Width = 210, Binding = new Binding(nameof(ProfileRule.Process)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        grid.Columns.Add(new DataGridTextColumn { Header = "Заголовок содержит", Width = 230, Binding = new Binding(nameof(ProfileRule.TitleContains)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        grid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Профиль", Width = 190, ItemsSource = System.Linq.Enumerable.Select(Enum.GetValues<ProfileKind>(), k => new Option<ProfileKind>(k, ProfileController.Name(k))).ToArray(),
            DisplayMemberPath = "Name", SelectedValuePath = "Value",
            SelectedValueBinding = new Binding(nameof(ProfileRule.Profile)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        });
        panel.Children.Add(grid);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Wpf.Ui.Controls.Button { Content = "Добавить правило", Padding = new Thickness(12, 6, 12, 6) };
        add.Click += (_, _) => { if (ChooseRuleProcess() is { } rule) { cfg.Profiles.Rules.Add(rule); grid.SelectedItem = rule; grid.ScrollIntoView(rule); } };
        var remove = new Wpf.Ui.Controls.Button { Content = "Удалить выбранное", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        remove.Click += (_, _) => { if (grid.SelectedItem is ProfileRule rule) cfg.Profiles.Rules.Remove(rule); };
        actions.Children.Add(add); actions.Children.Add(remove);
        foreach (var (label, direction) in new[] { ("Выше", -1), ("Ниже", 1) })
        {
            var move = new Wpf.Ui.Controls.Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
            move.Click += (_, _) => { if (grid.SelectedItem is ProfileRule rule) { int from = cfg.Profiles.Rules.IndexOf(rule), to = from + direction; if (to >= 0 && to < cfg.Profiles.Rules.Count) cfg.Profiles.Rules.Move(from, to); } };
            actions.Children.Add(move);
        }
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "Параметры профилей", FontSize = 18, Margin = new Thickness(0, 20, 0, 8) });
        panel.Children.Add(tabs);
        panel.Children.Add(hdrExpander);
    }

    internal ProfileRule? ChooseRuleProcess()
    {
        var dialog = CreateRuleProcessDialog();
        return dialog.ShowDialog() == true ? dialog.Tag as ProfileRule : null;
    }

    internal Window CreateRuleProcessDialog()
    {
        var dialog = new Window { Title = "Добавить правило — выбор процесса", Width = 620, Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)), Foreground = Brushes.White };
        if (IsVisible) dialog.Owner = this;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Hint("Выберите запущенный процесс или введите имя .exe. Список не ограничен браузерами и играми."));
        var processes = new ComboBox { Name = "RuleProcess", IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 240, Background = ProfileInputBackground, Foreground = Brushes.White };
        panel.Children.Add(processes);
        var refresh = new Wpf.Ui.Controls.Button { Content = "Обновить список процессов", Background = ProfileInputBackground, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 8) };
        async void LoadProcesses()
        {
            refresh.IsEnabled = false;
            try
            {
                string entered = processes.Text;
                processes.ItemsSource = await System.Threading.Tasks.Task.Run(ListRuleProcesses);
                processes.Text = entered;
            }
            catch (SystemException ex) { processes.ToolTip = "Не удалось обновить список: " + ex.Message; }
            finally { refresh.IsEnabled = true; }
        }
        refresh.Click += (_, _) => LoadProcesses();
        panel.Children.Add(refresh);
        var browse = new Wpf.Ui.Controls.Button { Content = "Выбрать EXE с диска…", Background = ProfileInputBackground, Foreground = Brushes.White };
        browse.Click += (_, _) => { var file = new Microsoft.Win32.OpenFileDialog { Filter = "Приложение (*.exe)|*.exe" }; if (file.ShowDialog(dialog) == true) processes.Text = System.IO.Path.GetFileName(file.FileName); };
        panel.Children.Add(browse);
        panel.Children.Add(Hint("Заголовок содержит (необязательно, например YouTube):"));
        var title = new TextBox { Name = "RuleTitle", Background = ProfileInputBackground, Foreground = Brushes.White };
        panel.Children.Add(title);
        panel.Children.Add(Hint("Профиль:"));
        var profile = new ComboBox { Name = "RuleProfile", ItemsSource = Enum.GetValues<ProfileKind>().Select(k => new Option<ProfileKind>(k, ProfileController.Name(k))).ToArray(),
            DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = ProfileKind.Game, Background = ProfileInputBackground, Foreground = Brushes.White };
        panel.Children.Add(profile);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var confirm = new Wpf.Ui.Controls.Button { Name = "RuleConfirm", Content = "Добавить", IsDefault = true, Background = ProfileInputBackground, Foreground = Brushes.White };
        var cancel = new Wpf.Ui.Controls.Button { Content = "Отмена", IsCancel = true, Background = ProfileInputBackground, Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 0) };
        confirm.Click += (_, _) =>
        {
            string process = ProfileClassifier.ProcessName(processes.Text);
            if (string.IsNullOrWhiteSpace(process)) { processes.Focus(); return; }
            dialog.Tag = new ProfileRule { Process = process + ".exe", TitleContains = title.Text.Trim(), Profile = (ProfileKind)profile.SelectedValue, Enabled = true };
            if (dialog.IsVisible) dialog.DialogResult = true;
        };
        buttons.Children.Add(confirm); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => LoadProcesses();
        return dialog;
    }

    internal static string[] ListRuleProcesses()
    {
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in System.Diagnostics.Process.GetProcesses())
            using (process) { try { if (!string.IsNullOrWhiteSpace(process.ProcessName)) names.Add(process.ProcessName + ".exe"); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 8) };
    private static void Bind(FrameworkElement control, DependencyProperty property, object source, string path, UpdateSourceTrigger trigger = UpdateSourceTrigger.PropertyChanged) =>
        control.SetBinding(property, new Binding(path) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = trigger });
    private static CheckBox Toggle(string label, object source, string path)
    {
        var control = new CheckBox { Content = label, Margin = new Thickness(0, 6, 0, 6) };
        Bind(control, CheckBox.IsCheckedProperty, source, path);
        return control;
    }
    private static Grid Row(string label, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(control, 1); row.Children.Add(control);
        return row;
    }
    private static Grid Scale(string label, object source, string path, double min, double max, double step)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal };
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Width = 210 };
        Bind(slider, Slider.ValueProperty, source, path);
        var value = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        value.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = source, StringFormat = "{0:0.##}" });
        group.Children.Add(slider); group.Children.Add(value);
        return Row(label, group);
    }
}
