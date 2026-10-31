using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>Una evaluación en el gráfico: su fecha, su nivel de riesgo (posición en la escala) y el color del nivel.</summary>
public sealed record ChartPoint(DateOnly Date, int LevelIndex, IBrush Color, string Tooltip);

/// <summary>
/// Evolución del nivel de riesgo en el tiempo: una línea escalonada (el nivel se mantiene hasta la evaluación
/// siguiente) con un punto del color del nivel en cada evaluación. El último estado llega hasta hoy.
/// </summary>
public sealed class EvolutionChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<EvolutionChart, IReadOnlyList<ChartPoint>?>(nameof(Points));

    /// <summary>Nombres de los niveles de riesgo, del más bajo al más alto (eje vertical).</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LevelNamesProperty =
        AvaloniaProperty.Register<EvolutionChart, IReadOnlyList<string>?>(nameof(LevelNames));

    private const double LeftAxis = 92;
    private const double BottomAxis = 22;
    private const double Pad = 10;

    static EvolutionChart()
    {
        AffectsRender<EvolutionChart>(PointsProperty, LevelNamesProperty);
    }

    public IReadOnlyList<ChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IReadOnlyList<string>? LevelNames
    {
        get => GetValue(LevelNamesProperty);
        set => SetValue(LevelNamesProperty, value);
    }

    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public override void Render(DrawingContext context)
    {
        var points = Points ?? [];
        var levels = LevelNames ?? [];
        if (points.Count == 0 || levels.Count == 0 || Bounds.Width < LeftAxis + 40)
        {
            return;
        }

        var ink = Brush("InkBrush", Brushes.Black);
        var muted = Brush("MutedTextBrush", Brushes.Gray);
        var grid = new Pen(Brush("BorderSubtleBrush", Brushes.LightGray), 1);
        var typeface = new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this));

        var plot = new Rect(LeftAxis, Pad, Bounds.Width - LeftAxis - Pad, Bounds.Height - Pad - BottomAxis);
        var first = points[0].Date;
        var last = points[^1].Date > Today ? points[^1].Date : Today;
        var days = Math.Max(1, last.DayNumber - first.DayNumber);
        double X(DateOnly d) => plot.Left + (plot.Width * (d.DayNumber - first.DayNumber) / days);
        double Y(int level) => levels.Count == 1
            ? plot.Center.Y
            : plot.Bottom - (plot.Height * level / (levels.Count - 1));

        // Eje vertical: una línea guía y el nombre por nivel.
        for (var i = 0; i < levels.Count; i++)
        {
            var y = Y(i);
            context.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = Text(levels[i], typeface, 11, muted);
            context.DrawText(label, new Point(LeftAxis - 8 - Math.Min(label.Width, LeftAxis - 12), y - (label.Height / 2)));
        }

        // Línea escalonada: el nivel se mantiene hasta la evaluación siguiente; el último, hasta hoy.
        var line = new Pen(ink, 2);
        for (var i = 0; i < points.Count; i++)
        {
            var x = X(points[i].Date);
            var y = Y(points[i].LevelIndex);
            var nextX = i + 1 < points.Count ? X(points[i + 1].Date) : X(last);
            context.DrawLine(line, new Point(x, y), new Point(nextX, y));
            if (i + 1 < points.Count)
            {
                context.DrawLine(line, new Point(nextX, y), new Point(nextX, Y(points[i + 1].LevelIndex)));
            }
        }

        // Puntos y fechas (se omiten las que se superpondrían con la anterior).
        var lastLabelRight = double.NegativeInfinity;
        foreach (var p in points)
        {
            var center = new Point(X(p.Date), Y(p.LevelIndex));
            context.DrawEllipse(p.Color, new Pen(ink, 1.5), center, 6, 6);
            var date = Text(p.Date.ToString("dd/MM/yy", CultureInfo.CurrentCulture), typeface, 10, muted);
            var left = Math.Clamp(center.X - (date.Width / 2), plot.Left - 20, Bounds.Width - date.Width);
            if (left > lastLabelRight + 6)
            {
                context.DrawText(date, new Point(left, plot.Bottom + 6));
                lastLabelRight = left + date.Width;
            }
        }
    }

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    private static FormattedText Text(string text, Typeface typeface, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
}
