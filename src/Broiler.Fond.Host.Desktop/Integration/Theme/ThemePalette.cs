namespace Broiler.Fond.Host.Desktop.Integration.Theme;

using System;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.UI.Standard;

/// <summary>
/// Semantic color palette conforming to WCAG 2.1 AA accessibility contrast guidelines.
/// </summary>
public sealed class ThemePalette
{
    public ThemeMode Mode { get; }
    public BColor Canvas { get; }
    public BColor Surface { get; }
    public BColor Border { get; }
    public BColor Foreground { get; }
    public BColor Primary { get; }
    public BColor Success { get; }
    public BColor Warning { get; }
    public BColor Danger { get; }

    public ThemePalette(
        ThemeMode mode,
        BColor canvas,
        BColor surface,
        BColor border,
        BColor foreground,
        BColor primary,
        BColor success,
        BColor warning,
        BColor danger)
    {
        Mode = mode;
        Canvas = canvas;
        Surface = surface;
        Border = border;
        Foreground = foreground;
        Primary = primary;
        Success = success;
        Warning = warning;
        Danger = danger;
    }

    public static ThemePalette StandardLight => new(
        ThemeMode.StandardLight,
        new BColor(248, 249, 250),
        new BColor(255, 255, 255),
        new BColor(222, 226, 230),
        new BColor(33, 37, 41),
        new BColor(13, 110, 253),
        new BColor(25, 135, 84),
        new BColor(255, 193, 7),
        new BColor(220, 53, 69));

    public static ThemePalette StandardDark => new(
        ThemeMode.StandardDark,
        new BColor(33, 37, 41),
        new BColor(43, 48, 53),
        new BColor(73, 80, 87),
        new BColor(248, 249, 250),
        new BColor(13, 110, 253),
        new BColor(25, 135, 84),
        new BColor(255, 193, 7),
        new BColor(220, 53, 69));

    public static ThemePalette HighContrastBlack => new(
        ThemeMode.HighContrastBlack,
        new BColor(0, 0, 0),
        new BColor(0, 0, 0),
        new BColor(255, 255, 255),
        new BColor(255, 255, 255),
        new BColor(255, 255, 0),
        new BColor(0, 255, 0),
        new BColor(255, 255, 0),
        new BColor(255, 0, 0));

    public static ThemePalette HighContrastWhite => new(
        ThemeMode.HighContrastWhite,
        new BColor(255, 255, 255),
        new BColor(255, 255, 255),
        new BColor(0, 0, 0),
        new BColor(0, 0, 0),
        new BColor(0, 0, 255),
        new BColor(0, 128, 0),
        new BColor(128, 128, 0),
        new BColor(255, 0, 0));

    public static ThemePalette Get(ThemeMode mode) => mode switch
    {
        ThemeMode.StandardDark => StandardDark,
        ThemeMode.HighContrastBlack => HighContrastBlack,
        ThemeMode.HighContrastWhite => HighContrastWhite,
        _ => StandardLight
    };

    /// <summary>
    /// Validates that foreground-to-background contrast ratio meets WCAG 2.1 AA (>= 4.5:1).
    /// </summary>
    public bool ValidateWcagContrast()
    {
        double l1 = CalculateLuminance(Foreground);
        double l2 = CalculateLuminance(Surface);
        double ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        return ratio >= 4.5;
    }

    private static double CalculateLuminance(BColor c)
    {
        static double Transform(byte channel)
        {
            double v = channel / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        double r = Transform(c.R);
        double g = Transform(c.G);
        double b = Transform(c.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}
