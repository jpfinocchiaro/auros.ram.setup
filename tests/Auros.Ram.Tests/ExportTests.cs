using System.Text;
using Auros.Ram.Export;

namespace Auros.Ram.Tests;

[Collection("AppPaths")]
public class ExportTests
{
    [Fact]
    public void Csv_risks_and_matrix()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(4);
        var risk = doc.Risks[0].Clone();
        risk.Title = "=CMD; con \"comillas\"";
        doc.UpdateRisk(risk);

        var path = dir.File("riesgos.csv");
        CsvExport.WriteRisks(doc, path);
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Equal(5, lines.Length);
        Assert.Contains("\"'=CMD; con \"\"comillas\"\"\"", File.ReadAllText(path), StringComparison.Ordinal);

        var matrix = dir.File("matriz.csv");
        CsvExport.WriteMatrix(doc, matrix);
        Assert.Equal(6, File.ReadAllLines(matrix).Length);   // encabezado + 5 niveles de impacto
    }

    [Fact]
    public void Xlsx_is_created()
    {
        using var dir = new TempDir();
        var path = dir.File("matriz.xlsx");
        XlsxExport.Write(Sample.Document(10), path);
        using var wb = new ClosedXML.Excel.XLWorkbook(path);
        Assert.Equal(5, wb.Worksheets.Count);   // proyecto, riesgos, matriz, historial y escalas
        Assert.Equal(11, wb.Worksheet(2).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Pdf_plain_and_password_protected_RNF38()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(6);
        var plain = dir.File("informe.pdf");
        PdfExport.Write(doc, plain, PdfReports.All, appVersion: "0.1.0");
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(plain), 0, 4), StringComparison.Ordinal);

        var protectedPdf = dir.File("protegido.pdf");
        PdfExport.Write(doc, protectedPdf, PdfReports.Matrix, password: "clave-del-pdf");
        Assert.Contains("/Encrypt", Encoding.Latin1.GetString(File.ReadAllBytes(protectedPdf)), StringComparison.Ordinal);
    }

    /// <summary>Con AUROS_EXPORT_SAMPLES=carpeta deja un PDF y un Excel de muestra para revisión visual.</summary>
    [Fact]
    public void Write_samples_for_review()
    {
        var target = Environment.GetEnvironmentVariable("AUROS_EXPORT_SAMPLES");
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        Directory.CreateDirectory(target);
        var doc = Sample.Document(9);
        var formatted = doc.Risks[0].Clone();
        formatted.ShortDescription = "Resumen con **negrita** e *itálica*.";
        formatted.LongDescription = """
            {g}Hallazgo{/g}
            Texto con __subrayado__ y {p}letra chica{/p}.
            - Viñeta uno
            - Viñeta dos
            1. Paso uno
            2. Paso dos
            """;
        doc.UpdateRisk(formatted);

        // Un riesgo con historial: evaluado hace meses, empeoró y después mejoró.
        var today = Auros.Ram.Core.RiskMatrixDocument.Today;
        var tracked = doc.AddRisk(Sample.NewRisk(doc, "Riesgo con historial", 2, 2), today.AddDays(-120), "Relevamiento inicial");
        var worse = doc.FindRisk(tracked.Id)!.Clone();
        worse.ProbabilityLevelId = Sample.Level(doc, Core.Model.ScaleType.Probability, 4).Id;
        doc.AddAssessment(worse.Id, worse.ImpactLevelId, worse.ProbabilityLevelId, today.AddDays(-60), "Nuevos incidentes");
        var better = doc.FindRisk(tracked.Id)!.Clone();
        better.ImpactLevelId = Sample.Level(doc, Core.Model.ScaleType.Impact, 1).Id;
        better.ProbabilityLevelId = Sample.Level(doc, Core.Model.ScaleType.Probability, 1).Id;
        doc.AddAssessment(better.Id, better.ImpactLevelId, better.ProbabilityLevelId, today.AddDays(-5), "Se aplicó un control");
        PdfExport.Write(doc, Path.Combine(target, "informe.pdf"), PdfReports.All, appVersion: "0.1.0");
        XlsxExport.Write(doc, Path.Combine(target, "matriz.xlsx"));
    }
}
