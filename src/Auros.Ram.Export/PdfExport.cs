using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Auros.Ram.Core.Text;
using Auros.Ram.Export.Resources;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Auros.Ram.Export;

[Flags]
public enum PdfReports
{
    None = 0,
    Matrix = 1,
    Summary = 2,
    Detail = 4,
    All = Matrix | Summary | Detail,
}

/// <summary>
/// PDF listo para imprimir (RNF-61, RF-15 a RF-17), opcionalmente protegido con contraseña (RNF-38).
/// </summary>
public static class PdfExport
{
    private static readonly Color Border = new(200, 200, 200);
    private static readonly Color HeaderFill = new(232, 234, 237);

    public static void Write(RiskMatrixDocument doc, string path, PdfReports reports, string? password = null, string appVersion = "")
    {
        SystemFontResolver.EnsureInstalled();
        var document = new Document();
        document.Info.Title = doc.Project.Name;
        document.Info.Subject = doc.Project.Company;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = SystemFontResolver.Family;
        normal.Font.Size = 9;

        if (reports.HasFlag(PdfReports.Matrix))
        {
            MatrixSection(document, doc, appVersion);
        }

        if (reports.HasFlag(PdfReports.Summary))
        {
            SummarySection(document, doc, appVersion);
        }

        if (reports.HasFlag(PdfReports.Detail))
        {
            DetailSection(document, doc, appVersion);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var pdf = renderer.PdfDocument;
        if (!string.IsNullOrEmpty(password))
        {
            pdf.SecuritySettings.UserPassword = password;
            pdf.SecuritySettings.OwnerPassword = password;
        }

        pdf.Save(path);
    }

    // ---- Matriz de riesgo ----------------------------------------------------

    private static void MatrixSection(Document document, RiskMatrixDocument doc, string appVersion)
    {
        var section = NewSection(document, Orientation.Landscape, appVersion);
        Banner(section, doc, ExportText.Title_Matrix);

        var m = PriorityMatrix.Build(doc);
        if (m.Columns.Count == 0 || m.Rows.Count == 0)
        {
            return;
        }

        var usable = Unit.FromCentimeter(29.7 - 3.0);
        var labelWidth = Unit.FromCentimeter(3.5);
        var colWidth = Unit.FromPoint((usable.Point - labelWidth.Point) / m.Columns.Count);

        var table = section.AddTable();
        table.Borders.Color = Border;
        table.Borders.Width = 0.75;
        table.AddColumn(labelWidth);
        foreach (var _ in m.Columns)
        {
            table.AddColumn(colWidth);
        }

        var axis = table.AddRow();
        axis.Cells[0].AddParagraph(ExportText.Axis_Impact).Format.Font.Bold = true;
        axis.Cells[1].MergeRight = m.Columns.Count - 1;
        var axisText = axis.Cells[1].AddParagraph(ExportText.Axis_Probability);
        axisText.Format.Font.Bold = true;
        axisText.Format.Alignment = ParagraphAlignment.Center;

        var head = table.AddRow();
        head.HeadingFormat = true;
        for (var c = 0; c < m.Columns.Count; c++)
        {
            LevelCell(head.Cells[c + 1], m.Columns[c], center: true);
        }

        // RF-14: todas las celdas con la altura de la que más riesgos tiene.
        var rowHeight = Unit.FromCentimeter(Math.Max(1.6, (m.MaxCellCount * 0.62) + 0.3));
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var row = table.AddRow();
            row.Height = rowHeight;
            row.HeightRule = RowHeightRule.AtLeast;
            LevelCell(row.Cells[0], m.Rows[r], center: false);
            row.Cells[0].VerticalAlignment = VerticalAlignment.Center;

            for (var c = 0; c < m.Columns.Count; c++)
            {
                // Mapa de calor: la celda toma el color de su nivel de riesgo; la abreviatura lo dice sin color (RNF-46).
                var cell = row.Cells[c + 1];
                var level = m.LevelAt(r, c);
                if (level is not null)
                {
                    cell.Shading.Color = ToColor(level.Color);
                    cell.Format.Font.Color = ToColor(level.TextColor);
                    var tag = cell.AddParagraph($"[{Fmt.Abbrev(level)}]");
                    tag.Format.Font.Bold = true;
                    tag.Format.Font.Size = 7;
                    tag.Format.SpaceAfter = Unit.FromPoint(2);
                }

                foreach (var risk in m[r, c])
                {
                    var p = cell.AddParagraph("• " + risk.Title);
                    p.Format.SpaceAfter = Unit.FromPoint(2);
                    p.Format.LeftIndent = Unit.FromPoint(2);
                }
            }
        }

        Legend(section, doc);
    }

    private static void Legend(Section section, RiskMatrixDocument doc)
    {
        var scale = doc.ProjectScale(ScaleType.Risk);
        if (scale is null)
        {
            return;
        }

        section.AddParagraph(ExportText.Matrix_Legend).Format.SpaceBefore = Unit.FromPoint(8);
        var levels = scale.OrderedLevels.Reverse().ToList();
        var table = section.AddTable();
        table.Borders.Visible = false;
        foreach (var _ in levels)
        {
            table.AddColumn(Unit.FromCentimeter(3.2));
        }

        var row = table.AddRow();
        for (var i = 0; i < levels.Count; i++)
        {
            var cell = row.Cells[i];
            cell.Shading.Color = ToColor(levels[i].Color);
            var p = cell.AddParagraph($"[{Fmt.Abbrev(levels[i])}] {levels[i].Name}");
            p.Format.Font.Bold = true;
            p.Format.Font.Color = ToColor(levels[i].TextColor);
            p.Format.Alignment = ParagraphAlignment.Center;
        }
    }

    // ---- Ficha resumen (RF-16) ---------------------------------------------

    private static void SummarySection(Document document, RiskMatrixDocument doc, string appVersion)
    {
        var section = NewSection(document, Orientation.Landscape, appVersion);
        Banner(section, doc, ExportText.Title_Summary);

        var table = section.AddTable();
        table.Borders.Color = Border;
        table.Borders.Width = 0.5;
        double[] widths = [1.0, 6.0, 11.7, 2.7, 2.7, 2.6];
        foreach (var w in widths)
        {
            table.AddColumn(Unit.FromCentimeter(w));
        }

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = HeaderFill;
        head.Format.Font.Bold = true;
        string[] titles = [ExportText.Col_Number, ExportText.Col_Title, ExportText.Col_Short, ExportText.Col_Risk, ExportText.Col_Impact, ExportText.Col_Probability];
        for (var i = 0; i < titles.Length; i++)
        {
            head.Cells[i].AddParagraph(titles[i]);
        }

        foreach (var x in Reports.Summary(doc))
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(x.Number.ToString(Fmt.Culture));
            row.Cells[1].AddParagraph(x.Risk.Title).Format.Font.Bold = true;
            AddRich(row.Cells[2].AddParagraph, x.Risk.ShortDescription, 9);
            LevelCell(row.Cells[3], x.RiskLevel, center: true);
            LevelCell(row.Cells[4], x.ImpactLevel, center: true);
            LevelCell(row.Cells[5], x.ProbabilityLevel, center: true);
            row.KeepWith = 0;
        }
    }

    // ---- Ficha detallada (RF-17) -------------------------------------------

    private static void DetailSection(Document document, RiskMatrixDocument doc, string appVersion)
    {
        var section = NewSection(document, Orientation.Portrait, appVersion);
        var rows = Reports.Summary(doc);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                section.AddPageBreak();
            }

            Banner(section, doc, ExportText.Title_Detail);
            var x = rows[i];
            var title = section.AddParagraph($"{x.Number}. {x.Risk.Title}");
            title.Format.Font.Size = 14;
            title.Format.Font.Bold = true;
            title.Format.SpaceAfter = Unit.FromPoint(4);

            var meta = section.AddParagraph($"{ExportText.Project_Name}: {doc.Project.Name}    {ExportText.Project_Company}: {doc.Project.Company}");
            meta.Format.SpaceAfter = Unit.FromPoint(8);

            var table = section.AddTable();
            table.Borders.Color = Border;
            table.Borders.Width = 0.5;
            table.AddColumn(Unit.FromCentimeter(4.2));
            table.AddColumn(Unit.FromCentimeter(3.0));
            table.AddColumn(Unit.FromCentimeter(10.8));
            foreach (var (type, level) in new[] { (ScaleType.Risk, x.RiskLevel), (ScaleType.Impact, x.ImpactLevel), (ScaleType.Probability, x.ProbabilityLevel) })
            {
                var row = table.AddRow();
                row.Cells[0].AddParagraph(type.DisplayName()).Format.Font.Bold = true;
                LevelCell(row.Cells[1], level, center: true);
                var justification = type == ScaleType.Risk ? ExportText.Label_FromHeatMap : x.Risk.JustificationFor(type);
                var j = row.Cells[2].AddParagraph(string.IsNullOrWhiteSpace(justification) ? ExportText.Label_NoJustification : justification);
                if (string.IsNullOrWhiteSpace(justification))
                {
                    j.Format.Font.Italic = true;
                }
            }

            Block(section, ExportText.Col_Short, x.Risk.ShortDescription);
            Block(section, ExportText.Col_Long, x.Risk.LongDescription);
        }
    }

    // ---- Comunes -----------------------------------------------------------

    private static Section NewSection(Document document, Orientation orientation, string appVersion)
    {
        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        // Se fijan las medidas en lugar de confiar en Orientation, que MigraDoc no siempre aplica.
        var landscape = orientation == Orientation.Landscape;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.PageWidth = Unit.FromCentimeter(landscape ? 29.7 : 21.0);
        section.PageSetup.PageHeight = Unit.FromCentimeter(landscape ? 21.0 : 29.7);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Colors.Gray;
        footer.AddText(Fmt.Format(ExportText.Generated_On, DateTime.Now.ToString("g", Fmt.Culture), appVersion) + "    ");
        var parts = ExportText.Page_Footer.Split(["{0}", "{1}"], StringSplitOptions.None);
        footer.AddText(parts[0]);
        footer.AddPageField();
        footer.AddText(parts.Length > 1 ? parts[1] : " / ");
        footer.AddNumPagesField();
        return section;
    }

    /// <summary>Encabezado con el logo de la empresa (RF-15).</summary>
    private static void Banner(Section section, RiskMatrixDocument doc, string title)
    {
        var width = section.PageSetup.PageWidth.Centimeter - 3.0;
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.AddColumn(Unit.FromCentimeter(4));
        table.AddColumn(Unit.FromCentimeter(width - 8));
        table.AddColumn(Unit.FromCentimeter(4));
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        Logo(row.Cells[0], doc.Project.CompanyLogo, ParagraphAlignment.Left);
        var center = row.Cells[1].AddParagraph();
        center.Format.Alignment = ParagraphAlignment.Center;
        center.AddFormattedText(title, new Font { Size = 14, Bold = true });
        center.AddLineBreak();
        center.AddText(string.IsNullOrWhiteSpace(doc.Project.Company) ? doc.Project.Name : $"{doc.Project.Name} — {doc.Project.Company}");
        center.AddLineBreak();
        center.AddFormattedText(doc.Project.Date.ToString("d", Fmt.Culture), new Font { Size = 8, Color = Colors.Gray });
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
    }

    private static void Logo(Cell cell, byte[]? bytes, ParagraphAlignment alignment)
    {
        var p = cell.AddParagraph();
        p.Format.Alignment = alignment;
        if (bytes is { Length: > 0 })
        {
            var image = p.AddImage("base64:" + Convert.ToBase64String(bytes));
            image.LockAspectRatio = true;
            image.Height = Unit.FromCentimeter(1.4);
        }
    }

    private static void Block(Section section, string label, string text)
    {
        var heading = section.AddParagraph(label);
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromPoint(10);
        heading.Format.SpaceAfter = Unit.FromPoint(3);
        AddRich(section.AddParagraph, text, 9);
    }

    /// <summary>Texto con formato (negrita, itálica, subrayado, tamaños y listas), un párrafo por línea.</summary>
    private static void AddRich(Func<Paragraph> newParagraph, string markup, double baseSize)
    {
        foreach (var block in RichText.Parse(markup))
        {
            var p = newParagraph();
            p.Format.SpaceAfter = Unit.FromPoint(2);
            var prefix = RichText.Prefix(block);
            if (prefix.Length > 0)
            {
                p.Format.LeftIndent = Unit.FromCentimeter(0.6);
                p.Format.FirstLineIndent = Unit.FromCentimeter(-0.4);
                p.AddText(prefix);
            }

            foreach (var run in block.Runs)
            {
                var t = p.AddFormattedText(run.Text);
                t.Bold = run.Bold;
                t.Italic = run.Italic;
                if (run.Underline)
                {
                    t.Underline = Underline.Single;
                }

                if (run.Size != RichSize.Normal)
                {
                    t.Size = Unit.FromPoint(baseSize * (run.Size == RichSize.Large ? 1.3 : 0.85));
                }
            }
        }
    }

    private static void LevelCell(Cell cell, Level level, bool center)
    {
        cell.Shading.Color = ToColor(level.Color);
        var p = cell.AddParagraph(level.Name);
        p.Format.Font.Bold = true;
        p.Format.Font.Color = ToColor(level.TextColor);
        p.Format.Alignment = center ? ParagraphAlignment.Center : ParagraphAlignment.Left;
    }

    private static Color ToColor(string hex)
    {
        var (r, g, b) = Fmt.Rgb(hex);
        return new Color(r, g, b);
    }
}
