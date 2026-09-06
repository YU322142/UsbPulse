using Avalonia;
using UsbPulse.Services;

namespace UsbPulse;

internal static class Program
{
    internal static AppearanceSettings StartupSettings { get; private set; } = new();

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

    public static AppBuilder BuildAvaloniaApp()
    {
        StartupSettings = AppearanceService.Load(Path.Combine(AppLog.DataDirectory, "settings.json"));
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (OperatingSystem.IsWindows())
        {
            var enhanced = StartupSettings.UseAcrylic || StartupSettings.HighRefreshRate;
            builder = builder.With(new Win32PlatformOptions
            {
                CompositionMode = enhanced
                    ? [Win32CompositionMode.WinUIComposition, Win32CompositionMode.RedirectionSurface]
                    : [Win32CompositionMode.RedirectionSurface],
                RenderingMode = enhanced
                    ? [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
                    : [Win32RenderingMode.Software]
            });
        }
        return builder;
    }
}
