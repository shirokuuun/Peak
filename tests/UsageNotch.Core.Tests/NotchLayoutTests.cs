using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public sealed class NotchLayoutTests
{
    [Theory]
    [InlineData(false, false, 360, 42)]
    [InlineData(false, true, 600, 78)]
    [InlineData(true, false, 600, 42)]
    [InlineData(true, true, 600, 78)]
    public void DynamicIslandGrowsForActivityAndDetails(bool expanded, bool active, double width, double header)
    {
        var layout = NotchLayout.Calculate(new(), expanded, active);
        Assert.Equal(width, layout.Width); Assert.Equal(header, layout.HeaderHeight);
        Assert.False(layout.AttachedToTop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedNotchKeepsItsRestingSizeUntilOpened(bool active)
    {
        var layout = NotchLayout.Calculate(new() { NotchStyle = "FixedNotch" }, false, active);
        Assert.Equal(360, layout.Width); Assert.Equal(42, layout.HeaderHeight);
        Assert.False(layout.ShowActivity); Assert.True(layout.AttachedToTop);
        var opened = NotchLayout.Calculate(new() { NotchStyle = "FixedNotch" }, true, active);
        Assert.Equal(600, opened.Width); Assert.Equal(active ? 78 : 42, opened.HeaderHeight);
        Assert.Equal(active, opened.ShowActivity);
    }

    [Fact]
    public void ExistingAndInvalidPreferencesKeepDynamicIsland()
    {
        Assert.Equal("DynamicIsland", JsonSerializer.Deserialize<AppSettings>("{}")!.Normalize().NotchStyle);
        Assert.Equal("DynamicIsland", new AppSettings { NotchStyle = "invalid" }.Normalize().NotchStyle);
    }

    [Fact]
    public void StyleAndSavedFloatingGapRoundTrip()
    {
        var chosen = new AppSettings { NotchStyle = "FixedNotch", TopOffset = 28 }.Normalize();
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(chosen))!.Normalize();
        Assert.Equal(chosen, restored);
        Assert.Equal(28, restored.TopOffset);
    }
}
