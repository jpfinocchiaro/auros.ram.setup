using Auros.Ram.Core.Model;

namespace Auros.Ram.Core.Services;

/// <summary>
/// Matriz de riesgo (F4): columnas = Probabilidad de ocurrencia de menor a mayor (izquierda → derecha),
/// filas = Impacto con el mayor arriba. Cada celda tiene su nivel de riesgo (mapa de calor) y lista sus
/// riesgos por título.
/// </summary>
public sealed class PriorityMatrix
{
    private readonly IReadOnlyList<Risk>[,] _cells;
    private readonly Level?[,] _levels;

    private PriorityMatrix(IReadOnlyList<Level> columns, IReadOnlyList<Level> rows, IReadOnlyList<Risk>[,] cells, Level?[,] levels)
    {
        Columns = columns;
        Rows = rows;
        _cells = cells;
        _levels = levels;
        foreach (var cell in cells)
        {
            MaxCellCount = Math.Max(MaxCellCount, cell.Count);
        }
    }

    /// <summary>Niveles de Probabilidad, de menor a mayor.</summary>
    public IReadOnlyList<Level> Columns { get; }

    /// <summary>Niveles de Impacto, de arriba hacia abajo: el mayor arriba, el menor abajo.</summary>
    public IReadOnlyList<Level> Rows { get; }

    /// <summary>RF-14: todas las celdas toman la altura de la que más riesgos tiene.</summary>
    public int MaxCellCount { get; }

    public IReadOnlyList<Risk> this[int row, int column] => _cells[row, column];

    /// <summary>Nivel de riesgo de la celda según el mapa de calor.</summary>
    public Level? LevelAt(int row, int column) => _levels[row, column];

    /// <param name="asOf">
    /// Fecha a la que se mira la matriz: cada riesgo se ubica según su evaluación vigente ese día y los que todavía
    /// no estaban evaluados no aparecen. Si se omite, el estado actual.
    /// </param>
    public static PriorityMatrix Build(RiskMatrixDocument doc, DateOnly? asOf = null)
    {
        // RN-08: solo las escalas asignadas al proyecto.
        var columns = doc.ProjectScale(ScaleType.Probability)?.OrderedLevels ?? [];
        var rows = doc.ProjectScale(ScaleType.Impact)?.OrderedLevels.Reverse().ToList() ?? [];

        var colIndex = columns.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var rowIndex = rows.Select((l, i) => (l.Id, i)).ToDictionary(x => x.Id, x => x.i);

        var buckets = new List<Risk>[rows.Count, columns.Count];
        var levels = new Level?[rows.Count, columns.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                buckets[r, c] = [];
                levels[r, c] = HeatMap.Resolve(doc, rows[r].Id, columns[c].Id);
            }
        }

        foreach (var risk in doc.Risks)
        {
            var (impact, probability) = (risk.ImpactLevelId, risk.ProbabilityLevelId);
            if (asOf is { } date && risk.Assessments.Count > 0)
            {
                if (risk.AssessmentAt(date) is not { } state)
                {
                    continue;
                }

                (impact, probability) = (state.ImpactLevelId, state.ProbabilityLevelId);
            }

            if (rowIndex.TryGetValue(impact, out var r) && colIndex.TryGetValue(probability, out var c))
            {
                buckets[r, c].Add(risk);
            }
        }

        var cells = new IReadOnlyList<Risk>[rows.Count, columns.Count];
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                cells[r, c] = [.. buckets[r, c].OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)];
            }
        }

        return new PriorityMatrix(columns, rows, cells, levels);
    }
}
