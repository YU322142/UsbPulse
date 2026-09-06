namespace UsbPulse.Services;

// Coalesce bursts and retry mount readiness only after an event; no idle scanning.
public sealed class EventRefresh(Func<CancellationToken, Task> refresh) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private readonly object _sync = new();
    private bool _disposed;

    public void Request(bool settle = true)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _ = RunAsync(_pending, settle);
        }
    }

    private async Task RunAsync(CancellationTokenSource request, bool settle)
    {
        var token = request.Token;
        try
        {
            int[] delays = settle ? [350, 1100, 2200] : [0];
            foreach (var delay in delays)
            {
                if (delay > 0) await Task.Delay(delay, token);
                await _gate.WaitAsync(token);
                try { await refresh(token); }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("Device refresh failed", ex); }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pending, request)) _pending = null;
                request.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _lifetime.Cancel();
            // Pending scans may still own the semaphore; they release it before finishing.
        }
    }
}
