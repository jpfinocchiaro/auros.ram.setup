using Auros.Ram.Core.Model;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un nivel pintado de su color, con texto legible sin distinguir colores (RNF-46).</summary>
public sealed class LevelChip
{
    /// <param name="unit">Unidad del rango del nivel (p. ej., "%"), si se muestra.</param>
    public LevelChip(Level level, string? unit = null)
    {
        RangeText = level.HasRange ? RangeFormat.Range(level.RangeFrom, level.RangeTo, unit) : null;
        Id = level.Id;
        Name = level.Name;
        Order = level.Order;
        Hex = level.Color;
        TextHex = level.TextColor;
        Background = Brushes.FromHex(level.Color);
        Foreground = Brushes.FromHex(level.TextColor);
        var trimmed = level.Name.Trim();
        Abbrev = (trimmed.Length <= 3 ? trimmed : trimmed[..3]).ToUpperInvariant();
    }

    public Guid Id { get; }
    public string Name { get; }
    public int Order { get; }
    public string Hex { get; }
    public string TextHex { get; }
    public string Abbrev { get; }
    public IBrush Background { get; }
    public IBrush Foreground { get; }

    /// <summary>Rango del nivel ("1.000,00 – 4.999,99" o "0,00 – 20,00 %"), si tiene.</summary>
    public string? RangeText { get; }

    public static LevelChip? From(Level? level) => level is null ? null : new LevelChip(level);

    public override string ToString() => Name;
}

internal static class Brushes
{
    public static IBrush FromHex(string hex, byte alpha = 0xFF) =>
        Color.TryParse(hex, out var c) ? new SolidColorBrush(new Color(alpha, c.R, c.G, c.B)) : Avalonia.Media.Brushes.Gray;

    public static Bitmap? Image(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Opción "Todos" + niveles, para los filtros.</summary>
public sealed record FilterOption(string Label, Guid? LevelId)
{
    public override string ToString() => Label;
}

/// <summary>Formato de montos y porcentajes: 2 decimales con separador de miles.</summary>
public static class RangeFormat
{
    public static string Value(decimal? value, string? unit = null) =>
        value is { } v ? v.ToString("N2", System.Globalization.CultureInfo.CurrentCulture) + (unit is null ? "" : " " + unit) : "";

    public static string Range(decimal? from, decimal? to, string? unit = null) =>
        $"{Value(from)} – {Value(to)}" + (unit is null ? "" : " " + unit);

    /// <summary>Unidad de los valores de una escala: "%" en Probabilidad; los montos no llevan unidad.</summary>
    public static string? UnitFor(ScaleType type) => type == ScaleType.Probability ? "%" : null;
}

public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
