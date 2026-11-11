using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using UsbPulse.Services;

namespace UsbPulse;

public partial class SettingsWindow : Window
{
    private readonly AppearanceService _appearance;
    private bool _initializing = true;

    public SettingsWindow(AppearanceService appearance)
    {
        _appearance = appearance;
        InitializeComponent();
        if (Screens.Primary is { } screen)
            MaxHeight = Math.Max(260, screen.WorkingArea.Height / screen.Scaling - 32);
        ThemeBox.SelectedIndex = appearance.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        TopmostSwitch.IsChecked = appearance.Settings.AlwaysOnTop;
        WindowsEffectsPanel.IsVisible = OperatingSystem.IsWindows();
        AcrylicSwitch.IsChecked = appearance.Settings.UseAcrylic;
        HighRefreshSwitch.IsChecked = appearance.Settings.HighRefreshRate;
        OpenDelayBox.Value = appearance.Settings.OpenRepeatDelaySeconds;
        WindowScaleBox.Value = appearance.Settings.WindowScalePercent;
        foreach (var palette in AppearanceService.Palettes)
        {
            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7 };
            content.Children.Add(new Ellipse { Width = 19, Height = 19, Fill = new SolidColorBrush(Color.Parse(palette.Light)) });
            content.Children.Add(new TextBlock { Text = palette.Name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var radio = new RadioButton
            {
                Content = content,
                GroupName = "Accent",
                Margin = new Thickness(0, 0, 4, 12),
                IsChecked = appearance.Settings.Accent == palette.Id
            };
            AutomationProperties.SetName(radio, palette.Name);
            radio.IsCheckedChanged += (_, _) =>
            {
                if (!_initializing && radio.IsChecked == true)
                    Save(appearance.Settings with { Accent = palette.Id });
            };
            Swatches.Children.Add(radio);
        }
        ThemeBox.SelectionChanged += (_, _) =>
        {
            if (!_initializing) Save(appearance.Settings with
            {
                Theme = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" }
            });
        };
        TopmostSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_initializing) Save(appearance.Settings with { AlwaysOnTop = TopmostSwitch.IsChecked == true });
        };
        AcrylicSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_initializing) Save(appearance.Settings with { UseAcrylic = AcrylicSwitch.IsChecked == true });
        };
        HighRefreshSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_initializing) Save(appearance.Settings with { HighRefreshRate = HighRefreshSwitch.IsChecked == true });
        };
        OpenDelayBox.ValueChanged += (_, _) =>
        {
            if (!_initializing && OpenDelayBox.Value is { } value)
                Save(appearance.Settings with { OpenRepeatDelaySeconds = (int)decimal.Round(value) });
        };
        WindowScaleBox.ValueChanged += (_, _) =>
        {
            if (!_initializing && WindowScaleBox.Value is { } value)
                Save(appearance.Settings with { WindowScalePercent = (int)decimal.Round(value) });
        };
        _initializing = false;
        UpdateRestartInfo();
    }

    private void Save(AppearanceSettings settings)
    {
        try
        {
            _appearance.Update(settings);
            SettingsError.IsOpen = false;
            UpdateRestartInfo();
        }
        catch (Exception ex)
        {
            SettingsError.Message = "设置保存失败：" + ex.Message;
            SettingsError.IsOpen = true;
            AppLog.Write("Settings save failed", ex);
        }
    }

    private void UpdateRestartInfo()
    {
        RestartInfo.IsOpen = OperatingSystem.IsWindows() &&
            (_appearance.Settings.UseAcrylic != Program.StartupSettings.UseAcrylic ||
             _appearance.Settings.HighRefreshRate != Program.StartupSettings.HighRefreshRate);
    }

    private void RestartClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前程序。");
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception ex)
        {
            SettingsError.Message = "重启失败：" + ex.Message;
            SettingsError.IsOpen = true;
            AppLog.Write("Restart failed", ex);
        }
    }
}
