using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Core.Tests;

public class NotchPaletteTests
{
    [Fact]
    public void TextAndStatusColorsStayReadableAcrossTheColorSpace()
    {
        var samples = Enumerable.Range(0, 256).Select(i => new RgbColor((byte)i, (byte)i, (byte)i)).ToList();
        for (var r = 0; r <= 255; r += 17)
        for (var g = 0; g <= 255; g += 17)
        for (var b = 0; b <= 255; b += 17) samples.Add(new((byte)r, (byte)g, (byte)b));
        foreach (var color in samples)
        {
            var palette = NotchPalette.Create(color);
            Assert.Equal(color, palette.Surface); AssertReadable(palette);
        }
    }
    [Fact]
    public void BuiltInThemesHaveReadableTextAndStatuses()
    {
        AssertReadable(NotchPalette.Create(new(9, 9, 9), new(24, 24, 24), new(34, 34, 34), new(59, 59, 59)));
        AssertReadable(NotchPalette.Create(new(246, 246, 246), new(236, 236, 236), RgbColor.White, new(217, 217, 217)));
        Assert.Equal(RgbColor.Black, NotchPalette.Create(RgbColor.White).Text);
        Assert.Equal(RgbColor.White, NotchPalette.Create(RgbColor.Black).Text);
        Assert.Equal(21, RgbColor.White.Contrast(RgbColor.Black), 10);
    }
    private static void AssertReadable(NotchPalette palette)
    {
        foreach (var background in new[] { palette.Surface, palette.Panel, palette.Card, palette.Track })
        foreach (var foreground in new[] { palette.Text, palette.Muted, palette.Success, palette.Warning, palette.Critical, palette.Accent, palette.Activity })
            Assert.True(background.Contrast(foreground) >= 4.5,
                $"{foreground.Hex} on {background.Hex}: {background.Contrast(foreground)}");
    }
    [Theory]
    [InlineData("#f0a", "#FF00AA")]
    [InlineData(" aFa4f6 ", "#AFA4F6")]
    [InlineData("#ffffff", "#FFFFFF")]
    public void CustomColorsNormalizeAndRoundTrip(string input, string expected)
    {
        var settings = new AppSettings { Theme = "Custom", CustomColor = input }.Normalize();
        var saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Custom", saved.Theme); Assert.Equal(expected, saved.CustomColor);
    }
    [Theory]
    [InlineData(null)] [InlineData("#80FFFFFF")] [InlineData("transparent")]
    [InlineData("#ZZZZZZ")] [InlineData("##ffffff")] [InlineData("#12")]
    public void InvalidOrTransparentColorsUseAnOpaqueFallback(string? input)
    {
        Assert.False(RgbColor.TryParse(input, out _));
        Assert.Equal(NotchPalette.DefaultColor, new AppSettings { CustomColor = input! }.Normalize().CustomColor);
    }
    [Fact]
    public void ExistingSettingsRetainTheirThemeAndGainADefaultCustomColor()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":\"Light\",\"TopOffset\":20}")!.Normalize();
        Assert.Equal("Light", old.Theme); Assert.Equal(20, old.TopOffset); Assert.Equal(NotchPalette.DefaultColor, old.CustomColor);
    }
    [Fact]
    public void PaletteAndHexConversionPreserveEveryChannel()
    {
        for (var r = 0; r <= 255; r += 17)
        for (var g = 0; g <= 255; g += 17)
        for (var b = 0; b <= 255; b += 17)
        {
            var color = new RgbColor((byte)r, (byte)g, (byte)b); var hsv = color.ToHsv();
            Assert.Equal(color, RgbColor.FromHsv(hsv.Hue, hsv.Saturation, hsv.Brightness));
        }
    }
}
