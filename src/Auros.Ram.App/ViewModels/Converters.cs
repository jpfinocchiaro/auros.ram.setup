using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Auros.Ram.App.ViewModels;

public static class Converters
{
    /// <summary>Fecha corta con el año de dos cifras, en el formato del idioma (01/11/26, 11/1/26…).</summary>
    public static string ShortDatePattern =>
        System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern.Replace("yyyy", "yy", StringComparison.Ordinal);

    /// <summary>Cursiva cuando el valor es falso (p. ej., "sin justificación").</summary>
    public static readonly IValueConverter ItalicUnless =
        new FuncValueConverter<bool, FontStyle>(b => b ? FontStyle.Normal : FontStyle.Italic);

    /// <summary>Flecha de un elemento desplegable: ▾ abierto, ▸ cerrado.</summary>
    public static readonly IValueConverter Chevron = new FuncValueConverter<bool, string>(open => open ? "▾" : "▸");

    /// <summary>Negrita cuando el valor es verdadero (p. ej., la opción vigente de un menú).</summary>
    public static readonly IValueConverter BoldIf =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
}
