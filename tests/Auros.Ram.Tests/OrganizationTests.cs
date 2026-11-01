using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Serialization;
using Auros.Ram.Core.Services;
using Auros.Ram.Persistence;

namespace Auros.Ram.Tests;

/// <summary>Varias empresas con sus unidades de negocio: maestro, empresa y unidad de cada riesgo, y vista filtrada.</summary>
[Collection("AppPaths")]
public class OrganizationTests
{
    [Fact]
    public void New_matrices_start_with_one_company_and_its_general_unit()
    {
        var doc = Sample.Document(3);
        var company = Assert.Single(doc.Project.Companies);
        Assert.Equal(doc.Project.Company, company.Name);
        Assert.Equal("General", Assert.Single(company.Units).Name);
        Assert.All(doc.Risks, r => Assert.Equal((company.Id, company.Units[0].Id), (r.CompanyId, r.UnitId)));
    }

    [Fact]
    public void Companies_and_units_are_added_once_renamed_and_removed_only_when_unused()
    {
        var doc = Sample.Document(1);
        var first = doc.Project.Companies[0];
        var other = doc.AddCompany("  Filial Norte ");
        Assert.Equal("Filial Norte", other.Name);
        Assert.Same(other, doc.AddCompany("filial norte"));   // sin repetir
        Assert.Equal("General", Assert.Single(other.Units).Name);

        var sales = doc.AddUnit(other.Id, "Ventas");
        Assert.Equal(sales, doc.AddUnit(other.Id, "VENTAS"));
        Assert.Throws<DomainException>(() => doc.RenameCompany(other.Id, first.Name));   // ya existe
        doc.RenameUnit(sales.Id, "Ventas mayoristas");
        Assert.Equal("Ventas mayoristas", doc.Project.FindUnit(sales.Id)!.Value.Unit.Name);

        // La empresa con riesgos no se puede quitar; la unidad sin riesgos sí, salvo la última de la empresa.
        Assert.Throws<DomainException>(() => doc.RemoveCompany(first.Id));
        doc.RemoveUnit(sales.Id);
        Assert.Throws<DomainException>(() => doc.RemoveUnit(doc.Project.FindCompany(other.Id)!.Units[0].Id));
        doc.RemoveCompany(other.Id);
        Assert.Single(doc.Project.Companies);
        Assert.Throws<DomainException>(() => doc.RemoveCompany(first.Id));   // la última
        doc.History.Undo();
        Assert.Equal(2, doc.Project.Companies.Count);
    }

    [Fact]
    public void A_risk_needs_a_company_and_a_unit_of_that_company()
    {
        var doc = Sample.Document(0);
        var north = doc.AddCompany("Norte");
        var risk = Sample.NewRisk(doc, "Sin unidad válida", 1, 1);
        risk.CompanyId = north.Id;
        risk.UnitId = doc.Project.Companies[0].Units[0].Id;   // unidad de otra empresa
        Assert.Throws<DomainException>(() => doc.AddRisk(risk));

        risk.CompanyId = Guid.NewGuid();
        Assert.Throws<DomainException>(() => doc.AddRisk(risk));

        risk.CompanyId = north.Id;
        risk.UnitId = north.Units[0].Id;
        Assert.Equal(north.Id, doc.AddRisk(risk).CompanyId);
    }

    [Fact]
    public void The_scoped_view_shows_only_the_chosen_company_and_unit_with_its_logo()
    {
        var doc = Sample.Document(4);
        var north = doc.AddCompany("Norte");
        var plant = doc.AddUnit(north.Id, "Planta");
        doc.SetCompanyLogo(north.Id, [1, 2, 3]);
        foreach (var r in doc.Risks.Take(2).ToList())
        {
            var moved = r.Clone();
            (moved.CompanyId, moved.UnitId) = (north.Id, plant.Id);
            doc.UpdateRisk(moved);
        }

        Assert.Same(doc, doc.ScopedTo(RiskScope.All));
        var byCompany = doc.ScopedTo(new RiskScope(north.Id, null));
        Assert.Equal(2, byCompany.Risks.Count);
        Assert.Equal([1, 2, 3], byCompany.Project.CompanyLogo);
        Assert.Contains("Norte", byCompany.Project.Company);

        Assert.Empty(doc.ScopedTo(new RiskScope(north.Id, north.Units[0].Id)).Risks);
        Assert.Equal(2, doc.ScopedTo(new RiskScope(doc.Project.Companies[0].Id, null)).Risks.Count);
        Assert.Equal(4, doc.Risks.Count);   // el documento no cambia
    }

    [Fact]
    public void Companies_survive_saving_and_json()
    {
        using var dir = new TempDir();
        var doc = Sample.Document(2);
        var north = doc.AddCompany("Norte");
        var plant = doc.AddUnit(north.Id, "Planta");
        doc.SetCompanyLogo(north.Id, [9, 8, 7]);
        var moved = doc.Risks[0].Clone();
        (moved.CompanyId, moved.UnitId) = (north.Id, plant.Id);
        doc.UpdateRisk(moved);

        var path = dir.File("empresas.rmx");
        using (var key = FileKey.CreateNew("clave-del-archivo"))
        {
            RmxFile.Save(doc, path, key);
        }

        var opened = RmxFile.Open(path, "clave-del-archivo");
        using var _ = opened.Key;
        foreach (var copy in new[] { opened.Document, JsonExchange.Import(JsonExchange.Export(doc)) })
        {
            Assert.Equal(doc.Project.Companies.Select(c => c.Name), copy.Project.Companies.Select(c => c.Name));
            Assert.Equal(["General", "Planta"], copy.Project.FindCompany(north.Id)!.Units.Select(u => u.Name));
            Assert.Equal([9, 8, 7], copy.Project.FindCompany(north.Id)!.Logo);
            Assert.Equal("Norte · Planta", copy.OrganizationName(copy.FindRisk(moved.Id)!));
        }
    }

    [Fact]
    public void Older_files_get_the_project_company_with_a_general_unit()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema-v6.rmx");
        using var dir = new TempDir();
        var copy = dir.File("v6.rmx");
        File.Copy(fixture, copy);
        var result = RmxFile.Open(copy, FixtureGenerator.Password);
        using var _ = result.Key;
        var company = Assert.Single(result.Document.Project.Companies);
        Assert.Equal("Cliente S.A.", company.Name);
        Assert.Equal("General", Assert.Single(company.Units).Name);
        Assert.All(result.Document.Risks, r => Assert.Equal("Cliente S.A. · General", result.Document.OrganizationName(r)));
    }
}
