using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace UsbPulse.Tests;

public sealed class WindowsDeviceMonitorTests
{
    [Theory]
    [InlineData(2, true)]
    [InlineData(5, false)]
    public void AcceptsOnlyVolumeBroadcastHeaders(int deviceType, bool expected)
    {
        var data = Marshal.AllocHGlobal(20);
        try
        {
            Marshal.WriteInt32(data, 20);
            Marshal.WriteInt32(data, 4, deviceType);
            var method = typeof(DeviceMonitor).GetMethod("IsVolumeBroadcast",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            Assert.Equal(expected, method.Invoke(null, [data]));
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}
