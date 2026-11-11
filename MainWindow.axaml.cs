using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using UsbPulse.Models;
using UsbPulse.Services;

namespace UsbPulse;

public partial class MainWindow : Window, IDisposable
{
    private readonly AppearanceService _appearance;
    private readonly DeviceMonitor _monitor = new();
    private readonly StorageService _storage = new();
    private readonly DeviceOpenGuard _openGuard = new();
    private readonly DeviceState _state = new();
    private readonly EventRefresh _refresh;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _acrylicAtStartup;
    private bool _systemBackdrop;
    private bool _monitoringStarted;
    private bool _disposed;
    public event EventHandler? SettingsRequested;

    public MainWindow(AppearanceService appearance)
    {
        _appearance = appearance;
        InitializeComponent();
        ApplyBehaviorSettings();
        _acrylicAtStartup = OperatingSystem.IsWindows() && Program.StartupSettings.UseAcrylic;
        ApplyWindowEffects();
        ActualThemeVariantChanged += (_, _) => ApplyCardBackground();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty) ApplyCardBackground();
        };
        DataContext = _state;
        _refresh = new EventRefresh(ScanAsync);
        Topmost = appearance.Settings.AlwaysOnTop;
        appearance.Changed += AppearanceChanged;
        _monitor.Changed += (_, _) => _refresh.Request();
        _monitor.Faulted += (_, message) => Dispatcher.UIThread.Post(() => _state.Message = message);
        Opened += (_, _) =>
        {
            PositionBottomRight();
            ApplyWindowEffects();
            ApplyWindowShape();
            LowerIfNeeded();
            AppLog.Write($"Window ready: position={Position}, scale={RenderScaling}, bounds={Bounds}");
        };
        SizeChanged += (_, _) =>
        {
            PositionBottomRight();
            ApplyWindowShape();
        };
        ScalingChanged += (_, _) =>
        {
            PositionBottomRight();
            ApplyWindowShape();
        };
        Screens.Changed += ScreensChanged;
        Deactivated += (_, _) => LowerIfNeeded();
        Closing += (_, e) =>
        {
            if (!_disposed && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void ShowCard(bool activate = false, bool refresh = true)
    {
        if (_disposed) return;
        if (refresh) RefreshDevices();
        if (!_state.HasDevice)
        {
            Hide();
            return;
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        PositionBottomRight();
        if (activate)
        {
            WindowState = WindowState.Normal;
            Activate();
        }
        else LowerIfNeeded();
    }

    public void RefreshDevices() => _refresh.Request(false);

    public void StartMonitoring()
    {
        if (_disposed || _monitoringStarted) return;
        _monitoringStarted = true;
        // Keep listening on the hidden native window so insertion can reveal the panel.
        _monitor.Start(this);
        RefreshDevices();
    }

    private void ApplyDeviceSnapshot(IReadOnlyList<StorageDevice> devices)
    {
        var inserted = _state.Apply(devices);
        if (!_state.HasDevice)
        {
            _state.Message = "";
            Hide();
        }
        else if (inserted)
        {
            _state.Message = "";
            ShowCard(true, refresh: false);
        }
    }

    private async Task ScanAsync(CancellationToken token)
    {
        try
        {
            var devices = await _storage.GetDevicesAsync(token);
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested) return;
                ApplyDeviceSnapshot(devices);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Write("Cannot enumerate storage", ex);
            await Dispatcher.UIThread.InvokeAsync(() => _state.Message = "设备读取失败：" + ex.Message);
        }
    }

    private void RefreshClick(object? sender, RoutedEventArgs e)
    {
        _state.Message = "";
        RefreshDevices();
    }

    private void SettingsClick(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private async void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (!_state.CanOpen || _state.Selected is not { } device) return;
        try
        {
            if (await _openGuard.TryOpenAsync(device.Id, () => _storage.OpenAsync(device, _lifetime.Token)) && !_disposed)
                _state.Message = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _state.Message = "打开失败：" + ex.Message; AppLog.Write("Open failed", ex); }
    }

    private async void EjectClick(object? sender, RoutedEventArgs e)
    {
        if (!_state.CanEject || _state.Selected is not { } device) return;
        _state.Busy = true;
        _state.Message = "";
        try
        {
            await _storage.EjectAsync(device, _lifetime.Token);
            _state.Message = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _state.Message = "弹出未完成：" + ex.Message;
            AppLog.Write("Eject not confirmed", ex);
        }
        finally
        {
            _state.Busy = false;
            RefreshDevices();
        }
    }

    private void AppearanceChanged(object? sender, EventArgs e)
    {
        ApplyBehaviorSettings();
        Topmost = _appearance.Settings.AlwaysOnTop;
        ApplyWindowEffects();
        LowerIfNeeded();
    }

    private void ApplyBehaviorSettings()
    {
        var settings = _appearance.Settings;
        _openGuard.SetRepeatDelay(TimeSpan.FromSeconds(settings.OpenRepeatDelaySeconds));
        var scale = settings.WindowScalePercent / 100d;
        CardScale.LayoutTransform = new ScaleTransform(scale, scale);
        // Keep the card's outside radius aligned with the system window corners.
        RootCard.CornerRadius = new CornerRadius(8 / scale);
        Width = 344 * scale;
    }

    private void ApplyWindowEffects()
    {
        if (_acrylicAtStartup)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                _systemBackdrop = TryApplySystemBackdrop();
            }
            if (!_systemBackdrop)
                TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur,
                    WindowTransparencyLevel.Transparent];
        }
        ApplyCardBackground();
    }

    private void ApplyCardBackground()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var solid = new SolidColorBrush(Color.Parse(dark ? "#202020" : "#F3F3F3"));
        TransparencyBackgroundFallback = solid;
        if (_systemBackdrop)
        {
            // DWM supplies Windows 11's own tint, luminosity, noise and transparency policy.
            RootCard.Background = Brushes.Transparent;
            TryApplySystemBackdrop();
            return;
        }
        var blurred = ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur ||
            ActualTransparencyLevel == WindowTransparencyLevel.Blur;
        RootCard.Background = _acrylicAtStartup && blurred
            ? new SolidColorBrush(Color.Parse(dark ? "#B3202020" : "#CCF3F3F3"))
            : solid;
    }

    private bool TryApplySystemBackdrop()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) ||
            TryGetPlatformHandle() is not { } handle) return false;
        const int useImmersiveDarkMode = 20, systemBackdropType = 38, useHostBackdropBrush = 17;
        var dark = ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
        var backdrop = 3; // DWMSBT_TRANSIENTWINDOW: Windows 11 Desktop Acrylic.
        if (DwmSetWindowAttribute(handle.Handle, systemBackdropType, ref backdrop, sizeof(int)) != 0)
            return false;
        DwmSetWindowAttribute(handle.Handle, useImmersiveDarkMode, ref dark, sizeof(int));
        var hostBackdrop = 0;
        DwmSetWindowAttribute(handle.Handle, useHostBackdropBrush, ref hostBackdrop, sizeof(int));
        var margins = new FrameMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(handle.Handle, ref margins) == 0) return true;
        backdrop = 1; // Remove the system backdrop before falling back to Avalonia's blur.
        DwmSetWindowAttribute(handle.Handle, systemBackdropType, ref backdrop, sizeof(int));
        return false;
    }

    private void ApplyWindowShape()
    {
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle() is not { } handle ||
            ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var round = 2; // DWMWCP_ROUND; keep DWM's antialiased corners and shadow.
            if (DwmSetWindowAttribute(handle.Handle, 33, ref round, sizeof(int)) == 0)
            {
                if (_systemBackdrop) TryApplySystemBackdrop();
                return;
            }
        }
        var width = (int)Math.Ceiling(ClientSize.Width * RenderScaling);
        var height = (int)Math.Ceiling(ClientSize.Height * RenderScaling);
        var diameter = (int)Math.Round(RootCard.CornerRadius.TopLeft *
            (_appearance.Settings.WindowScalePercent / 100d) * 2 * RenderScaling);
        // Native blur fills the HWND, so clip that too, including the rounded corners.
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region != IntPtr.Zero && SetWindowRgn(handle.Handle, region, true) == 0)
            DeleteObject(region); // On success Windows owns the region.
    }

    private void ScreensChanged(object? sender, EventArgs e) => PositionBottomRight();

    private void PositionBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null) return;
        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        MaxWidth = Math.Max(200, work.Width / scale - 24);
        var width = (int)Math.Ceiling((Bounds.Width > 0 ? Bounds.Width : Width) * scale);
        var height = (int)Math.Ceiling((Bounds.Height > 0 ? Bounds.Height : 106) * scale);
        var gap = (int)Math.Round(8 * scale);
        Position = new PixelPoint(Math.Max(work.X, work.Right - width - gap),
            Math.Max(work.Y, work.Bottom - height - gap));
    }

    private void LowerIfNeeded()
    {
        if (_appearance.Settings.AlwaysOnTop || !IsVisible || _disposed) return;
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
            SetWindowPos(handle.Handle, new IntPtr(1), 0, 0, 0, 0, 0x13);
        // Other compositors retain a normal, non-topmost window; do not steal focus.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _refresh.Dispose();
        _monitor.Dispose();
        _appearance.Changed -= AppearanceChanged;
        Screens.Changed -= ScreensChanged;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameMargins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref FrameMargins margins);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);
}
