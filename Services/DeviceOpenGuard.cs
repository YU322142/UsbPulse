namespace UsbPulse.Services;

/// <summary>Coalesces pending launches, with a three-second fallback when window detection is unavailable.</summary>
public sealed class DeviceOpenGuard(TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromSeconds(3);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, OpenAttempt> _attempts = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // The callback returns true when the target window has been confirmed and can be reused.
    public async Task<bool> TryOpenAsync(string deviceId, Func<Task<bool>> open)
    {
        OpenAttempt attempt;
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (_attempts.TryGetValue(deviceId, out var previous))
            {
                var repeated = previous.Pending || _time.GetElapsedTime(previous.LastAttempt, now) < RepeatDelay;
                previous.LastAttempt = now;
                if (repeated) return false;
            }
            attempt = new OpenAttempt(now);
            _attempts[deviceId] = attempt;
        }

        try
        {
            var confirmed = await open().ConfigureAwait(false);
            lock (_gate)
            {
                if (confirmed) _attempts.Remove(deviceId);
                else attempt.Pending = false;
            }
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
