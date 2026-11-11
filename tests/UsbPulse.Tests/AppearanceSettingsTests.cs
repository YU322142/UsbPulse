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
            Assert.Equal(5, settings.OpenRepeatDelaySeconds);
            Assert.Equal(100, settings.WindowScalePercent);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(0, 0, 1, 75)]
    [InlineData(61, 201, 60, 200)]
    [InlineData(5, 100, 5, 100)]
    [InlineData(15, 150, 15, 150)]
    [InlineData(-1, 1000, 1, 200)]
    [InlineData(60, 75, 60, 75)]
    public void WaitAndWindowScaleStayInSupportedRanges(int seconds, int percent, int expectedSeconds, int expectedPercent)
    {
        var settings = AppearanceService.Normalize(new AppearanceSettings(
            OpenRepeatDelaySeconds: seconds, WindowScalePercent: percent));

        Assert.Equal(expectedSeconds, settings.OpenRepeatDelaySeconds);
        Assert.Equal(expectedPercent, settings.WindowScalePercent);
    }

    [Fact]
    public void SavedWaitAndWindowScaleCanBeLoaded()
    {
        var path = Path.Combine(Path.GetTempPath(), "UsbPulse-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var saved = new AppearanceSettings(Theme: "Dark", OpenRepeatDelaySeconds: 12, WindowScalePercent: 125);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(saved));

            var loaded = AppearanceService.Load(path);

            Assert.Equal(saved, loaded);
        }
        finally { File.Delete(path); }
    }
}
