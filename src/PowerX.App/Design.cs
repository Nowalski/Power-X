using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PowerX.App;

/// <summary>Shared visual helpers. Heat washes work on both light and dark (translucent over the row).</summary>
internal static class Design
{
    // hue per resource, alpha scales with intensity — see docs/DESIGN_SYSTEM.md §heat maps
    public static Brush CpuHeat(double pct) => Wash(0x4C, 0x8B, 0xF5, pct);   // cool blue
    public static Brush MemHeat(double pct) => Wash(0xB0, 0x6A, 0xF0, pct);   // violet
    public static Brush DiskHeat(double pct) => Wash(0xE0, 0x9B, 0x3A, pct);  // amber
    public static Brush NetHeat(double pct) => Wash(0x33, 0xB0, 0xA6, pct);   // teal

    private static readonly SolidColorBrush Transparent = new(Colors.Transparent);

    /// <summary>Cell wash behind a number — a visible pill that deepens with usage. pct is 0..100.</summary>
    private static Brush Wash(byte r, byte g, byte b, double pct)
    {
        if (pct < 3) return Transparent;
        double t = Math.Clamp(pct / 100.0, 0, 1);
        byte a = (byte)(0x22 + Math.Pow(t, 0.5) * 0xBE);   // ~34 at 3%, ~210 at 100%
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    /// <summary>
    /// Left accent bar for a process row — off below ~4%, then blue → amber → red as the process's
    /// resource weight climbs, so the hogs are obvious while scrolling. <paramref name="weight"/> is
    /// roughly a percentage (max of CPU% and a scaled memory share).
    /// </summary>
    public static Brush CpuBar(double weight)
    {
        if (weight < 4) return Transparent;
        double t = Math.Clamp((weight - 4) / 56.0, 0, 1);   // 4 → 0, 60 → 1
        Color c = t < 0.5
            ? Lerp(Rgb(0x4C, 0x8B, 0xF5), Rgb(0xE0, 0x9B, 0x3A), t * 2)
            : Lerp(Rgb(0xE0, 0x9B, 0x3A), Rgb(0xE0, 0x4F, 0x4F), (t - 0.5) * 2);
        return new SolidColorBrush(c);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(0xFF,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    public static Brush RiskBrush(PowerX.Core.Tweaks.TweakRisk risk) => risk switch
    {
        PowerX.Core.Tweaks.TweakRisk.Low => new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0xA0, 0x55)),
        PowerX.Core.Tweaks.TweakRisk.Moderate => new SolidColorBrush(Color.FromArgb(0xFF, 0xC9, 0x93, 0x2E)),
        PowerX.Core.Tweaks.TweakRisk.Advanced => new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0x7B, 0x2A)),
        _ => new SolidColorBrush(Color.FromArgb(0xFF, 0xD1, 0x34, 0x38)),
    };
}
