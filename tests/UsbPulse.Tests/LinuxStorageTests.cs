using UsbPulse.Services;
using Xunit;

namespace UsbPulse.Tests;

public sealed class LinuxStorageTests
{
    [Fact]
    public void ParseLinux_ReturnsExternalPartitionsAndIgnoresInternalDisk()
    {
        const string json = """
        {
          "blockdevices": [
            { "path":"/dev/nvme0n1", "label":null, "type":"disk", "rm":false, "tran":"nvme",
              "mountpoints":[null], "size":1000,
              "children":[{"path":"/dev/nvme0n1p1","label":null,"type":"part","rm":false,"tran":null,"mountpoints":["/"],"size":900}] },
            { "path":"/dev/sdb", "label":null, "type":"disk", "rm":true, "tran":"usb",
              "mountpoints":[null], "size":64000000000,
              "children":[{"path":"/dev/sdb1","label":"USB DATA","type":"part","rm":true,"tran":null,"mountpoints":["/run/media/me/USB DATA"],"size":63000000000}] }
          ]
        }
        """;

        var devices = StorageService.ParseLinux(json);

        var device = Assert.Single(devices);
        Assert.Equal("/dev/sdb1", device.Id);
        Assert.Equal("/dev/sdb", device.DiskPath);
        Assert.Equal("USB DATA", device.Label);
        Assert.Equal("/run/media/me/USB DATA", device.MountPath);
    }
}
