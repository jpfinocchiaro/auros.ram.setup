using Auros.Ram.Core.Model;
using Auros.Ram.Core.Resources;

namespace Auros.Ram.Core.Services;

/// <summary>Cómo cambió un riesgo entre dos fechas.</summary>
public enum RiskTrend
{
    /// <summary>Empeoró: subió su nivel de riesgo (o, al mismo nivel, su impacto o su probabilidad).</summary>
    Worse,

    /// <summary>No estaba evaluado al comienzo del período.</summary>
    New,

    /// <summary>Mejoró: bajó su nivel de riesgo (o, al mismo nivel, su impacto o su probabilidad).</summary>
    Better,

    /// <summary>Terminó igual que empezó.</summary>
    Same,
}

/// <summary>Una evaluación con sus niveles resueltos: un paso del recorrido del riesgo.</summary>
/// <param name="RiskLevel">Nivel de riesgo según el mapa de calor vigente.</param>
public sealed record HistoryStep(RiskAssessment Assessment, Level Impact, Level Probability, Level? RiskLevel);

/// <summary>Un renglón del informe de evolución.</summary>
/// <param name="From">Estado al comienzo del período (null si el riesgo es nuevo).</param>
/// <param name="To">Estado al final del período.</param>
/// <param name="Changes">Evaluaciones registradas dentro del período, en orden.</param>
public sealed record EvolutionRow(Risk Risk, HistoryStep? From, HistoryStep To, RiskTrend Trend, IReadOnlyList<HistoryStep> Changes);

/// <summary>Un riesgo que cambió de una "foto" a la siguiente (o apareció: <paramref name="Before"/> null).</summary>
public sealed record SnapshotChange(Risk Risk, HistoryStep? Before, HistoryStep After);

/// <summary>Una "foto" de la matriz: cómo estaba en una fecha y qué riesgos cambiaron desde la foto anterior.</summary>
public sealed record MatrixSnapshot(DateOnly Date, PriorityMatrix Matrix, IReadOnlyList<SnapshotChange> Changes);

/// <summary>Consultas sobre el historial de evaluaciones: estado a una fecha, recorrido y evolución en un período.</summary>
public static class RiskHistory
{
    /// <summary>
    /// Las "fotos" de la matriz entre dos fechas, para verla cambiar: la primera es la matriz al comienzo del período
    /// y después una por cada fecha con evaluaciones dentro del período.
    /// </summary>
    public static IReadOnlyList<MatrixSnapshot> Snapshots(RiskMatrixDocument doc, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var dates = doc.Risks.SelectMany(r => r.Assessments)
            .Select(a => a.Date)
            .Where(d => d > from && d <= to)
            .Distinct()
            .Order()
            .Prepend(from)
            .ToList();

        var snapshots = new List<MatrixSnapshot>();
        DateOnly? previous = null;
        foreach (var date in dates)
        {
            var changes = new List<SnapshotChange>();
            foreach (var risk in doc.Risks)
            {
                if (StateAt(doc, risk, date) is not { } now)
                {
                    continue;
                }

                var before = previous is { } p ? StateAt(doc, risk, p) : null;
                var moved = before is null
                    ? previous is not null
                    : before.Impact.Id != now.Impact.Id || before.Probability.Id != now.Probability.Id;
                if (moved)
                {
                    changes.Add(new SnapshotChange(risk, before, now));
                }
            }

            snapshots.Add(new MatrixSnapshot(date, PriorityMatrix.Build(doc, date), changes));
            previous = date;
        }

        return snapshots;
    }

    /// <summary>Las evaluaciones del riesgo hasta <paramref name="until"/> (todas si se omite), con sus niveles.</summary>
    public static IReadOnlyList<HistoryStep> Steps(RiskMatrixDocument doc, Risk risk, DateOnly? until = null) =>
        [.. risk.Assessments
            .Where(a => until is null || a.Date <= until)
            .Select(a => Step(doc, a))
            .OfType<HistoryStep>()];

    /// <summary>El estado del riesgo en una fecha, o null si todavía no estaba evaluado.</summary>
    public static HistoryStep? StateAt(RiskMatrixDocument doc, Risk risk, DateOnly date) =>
        risk.AssessmentAt(date) is { } a ? Step(doc, a) : null;

    /// <summary>
    /// Evolución de los riesgos entre dos fechas: cómo estaban al comienzo, cómo terminaron y qué cambios hubo.
    /// Primero los que empeoraron, después los nuevos, los que mejoraron y los que siguen igual; dentro de cada
    /// grupo, del nivel de riesgo final más alto al más bajo.
    /// </summary>
    public static IReadOnlyList<EvolutionRow> Evolution(RiskMatrixDocument doc, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var rows = new List<EvolutionRow>();
        foreach (var risk in doc.Risks)
        {
            if (StateAt(doc, risk, to) is not { } end)
            {
                continue;
            }

            var start = StateAt(doc, risk, from);
            var changes = risk.Assessments
                .Where(a => a.Date > from && a.Date <= to)
                .Select(a => Step(doc, a))
                .OfType<HistoryStep>()
                .ToList();
            rows.Add(new EvolutionRow(risk, start, end, Trend(start, end), changes));
        }

        return [.. rows
            .OrderBy(r => r.Trend)
            .ThenByDescending(r => r.To.RiskLevel?.Order ?? -1)
            .ThenByDescending(r => r.To.Impact.Order)
            .ThenBy(r => r.Risk.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Período por omisión del informe de evolución: desde la primera evaluación del archivo hasta hoy.</summary>
    public static (DateOnly From, DateOnly To) DefaultPeriod(RiskMatrixDocument doc)
    {
        var today = RiskMatrixDocument.Today;
        var first = doc.Risks.SelectMany(r => r.Assessments).Select(a => a.Date).DefaultIfEmpty(today).Min();
        return (first < today ? first : today, today);
    }

    public static string DisplayName(this RiskTrend trend) => trend switch
    {
        RiskTrend.Worse => Messages.Trend_Worse,
        RiskTrend.Better => Messages.Trend_Better,
        RiskTrend.New => Messages.Trend_New,
        _ => Messages.Trend_Same,
    };

    /// <summary>Compara dos estados: primero por nivel de riesgo, después por impacto y probabilidad.</summary>
    public static RiskTrend Trend(HistoryStep? from, HistoryStep to)
    {
        if (from is null)
        {
            return RiskTrend.New;
        }

        var diff = Math.Sign((to.RiskLevel?.Order ?? 0) - (from.RiskLevel?.Order ?? 0));
        if (diff == 0)
        {
            diff = Math.Sign(to.Impact.Order + to.Probability.Order - from.Impact.Order - from.Probability.Order);
        }

        return diff switch
        {
            > 0 => RiskTrend.Worse,
            < 0 => RiskTrend.Better,
            _ => RiskTrend.Same,
        };
    }

    private static HistoryStep? Step(RiskMatrixDocument doc, RiskAssessment a)
    {
        var impact = doc.ProjectScale(ScaleType.Impact)?.FindLevel(a.ImpactLevelId);
        var probability = doc.ProjectScale(ScaleType.Probability)?.FindLevel(a.ProbabilityLevelId);
        return impact is null || probability is null
            ? null
            : new HistoryStep(a, impact, probability, HeatMap.Resolve(doc, a.ImpactLevelId, a.ProbabilityLevelId));
    }
}
