using System.ComponentModel;

namespace UsbPulse.Models;

public sealed record StorageDevice(string Id, string Label, string MountPath, string DiskPath,
    long Size = 0, long? FreeSpace = null)
{
    public string DisplayName => $"{Label} ({(OperatingSystem.IsWindows() ? MountPath.TrimEnd('\\') : Id)})";
    public long? AvailableBytes => Size > 0 && FreeSpace is >= 0 ? Math.Min(FreeSpace.Value, Size) : null;
    public long? UsedBytes => AvailableBytes is { } free ? Size - free : null;
    public double UsedPercentage => UsedBytes is { } used ? (double)used / Size * 100 : 0;

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

public sealed class DeviceState : INotifyPropertyChanged
{
    private IReadOnlyList<StorageDevice> _devices = [];
    private StorageDevice? _selected;
    private bool _busy;
    private bool _applyingSnapshot;
    private string _message = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<StorageDevice> Devices => _devices;
    public StorageDevice? Selected
    {
        get => _selected;
        set
        {
            if (_applyingSnapshot || _selected == value) return;
            _selected = value;
            NotifyAll();
        }
    }
    public bool Busy
    {
        get => _busy;
        set { if (_busy != value) { _busy = value; NotifyAll(); } }
    }
    public string Message
    {
        get => _message;
        set { if (_message != value) { _message = value; NotifyAll(); } }
    }
    public bool HasMessage => Message.Length > 0;
    public bool HasDevice => Selected is not null;
    public bool HasMultiple => Devices.Count > 1;
    public bool CanEject => HasDevice && !Busy;
    public bool CanOpen => HasDevice && !Busy && Selected!.MountPath.Length > 0;
    public string Detail => Selected?.DisplayName ?? "";
    public string Capacity => Selected is not { } device ? "" : device.Size <= 0 ? "容量未知"
        : device.AvailableBytes is { } free
            ? $"可用 {StorageDevice.FormatSize(free)} / {StorageDevice.FormatSize(device.Size)}"
            : $"总计 {StorageDevice.FormatSize(device.Size)}";
    public bool HasUsage => Selected?.AvailableBytes is not null;
    public double UsedPercentage => Selected?.UsedPercentage ?? 0;
    public bool IsNearlyFull => HasUsage && UsedPercentage >= 90;
    public string UsageDescription => Selected?.UsedBytes is { } used
        ? $"已用 {StorageDevice.FormatSize(used)}（{UsedPercentage:0.#}%），{Capacity}"
        : Capacity;
    public string EjectLabel => Busy ? "弹出中…" : "弹出";

    public bool Apply(IReadOnlyList<StorageDevice> devices)
    {
        var inserted = devices.FirstOrDefault(d => !_devices.Any(old => old.Id == d.Id));
        var selected = inserted ?? devices.FirstOrDefault(d => d.Id == Selected?.Id) ?? devices.FirstOrDefault();
        var changed = _selected != selected || !_devices.SequenceEqual(devices);
        _devices = devices;
        if (changed)
        {
            // Replacing ComboBox.ItemsSource can send its old selection back during notification.
            _applyingSnapshot = true;
            try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Devices))); }
            finally { _applyingSnapshot = false; }
            _selected = selected;
            NotifyAll();
        }
        return inserted is not null;
    }

    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
