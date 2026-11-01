using System.Globalization;
using Auros.Ram.Core.Model;

namespace Auros.Ram.Export;

internal static class Fmt
{
    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    public static string Format(string template, params object[] args) => string.Format(Culture, template, args);

    /// <summary>Monto o porcentaje con 2 decimales y separador de miles (vacío si no se cargó).</summary>
    public static string Amount(decimal? value, string unit = "") =>
        value is { } v ? v.ToString("N2", Culture) + unit : "";

    /// <summary>Abreviatura del nivel, para leer la matriz sin depender del color (RNF-46).</summary>
    public static string Abbrev(Level level)
    {
        var name = level.Name.Trim();
        return (name.Length <= 3 ? name : name[..3]).ToUpper(Culture);
    }

    public static (byte R, byte G, byte B) Rgb(string hex)
    {
        if (!Palette.IsValidHex(hex))
        {
            return (128, 128, 128);
        }

        return (
            byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
