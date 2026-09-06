using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
        ThemeBox.SelectedIndex = appearance.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        TopmostSwitch.IsChecked = appearance.Settings.AlwaysOnTop;
        foreach (var palette in AppearanceService.Palettes)
        {
            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7 };
            content.Children.Add(new Ellipse { Width = 19, Height = 19, Fill = new SolidColorBrush(Color.Parse(palette.Light)) });
            content.Children.Add(new TextBlock { Text = palette.Name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var radio = new RadioButton
            {
                Content = content, GroupName = "Accent", Margin = new Thickness(0, 0, 4, 12),
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
        _initializing = false;
    }

    private void Save(AppearanceSettings settings)
    {
        try { _appearance.Update(settings); SettingsError.IsOpen = false; }
        catch (Exception ex)
        {
            SettingsError.Message = "设置保存失败：" + ex.Message;
            SettingsError.IsOpen = true;
            AppLog.Write("Settings save failed", ex);
        }
    }
}
