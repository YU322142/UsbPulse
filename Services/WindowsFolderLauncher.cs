using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.CSharp.RuntimeBinder;

namespace UsbPulse.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsFolderLauncher
{
    public static Task<bool> OpenAsync(string path, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var existing = FindWindow(path);
                if (existing is { } handle && handle != IntPtr.Zero)
                {
                    if (IsIconic(handle)) ShowWindowAsync(handle, 9);
                    SetForegroundWindow(handle);
                    completion.TrySetResult(true);
                    return;
                }

                var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                info.ArgumentList.Add(path);
                using var process = Process.Start(info) ?? throw new IOException("无法启动资源管理器。");

                // Only inspect windows after a launch, never poll while the application is idle.
                var elapsed = Stopwatch.StartNew();
                while (existing is not null && elapsed.Elapsed < TimeSpan.FromSeconds(3))
                {
                    token.ThrowIfCancellationRequested();
                    existing = FindWindow(path);
                    if (existing is { } opened && opened != IntPtr.Zero)
                    {
                        completion.TrySetResult(true);
                        return;
                    }
                    if (token.WaitHandle.WaitOne(100)) token.ThrowIfCancellationRequested();
                }
                completion.TrySetResult(false);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "UsbPulse open folder" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    // null means Shell window discovery is unavailable; zero means no visible match yet.
    private static IntPtr? FindWindow(string path)
    {
        object? shell = null, windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
            windows = ((dynamic)shell!).Windows();
            var target = Path.GetFullPath(path).TrimEnd('\\', '/');
            var count = (int)((dynamic)windows).Count;
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = ((dynamic)windows).Item(i);
                    if (window is null) continue;
                    var location = (string)((dynamic)window).LocationURL;
                    if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || !uri.IsFile ||
                        !string.Equals(uri.LocalPath.TrimEnd('\\', '/'), target, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var handle = new IntPtr((long)((dynamic)window).HWND);
                    if (IsWindowVisible(handle)) return handle;
                }
                // Explorer can navigate or close a window while it is being inspected.
                catch (Exception ex) when (ex is COMException or InvalidCastException or RuntimeBinderException) { }
                finally { Release(window); }
            }
            return IntPtr.Zero;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or RuntimeBinderException)
        {
            AppLog.Write("Explorer window discovery unavailable; using repeat-click fallback", ex);
            return null;
        }
        finally
        {
            Release(windows);
            Release(shell);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
