using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using UsbPulse.Services;

namespace UsbPulse;

public sealed class DeviceMonitor : IDisposable
{
    private Window? _window;
    private Process? _linuxMonitor;
    private bool _disposed;
    public event EventHandler? Changed;
    public event EventHandler<string>? Faulted;
    public string Mode { get; private set; } = "手动刷新";

    public void Start(Window window)
    {
        _window = window;
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(window, WndProc);
            Mode = "Windows 设备事件";
        }
        else if (OperatingSystem.IsLinux())
        {
            StartLinux();
        }
        else
        {
            Faulted?.Invoke(this, "此平台暂不支持自动监听，请手动刷新。");
        }
        AppLog.Write($"Device monitor: {Mode}; no periodic drive polling");
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Ignore DBT_DEVNODES_CHANGED: it also fires for Bluetooth, virtual and
        // other unrelated devices. Volume arrival/removal broadcasts contain a
        // DEV_BROADCAST_VOLUME header whose device type is DBT_DEVTYP_VOLUME.
        if (msg == 0x0219 && ((long)wParam is 0x8000 or 0x8004) && IsVolumeBroadcast(lParam))
            Changed?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }

    private static bool IsVolumeBroadcast(IntPtr data)
    {
        if (data == IntPtr.Zero) return false;
        var size = Marshal.ReadInt32(data);
        return size >= 12 && Marshal.ReadInt32(data, 4) == 2;
    }

    private void StartLinux()
    {
        try
        {
            _linuxMonitor = new Process
            {
                StartInfo = ProcessRunner.Create("udisksctl", ["monitor"]),
                EnableRaisingEvents = true
            };
            _linuxMonitor.OutputDataReceived += (_, e) =>
            {
                if (!_disposed && !string.IsNullOrWhiteSpace(e.Data))
                    Changed?.Invoke(this, EventArgs.Empty);
            };
            _linuxMonitor.ErrorDataReceived += (_, e) =>
            {
                if (!_disposed && !string.IsNullOrWhiteSpace(e.Data))
                    AppLog.Write("UDisks2: " + e.Data);
            };
            _linuxMonitor.Exited += (_, _) =>
            {
                if (!_disposed)
                    Faulted?.Invoke(this, "UDisks2 监听已停止，请检查 udisks2 服务；当前可手动刷新。");
            };
            _linuxMonitor.Start();
            _linuxMonitor.BeginOutputReadLine();
            _linuxMonitor.BeginErrorReadLine();
            Mode = "Linux UDisks2 事件";
        }
        catch (Exception ex)
        {
            AppLog.Write("UDisks2 unavailable", ex);
            Faulted?.Invoke(this, "未找到 udisksctl，请安装 udisks2；当前可手动刷新。");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (OperatingSystem.IsWindows() && _window is not null)
            Win32Properties.RemoveWndProcHookCallback(_window, WndProc);
        if (_linuxMonitor is not null)
        {
            try { if (!_linuxMonitor.HasExited) _linuxMonitor.Kill(true); }
            catch (InvalidOperationException) { }
            _linuxMonitor.Dispose();
        }
    }
}
