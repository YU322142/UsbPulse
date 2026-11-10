using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using UsbPulse.Models;

namespace UsbPulse.Services;

public sealed class StorageService
{
    public async Task<IReadOnlyList<StorageDevice>> GetDevicesAsync(CancellationToken token)
    {
        if (OperatingSystem.IsLinux())
        {
            var json = await ProcessRunner.RunAsync("lsblk",
                ["--json", "--bytes", "--paths", "--tree", "--output",
                    "PATH,LABEL,TYPE,RM,TRAN,MOUNTPOINTS,SIZE,FSSIZE,FSAVAIL"], token);
            return ParseLinux(json);
        }
        return await Task.Run<IReadOnlyList<StorageDevice>>(() =>
        {
            var list = new List<StorageDevice>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var external = drive.DriveType == DriveType.Removable ||
                        (OperatingSystem.IsWindows() && drive.DriveType == DriveType.Fixed && IsUsb(drive.Name));
                    if (!external || !drive.IsReady) continue;
                    long? available = null;
                    try { available = drive.AvailableFreeSpace; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    list.Add(new StorageDevice(drive.Name, string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? "可移动磁盘" : drive.VolumeLabel, drive.Name, drive.Name, drive.TotalSize, available));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return list;
        }, token);
    }

    public static IReadOnlyList<StorageDevice> ParseLinux(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<StorageDevice>();
        foreach (var disk in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
            Visit(disk, false, "");
        return result.DistinctBy(d => d.Id).ToArray();

        void Visit(JsonElement node, bool external, string parent)
        {
            var path = Text(node, "path");
            var type = Text(node, "type");
            external |= Text(node, "tran") == "usb" || (node.TryGetProperty("rm", out var rm) &&
                (rm.ValueKind == JsonValueKind.True || rm.ToString() == "1"));
            if (type == "disk") parent = path;
            // Do not expose internal mounts even if a device was misclassified.
            var mounts = node.TryGetProperty("mountpoints", out var m) && m.ValueKind == JsonValueKind.Array
                ? m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
                    .Where(x => x.Length > 0).ToArray() : [];
            var hasChildren = node.TryGetProperty("children", out var children) && children.GetArrayLength() > 0;
            if (external && !hasChildren && type is "part" or "disk" && path.StartsWith("/dev/", StringComparison.Ordinal) &&
                !mounts.Any(x => x is "/" or "/boot" or "/boot/efi" or "/home" or "[SWAP]"))
            {
                var label = Text(node, "label");
                _ = long.TryParse(Text(node, "size"), out var size);
                if (long.TryParse(Text(node, "fssize"), out var fileSystemSize) && fileSystemSize > 0)
                    size = fileSystemSize;
                long? available = mounts.Length > 0 && long.TryParse(Text(node, "fsavail"), out var free) && free >= 0
                    ? free : null;
                result.Add(new(path, string.IsNullOrWhiteSpace(label) ? Path.GetFileName(path) : label,
                    mounts.FirstOrDefault() ?? "", parent, size, available));
            }
            if (hasChildren)
                foreach (var child in children.EnumerateArray()) Visit(child, external, parent);
        }
    }

    private static string Text(JsonElement node, string key) =>
        node.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";

    public Task OpenAsync(StorageDevice device, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (device.MountPath.Length == 0) throw new IOException("设备尚未挂载，请在系统文件管理器中挂载。");
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(device.MountPath);
        using var process = Process.Start(info) ?? throw new IOException("无法启动文件管理器。");
        return Task.CompletedTask;
    }

    public async Task EjectAsync(StorageDevice device, CancellationToken token)
    {
        var current = await GetDevicesAsync(token);
        var live = current.FirstOrDefault(d => d.Id == device.Id);
        if (live is null) throw new IOException("设备已移除。");
        if (OperatingSystem.IsWindows())
        {
            await EjectWindowsAsync(live.MountPath);
            // Shell's verb only submits a request. Observe removal before reporting success.
            for (var attempt = 0; attempt < 16; attempt++)
            {
                await Task.Delay(500, token);
                if (!(await GetDevicesAsync(token)).Any(d => d.Id == live.Id)) return;
            }
            throw new IOException("设备仍在使用或系统未确认弹出。请关闭文件后重试。");
        }
        else if (OperatingSystem.IsLinux())
        {
            var siblings = current.Where(d => d.DiskPath == live.DiskPath);
            foreach (var part in siblings.Where(d => d.MountPath.Length > 0))
                await ProcessRunner.RunAsync("udisksctl", ["unmount", "--no-user-interaction", "-b", part.Id], token);
            await ProcessRunner.RunAsync("udisksctl", ["power-off", "--no-user-interaction", "-b", live.DiskPath], token);
        }
        else if (OperatingSystem.IsMacOS())
            await ProcessRunner.RunAsync("diskutil", ["eject", live.MountPath], token);
        else throw new PlatformNotSupportedException("此平台暂不支持弹出。");
    }

    [SupportedOSPlatform("windows")]
    private static Task EjectWindowsAsync(string root)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null, computer = null, item = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
                computer = ((dynamic)shell!).NameSpace(17);
                item = ((dynamic)computer!).ParseName(root.TrimEnd('\\'));
                if (item is null) throw new IOException("无法找到待弹出的设备。");
                ((dynamic)item).InvokeVerb("Eject");
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                foreach (var obj in new[] { item, computer, shell })
                    if (obj is not null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
            }
        })
        { IsBackground = true, Name = "UsbPulse safe eject" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsUsb(string root)
    {
        using var handle = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return false;
        byte[] query = new byte[12], descriptor = new byte[1024];
        return DeviceIoControl(handle, 0x002D1400, query, query.Length, descriptor, descriptor.Length,
            out var count, IntPtr.Zero) && count >= 32 && BitConverter.ToInt32(descriptor, 28) == 7;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        byte[] output, int outputSize, out int returned, IntPtr overlapped);
}
