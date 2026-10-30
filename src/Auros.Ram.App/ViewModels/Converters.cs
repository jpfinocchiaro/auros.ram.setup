using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Auros.Ram.App.ViewModels;

public static class Converters
{
    /// <summary>Cursiva cuando el valor es falso (p. ej., "sin justificación").</summary>
    public static readonly IValueConverter ItalicUnless =
        new FuncValueConverter<bool, FontStyle>(b => b ? FontStyle.Normal : FontStyle.Italic);
}
