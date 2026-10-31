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
    public const string White = "#FFFFFF";
    public const string Black = "#000000";

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

    /// <summary>Colores de fuente: blanco y negro primero, después los mismos de la paleta.</summary>
    public static IReadOnlyList<NamedColor> TextNamed { get; } =
    [
        new(Messages.Color_White, White),
        new(Messages.Color_Black, Black),
        .. Named,
    ];

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexRegex();

    public static bool IsValidHex(string? value) => value is not null && HexRegex().IsMatch(value);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    /// <summary>Relación de contraste WCAG entre dos colores (1 a 21). AA para texto normal pide 4,5.</summary>
    public static double ContrastRatio(string a, string b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Contraste mínimo para preferir fuente blanca: ~3:1, el mínimo WCAG para texto en negrita de las
    /// etiquetas de nivel. Así los verdes, naranjas y rojos oscuros llevan fuente blanca.
    /// </summary>
    public const double WhiteTextMinContrast = 2.9;

    /// <summary>
    /// Fuente para un fondo: blanca si alcanza <see cref="WhiteTextMinContrast"/> (p. ej., verde oscuro);
    /// si no, negra (amarillos, verdes y naranjas claros).
    /// </summary>
    public static string ContrastingText(string hex) =>
        ContrastRatio(hex, White) >= WhiteTextMinContrast ? White : Black;

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
