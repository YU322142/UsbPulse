using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UsbPulse.Models;

public sealed record StorageDevice(string Id, string Label, string MountPath, string DiskPath, long Size = 0)
{
    public string DisplayName => $"{Label} ({(OperatingSystem.IsWindows() ? MountPath.TrimEnd('\\') : Id)})";
}

public sealed class DeviceState : INotifyPropertyChanged
{
    private IReadOnlyList<StorageDevice> _devices = [];
    private StorageDevice? _selected;
    private bool _busy;
    private string _message = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<StorageDevice> Devices => _devices;
    public StorageDevice? Selected
    {
        get => _selected;
        set { _selected = value; NotifyAll(); }
    }
    public bool Busy { get => _busy; set { _busy = value; NotifyAll(); } }
    public string Message { get => _message; set { _message = value; NotifyAll(); } }
    public bool HasMessage => Message.Length > 0;
    public bool HasDevice => Selected is not null;
    public bool HasMultiple => Devices.Count > 1;
    public bool CanEject => HasDevice && !Busy;
    public bool CanOpen => HasDevice && !Busy && Selected!.MountPath.Length > 0;
    public string Heading => Busy ? "正在处理设备" : Devices.Count switch
    {
        0 => "等待设备接入",
        1 => "存储设备已连接",
        _ => $"已连接 {Devices.Count} 个存储设备"
    };
    public string Detail => Selected?.DisplayName ?? "暂无可移动存储设备";
    public string Capacity => Selected is null ? "UsbPulse" : Selected.MountPath.Length == 0
        ? "尚未挂载" : Selected.Size > 0 ? $"{Selected.Size / 1073741824d:0.#} GB" : "已就绪";

    public bool Apply(IReadOnlyList<StorageDevice> devices)
    {
        var inserted = devices.FirstOrDefault(d => !_devices.Any(old => old.Id == d.Id));
        _selected = inserted ?? devices.FirstOrDefault(d => d.Id == Selected?.Id) ?? devices.FirstOrDefault();
        _devices = devices;
        NotifyAll();
        return inserted is not null;
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
