using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>
/// Banderita del país de un idioma (por el código de región ISO: AR, ES, US, GB, BR, PT, FR…), dibujada con formas
/// simples porque Windows no muestra los emojis de banderas. Un país sin dibujo muestra su código.
/// </summary>
public sealed class Flag : Control
{
    public static readonly StyledProperty<string?> RegionProperty =
        AvaloniaProperty.Register<Flag, string?>(nameof(Region));

    static Flag() => AffectsRender<Flag>(RegionProperty);

    public Flag()
    {
        Width = 24;
        Height = 16;
        ClipToBounds = true;
    }

    public string? Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        using (context.PushClip(new RoundedRect(new Rect(0, 0, w, h), 2)))
        {
            Draw(context, (Region ?? "").ToUpperInvariant(), w, h);
        }

        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 1), new Rect(0.5, 0.5, w - 1, h - 1), 2, 2);
    }

    private static void Draw(DrawingContext c, string region, double w, double h)
    {
        switch (region)
        {
            case "AR":
                Horizontal(c, w, h, "#74ACDF", "#FFFFFF", "#74ACDF");
                c.DrawEllipse(Brush("#F6B40E"), null, new Point(w / 2, h / 2), h / 8, h / 8);
                break;
            case "ES":
                Bands(c, w, h, horizontal: true, ("#AA151B", 1), ("#F1BF00", 2), ("#AA151B", 1));
                break;
            case "MX":
                Vertical(c, w, h, "#006847", "#FFFFFF", "#CE1126");
                c.DrawEllipse(Brush("#8C5A2B"), null, new Point(w / 2, h / 2), h / 7, h / 7);
                break;
            case "UY":
                for (var i = 0; i < 9; i++)
                {
                    c.FillRectangle(Brush(i % 2 == 0 ? "#FFFFFF" : "#0038A8"), new Rect(0, i * h / 9, w, h / 9 + 0.5));
                }

                c.FillRectangle(Brush("#FFFFFF"), new Rect(0, 0, w * 0.4, h * 5 / 9));
                c.DrawEllipse(Brush("#FCD116"), null, new Point(w * 0.2, h * 2.5 / 9), h / 7, h / 7);
                break;
            case "CL":
                c.FillRectangle(Brush("#FFFFFF"), new Rect(0, 0, w, h / 2));
                c.FillRectangle(Brush("#D52B1E"), new Rect(0, h / 2, w, h / 2));
                c.FillRectangle(Brush("#0039A6"), new Rect(0, 0, w / 3, h / 2));
                c.DrawEllipse(Brush("#FFFFFF"), null, new Point(w / 6, h / 4), h / 10, h / 10);
                break;
            case "CO":
                Bands(c, w, h, horizontal: true, ("#FCD116", 2), ("#003893", 1), ("#CE1126", 1));
                break;
            case "PE":
                Vertical(c, w, h, "#D91023", "#FFFFFF", "#D91023");
                break;
            case "US":
                for (var i = 0; i < 13; i++)
                {
                    c.FillRectangle(Brush(i % 2 == 0 ? "#B22234" : "#FFFFFF"), new Rect(0, i * h / 13, w, h / 13 + 0.5));
                }

                c.FillRectangle(Brush("#3C3B6E"), new Rect(0, 0, w * 0.4, h * 7 / 13));
                break;
            case "GB":
                c.FillRectangle(Brush("#012169"), new Rect(0, 0, w, h));
                var white = new Pen(Brush("#FFFFFF"), h / 5);
                var red = new Pen(Brush("#C8102E"), h / 15);
                c.DrawLine(white, new Point(0, 0), new Point(w, h));
                c.DrawLine(white, new Point(w, 0), new Point(0, h));
                c.DrawLine(red, new Point(0, 0), new Point(w, h));
                c.DrawLine(red, new Point(w, 0), new Point(0, h));
                c.FillRectangle(Brush("#FFFFFF"), new Rect(0, h * 0.35, w, h * 0.3));
                c.FillRectangle(Brush("#FFFFFF"), new Rect(w / 2 - h * 0.15, 0, h * 0.3, h));
                c.FillRectangle(Brush("#C8102E"), new Rect(0, h * 0.41, w, h * 0.18));
                c.FillRectangle(Brush("#C8102E"), new Rect(w / 2 - h * 0.09, 0, h * 0.18, h));
                break;
            case "BR":
                c.FillRectangle(Brush("#009C3B"), new Rect(0, 0, w, h));
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(w / 2, h * 0.12), true);
                    g.LineTo(new Point(w * 0.92, h / 2));
                    g.LineTo(new Point(w / 2, h * 0.88));
                    g.LineTo(new Point(w * 0.08, h / 2));
                    g.EndFigure(true);
                }

                c.DrawGeometry(Brush("#FFDF00"), null, diamond);
                c.DrawEllipse(Brush("#002776"), null, new Point(w / 2, h / 2), h * 0.22, h * 0.22);
                break;
            case "PT":
                c.FillRectangle(Brush("#006600"), new Rect(0, 0, w * 0.4, h));
                c.FillRectangle(Brush("#FF0000"), new Rect(w * 0.4, 0, w * 0.6, h));
                c.DrawEllipse(Brush("#FFCC00"), null, new Point(w * 0.4, h / 2), h / 5, h / 5);
                break;
            case "FR":
                Vertical(c, w, h, "#0055A4", "#FFFFFF", "#EF4135");
                break;
            case "BE":
                Vertical(c, w, h, "#000000", "#FDDA24", "#EF3340");
                break;
            case "CA":
                Bands(c, w, h, horizontal: false, ("#D52B1E", 1), ("#FFFFFF", 2), ("#D52B1E", 1));
                c.DrawEllipse(Brush("#D52B1E"), null, new Point(w / 2, h / 2), h / 6, h / 6);
                break;
            case "CH":
                c.FillRectangle(Brush("#DA291C"), new Rect(0, 0, w, h));
                c.FillRectangle(Brush("#FFFFFF"), new Rect(w / 2 - h * 0.08, h * 0.2, h * 0.16, h * 0.6));
                c.FillRectangle(Brush("#FFFFFF"), new Rect(w / 2 - h * 0.3, h * 0.42, h * 0.6, h * 0.16));
                break;
            case "IT":
                Vertical(c, w, h, "#009246", "#FFFFFF", "#CE2B37");
                break;
            case "DE":
                Horizontal(c, w, h, "#000000", "#DD0000", "#FFCE00");
                break;
            case "AT":
                Horizontal(c, w, h, "#ED2939", "#FFFFFF", "#ED2939");
                break;
            case "NL":
                Horizontal(c, w, h, "#AE1C28", "#FFFFFF", "#21468B");
                break;
            case "IE":
                Vertical(c, w, h, "#169B62", "#FFFFFF", "#FF883E");
                break;
            case "RU":
                Horizontal(c, w, h, "#FFFFFF", "#0039A6", "#D52B1E");
                break;
            case "PL":
                Bands(c, w, h, horizontal: true, ("#FFFFFF", 1), ("#DC143C", 1));
                break;
            case "UA":
                Bands(c, w, h, horizontal: true, ("#0057B7", 1), ("#FFD700", 1));
                break;
            case "JP":
                c.FillRectangle(Brush("#FFFFFF"), new Rect(0, 0, w, h));
                c.DrawEllipse(Brush("#BC002D"), null, new Point(w / 2, h / 2), h * 0.3, h * 0.3);
                break;
            case "CN":
                c.FillRectangle(Brush("#DE2910"), new Rect(0, 0, w, h));
                c.DrawEllipse(Brush("#FFDE00"), null, new Point(w * 0.2, h * 0.3), h * 0.15, h * 0.15);
                break;
            default:
                c.FillRectangle(Brush("#D9DDD8"), new Rect(0, 0, w, h));
                if (region.Length > 0)
                {
                    var text = new FormattedText(region, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(FontFamily.Default, weight: FontWeight.SemiBold), h * 0.6, Brush("#1C2733"));
                    c.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
                }

                break;
        }
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static void Horizontal(DrawingContext c, double w, double h, string a, string b, string d) =>
        Bands(c, w, h, horizontal: true, (a, 1), (b, 1), (d, 1));

    private static void Vertical(DrawingContext c, double w, double h, string a, string b, string d) =>
        Bands(c, w, h, horizontal: false, (a, 1), (b, 1), (d, 1));

    /// <summary>Franjas proporcionales (p. ej., España: rojo 1, amarillo 2, rojo 1).</summary>
    private static void Bands(DrawingContext c, double w, double h, bool horizontal, params (string Color, double Weight)[] bands)
    {
        var total = bands.Sum(b => b.Weight);
        var at = 0.0;
        foreach (var (color, weight) in bands)
        {
            var size = (horizontal ? h : w) * weight / total;
            c.FillRectangle(Brush(color), horizontal ? new Rect(0, at, w, size + 0.5) : new Rect(at, 0, size + 0.5, h));
            at += size;
        }
    }
}
