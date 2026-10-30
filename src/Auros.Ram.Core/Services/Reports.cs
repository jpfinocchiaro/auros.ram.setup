using Auros.Ram.Core.Model;

namespace Auros.Ram.Core.Services;

/// <summary>Un renglón de la Ficha resumen (RF-16) y base de la Ficha detallada (RF-17).</summary>
public sealed record ReportRow(int Number, Risk Risk, Level RiskLevel, Level ImpactLevel, Level ProbabilityLevel);

/// <summary>Cantidad de riesgos de un nivel de riesgo (RF-18).</summary>
public sealed record DistributionItem(Level Level, int Count, double Share);

/// <summary>Informes generados en el momento a partir de los riesgos (RF-19, RN-07).</summary>
public static class Reports
{
    /// <summary>Riesgos numerados, del nivel de riesgo más alto al más bajo; a igual nivel, mayor impacto primero.</summary>
    public static IReadOnlyList<ReportRow> Summary(RiskMatrixDocument doc)
    {
        var rows = new List<(Risk Risk, Level R, Level I, Level P)>();
        foreach (var risk in doc.Risks)
        {
            var r = doc.RiskLevelOf(risk);
            var i = doc.LevelOf(risk, ScaleType.Impact);
            var p = doc.LevelOf(risk, ScaleType.Probability);
            if (r is not null && i is not null && p is not null)
            {
                rows.Add((risk, r, i, p));
            }
        }

        return [.. rows
            .OrderByDescending(x => x.R.Order)
            .ThenByDescending(x => x.I.Order)
            .ThenByDescending(x => x.P.Order)
            .ThenBy(x => x.Risk.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select((x, n) => new ReportRow(n + 1, x.Risk, x.R, x.I, x.P))];
    }

    /// <summary>Distribución por nivel de riesgo, del más alto al más bajo.</summary>
    public static IReadOnlyList<DistributionItem> Distribution(RiskMatrixDocument doc)
    {
        var scale = doc.ProjectScale(ScaleType.Risk);
        if (scale is null)
        {
            return [];
        }

        var byLevel = doc.Risks.GroupBy(r => doc.RiskLevelOf(r)?.Id).ToDictionary(g => g.Key ?? Guid.Empty, g => g.Count());
        var total = doc.Risks.Count;
        return [.. scale.OrderedLevels.Reverse().Select(level =>
        {
            var count = byLevel.GetValueOrDefault(level.Id);
            return new DistributionItem(level, count, total == 0 ? 0 : (double)count / total);
        })];
    }
}
