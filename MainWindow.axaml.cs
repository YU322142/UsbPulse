using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UsbPulse.Models;
using UsbPulse.Services;

namespace UsbPulse;

public partial class MainWindow : Window, IDisposable
{
    private readonly AppearanceService _appearance;
    private readonly DeviceMonitor _monitor = new();
    private readonly StorageService _storage = new();
    private readonly DeviceState _state = new();
    private readonly EventRefresh _refresh;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public event EventHandler? SettingsRequested;

    public MainWindow(AppearanceService appearance)
    {
        _appearance = appearance;
        InitializeComponent();
        DataContext = _state;
        _refresh = new EventRefresh(ScanAsync);
        Topmost = appearance.Settings.AlwaysOnTop;
        appearance.Changed += AppearanceChanged;
        _monitor.Changed += (_, _) => _refresh.Request();
        _monitor.Faulted += (_, message) => Dispatcher.UIThread.Post(() => _state.Message = message);
        Opened += (_, _) =>
        {
            PositionBottomRight();
            _monitor.Start(this);
            _refresh.Request(false);
            LowerIfNeeded();
            AppLog.Write($"Window ready: position={Position}, scale={RenderScaling}, bounds={Bounds}");
        };
        SizeChanged += (_, _) => PositionBottomRight();
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

    public void ShowCard(bool activate = false)
    {
        if (_disposed) return;
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

    private async Task ScanAsync(CancellationToken token)
    {
        try
        {
            var devices = await _storage.GetDevicesAsync(token);
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested) return;
                if (_state.Apply(devices)) ShowCard(true);
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

    private void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (!_state.CanOpen || _state.Selected is not { } device) return;
        try { _storage.Open(device); _state.Message = ""; }
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
        Topmost = _appearance.Settings.AlwaysOnTop;
        LowerIfNeeded();
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
        var height = (int)Math.Ceiling((Bounds.Height > 0 ? Bounds.Height : 188) * scale);
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
}
