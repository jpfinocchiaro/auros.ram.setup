using Auros.Ram.Core.Model;

namespace Auros.Ram.Core.Services;

/// <summary>Un renglón de la Ficha resumen (RF-16) y base de la Ficha detallada (RF-17).</summary>
public sealed record ReportRow(int Number, Risk Risk, Level RiskLevel, Level ValueLevel, Level DifficultyLevel);

/// <summary>Cantidad de riesgos de un nivel de riesgo (RF-18).</summary>
public sealed record DistributionItem(Level Level, int Count, double Share);

/// <summary>Informes generados en el momento a partir de los riesgos (RF-19, RN-07).</summary>
public static class Reports
{
    /// <summary>Riesgos numerados, del riesgo más alto al más bajo.</summary>
    public static IReadOnlyList<ReportRow> Summary(RiskMatrixDocument doc)
    {
        var riskScale = doc.ProjectScale(ScaleType.Risk);
        var valueScale = doc.ProjectScale(ScaleType.Value);
        var difficultyScale = doc.ProjectScale(ScaleType.Difficulty);
        if (riskScale is null || valueScale is null || difficultyScale is null)
        {
            return [];
        }

        var rows = new List<(Risk Risk, Level R, Level V, Level D)>();
        foreach (var risk in doc.Risks)
        {
            var r = riskScale.FindLevel(risk.RiskLevelId);
            var v = valueScale.FindLevel(risk.ValueLevelId);
            var d = difficultyScale.FindLevel(risk.DifficultyLevelId);
            if (r is not null && v is not null && d is not null)
            {
                rows.Add((risk, r, v, d));
            }
        }

        return [.. rows
            .OrderByDescending(x => x.R.Order)
            .ThenByDescending(x => x.V.Order)
            .ThenBy(x => x.D.Order)
            .ThenBy(x => x.Risk.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select((x, i) => new ReportRow(i + 1, x.Risk, x.R, x.V, x.D))];
    }

    /// <summary>Distribución por nivel de riesgo, del más alto al más bajo.</summary>
    public static IReadOnlyList<DistributionItem> Distribution(RiskMatrixDocument doc)
    {
        var scale = doc.ProjectScale(ScaleType.Risk);
        if (scale is null)
        {
            return [];
        }

        var total = doc.Risks.Count;
        return [.. scale.OrderedLevels.Reverse().Select(level =>
        {
            var count = doc.Risks.Count(r => r.RiskLevelId == level.Id);
            return new DistributionItem(level, count, total == 0 ? 0 : (double)count / total);
        })];
    }
}
