using Auros.Ram.Core.Model;

namespace Auros.Ram.Core.Services;

/// <summary>
/// Matriz de priorización (F4): columnas = Valor agregado de menor a mayor (izquierda → derecha),
/// filas = Dificultad con la más fácil abajo. Cada celda lista sus riesgos del más alto al más bajo.
/// </summary>
public sealed class PriorityMatrix
{
    private readonly IReadOnlyList<Risk>[,] _cells;

    private PriorityMatrix(IReadOnlyList<Level> columns, IReadOnlyList<Level> rows, IReadOnlyList<Risk>[,] cells)
    {
        Columns = columns;
        Rows = rows;
        _cells = cells;
        foreach (var cell in cells)
        {
            MaxCellCount = Math.Max(MaxCellCount, cell.Count);
        }
    }

    /// <summary>Niveles de Valor agregado, de menor a mayor.</summary>
    public IReadOnlyList<Level> Columns { get; }

    /// <summary>Niveles de Dificultad, de arriba hacia abajo: la más difícil arriba, la más fácil abajo.</summary>
    public IReadOnlyList<Level> Rows { get; }

    /// <summary>RF-14: todas las celdas toman la altura de la que más riesgos tiene.</summary>
    public int MaxCellCount { get; }

    public IReadOnlyList<Risk> this[int row, int column] => _cells[row, column];

    public static PriorityMatrix Build(RiskMatrixDocument doc)
    {
        // RN-08: solo las escalas asignadas al proyecto.
        var columns = doc.ProjectScale(ScaleType.Value)?.OrderedLevels ?? [];
        var rows = doc.ProjectScale(ScaleType.Difficulty)?.OrderedLevels.Reverse().ToList() ?? [];
        var riskOrders = doc.ProjectScale(ScaleType.Risk)?.Levels.ToDictionary(l => l.Id, l => l.Order) ?? [];

        var colIndex = columns.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var rowIndex = rows.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);

        var buckets = new List<Risk>[rows.Count, columns.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                buckets[r, c] = [];
            }
        }

        foreach (var risk in doc.Risks)
        {
            if (rowIndex.TryGetValue(risk.DifficultyLevelId, out var r) && colIndex.TryGetValue(risk.ValueLevelId, out var c))
            {
                buckets[r, c].Add(risk);
            }
        }

        var cells = new IReadOnlyList<Risk>[rows.Count, columns.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                cells[r, c] = [.. buckets[r, c]
                    .OrderByDescending(x => riskOrders.TryGetValue(x.RiskLevelId, out var o) ? o : int.MinValue)
                    .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)];
            }
        }

        return new PriorityMatrix(columns, rows, cells);
    }
}
