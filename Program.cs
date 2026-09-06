using Avalonia;
using UsbPulse.Services;

namespace UsbPulse;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                AppLog.Write("Unhandled exception", e.ExceptionObject as Exception);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failed", ex);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
