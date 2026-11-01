using System.Text;
using Auros.Ram.Core;
using Auros.Ram.Core.Services;
using Auros.Ram.Core.Text;
using Auros.Ram.Export.Resources;

namespace Auros.Ram.Export;

/// <summary>
/// CSV plano (RNF-61). Separador punto y coma y UTF-8 con BOM, que es lo que Excel
/// en español abre directamente con las columnas separadas.
/// </summary>
public static class CsvExport
{
    public const char Separator = ';';

    public static void WriteRisks(RiskMatrixDocument doc, string path)
    {
        var sb = new StringBuilder();
        Line(sb, ExportText.Col_Number, ExportText.Col_Title, ExportText.Col_Short, ExportText.Col_Long,
            ExportText.Col_Risk, ExportText.Col_Impact, ExportText.Col_Probability,
            ExportText.Col_ImpactJustification, ExportText.Col_ProbabilityJustification,
            ExportText.Col_ImpactAmount, ExportText.Col_ProbabilityPercent);
        foreach (var row in Reports.Summary(doc))
        {
            var r = row.Risk;
            Line(sb, row.Number.ToString(Fmt.Culture), r.Title, RichText.ToPlain(r.ShortDescription), RichText.ToPlain(r.LongDescription),
                row.RiskLevel.Name, row.ImpactLevel.Name, row.ProbabilityLevel.Name,
                r.ImpactJustification, r.ProbabilityJustification,
                Fmt.Amount(r.ImpactAmount), Fmt.Amount(r.ProbabilityPercent));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>Matriz de riesgo: una fila por nivel de impacto, una columna por nivel de probabilidad.</summary>
    public static void WriteMatrix(RiskMatrixDocument doc, string path)
    {
        var m = PriorityMatrix.Build(doc);
        var risk = RiskQuery.LevelOrders(doc);
        var sb = new StringBuilder();
        Line(sb, [$"{ExportText.Col_Impact} \\ {ExportText.Col_Probability}", .. m.Columns.Select(c => c.Name)]);
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var cells = new List<string> { m.Rows[r].Name };
            for (var c = 0; c < m.Columns.Count; c++)
            {
                var titles = string.Join(" | ", m[r, c].Select(x => x.Title));
                cells.Add(m.LevelAt(r, c) is { } level ? $"[{level.Name}] {titles}".TrimEnd() : titles);
            }

            Line(sb, [.. cells]);
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void Line(StringBuilder sb, params string[] values)
    {
        sb.AppendJoin(Separator, values.Select(Escape));
        sb.Append("\r\n");
    }

    private static string Escape(string value)
    {
        var needsQuotes = value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0;
        // Evita que Excel interprete el texto como fórmula al abrir el CSV.
        if (value.Length > 0 && "=+-@".Contains(value[0], StringComparison.Ordinal))
        {
            value = "'" + value;
        }

        return needsQuotes ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
