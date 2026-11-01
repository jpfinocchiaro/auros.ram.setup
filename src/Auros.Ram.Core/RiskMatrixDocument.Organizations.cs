using Auros.Ram.Core.Model;
using Auros.Ram.Core.Resources;
using Auros.Ram.Core.Services;

namespace Auros.Ram.Core;

/// <summary>Empresas y unidades de negocio (maestro de la matriz) y vista filtrada por empresa o unidad.</summary>
public sealed partial class RiskMatrixDocument
{
    /// <summary>La empresa con que arranca una matriz: la del proyecto (o "Empresa"), con su logo y la unidad "General".</summary>
    public static Company DefaultCompany(ProjectInfo project) => new(
        Guid.NewGuid(),
        string.IsNullOrWhiteSpace(project.Company) ? Messages.Default_Company : project.Company.Trim(),
        project.CompanyLogo,
        [new BusinessUnit(Guid.NewGuid(), Messages.Default_Unit)]);

    /// <summary>
    /// Vista de solo lectura con los riesgos de una empresa o unidad (o todos). El proyecto muestra la empresa elegida
    /// (nombre y logo) para que encabezados, informes y exportaciones digan de quién son.
    /// </summary>
    public RiskMatrixDocument ScopedTo(RiskScope scope)
    {
        if (scope.IsAll)
        {
            return this;
        }

        var project = Project.Clone();
        if (scope.CompanyId is { } companyId && Project.FindCompany(companyId) is { } company)
        {
            var unit = scope.UnitId is { } unitId ? company.Units.FirstOrDefault(u => u.Id == unitId) : null;
            project.Company = unit is null ? company.Name : $"{company.Name} · {unit.Name}";
            project.CompanyLogo = company.Logo ?? Project.CompanyLogo;
        }

        return new RiskMatrixDocument(project, _scales, _risks.Where(scope.Includes)) { DatePolicy = DatePolicy, EntryPolicy = EntryPolicy };
    }

    /// <summary>Riesgos de una empresa o de una unidad.</summary>
    public int CompanyUsage(Guid companyId) => _risks.Count(r => r.CompanyId == companyId);

    public int UnitUsage(Guid unitId) => _risks.Count(r => r.UnitId == unitId);

    /// <summary>Agrega una empresa (o devuelve la que ya tiene ese nombre), con una unidad "General".</summary>
    public Company AddCompany(string name)
    {
        var trimmed = (name ?? "").Trim();
        Validation.ValidateCatalogName(trimmed);
        if (Project.Companies.FirstOrDefault(c => Same(c.Name, trimmed)) is { } existing)
        {
            return existing;
        }

        var company = new Company(Guid.NewGuid(), trimmed, null, [new BusinessUnit(Guid.NewGuid(), Messages.Default_Unit)]);
        SetCompanies([.. Project.Companies, company], Validation.Format(Messages.Undo_AddCompany, trimmed));
        return company;
    }

    public void RenameCompany(Guid companyId, string name)
    {
        var trimmed = (name ?? "").Trim();
        Validation.ValidateCatalogName(trimmed);
        var company = Project.FindCompany(companyId) ?? throw new DomainException(Messages.Error_CompanyNotFound);
        if (Project.Companies.Any(c => c.Id != companyId && Same(c.Name, trimmed)))
        {
            throw new DomainException(Validation.Format(Messages.Error_CatalogDuplicate, trimmed));
        }

        Replace(company with { Name = trimmed }, Validation.Format(Messages.Undo_EditCompany, trimmed));
    }

    public void SetCompanyLogo(Guid companyId, byte[]? logo)
    {
        var company = Project.FindCompany(companyId) ?? throw new DomainException(Messages.Error_CompanyNotFound);
        Replace(company with { Logo = logo }, Validation.Format(Messages.Undo_EditCompany, company.Name));
    }

    /// <summary>Quita una empresa sin riesgos. La última no se puede quitar.</summary>
    public void RemoveCompany(Guid companyId)
    {
        var company = Project.FindCompany(companyId) ?? throw new DomainException(Messages.Error_CompanyNotFound);
        if (CompanyUsage(companyId) is var used and > 0)
        {
            throw new DomainException(Validation.Format(Messages.Error_CatalogItemInUse, company.Name, used));
        }

        if (Project.Companies.Count == 1)
        {
            throw new DomainException(Messages.Error_LastCompany);
        }

        SetCompanies([.. Project.Companies.Where(c => c.Id != companyId)], Validation.Format(Messages.Undo_RemoveCompany, company.Name));
    }

    /// <summary>Agrega una unidad de negocio a la empresa (o devuelve la que ya tiene ese nombre).</summary>
    public BusinessUnit AddUnit(Guid companyId, string name)
    {
        var trimmed = (name ?? "").Trim();
        Validation.ValidateCatalogName(trimmed);
        var company = Project.FindCompany(companyId) ?? throw new DomainException(Messages.Error_CompanyNotFound);
        if (company.Units.FirstOrDefault(u => Same(u.Name, trimmed)) is { } existing)
        {
            return existing;
        }

        var unit = new BusinessUnit(Guid.NewGuid(), trimmed);
        Replace(company with { Units = [.. company.Units, unit] }, Validation.Format(Messages.Undo_AddUnit, trimmed, company.Name));
        return unit;
    }

    public void RenameUnit(Guid unitId, string name)
    {
        var trimmed = (name ?? "").Trim();
        Validation.ValidateCatalogName(trimmed);
        var (company, unit) = Project.FindUnit(unitId) ?? throw new DomainException(Messages.Error_UnitNotFound);
        if (company.Units.Any(u => u.Id != unitId && Same(u.Name, trimmed)))
        {
            throw new DomainException(Validation.Format(Messages.Error_CatalogDuplicate, trimmed));
        }

        Replace(company with { Units = [.. company.Units.Select(u => u.Id == unitId ? unit with { Name = trimmed } : u)] },
            Validation.Format(Messages.Undo_EditUnit, trimmed));
    }

    /// <summary>Quita una unidad sin riesgos. La última de una empresa no se puede quitar.</summary>
    public void RemoveUnit(Guid unitId)
    {
        var (company, unit) = Project.FindUnit(unitId) ?? throw new DomainException(Messages.Error_UnitNotFound);
        if (UnitUsage(unitId) is var used and > 0)
        {
            throw new DomainException(Validation.Format(Messages.Error_CatalogItemInUse, unit.Name, used));
        }

        if (company.Units.Count == 1)
        {
            throw new DomainException(Validation.Format(Messages.Error_LastUnit, company.Name));
        }

        Replace(company with { Units = [.. company.Units.Where(u => u.Id != unitId)] }, Validation.Format(Messages.Undo_RemoveUnit, unit.Name));
    }

    /// <summary>Un riesgo sin empresa va a la primera empresa y a su primera unidad.</summary>
    private void AssignDefaultUnit(Risk risk)
    {
        if (risk.CompanyId == Guid.Empty && Project.Companies.Count > 0)
        {
            risk.CompanyId = Project.Companies[0].Id;
        }

        if (risk.UnitId == Guid.Empty && Project.FindCompany(risk.CompanyId) is { Units.Count: > 0 } company)
        {
            risk.UnitId = company.Units[0].Id;
        }
    }

    private void Replace(Company company, string description) =>
        SetCompanies([.. Project.Companies.Select(c => c.Id == company.Id ? company : c)], description);

    private void SetCompanies(IReadOnlyList<Company> companies, string description)
    {
        var next = Project.Clone();
        next.Companies = companies;
        var previous = Project;
        Apply(description, DocumentChangeKind.Project, () => Project = next, () => Project = previous);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}
