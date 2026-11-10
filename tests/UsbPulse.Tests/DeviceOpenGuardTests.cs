using UsbPulse.Services;
using Xunit;

namespace UsbPulse.Tests;

public sealed class DeviceOpenGuardTests
{
    [Fact]
    public async Task FirstClickOpensImmediatelyAndRapidFallbackClicksOpenOnlyOnce()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        var opens = 0;
        Task<bool> Open() { opens++; return Task.FromResult(false); }

        Assert.True(await guard.TryOpenAsync("E:", Open));
        Assert.Equal(1, opens);
        for (var i = 0; i < 20; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(200));
            Assert.False(await guard.TryOpenAsync("E:", Open));
        }
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task FallbackAllowsRetryAfterThreeSecondsWithoutClicks()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        Task<bool> Open() => Task.FromResult(false);
        await guard.TryOpenAsync("E:", Open);
        time.Advance(TimeSpan.FromMilliseconds(2999));
        Assert.False(await guard.TryOpenAsync("E:", Open));
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.True(await guard.TryOpenAsync("E:", Open));
    }

    [Fact]
    public async Task PendingWindowPreventsAnotherLaunchEvenAfterFallbackInterval()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        var windowReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = guard.TryOpenAsync("E:", () => windowReady.Task);
        time.Advance(TimeSpan.FromSeconds(4));

        Assert.False(await guard.TryOpenAsync("E:", () => throw new InvalidOperationException("duplicate launch")));
        Assert.False(pending.IsCompleted);
        windowReady.SetResult(true);
        Assert.True(await pending);
    }

    [Fact]
    public async Task ConfirmedWindowCanBeReusedWithoutWaitingThreeSeconds()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());
        Task<bool> OpenOrActivate() => Task.FromResult(true);

        Assert.True(await guard.TryOpenAsync("E:", OpenOrActivate));
        Assert.True(await guard.TryOpenAsync("E:", OpenOrActivate));
    }

    [Fact]
    public async Task SwitchingDevicesDoesNotBypassOrShareTheFallbackGuard()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());
        Task<bool> Open() => Task.FromResult(false);

        Assert.True(await guard.TryOpenAsync("E:", Open));
        Assert.True(await guard.TryOpenAsync("F:", Open));
        Assert.False(await guard.TryOpenAsync("E:", Open));
    }

    [Fact]
    public async Task FailedOpenCanBeRetriedImmediately()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());

        await Assert.ThrowsAsync<IOException>(() => guard.TryOpenAsync("E:", () => throw new IOException("unavailable")));
        Assert.True(await guard.TryOpenAsync("E:", () => Task.FromResult(false)));
    }

    [Fact]
    public async Task CanceledOpenCanBeRetriedImmediately()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.TryOpenAsync("E:",
            () => Task.FromCanceled<bool>(new CancellationToken(true))));
        Assert.True(await guard.TryOpenAsync("E:", () => Task.FromResult(false)));
    }

    [Fact]
    public async Task ConcurrentRequestsOpenOnlyOnce()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());
        var opens = 0;

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => guard.TryOpenAsync("E:",
            () => { Interlocked.Increment(ref opens); return Task.FromResult(false); }))));

        Assert.Equal(1, opens);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }
}
