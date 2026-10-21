using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlowSync;
using GlowSync.UI;

internal static class UiSmoke
{
    public static int Run()
    {
        int result = 0;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
                using var controller = new AppController(); // Never Start: no device, screen, audio or foreground capture.
                var window = new MainWindow(controller);
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1100, 800));
                root.Arrange(new Rect(0, 0, 1100, 800));
                root.UpdateLayout();
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                var nav = (ListBox)window.FindName("Nav");
                nav.SelectedIndex = 1;
                var panel = (StackPanel)window.FindName("ProfileSettingsPanel");
                if (panel.Children.Count < 9) throw new Exception("Profile controls were not built");
                var breathingColor = (CheckBox)window.FindName("BreathingChangeColorBox");
                breathingColor.IsChecked = true;
                if (!controller.Store.Config.Effects.BreathingRainbow) throw new Exception("Breathing checkbox does not enable color change");
                breathingColor.IsChecked = false;
                if (controller.Store.Config.Effects.BreathingRainbow) throw new Exception("Breathing checkbox does not disable color change");
                Console.WriteLine("PASS UI: breathing color-change checkbox updates configuration in both directions");
                var ruleButton = (Wpf.Ui.Controls.Button)panel.Children[0];
                controller.Store.Config.Profiles.Selection = GlowSync.Config.ProfileSelection.Custom;
                ruleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (controller.Store.Config.Profiles.Selection != GlowSync.Config.ProfileSelection.Auto || !controller.Profiles.Status.Contains("включено") || !ruleButton.Content.ToString()!.Contains("включена")) throw new Exception("Rules button provides no immediate mode feedback");
                ruleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (controller.Store.Config.Profiles.Selection != GlowSync.Config.ProfileSelection.Custom) throw new Exception("Rules mode cannot be turned off");
                if (MainWindow.ListRuleProcesses().Length == 0) throw new Exception("Process chooser is empty");
                Console.WriteLine("PASS UI: rules mode button toggles immediately with visible status; running processes enumerated");
                var processDialog = window.CreateRuleProcessDialog();
                var chooser = (StackPanel)processDialog.Content;
                var processChoice = (ComboBox)chooser.Children[1];
                processChoice.ItemsSource = MainWindow.ListRuleProcesses();
                processChoice.Text = "vivaldi.exe";
                ((TextBox)chooser.Children[5]).Text = "YouTube";
                ((ComboBox)chooser.Children[7]).SelectedValue = GlowSync.Config.ProfileKind.Video;
                var chooserButtons = (StackPanel)chooser.Children[8];
                ((Wpf.Ui.Controls.Button)chooserButtons.Children[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                if (processDialog.Tag is not GlowSync.Config.ProfileRule chosen || chosen.Process != "vivaldi.exe" || chosen.TitleContains != "YouTube" || chosen.Profile != GlowSync.Config.ProfileKind.Video || !chosen.Enabled) throw new Exception("Process chooser did not produce selected rule");
                var chooserRoot = (FrameworkElement)processDialog.Content;
                chooserRoot.Measure(new Size(600, 460)); chooserRoot.Arrange(new Rect(0, 0, 600, 460)); chooserRoot.UpdateLayout();
                var chooserBitmap = new RenderTargetBitmap(600, 460, 96, 96, PixelFormats.Pbgra32); chooserBitmap.Render(chooserRoot);
                var chooserEncoder = new PngBitmapEncoder(); chooserEncoder.Frames.Add(BitmapFrame.Create(chooserBitmap));
                Directory.CreateDirectory(Environment.GetEnvironmentVariable("GLOWSYNC_NEXT_DATA_DIR")!);
                using (var stream = File.Create(Path.Combine(Environment.GetEnvironmentVariable("GLOWSYNC_NEXT_DATA_DIR")!, "process-chooser-smoke.png"))) chooserEncoder.Save(stream);
                processDialog.Close();
                Console.WriteLine("PASS UI: process chooser creates enabled EXE/title/profile rule; offscreen dialog render");
                ((Wpf.Ui.Controls.NumberBox)window.FindName("LayoutLeftBox")).Value = 20;
                ((Wpf.Ui.Controls.NumberBox)window.FindName("LayoutTopBox")).Value = 41;
                ((Wpf.Ui.Controls.NumberBox)window.FindName("LayoutRightBox")).Value = 20;
                ((Wpf.Ui.Controls.NumberBox)window.FindName("LayoutBottomBox")).Value = 0;
                if (controller.Store.Config.Layout.Leds.Count != 81) throw new Exception($"NumberBox edits did not rebuild 81 LED layout: {controller.Store.Config.Layout.Left}/{controller.Store.Config.Layout.Top}/{controller.Store.Config.Layout.Right}/{controller.Store.Config.Layout.Bottom} count={controller.Store.Config.Layout.Leds.Count}");
                Console.WriteLine("PASS UI: NumberBox edits update live layout to 81 LEDs");
                controller.Store.Config.Profiles.Selection = GlowSync.Config.ProfileSelection.Game;
                controller.Profiles.Refresh();
                if (controller.Engine.ActiveMode != GlowSync.Config.EffectMode.Screen) throw new Exception("Game profile did not reach engine");
                var brightness = (Slider)window.FindName("MainBrightnessSlider");
                brightness.Value = 42;
                if (controller.Store.Config.Profiles.Game.Brightness != 42) throw new Exception("Main brightness does not edit active profile");
                var fps = (Slider)window.FindName("ScreenFpsSlider");
                int manualFps = controller.Store.Config.Screen.Fps;
                fps.Value = 57;
                if (controller.Store.Config.Profiles.Game.Screen.Fps != 57 || controller.Store.Config.Screen.Fps != manualFps) throw new Exception("Screen page does not edit active profile independently of manual settings");
                controller.Store.Config.Profiles.Game.Screen.Fps = 60;
                if (fps.Value != 60) throw new Exception("Profile change did not update screen page");
                var picker = (ColorPicker)window.FindName("StaticColorPicker");
                picker.Hex = "#123456";
                if (controller.Store.Config.Profiles.Game.StaticColor != "#123456") throw new Exception("Static picker does not edit active profile");
                controller.Store.Config.Profiles.Selection = GlowSync.Config.ProfileSelection.Custom;
                controller.Profiles.Refresh();
                fps.Value = 53;
                picker.Hex = "#654321";
                if (controller.Store.Config.Screen.Fps != 53 || controller.Store.Config.Profiles.Game.Screen.Fps != 60 || controller.Store.Config.Effects.StaticColor != "#654321") throw new Exception("Manual controls leak into profile");
                controller.Store.Config.Profiles.Selection = GlowSync.Config.ProfileSelection.Video;
                controller.Profiles.Refresh();
                fps.Value = 45;
                if (controller.Store.Config.Profiles.Video.Screen.Fps != 45 || controller.Store.Config.Profiles.Game.Screen.Fps != 60 || controller.Store.Config.Screen.Fps != 53) throw new Exception("Video/game/manual profile settings conflict");
                controller.Store.Config.Profiles.Selection = GlowSync.Config.ProfileSelection.Game;
                controller.Profiles.Refresh();
                var replacement = GlowSync.Config.ConfigStore.Clone(controller.Store.Config);
                replacement.Profiles.Game.Screen.Fps = 58;
                replacement.Profiles.Game.Brightness = 39;
                replacement.Profiles.Game.StaticColor = "#ABCDEF";
                controller.Store.Replace(replacement);
                if (fps.Value != 58 || brightness.Value != 39 || picker.Hex != "#ABCDEF") throw new Exception("Config replacement leaves stale cross-tab bindings");
                Console.WriteLine("PASS UI: Screen FPS and static color follow active profile, cross-tab two-way sync and manual/game/video isolation");
                ((Grid)root).Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
                root.Measure(new Size(1100, 800));
                root.Arrange(new Rect(0, 0, 1100, 800));
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                string output = Path.Combine(Environment.GetEnvironmentVariable("GLOWSYNC_NEXT_DATA_DIR")!, "profiles-smoke.png");
                using (var stream = File.Create(output)) encoder.Save(stream);
                foreach (var child in panel.Children)
                    if (child is TabControl tabs) tabs.SelectedIndex = 1;
                root.UpdateLayout();
                var gameBitmap = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32);
                gameBitmap.Render(root);
                var gameEncoder = new PngBitmapEncoder();
                gameEncoder.Frames.Add(BitmapFrame.Create(gameBitmap));
                using (var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, "game-smoke.png"))) gameEncoder.Save(stream);
                controller.Store.Config.Device.Host = "192.0.2.111";
                controller.Store.Config.Device.Serial = "PRIVATE-SERIAL-TEST";
                var report = System.Text.Json.JsonSerializer.Serialize(GlowSync.Core.DiagnosticReport.Create(controller));
                if (report.Contains("192.0.2.111") || report.Contains("PRIVATE-SERIAL-TEST")) throw new Exception("Diagnostic report leaked endpoint/serial");
                foreach (int page in new[] { 6, 7 })
                {
                    nav.SelectedIndex = page;
                    window.UpdateDiagnostics();
                    root.Measure(new Size(1100, 800)); root.Arrange(new Rect(0, 0, 1100, 800)); root.UpdateLayout();
                    var pageBitmap = new RenderTargetBitmap(1100, 800, 96, 96, PixelFormats.Pbgra32);
                    pageBitmap.Render(root);
                    var pageEncoder = new PngBitmapEncoder(); pageEncoder.Frames.Add(BitmapFrame.Create(pageBitmap));
                    using var stream = File.Create(Path.Combine(Path.GetDirectoryName(output)!, page == 6 ? "outputs-smoke.png" : "diagnostics-smoke.png"));
                    pageEncoder.Save(stream);
                }
                Console.WriteLine("PASS UI: outputs/diagnostics pages render; exported report excludes private identifiers");
                Console.WriteLine("PASS UI: XAML, dynamic profile editors and offscreen rendering: " + output);
                window.Close();
                app.Shutdown();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }
}
