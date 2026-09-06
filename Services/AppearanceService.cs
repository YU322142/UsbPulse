using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace UsbPulse.Services;

public sealed record AppearanceSettings(
    string Theme = "System",
    string Accent = "Ocean",
    bool AlwaysOnTop = false,
    bool UseAcrylic = false,
    bool HighRefreshRate = false);
public sealed record AccentPalette(string Id, string Name, string Light, string Dark);

public sealed class AppearanceService
{
    public static readonly AccentPalette[] Palettes =
    [
        new("Ocean", "海蓝", "#0067C0", "#60CDFF"),
        new("Jade", "青绿", "#087F63", "#5DDBB1"),
        new("Amber", "琥珀", "#A15C00", "#F5BB55"),
        new("Rose", "玫红", "#B1255A", "#FF91B5"),
        new("Violet", "鸢尾", "#6D47B5", "#C7A4FF"),
        new("Graphite", "石墨", "#53606D", "#C3CBD3")
    ];
    private readonly Application _app;
    public AppearanceSettings Settings { get; private set; }
    public event EventHandler? Changed;

    public AppearanceService(Application app)
    {
        _app = app;
        Settings = Load(Path.Combine(AppLog.DataDirectory, "settings.json"));
        _app.ActualThemeVariantChanged += (_, _) => UpdateAccent();
    }

    public static AppearanceSettings Load(string path)
    {
        try { return Normalize(JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path)) ?? new()); }
        catch { return new(); }
    }

    public static AppearanceSettings Normalize(AppearanceSettings settings) => settings with
    {
        Theme = settings.Theme is "Light" or "Dark" or "System" ? settings.Theme : "System",
        Accent = Palettes.Any(p => p.Id == settings.Accent) ? settings.Accent : "Ocean"
    };

    public void Update(AppearanceSettings settings)
    {
        Settings = Normalize(settings);
        Apply();
        Directory.CreateDirectory(AppLog.DataDirectory);
        var path = Path.Combine(AppLog.DataDirectory, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Settings));
        File.Move(path + ".tmp", path, true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Apply()
    {
        _app.RequestedThemeVariant = Settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        UpdateAccent();
    }

    private void UpdateAccent()
    {
        var palette = Palettes.First(p => p.Id == Settings.Accent);
        var color = Color.Parse(_app.ActualThemeVariant == ThemeVariant.Dark ? palette.Dark : palette.Light);
        var theme = _app.Styles.OfType<FluentAvaloniaTheme>().Single();
        theme.CustomAccentColor = color;
        _app.Resources["UsbAccentBrush"] = new SolidColorBrush(color);
        _app.Resources["UsbAccentSoftBrush"] = new SolidColorBrush(color, 0.13);
    }
}
