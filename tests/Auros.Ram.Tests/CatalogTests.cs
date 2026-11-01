using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

/// <summary>
/// Consecuencias potenciales y criterio de materialidad (catálogos de la matriz), borrado de varios riesgos y
/// evaluaciones, y orden de los riesgos dentro de cada celda.
/// </summary>
[Collection("AppPaths")]
public class CatalogTests
{
    [Fact]
    public void New_matrices_start_with_the_suggested_catalogs()
    {
        var doc = Sample.Document(0);
        Assert.Contains(doc.Project.Consequences, c => c.Name == "Accidente laboral");
        Assert.Contains(doc.Project.Consequences, c => c.Name == "Pérdida de reputación");
        Assert.NotEmpty(doc.Project.MaterialityCriteria);
    }

    [Fact]
    public void Catalog_items_are_added_once_renamed_and_removed_only_when_unused()
    {
        var doc = Sample.Document(0);
        var item = doc.AddCatalogItem(CatalogKind.Consequence, "  Multa  ");
        Assert.Equal("Multa", item.Name);
        Assert.Same(item, doc.AddCatalogItem(CatalogKind.Consequence, "multa"));   // sin repetir
        Assert.Throws<DomainException>(() => doc.AddCatalogItem(CatalogKind.Consequence, " "));

        Assert.Throws<DomainException>(() => doc.RenameCatalogItem(CatalogKind.Consequence, item.Id, "Fraude"));   // ya existe
        doc.RenameCatalogItem(CatalogKind.Consequence, item.Id, "Multa regulatoria");

        var risk = Sample.NewRisk(doc, "Con consecuencias", 0, 0);
        risk.ConsequenceIds = [item.Id];
        doc.AddRisk(risk);
        Assert.Equal(1, doc.CatalogItemUsage(CatalogKind.Consequence, item.Id));
        Assert.Throws<DomainException>(() => doc.RemoveCatalogItem(CatalogKind.Consequence, item.Id));

        var unused = doc.Project.Consequences.First(c => c.Name == "Daño ambiental");
        doc.RemoveCatalogItem(CatalogKind.Consequence, unused.Id);
        Assert.DoesNotContain(doc.Project.Consequences, c => c.Id == unused.Id);
        doc.History.Undo();
        Assert.Contains(doc.Project.Consequences, c => c.Id == unused.Id);
    }

    [Fact]
    public void Risks_keep_their_consequences_and_materiality_through_saving_and_json()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(0);
        var risk = Sample.NewRisk(doc, "Fraude en compras", 2, 2);
        var fraud = doc.Project.Consequences.First(c => c.Name == "Fraude");
        var loss = doc.Project.Consequences.First(c => c.Name == "Pérdida de rentabilidad");
        risk.ConsequenceIds = [fraud.Id, loss.Id, fraud.Id];
        risk.MaterialityId = doc.Project.MaterialityCriteria[0].Id;
        var id = doc.AddRisk(risk).Id;
        Assert.Equal([fraud.Id, loss.Id], doc.FindRisk(id)!.ConsequenceIds);   // sin repetidas
        Assert.Equal(["Fraude", "Pérdida de rentabilidad"], doc.ConsequenceNames(doc.FindRisk(id)!));

        // Un id que no está en el catálogo se rechaza.
        var bad = doc.FindRisk(id)!.Clone();
        bad.MaterialityId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => doc.UpdateRisk(bad));

        var path = dir.File("catalogos.rmx");
        using (var key = FileKey.CreateNew("clave-del-archivo"))
        {
            RmxFile.Save(doc, path, key);
        }

        var opened = RmxFile.Open(path, "clave-del-archivo");
        using var _ = opened.Key;
        foreach (var copy in new[] { opened.Document, JsonExchange.Import(JsonExchange.Export(doc)) })
        {
            Assert.Equal(doc.Project.Consequences.Select(c => c.Name), copy.Project.Consequences.Select(c => c.Name));
            var r = copy.FindRisk(id)!;
            Assert.Equal(["Fraude", "Pérdida de rentabilidad"], copy.ConsequenceNames(r));
            Assert.Equal(doc.Project.MaterialityCriteria[0].Name, copy.MaterialityName(r));
        }
    }

    [Fact]
    public void Older_files_get_the_suggested_catalogs()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema-v5.rmx");
        using var dir = new TempDir();
        var copy = dir.File("v5.rmx");
        File.Copy(fixture, copy);
        var result = RmxFile.Open(copy, FixtureGenerator.Password);
        using var _ = result.Key;
        Assert.Contains(result.Document.Project.Consequences, c => c.Name == "Accidente laboral");
        Assert.Equal(5, result.Document.Project.MaterialityCriteria.Count);
        Assert.All(result.Document.Risks, r => Assert.Empty(r.ConsequenceIds));
    }

    [Fact]
    public void Several_risks_and_assessments_are_removed_in_one_undoable_step()
    {
        var doc = Sample.Document(5);
        var today = RiskMatrixDocument.Today;
        foreach (var r in doc.Risks.ToList())
        {
            doc.AddAssessment(r.Id, r.ImpactLevelId, Sample.Level(doc, ScaleType.Probability, 0).Id, today.AddDays(-1));
        }

        // Se quitan dos riesgos enteros y una evaluación de un tercero.
        var titles = doc.Risks.Select(r => r.Title).ToList();
        var third = doc.Risks[2];
        doc.RemoveAssessments([(third.Id, third.Assessments[^1].Id)]);
        doc.RemoveRisks([doc.Risks[0].Id, doc.Risks[4].Id]);
        Assert.Equal(3, doc.Risks.Count);
        Assert.Single(doc.FindRisk(third.Id)!.Assessments);

        // A un riesgo no se le pueden quitar todas las evaluaciones.
        var only = doc.FindRisk(third.Id)!;
        Assert.Throws<DomainException>(() => doc.RemoveAssessments([(only.Id, only.Assessments[0].Id)]));

        doc.History.Undo();
        doc.History.Undo();
        Assert.Equal(titles, doc.Risks.Select(r => r.Title));   // mismo orden que antes
        Assert.Equal(2, doc.FindRisk(third.Id)!.Assessments.Count);
    }

    [Fact]
    public void Within_a_cell_risks_go_from_the_largest_amount_to_the_smallest()
    {
        var doc = Sample.Document(0);
        Risk Add(string title, decimal? amount)
        {
            var r = Sample.NewRisk(doc, title, 2, 2);
            r.ImpactAmount = amount;
            return doc.AddRisk(r);
        }

        Add("B sin monto", null);
        Add("Chico", 100m);
        Add("A sin monto", null);
        Add("Grande", 5000m);

        var m = PriorityMatrix.Build(doc);
        Assert.Equal(["Grande", "Chico", "A sin monto", "B sin monto"], m[2, 2].Select(r => r.Title));
        Assert.Equal(5000m, m.AmountOf(m[2, 2][0].Id));
    }
}
