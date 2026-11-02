using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>Una evaluación en el gráfico: su fecha, su nivel de riesgo (posición en la escala) y el color del nivel.</summary>
public sealed record ChartPoint(DateOnly Date, int LevelIndex, IBrush Color, string Tooltip);

/// <summary>
/// Evolución del nivel de riesgo: una línea escalonada (el nivel se mantiene hasta la evaluación siguiente) con un
/// punto del color del nivel en cada evaluación. Las evaluaciones van una al lado de la otra, a distancia fija
/// desde la izquierda (no proporcional al tiempo transcurrido), con su fecha debajo.
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
    private const double Step = 96;
    private const double Inset = 26;

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
        // Distancia fija entre evaluaciones; si no entran todas, se achica para que quepan.
        var start = plot.Left + Inset;
        var step = points.Count < 2 ? 0 : Math.Min(Step, (plot.Width - (2 * Inset)) / (points.Count - 1));
        double X(int index) => start + (index * step);
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

        // Línea escalonada: el nivel se mantiene hasta la evaluación siguiente.
        var line = new Pen(ink, 2);
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var y = Y(points[i].LevelIndex);
            var nextX = X(i + 1);
            context.DrawLine(line, new Point(X(i), y), new Point(nextX, y));
            context.DrawLine(line, new Point(nextX, y), new Point(nextX, Y(points[i + 1].LevelIndex)));
        }

        // Puntos y fechas (se omiten las que se superpondrían con la anterior).
        var lastLabelRight = double.NegativeInfinity;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var center = new Point(X(i), Y(p.LevelIndex));
            context.DrawEllipse(p.Color, new Pen(ink, 1.5), center, 6, 6);
            var date = Text(p.Date.ToString(ViewModels.Converters.ShortDatePattern, CultureInfo.CurrentCulture), typeface, 10, muted);
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
