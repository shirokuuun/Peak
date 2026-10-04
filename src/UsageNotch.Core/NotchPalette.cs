using System.Globalization;

namespace UsageNotch.Core;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public static RgbColor Black => new(0, 0, 0);
    public static RgbColor White => new(255, 255, 255);
    public static bool TryParse(string? value, out RgbColor color)
    {
        color = default;
        var hex = value?.Trim();
        if (hex?.StartsWith('#') == true) hex = hex[1..];
        if (hex?.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex?.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
    public RgbColor Mix(RgbColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return new(Channel(R, other.R), Channel(G, other.G), Channel(B, other.B));
    }
    // WCAG sRGB relative luminance, calculated on opaque colors.
    public double Luminance
    {
        get
        {
            static double Linear(byte channel)
            {
                var value = channel / 255d;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(R) + .7152 * Linear(G) + .0722 * Linear(B);
        }
    }
    public double Contrast(RgbColor other) => (Math.Max(Luminance, other.Luminance) + .05) /
        (Math.Min(Luminance, other.Luminance) + .05);
    public static RgbColor FromHsv(double hue, double saturation, double brightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1); brightness = Math.Clamp(brightness, 0, 1);
        var chroma = brightness * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        var m = brightness - chroma;
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    public (double Hue, double Saturation, double Brightness) ToHsv()
    {
        var r = R / 255d; var g = G / 255d; var b = B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) :
            max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
}

public sealed record NotchPalette(RgbColor Surface, RgbColor Panel, RgbColor Card, RgbColor Track,
    RgbColor Text, RgbColor Muted, RgbColor Border, RgbColor Success, RgbColor Warning,
    RgbColor Critical, RgbColor Accent, RgbColor Activity)
{
    public const string DefaultColor = "#090909";
    public static NotchPalette Create(RgbColor surface, RgbColor? panel = null, RgbColor? card = null, RgbColor? track = null)
    {
        var text = surface.Contrast(RgbColor.Black) >= surface.Contrast(RgbColor.White) ? RgbColor.Black : RgbColor.White;
        var anchor = text == RgbColor.Black ? RgbColor.White : RgbColor.Black;
        RgbColor Layer(double amount)
        {
            var candidate = surface.Mix(text, amount);
            return candidate.Contrast(text) >= 4.5 ? candidate : surface.Mix(anchor, amount);
        }
        var panelColor = panel ?? Layer(.06); var cardColor = card ?? Layer(.10); var trackColor = track ?? Layer(.16);
        RgbColor[] backgrounds = [surface, panelColor, cardColor, trackColor];
        text = backgrounds.Min(c => c.Contrast(RgbColor.Black)) >= backgrounds.Min(c => c.Contrast(RgbColor.White))
            ? RgbColor.Black : RgbColor.White;
        RgbColor Readable(RgbColor desired)
        {
            for (var i = 0; i <= 100; i++)
            {
                var candidate = desired.Mix(text, i / 100d);
                if (backgrounds.All(c => c.Contrast(candidate) >= 4.5)) return candidate;
            }
            return text;
        }
        return new(surface, panelColor, cardColor, trackColor, text, Readable(text.Mix(surface, .30)),
            text.Mix(surface, .70), Readable(new(54, 211, 153)), Readable(new(245, 180, 60)),
            Readable(new(248, 89, 92)), Readable(new(127, 135, 255)), Readable(new(0, 191, 255)));
    }
}
