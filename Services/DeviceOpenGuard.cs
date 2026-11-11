namespace UsbPulse.Services;

/// <summary>Opens immediately, then ignores repeats until the same device has been idle for the configured interval.</summary>
public sealed class DeviceOpenGuard(TimeProvider? timeProvider = null)
{
    private TimeSpan _repeatDelay = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, OpenAttempt> _attempts = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void SetRepeatDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        lock (_gate) _repeatDelay = delay;
    }

    public async Task<bool> TryOpenAsync(string deviceId, Func<Task> open)
    {
        OpenAttempt attempt;
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (_attempts.TryGetValue(deviceId, out var previous))
            {
                var repeated = previous.Pending || _time.GetElapsedTime(previous.LastAttempt, now) < _repeatDelay;
                previous.LastAttempt = now;
                if (repeated) return false;
            }
            attempt = new OpenAttempt(now);
            _attempts[deviceId] = attempt;
        }

        try
        {
            await open().ConfigureAwait(false);
            // Completing the launch must not release the repeat-click protection early.
            lock (_gate) attempt.Pending = false;
            return true;
        }
        catch
        {
            lock (_gate) _attempts.Remove(deviceId);
            throw;
        }
    }

    private sealed class OpenAttempt(long timestamp)
    {
        public long LastAttempt { get; set; } = timestamp;
        public bool Pending { get; set; } = true;
    }
}
