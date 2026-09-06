using System.Diagnostics;

namespace UsbPulse.Services;

public static class ProcessRunner
{
    public static ProcessStartInfo Create(string file, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["LC_ALL"] = "C";
        return info;
    }

    public static async Task<string> RunAsync(string file, IEnumerable<string> args, CancellationToken token)
    {
        using var process = new Process { StartInfo = Create(file, args) };
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
                throw new IOException(string.IsNullOrWhiteSpace(error) ? $"{file}: {process.ExitCode}" : error.Trim());
            return output;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr); } catch { }
            throw;
        }
    }
}
