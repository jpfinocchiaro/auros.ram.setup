using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>Una evaluación del recorrido: la celda (fila y columna de la matriz) y su fecha.</summary>
public sealed record PathMarker(int Row, int Column, string Date);

/// <summary>
/// Recorrido de un riesgo sobre la matriz: un círculo numerado por evaluación (en orden) y flechas entre celdas.
/// Se dibuja encima de las celdas, que tienen todas el mismo tamaño (RF-14); no recibe clics.
/// </summary>
public sealed class MatrixPathOverlay : Control
{
    public static readonly StyledProperty<IReadOnlyList<PathMarker>?> MarkersProperty =
        AvaloniaProperty.Register<MatrixPathOverlay, IReadOnlyList<PathMarker>?>(nameof(Markers));

    public static readonly StyledProperty<int> RowCountProperty =
        AvaloniaProperty.Register<MatrixPathOverlay, int>(nameof(RowCount), 1);

    public static readonly StyledProperty<int> ColumnCountProperty =
        AvaloniaProperty.Register<MatrixPathOverlay, int>(nameof(ColumnCount), 1);

    private const double Radius = 11;

    static MatrixPathOverlay()
    {
        AffectsRender<MatrixPathOverlay>(MarkersProperty, RowCountProperty, ColumnCountProperty);
    }

    public IReadOnlyList<PathMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public int RowCount
    {
        get => GetValue(RowCountProperty);
        set => SetValue(RowCountProperty, value);
    }

    public int ColumnCount
    {
        get => GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var markers = Markers ?? [];
        if (markers.Count == 0 || RowCount < 1 || ColumnCount < 1)
        {
            return;
        }

        var accent = this.TryFindResource("BrandBlueBrush", ActualThemeVariant, out var b) && b is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse("#2F72E0"));
        var typeface = new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Bold);
        var cellW = Bounds.Width / ColumnCount;
        var cellH = Bounds.Height / RowCount;

        // Evaluaciones seguidas en la misma celda comparten un círculo ("1, 2").
        var stops = new List<(int Row, int Column, List<int> Numbers, string Date)>();
        for (var i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            if (stops.Count > 0 && stops[^1].Row == m.Row && stops[^1].Column == m.Column)
            {
                stops[^1].Numbers.Add(i + 1);
                stops[^1] = stops[^1] with { Date = m.Date };
            }
            else
            {
                stops.Add((m.Row, m.Column, [i + 1], m.Date));
            }
        }

        // Una celda puede repetirse más adelante en el recorrido: cada visita se corre un poco para no taparse.
        var visits = new Dictionary<(int, int), int>();
        var centers = new List<Point>();
        foreach (var s in stops)
        {
            var n = visits.GetValueOrDefault((s.Row, s.Column));
            visits[(s.Row, s.Column)] = n + 1;
            var offset = n * (Radius * 2.4);
            centers.Add(new Point(((s.Column + 1) * cellW) - (Radius * 2.2) - offset, ((s.Row + 0.5) * cellH) + (cellH * 0.12)));
        }

        var halo = new Pen(Brushes.White, 6) { LineCap = PenLineCap.Round };
        var line = new Pen(accent, 3) { LineCap = PenLineCap.Round };
        for (var i = 1; i < centers.Count; i++)
        {
            Arrow(context, centers[i - 1], centers[i], halo, line);
        }

        for (var i = 0; i < stops.Count; i++)
        {
            var label = string.Join(", ", stops[i].Numbers);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 11, Brushes.White);
            var r = Math.Max(Radius, (text.Width / 2) + 5);
            context.DrawEllipse(accent, new Pen(Brushes.White, 2), centers[i], r, Radius);
            context.DrawText(text, new Point(centers[i].X - (text.Width / 2), centers[i].Y - (text.Height / 2)));

            var date = new FormattedText(stops[i].Date, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, accent);
            var box = new Rect(centers[i].X - (date.Width / 2) - 3, centers[i].Y + Radius + 2, date.Width + 6, date.Height + 1);
            context.DrawRectangle(Brushes.White, null, box, 2, 2);
            context.DrawText(date, new Point(box.X + 3, box.Y));
        }
    }

    /// <summary>Flecha de un círculo al siguiente, cortada en el borde de cada uno.</summary>
    private static void Arrow(DrawingContext context, Point from, Point to, IPen halo, IPen line)
    {
        var v = to - from;
        var length = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
        if (length < (Radius * 2) + 4)
        {
            return;
        }

        var u = new Vector(v.X / length, v.Y / length);
        var start = from + (u * (Radius + 2));
        var end = to - (u * (Radius + 3));
        var normal = new Vector(-u.Y, u.X);
        var wing1 = end - (u * 10) + (normal * 6);
        var wing2 = end - (u * 10) - (normal * 6);
        foreach (var pen in new[] { halo, line })
        {
            context.DrawLine(pen, start, end);
            context.DrawLine(pen, end, wing1);
            context.DrawLine(pen, end, wing2);
        }
    }
}
