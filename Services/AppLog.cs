namespace UsbPulse.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    public static string DataDirectory => Environment.GetEnvironmentVariable("USBPULSE_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsbPulse");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDirectory);
                var path = Path.Combine(DataDirectory, "UsbPulse.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path,
                    $"{DateTimeOffset.Now:O} {message}{(exception is null ? "" : "\n" + exception)}\n");
            }
        }
        catch { /* Logging cannot prevent startup or shutdown. */ }
    }
}
