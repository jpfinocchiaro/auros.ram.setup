using Auros.Ram.Core;
using Auros.Ram.Core.Model;

namespace Auros.Ram.Tests;

internal static class Sample
{
    public static RiskMatrixDocument Document(int riskCount = 3)
    {
        var doc = RiskMatrixDocument.CreateNew(new ProjectInfo { Name = "Relevamiento 2026", Client = "Cliente S.A.", Consultancy = "Auros" });
        for (var i = 0; i < riskCount; i++)
        {
            doc.AddRisk(NewRisk(doc, $"Riesgo {i + 1}", i % 4, i % 3, (i + 1) % 3));
        }

        doc.MarkSaved();
        return doc;
    }

    public static Risk NewRisk(RiskMatrixDocument doc, string title, int risk, int value, int difficulty) => new()
    {
        Title = title,
        ShortDescription = "Resumen de " + title,
        LongDescription = "Detalle, análisis y recomendación de " + title,
        RiskLevelId = Level(doc, ScaleType.Risk, risk).Id,
        ValueLevelId = Level(doc, ScaleType.Value, value).Id,
        DifficultyLevelId = Level(doc, ScaleType.Difficulty, difficulty).Id,
        RiskJustification = "Porque sí",
    };

    public static Level Level(RiskMatrixDocument doc, ScaleType type, int index) =>
        doc.ProjectScale(type)!.OrderedLevels[index];
}

/// <summary>Carpeta temporal por prueba; también aísla la carpeta de datos de la aplicación.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "auros-ram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Persistence.AppPaths.UseRoot(System.IO.Path.Combine(Path, "appdata"));
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
