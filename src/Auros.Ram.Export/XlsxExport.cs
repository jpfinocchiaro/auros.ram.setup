using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Auros.Ram.Export.Resources;
using ClosedXML.Excel;

namespace Auros.Ram.Export;

/// <summary>Excel con formato (RNF-61): proyecto, ficha resumen, matriz de priorización y escalas.</summary>
public static class XlsxExport
{
    public static void Write(RiskMatrixDocument doc, string path)
    {
        using var wb = new XLWorkbook();
        WriteProject(wb.AddWorksheet(ExportText.Sheet_Project), doc);
        WriteRisks(wb.AddWorksheet(ExportText.Sheet_Risks), doc);
        WriteMatrix(wb.AddWorksheet(ExportText.Sheet_Matrix), doc);
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
            (ExportText.Project_Client, p.Client),
            (ExportText.Project_Consultancy, p.Consultancy),
            (ExportText.Project_Date, p.Date.ToString("d", Fmt.Culture)),
            (ExportText.Project_Status, p.Status.DisplayName()),
            (ScaleType.Risk.DisplayName(), doc.ProjectScale(ScaleType.Risk)?.Name ?? ""),
            (ScaleType.Value.DisplayName(), doc.ProjectScale(ScaleType.Value)?.Name ?? ""),
            (ScaleType.Difficulty.DisplayName(), doc.ProjectScale(ScaleType.Difficulty)?.Name ?? ""),
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
            ExportText.Col_Number, ExportText.Col_Title, ExportText.Col_Short, ExportText.Col_Risk, ExportText.Col_Value,
            ExportText.Col_Difficulty, ExportText.Col_Long, ExportText.Col_RiskJustification,
            ExportText.Col_ValueJustification, ExportText.Col_DifficultyJustification,
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
            ws.Cell(row, 3).Value = x.Risk.ShortDescription;
            Colored(ws.Cell(row, 4), x.RiskLevel);
            Colored(ws.Cell(row, 5), x.ValueLevel);
            Colored(ws.Cell(row, 6), x.DifficultyLevel);
            ws.Cell(row, 7).Value = x.Risk.LongDescription;
            ws.Cell(row, 8).Value = x.Risk.RiskJustification;
            ws.Cell(row, 9).Value = x.Risk.ValueJustification;
            ws.Cell(row, 10).Value = x.Risk.DifficultyJustification;
            row++;
        }

        ws.Column(1).Width = 6;
        ws.Column(2).Width = 40;
        ws.Column(3).Width = 60;
        ws.Columns(4, 6).Width = 16;
        ws.Columns(7, 10).Width = 60;
        ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length).Style.Alignment.WrapText = true;
        ws.Range(1, 1, Math.Max(row - 1, 1), headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        ws.SheetView.FreezeRows(1);
        if (row > 2)
        {
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        }
    }

    /// <summary>Cada nivel de dificultad ocupa un bloque de filas; cada riesgo es una celda con su color.</summary>
    private static void WriteMatrix(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        var m = PriorityMatrix.Build(doc);
        var block = Math.Max(1, m.MaxCellCount);

        ws.Cell(1, 2).Value = ExportText.Axis_Value;
        ws.Range(1, 2, 1, m.Columns.Count + 1).Merge().Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        for (var c = 0; c < m.Columns.Count; c++)
        {
            Colored(ws.Cell(2, c + 2), m.Columns[c]);
            ws.Column(c + 2).Width = 38;
        }

        Header(ws.Cell(2, 1), ExportText.Axis_Difficulty);
        ws.Column(1).Width = 26;

        for (var r = 0; r < m.Rows.Count; r++)
        {
            var top = 3 + (r * block);
            var label = ws.Range(top, 1, top + block - 1, 1).Merge();
            Colored(label.FirstCell(), m.Rows[r]);
            label.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            for (var c = 0; c < m.Columns.Count; c++)
            {
                var cellRisks = m[r, c];
                for (var i = 0; i < cellRisks.Count; i++)
                {
                    var level = doc.LevelOf(cellRisks[i], ScaleType.Risk);
                    var cell = ws.Cell(top + i, c + 2);
                    cell.Value = level is null ? cellRisks[i].Title : $"[{Fmt.Abbrev(level)}] {cellRisks[i].Title}";
                    if (level is not null)
                    {
                        Fill(cell, level.Color);
                    }

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
                Fill(cell, level.Color);
            }
        }
    }

    private static void WriteScales(IXLWorksheet ws, RiskMatrixDocument doc)
    {
        string[] headers = [ExportText.Col_Scale, ExportText.Col_Type, ExportText.Col_Order, ExportText.Col_Level, ExportText.Col_Color];
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
                row++;
            }
        }

        ws.Columns().AdjustToContents();
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
        Fill(cell, level.Color);
        cell.Style.Font.Bold = true;
    }

    private static void Fill(IXLCell cell, string hex)
    {
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(hex);
        cell.Style.Font.FontColor = XLColor.FromHtml(Palette.ContrastingText(hex));
    }
}
