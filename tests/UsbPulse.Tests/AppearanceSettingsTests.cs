using UsbPulse.Services;
using Xunit;

namespace UsbPulse.Tests;

public sealed class AppearanceSettingsTests
{
    [Fact]
    public void LegacySettingsKeepEnhancedRenderingDisabled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UsbPulse.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"Theme\":\"Dark\",\"Accent\":\"Jade\",\"AlwaysOnTop\":true}");

            var settings = AppearanceService.Load(path);

            Assert.Equal("Dark", settings.Theme);
            Assert.Equal("Jade", settings.Accent);
            Assert.True(settings.AlwaysOnTop);
            Assert.False(settings.UseAcrylic);
            Assert.False(settings.HighRefreshRate);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
