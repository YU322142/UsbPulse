using UsbPulse.Models;
using Xunit;

namespace UsbPulse.Tests;

public sealed class StorageCapacityTests
{
    private const long Gigabyte = 1073741824;

    [Fact]
    public void RemovingTheLastDeviceClearsTheNameAndCapacity()
    {
        var state = new DeviceState();
        state.Apply([new("E:", "USB", "E:", "E:", 64 * Gigabyte, 40 * Gigabyte)]);

        state.Apply([]);

        Assert.False(state.HasDevice);
        Assert.Equal("", state.Detail);
        Assert.Equal("", state.Capacity);
        Assert.False(state.HasUsage);
        Assert.False(state.CanOpen);
        Assert.False(state.CanEject);
    }

    [Fact]
    public void ShowsAvailableAndTotalCapacityWithTheUsedFraction()
    {
        var state = new DeviceState();
        state.Apply([new("E:", "USB", "E:", "E:", 64 * Gigabyte, 40 * Gigabyte)]);

        Assert.Equal("可用 40 GB / 64 GB", state.Capacity);
        Assert.Equal(37.5, state.UsedPercentage);
        Assert.True(state.HasUsage);
        Assert.False(state.IsNearlyFull);
        Assert.Contains("24 GB", state.UsageDescription);
    }

    [Theory]
    [InlineData(0, 100, true)]
    [InlineData(10, 90, true)]
    [InlineData(11, 89, false)]
    [InlineData(100, 0, false)]
    [InlineData(120, 0, false)]
    public void EmptyFullAndNearFullVolumesHaveValidPercentages(long free, double expected, bool nearlyFull)
    {
        var state = new DeviceState();
        state.Apply([new("E:", "USB", "E:", "E:", 100, free)]);

        Assert.True(state.HasUsage);
        Assert.Equal(expected, state.UsedPercentage);
        Assert.Equal(nearlyFull, state.IsNearlyFull);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public void UnreadableFreeSpaceDoesNotPretendTheDeviceIsEmptyOrFull(long? free)
    {
        var state = new DeviceState();
        state.Apply([new("E:", "USB", "E:", "E:", 64 * Gigabyte, free)]);

        Assert.Equal("总计 64 GB", state.Capacity);
        Assert.False(state.HasUsage);
        Assert.False(state.IsNearlyFull);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void UnknownTotalDoesNotDivideByZeroOrShowAnInvalidUsageBar(long size)
    {
        var state = new DeviceState();
        state.Apply([new("E:", "USB", "E:", "E:", size, 0)]);

        Assert.Equal("容量未知", state.Capacity);
        Assert.False(state.HasUsage);
        Assert.Equal(0, state.UsedPercentage);
    }

    [Fact]
    public void UpdatedFreeSpaceRefreshesCapacityWithoutChangingTheSelectedDevice()
    {
        var state = new DeviceState();
        var device = new StorageDevice("E:", "USB", "E:", "E:", 64 * Gigabyte, 40 * Gigabyte);
        state.Apply([device]);
        var notified = false;
        state.PropertyChanged += (_, _) => notified = true;

        Assert.False(state.Apply([device with { FreeSpace = 16 * Gigabyte }]));

        Assert.Equal(device.Id, state.Selected!.Id);
        Assert.Equal("可用 16 GB / 64 GB", state.Capacity);
        Assert.Equal(75, state.UsedPercentage);
        Assert.True(notified);
    }

    [Fact]
    public void SelectingAnotherDeviceChangesTheUsageBar()
    {
        var state = new DeviceState();
        var first = new StorageDevice("E:", "USB", "E:", "E:", 100, 60);
        var second = new StorageDevice("F:", "BACKUP", "F:", "F:", 200, 10);
        state.Apply([first, second]);
        state.Selected = first;
        Assert.Equal(40, state.UsedPercentage);

        state.Selected = second;

        Assert.Equal(95, state.UsedPercentage);
        Assert.True(state.IsNearlyFull);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1099511627776L, "1 TB")]
    public void CapacityTextUsesReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, StorageDevice.FormatSize(bytes));
    }
}
