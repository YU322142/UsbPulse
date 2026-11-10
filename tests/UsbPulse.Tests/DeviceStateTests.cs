using UsbPulse.Models;
using Xunit;

namespace UsbPulse.Tests;

public sealed class DeviceStateTests
{
    [Fact]
    public void Apply_SelectsNewDeviceAndKeepsAllDevices()
    {
        var first = new StorageDevice("E:\\", "FIRST", "E:\\", "E:\\");
        var second = new StorageDevice("F:\\", "SECOND", "F:\\", "F:\\");
        var state = new DeviceState();

        Assert.True(state.Apply([first]));
        Assert.True(state.Apply([first, second]));

        Assert.Equal(second, state.Selected);
        Assert.True(state.HasMultiple);
        Assert.Equal(second.DisplayName, state.Detail);
    }

    [Fact]
    public void Apply_SelectsRemainingDeviceAfterRemoval()
    {
        var first = new StorageDevice("E:\\", "FIRST", "E:\\", "E:\\");
        var second = new StorageDevice("F:\\", "SECOND", "F:\\", "F:\\");
        var state = new DeviceState();
        state.Apply([first, second]);
        state.Selected = second;

        Assert.False(state.Apply([first]));

        Assert.Equal(first, state.Selected);
        Assert.False(state.HasMultiple);
    }

    [Fact]
    public void Apply_KeepsNewDeviceWhenItemsBindingReportsThePreviousSelection()
    {
        var first = new StorageDevice("E:\\", "FIRST", "E:\\", "E:\\");
        var second = new StorageDevice("F:\\", "SECOND", "F:\\", "F:\\");
        var state = new DeviceState();
        state.Apply([first]);
        state.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceState.Devices)) state.Selected = first;
        };

        Assert.True(state.Apply([first, second]));

        Assert.Equal(second, state.Selected);
    }

    [Fact]
    public void Apply_DoesNotNotifyWhenSnapshotIsUnchanged()
    {
        var state = new DeviceState();
        var device = new StorageDevice("D:\\", "USB", "D:\\", "D:\\");
        state.Apply([device]);
        var notifications = 0;
        state.PropertyChanged += (_, _) => notifications++;

        var inserted = state.Apply([device]);

        Assert.False(inserted);
        Assert.Equal(0, notifications);
    }
}
