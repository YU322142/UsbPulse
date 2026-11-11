using UsbPulse.Services;
using Xunit;

namespace UsbPulse.Tests;

public sealed class DeviceOpenGuardTests
{
    [Fact]
    public async Task FirstClickOpensImmediatelyWithoutWaiting()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());
        var opens = 0;

        var request = guard.TryOpenAsync("E:", () => { opens++; return Task.CompletedTask; });

        Assert.Equal(1, opens);
        Assert.True(request.IsCompletedSuccessfully);
        Assert.True(await request);
    }

    [Fact]
    public async Task SustainedClicksAcrossMultipleFiveSecondIntervalsOpenOnlyOnce()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        var opens = 0;
        Task Open() { opens++; return Task.CompletedTask; }
        Assert.True(await guard.TryOpenAsync("E:", Open));

        for (var i = 0; i < 100; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(200));
            Assert.False(await guard.TryOpenAsync("E:", Open));
        }

        Assert.Equal(1, opens);
    }

    [Theory]
    [InlineData(3000, false)]
    [InlineData(4999, false)]
    [InlineData(5000, true)]
    [InlineData(5001, true)]
    public async Task CooldownExpiresOnlyAfterFiveSeconds(int elapsedMilliseconds, bool expected)
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        await guard.TryOpenAsync("E:", () => Task.CompletedTask);
        time.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.Equal(expected, await guard.TryOpenAsync("E:", () => Task.CompletedTask));
    }

    [Fact]
    public async Task EveryIgnoredClickRestartsTheFiveSecondInterval()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        Task Open() => Task.CompletedTask;
        await guard.TryOpenAsync("E:", Open);
        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(await guard.TryOpenAsync("E:", Open));
        time.Advance(TimeSpan.FromMilliseconds(4999));
        Assert.False(await guard.TryOpenAsync("E:", Open));
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(await guard.TryOpenAsync("E:", Open));
    }

    [Fact]
    public async Task CompletingALaunchDoesNotClearTheCooldown()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = guard.TryOpenAsync("E:", () => launched.Task);
        time.Advance(TimeSpan.FromMilliseconds(100));
        launched.SetResult();
        Assert.True(await first);

        Assert.False(await guard.TryOpenAsync("E:", () => throw new InvalidOperationException("duplicate launch")));
    }

    [Fact]
    public async Task PendingLaunchAndItsRepeatedClicksRemainProtected()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = guard.TryOpenAsync("E:", () => launched.Task);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(await guard.TryOpenAsync("E:", () => throw new InvalidOperationException("duplicate launch")));
        launched.SetResult();
        Assert.True(await first);
        Assert.False(await guard.TryOpenAsync("E:", () => throw new InvalidOperationException("cooldown bypass")));
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(await guard.TryOpenAsync("E:", () => Task.CompletedTask));
    }

    [Fact]
    public async Task SwitchingDevicesDoesNotBypassOrShareTheGuard()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());
        Task Open() => Task.CompletedTask;

        Assert.True(await guard.TryOpenAsync("E:", Open));
        Assert.True(await guard.TryOpenAsync("F:", Open));
        Assert.False(await guard.TryOpenAsync("E:", Open));
    }

    [Fact]
    public async Task FailedOpenCanBeRetriedImmediately()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());

        await Assert.ThrowsAsync<IOException>(() => guard.TryOpenAsync("E:", () => throw new IOException("unavailable")));
        Assert.True(await guard.TryOpenAsync("E:", () => Task.CompletedTask));
    }

    [Fact]
    public async Task CanceledOpenCanBeRetriedImmediately()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.TryOpenAsync("E:",
            () => Task.FromCanceled(new CancellationToken(true))));
        Assert.True(await guard.TryOpenAsync("E:", () => Task.CompletedTask));
    }

    [Fact]
    public async Task ConcurrentRequestsOpenOnlyOnce()
    {
        var guard = new DeviceOpenGuard(new ManualTimeProvider());
        var opens = 0;

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => guard.TryOpenAsync("E:",
            () => { Interlocked.Increment(ref opens); return Task.CompletedTask; }))));

        Assert.Equal(1, opens);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    [Theory]
    [InlineData(1, 999, false)]
    [InlineData(1, 1000, true)]
    [InlineData(12, 11000, false)]
    [InlineData(12, 12000, true)]
    [InlineData(60, 59999, false)]
    [InlineData(60, 60000, true)]
    public async Task UsesTheConfiguredWait(int seconds, int elapsedMilliseconds, bool expected)
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        guard.SetRepeatDelay(TimeSpan.FromSeconds(seconds));
        await guard.TryOpenAsync("E:", () => Task.CompletedTask);
        time.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.Equal(expected, await guard.TryOpenAsync("E:", () => Task.CompletedTask));
    }

    [Fact]
    public async Task ChangingTheWaitAppliesToTheCurrentProtectionInterval()
    {
        var time = new ManualTimeProvider();
        var guard = new DeviceOpenGuard(time);
        await guard.TryOpenAsync("E:", () => Task.CompletedTask);
        time.Advance(TimeSpan.FromSeconds(4));
        guard.SetRepeatDelay(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(await guard.TryOpenAsync("E:", () => Task.CompletedTask));
        guard.SetRepeatDelay(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await guard.TryOpenAsync("E:", () => Task.CompletedTask));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonpositiveWaits(int seconds)
    {
        var guard = new DeviceOpenGuard();

        Assert.Throws<ArgumentOutOfRangeException>(() => guard.SetRepeatDelay(TimeSpan.FromSeconds(seconds)));
    }
}
