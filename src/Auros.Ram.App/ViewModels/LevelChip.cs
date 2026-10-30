using Auros.Ram.Core.Model;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un nivel pintado de su color, con texto legible sin distinguir colores (RNF-46).</summary>
public sealed class LevelChip
{
    public LevelChip(Level level)
    {
        Id = level.Id;
        Name = level.Name;
        Order = level.Order;
        Hex = level.Color;
        Background = Brushes.FromHex(level.Color);
        Foreground = Brushes.FromHex(Palette.ContrastingText(level.Color));
        var trimmed = level.Name.Trim();
        Abbrev = (trimmed.Length <= 3 ? trimmed : trimmed[..3]).ToUpperInvariant();
    }

    public Guid Id { get; }
    public string Name { get; }
    public int Order { get; }
    public string Hex { get; }
    public string Abbrev { get; }
    public IBrush Background { get; }
    public IBrush Foreground { get; }

    public static LevelChip? From(Level? level) => level is null ? null : new LevelChip(level);

    public override string ToString() => Name;
}

internal static class Brushes
{
    public static IBrush FromHex(string hex) =>
        Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Avalonia.Media.Brushes.Gray;

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

public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
