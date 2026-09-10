namespace OrderSphere.Web.Services;

/// <summary>
/// The palette of the "Bold Editorial" design system, as literal values.
/// This is the C# half of a pair: <c>wwwroot/css/tokens.css</c> holds the same
/// values for CSS, and <c>TokensCssSyncTests</c> fails the build if the two drift.
/// Contrast ratios quoted below are measured against the mode's own canvas and
/// are asserted by <c>DesignTokensContrastTests</c>.
/// </summary>
public static class DesignTokens
{
    /// <summary>Font stacks. Files are self-hosted under <c>wwwroot/fonts</c> (SIL OFL 1.1).</summary>
    public static class Fonts
    {
        public static readonly string[] Display =
            ["Instrument Serif", "Iowan Old Style", "Times New Roman", "Times", "serif"];

        public static readonly string[] Body =
            ["Manrope", "Segoe UI", "system-ui", "-apple-system", "sans-serif"];

        public static readonly string[] Mono =
            ["Geist Mono", "ui-monospace", "Cascadia Mono", "Consolas", "monospace"];
    }

    /// <summary>Light mode. Warm paper, near-black ink, one vivid accent.</summary>
    public static class Light
    {
        public const string Canvas = "#F7F5F0";
        public const string Paper = "#FFFFFF";
        public const string Sunk = "#EFECE5";
        public const string Ink = "#14120F";
        public const string Muted = "#6B6560";

        /// <summary>3.04:1 on canvas — fills, display text (>=24px) and borders only.</summary>
        public const string Accent = "#FF4D1F";

        /// <summary>4.71:1 on canvas — the accent to use for body-sized text.</summary>
        public const string AccentInk = "#C93A0F";

        /// <summary>Ink on accent is 5.64:1; white on accent would be 3.32:1 and is never used.</summary>
        public const string OnAccent = "#14120F";

        /// <summary>Links and focus rings.</summary>
        public const string Accent2 = "#1F4DFF";

        public const string Success = "#1E7B45";
        public const string Warning = "#8A5A00";
        public const string WarningFill = "#E8A317";
        public const string Error = "#C62828";

        public const string Hairline = "rgba(20,18,15,0.12)";
        public const string HairlineStrong = "rgba(20,18,15,0.24)";

        /// <summary>Interactive control outlines. 3.25:1 on canvas, meeting WCAG 1.4.11.</summary>
        public const string BorderControl = "rgba(20,18,15,0.48)";
        public const string TextDisabled = "rgba(20,18,15,0.38)";
        public const string Skeleton = "rgba(20,18,15,0.07)";
        public const string OverlayDark = "rgba(20,18,15,0.48)";
        public const string OverlayLight = "rgba(247,245,240,0.60)";
        public const string AppbarBackground = "rgba(247,245,240,0.88)";

        public const string AccentDarken = "#E63E10";
        public const string AccentLighten = "#FFD9CC";
    }

    /// <summary>Dark mode. The accent lifts to stay legible; ink and canvas swap roles.</summary>
    public static class Dark
    {
        public const string Canvas = "#0F0E0C";
        public const string Paper = "#171613";
        public const string Sunk = "#0A0908";
        public const string Ink = "#F2EFE9";
        public const string Muted = "#A39D95";

        /// <summary>5.13:1 on canvas — usable for text, unlike its light-mode counterpart.</summary>
        public const string Accent = "#FF6A3D";
        public const string AccentInk = "#FF6A3D";
        public const string OnAccent = "#0F0E0C";
        public const string Accent2 = "#6B8CFF";

        public const string Success = "#4CD97B";
        public const string Warning = "#FFB84D";
        public const string WarningFill = "#FFB84D";
        public const string Error = "#FF6E6E";

        public const string Hairline = "rgba(242,239,233,0.12)";
        public const string HairlineStrong = "rgba(242,239,233,0.24)";

        /// <summary>Interactive control outlines. 4.5:1 on canvas.</summary>
        public const string BorderControl = "rgba(242,239,233,0.48)";
        public const string TextDisabled = "rgba(242,239,233,0.38)";
        public const string Skeleton = "rgba(242,239,233,0.07)";
        public const string OverlayDark = "rgba(0,0,0,0.66)";
        public const string OverlayLight = "rgba(15,14,12,0.60)";
        public const string AppbarBackground = "rgba(15,14,12,0.88)";

        public const string AccentDarken = "#FF4D1F";
        public const string AccentLighten = "#3A1F16";
    }

    /// <summary>Layout constants shared by the MudBlazor theme and tokens.css.</summary>
    public static class Layout
    {
        public const string RadiusControl = "6px";
        public const string HeaderHeight = "64px";
        public const string DrawerWidthLeft = "264px";
        public const string DrawerWidthRight = "440px";
    }
}
