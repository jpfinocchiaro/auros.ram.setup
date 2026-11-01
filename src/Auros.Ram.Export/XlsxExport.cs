using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Auros.Ram.Core.Text;
using Auros.Ram.Export.Resources;
using ClosedXML.Excel;

namespace Auros.Ram.Export;

/// <summary>Excel con formato (RNF-61): proyecto, ficha resumen, matriz de riesgo, historial de evaluaciones y escalas.</summary>
public static class XlsxExport
{
    public static void Write(RiskMatrixDocument doc, string path)
    {
        using var wb = new XLWorkbook();
        WriteProject(wb.AddWorksheet(ExportText.Sheet_Project), doc);
        WriteRisks(wb.AddWorksheet(ExportText.Sheet_Risks), doc);
        WriteMatrix(wb.AddWorksheet(ExportText.Sheet_Matrix), doc);
        WriteHistory(wb.AddWorksheet(ExportText.Sheet_History), doc);
        WriteScales(wb.AddWorksheet(ExportText.Sheet_Scales), doc);
        wb.Worksheet(2).SetTabActive();
        wb.SaveAs(path);
    }

    private static void WriteProject(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        var p = doc.Project;
        Header(ws.Cell(1, 1), ExportText.Col_Field);
        Header(ws.Cell(1, 2), ExportText.Col_FieldValue);
        (string, string)[] rows =
        [
            (ExportText.Project_Name, p.Name),
            (ExportText.Project_Company, p.Company),
            (ExportText.Project_Date, p.Date.ToString("d", Fmt.Culture)),
            (ExportText.Project_Status, p.Status.DisplayName()),
            (ScaleType.Impact.DisplayName(), doc.ProjectScale(ScaleType.Impact)?.Name ?? ""),
            (ScaleType.Probability.DisplayName(), doc.ProjectScale(ScaleType.Probability)?.Name ?? ""),
            (ScaleType.Risk.DisplayName(), doc.ProjectScale(ScaleType.Risk)?.Name ?? ""),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Item1;
            ws.Cell(i + 2, 1).Style.Font.Bold = true;
            ws.Cell(i + 2, 2).Value = rows[i].Item2;
        }

        ws.Columns().AdjustToContents();
    }

    private static void WriteRisks(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        string[] headers =
        [
            ExportText.Col_Number, ExportText.Col_Title, ExportText.Col_Short, ExportText.Col_Risk, ExportText.Col_Impact,
            ExportText.Col_Probability, ExportText.Col_Long, ExportText.Col_ImpactJustification,
            ExportText.Col_ProbabilityJustification, ExportText.Col_ImpactAmount, ExportText.Col_ProbabilityPercent,
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            Header(ws.Cell(1, c + 1), headers[c]);
        }

        var row = 2;
        foreach (var x in Reports.Summary(doc))
        {
            ws.Cell(row, 1).Value = x.Number;
            ws.Cell(row, 2).Value = x.Risk.Title;
            Rich(ws.Cell(row, 3), x.Risk.ShortDescription);
            Colored(ws.Cell(row, 4), x.RiskLevel);
            Colored(ws.Cell(row, 5), x.ImpactLevel);
            Colored(ws.Cell(row, 6), x.ProbabilityLevel);
            Rich(ws.Cell(row, 7), x.Risk.LongDescription);
            ws.Cell(row, 8).Value = x.Risk.ImpactJustification;
            ws.Cell(row, 9).Value = x.Risk.ProbabilityJustification;
            Number(ws.Cell(row, 10), x.Risk.ImpactAmount, "#,##0.00");
            Number(ws.Cell(row, 11), x.Risk.ProbabilityPercent, "0.00\" %\"");
            row++;
        }

        ws.Column(1).Width = 6;
        ws.Column(2).Width = 40;
        ws.Column(3).Width = 60;
        ws.Columns(4, 6).Width = 16;
        ws.Columns(7, 9).Width = 60;
        ws.Columns(10, 11).Width = 18;
        ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length).Style.Alignment.WrapText = true;
        ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.SheetView.FreezeRows(1);
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }
    }

    /// <summary>Cada nivel de impacto ocupa un bloque de filas; el bloque de cada celda toma el color de su nivel de riesgo.</summary>
    private static void WriteMatrix(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        var m = PriorityMatrix.Build(doc);
        var block = Math.Max(1, m.MaxCellCount);

        ws.Cell(1, 2).Value = ExportText.Axis_Probability;
        ws.Range(1, 2, 1, m.Columns.Count + 1).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        for (var c = 0; c < m.Columns.Count; c++)
        {
            Colored(ws.Cell(2, c + 2), m.Columns[c]);
            ws.Column(c + 2).Width = 38;
        }

        Header(ws.Cell(2, 1), ExportText.Axis_Impact);
        ws.Column(1).Width = 26;

        for (var r = 0; r < m.Rows.Count; r++)
        {
            var top = 3 + (r * block);
            var label = ws.Range(top, 1, top + block - 1, 1).Merge();
            Colored(label.FirstCell(), m.Rows[r]);
            label.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            for (var c = 0; c < m.Columns.Count; c++)
            {
                var level = m.LevelAt(r, c);
                if (level is not null)
                {
                    var area = ws.Range(top, c + 2, top + block - 1, c + 2);
                    area.Style.Fill.BackgroundColor = XLColor.FromHtml(level.Color);
                    area.Style.Font.FontColor = XLColor.FromHtml(level.TextColor);
                }

                var cellRisks = m[r, c];
                for (var i = 0; i < cellRisks.Count; i++)
                {
                    var cell = ws.Cell(top + i, c + 2);
                    cell.Value = level is null ? cellRisks[i].Title : $"[{Fmt.Abbrev(level)}] {cellRisks[i].Title}";
                    cell.Style.Alignment.WrapText = true;
                }
            }

            ws.Range(top, 1, top + block - 1, m.Columns.Count + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            for (var c = 0; c < m.Columns.Count; c++)
            {
                ws.Range(top, c + 2, top + block - 1, c + 2).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }
        }

        var legendRow = 4 + (m.Rows.Count * block);
        ws.Cell(legendRow, 1).Value = ExportText.Matrix_Legend;
        var riskScale = doc.ProjectScale(ScaleType.Risk);
        if (riskScale is not null)
        {
            var col = 2;
            foreach (var level in riskScale.OrderedLevels.Reverse())
            {
                var cell = ws.Cell(legendRow, col++);
                cell.Value = $"[{Fmt.Abbrev(level)}] {level.Name}";
                Fill(cell, level);
            }
        }
    }

    private static void WriteScales(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        string[] headers = [ExportText.Col_Scale, ExportText.Col_Type, ExportText.Col_Order, ExportText.Col_Level, ExportText.Col_Color, ExportText.Col_TextColor];
        for (var c = 0; c < headers.Length; c++)
        {
            Header(ws.Cell(1, c + 1), headers[c]);
        }

        var row = 2;
        foreach (var scale in doc.Scales.OrderBy(s => s.Type))
        {
            foreach (var level in scale.OrderedLevels)
            {
                ws.Cell(row, 1).Value = scale.Name;
                ws.Cell(row, 2).Value = scale.Type.DisplayName();
                ws.Cell(row, 3).Value = level.Order;
                Colored(ws.Cell(row, 4), level);
                ws.Cell(row, 5).Value = level.Color;
                ws.Cell(row, 6).Value = level.TextColor;
                row++;
            }
        }

        ws.Columns().AdjustToContents();
    }

    /// <summary>Todas las evaluaciones de todos los riesgos, una por fila, para medir la evolución (filtros y tablas dinámicas).</summary>
    private static void WriteHistory(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        string[] headers =
        [
            ExportText.Col_Title, ExportText.Col_Date, ExportText.Col_Impact, ExportText.Col_Probability, ExportText.Col_Risk, ExportText.Col_Note,
            ExportText.Col_ImpactAmount, ExportText.Col_ProbabilityPercent,
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            Header(ws.Cell(1, c + 1), headers[c]);
        }

        var row = 2;
        foreach (var risk in doc.Risks.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            foreach (var step in RiskHistory.Steps(doc, risk))
            {
                ws.Cell(row, 1).Value = risk.Title;
                ws.Cell(row, 2).Value = step.Assessment.Date.ToDateTime(TimeOnly.MinValue);
                ws.Cell(row, 2).Style.DateFormat.Format = "dd/mm/yyyy";
                Colored(ws.Cell(row, 3), step.Impact);
                Colored(ws.Cell(row, 4), step.Probability);
                if (step.RiskLevel is { } level)
                {
                    Colored(ws.Cell(row, 5), level);
                }

                ws.Cell(row, 6).Value = step.Assessment.Note;
                Number(ws.Cell(row, 7), step.Assessment.ImpactAmount, "#,##0.00");
                Number(ws.Cell(row, 8), step.Assessment.ProbabilityPercent, "0.00\" %\"");
                row++;
            }
        }

        ws.Column(1).Width = 40;
        ws.Column(2).Width = 12;
        ws.Columns(3, 5).Width = 18;
        ws.Column(6).Width = 60;
        ws.Columns(7, 8).Width = 18;
        ws.SheetView.FreezeRows(1);
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }
    }

    /// <summary>Número con formato (monto o porcentaje); la celda queda vacía si no se cargó.</summary>
    private static void Number(IXLCell cell, decimal? value, string format)
    {
        if (value is { } v)
        {
            cell.Value = v;
            cell.Style.NumberFormat.Format = format;
        }
    }

    private static void Header(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EAED");
    }

    private static void Colored(IXLCell cell, Level level)
    {
        cell.Value = level.Name;
        Fill(cell, level);
        cell.Style.Font.Bold = true;
    }

    /// <summary>Celda con texto enriquecido: negrita, itálica, subrayado, tamaños y listas (una línea por ítem).</summary>
    private static void Rich(IXLCell cell, string markup)
    {
        var rich = cell.CreateRichText();
        var blocks = RichText.Parse(markup);
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                rich.AddNewLine();
            }

            var prefix = RichText.Prefix(blocks[i]);
            if (prefix.Length > 0)
            {
                rich.AddText(prefix);
            }

            foreach (var run in blocks[i].Runs)
            {
                var t = rich.AddText(run.Text);
                t.SetBold(run.Bold).SetItalic(run.Italic);
                if (run.Underline)
                {
                    t.SetUnderline(XLFontUnderlineValues.Single);
                }

                if (run.Size != RichSize.Normal)
                {
                    t.SetFontSize(run.Size == RichSize.Large ? 14 : 9);
                }
            }
        }
    }

    private static void Fill(IXLCell cell, Level level)
    {
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(level.Color);
        cell.Style.Font.FontColor = XLColor.FromHtml(level.TextColor);
    }
}
