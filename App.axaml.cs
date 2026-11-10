using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using UsbPulse.Services;

namespace UsbPulse;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private AppearanceService? _appearance;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _appearance = new AppearanceService(this);
            _appearance.Apply();
            _mainWindow = new MainWindow(_appearance);
            _mainWindow.SettingsRequested += (_, _) => ShowSettings();
            // The desktop lifetime shows MainWindow at startup, so assign it only after a device reveals it.
            _mainWindow.Opened += (_, _) => desktop.MainWindow = _mainWindow;
            // An unavailable system tray must not prevent the panel from opening.
            try { CreateTray(desktop); }
            catch (Exception ex) { AppLog.Write("Tray initialization failed", ex); }
            desktop.Exit += (_, _) =>
            {
                _mainWindow.Dispose();
                _trayIcon?.Dispose();
                AppLog.Write("Shutdown");
            };
            _mainWindow.StartMonitoring();
            AppLog.Write("Monitoring storage; panel stays hidden until a device is available; version 0.2.0");
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();
        Add(menu, "显示设备面板", () => _mainWindow?.ShowCard(true));
        Add(menu, "外观设置", ShowSettings);
        Add(menu, "刷新设备", () => _mainWindow?.RefreshDevices());
        menu.Items.Add(new NativeMenuItemSeparator());
        Add(menu, "退出 UsbPulse", () => desktop.Shutdown());
        using var stream = AssetLoader.Open(new Uri("avares://UsbPulse/Assets/UsbPulse.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(stream),
            ToolTipText = "UsbPulse · U 盘助手",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => _mainWindow?.ShowCard(true);
        _appearance!.Changed += (_, _) => UpdateTrayIcon();
        ActualThemeVariantChanged += (_, _) => UpdateTrayIcon();
        UpdateTrayIcon();
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon is null) return;
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        using var stream = AssetLoader.Open(new Uri(
            $"avares://UsbPulse/Assets/{(dark ? "UsbPulse-light.ico" : "UsbPulse.ico")}"));
        _trayIcon.Icon = new WindowIcon(stream);
        MacOSProperties.SetIsTemplateIcon(_trayIcon, true);
    }

    private void ShowSettings()
    {
        if (_appearance is null) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_appearance);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static void Add(NativeMenu menu, string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
