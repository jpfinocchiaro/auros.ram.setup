using System.Globalization;
using System.Text.RegularExpressions;
using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Model;

/// <summary>Colores ofrecidos para los niveles (RF-03) y utilidades de contraste (RNF-46).</summary>
public static partial class Palette
{
    public const string Green = "#2E9E44";
    public const string Yellow = "#F2C200";
    public const string Orange = "#F28C28";
    public const string Red = "#D93025";
    public const string Violet = "#7B3FB5";
    public const string Blue = "#1E6FD9";
    public const string Gray = "#8A8F98";

    public static IReadOnlyList<NamedColor> Named { get; } =
    [
        new(Messages.Color_Green, Green),
        new(Messages.Color_Yellow, Yellow),
        new(Messages.Color_Orange, Orange),
        new(Messages.Color_Red, Red),
        new(Messages.Color_Violet, Violet),
        new(Messages.Color_Blue, Blue),
        new(Messages.Color_Gray, Gray),
    ];

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexRegex();

    public static bool IsValidHex(string? value) => value is not null && HexRegex().IsMatch(value);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    /// <summary>Negro o blanco, el que tenga más contraste sobre el color dado (WCAG).</summary>
    public static string ContrastingText(string hex)
    {
        var l = RelativeLuminance(hex);
        var contrastWhite = 1.05 / (l + 0.05);
        var contrastBlack = (l + 0.05) / 0.05;
        return contrastBlack >= contrastWhite ? "#000000" : "#FFFFFF";
    }

    public static double RelativeLuminance(string hex)
    {
        if (!IsValidHex(hex))
        {
            return 0.5;
        }

        static double Channel(string h, int i)
        {
            var c = int.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(hex, 1)) + (0.7152 * Channel(hex, 3)) + (0.0722 * Channel(hex, 5));
    }
}

public sealed record NamedColor(string Name, string Hex);
