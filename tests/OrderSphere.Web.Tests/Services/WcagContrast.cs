using System.Globalization;

namespace OrderSphere.Web.Tests.Services;

/// <summary>
/// WCAG 2.1 relative luminance and contrast ratio, for asserting that the design
/// tokens stay legible. Accepts <c>#RRGGBB</c> and <c>rgba(r,g,b,a)</c>; an alpha
/// value is composited over the supplied background, which is what a hairline or
/// a muted-text token actually renders as.
/// </summary>
internal static class WcagContrast
{
    /// <summary>Contrast ratio between two colours, 1.0 (identical) to 21.0 (black on white).</summary>
    public static double Ratio(string foreground, string background)
    {
        var bg = Parse(background);
        var fg = Composite(Parse(foreground), bg);

        var l1 = RelativeLuminance(fg);
        var l2 = RelativeLuminance(bg);
        var (lighter, darker) = l1 >= l2 ? (l1, l2) : (l2, l1);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static (double R, double G, double B, double A) Parse(string colour)
    {
        colour = colour.Trim();

        if (colour.StartsWith('#'))
        {
            return (
                Channel(colour.Substring(1, 2)),
                Channel(colour.Substring(3, 2)),
                Channel(colour.Substring(5, 2)),
                1.0);
        }

        if (colour.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ||
            colour.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            var parts = colour[(colour.IndexOf('(') + 1)..colour.IndexOf(')')]
                .Split(',', StringSplitOptions.TrimEntries);

            return (
                double.Parse(parts[0], CultureInfo.InvariantCulture) / 255.0,
                double.Parse(parts[1], CultureInfo.InvariantCulture) / 255.0,
                double.Parse(parts[2], CultureInfo.InvariantCulture) / 255.0,
                parts.Length > 3 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 1.0);
        }

        throw new FormatException($"Unsupported colour format: '{colour}'.");

        static double Channel(string hex) => Convert.ToInt32(hex, 16) / 255.0;
    }

    /// <summary>Alpha-composites <paramref name="fg"/> over an opaque background.</summary>
    private static (double R, double G, double B, double A) Composite(
        (double R, double G, double B, double A) fg,
        (double R, double G, double B, double A) bg)
    {
        if (fg.A >= 1.0)
            return fg;

        return (
            (fg.R * fg.A) + (bg.R * (1 - fg.A)),
            (fg.G * fg.A) + (bg.G * (1 - fg.A)),
            (fg.B * fg.A) + (bg.B * (1 - fg.A)),
            1.0);
    }

    private static double RelativeLuminance((double R, double G, double B, double A) c) =>
        (0.2126 * Linearize(c.R)) + (0.7152 * Linearize(c.G)) + (0.0722 * Linearize(c.B));

    private static double Linearize(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
