using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Una empresa en edición: su nombre (se puede corregir), su logo y cuántos riesgos tiene.</summary>
public sealed partial class CompanyRow(Company company, int usage, OrganizationsViewModel owner) : ObservableObject
{
    public Guid Id { get; } = company.Id;
    public string OriginalName { get; } = company.Name;
    public OrganizationsViewModel Owner { get; } = owner;
    public Bitmap? LogoImage { get; } = Brushes.Image(company.Logo);
    public bool HasLogo => LogoImage is not null;

    [ObservableProperty]
    private string _name = company.Name;

    public string Usage => usage == 0 ? Strings.Catalog_Unused : string.Format(CultureInfo.CurrentCulture, Strings.Catalog_Usage, usage);
}

/// <summary>Una unidad de negocio en edición: su nombre y cuántos riesgos tiene.</summary>
public sealed partial class UnitRow(BusinessUnit unit, int usage, OrganizationsViewModel owner) : ObservableObject
{
    public Guid Id { get; } = unit.Id;
    public string OriginalName { get; } = unit.Name;
    public OrganizationsViewModel Owner { get; } = owner;

    [ObservableProperty]
    private string _name = unit.Name;

    public string Usage => usage == 0 ? Strings.Catalog_Unused : string.Format(CultureInfo.CurrentCulture, Strings.Catalog_Usage, usage);
}

/// <summary>
/// Maestro de empresas y unidades de negocio (Configuración → Empresas y unidades de negocio): agregar, corregir y
/// quitar empresas (con su logo) y las unidades de cada una. Los cambios se aplican en el documento y se pueden deshacer.
/// </summary>
public sealed partial class OrganizationsViewModel : ObservableObject
{
    private const int MaxLogoBytes = 2 * 1024 * 1024;
    private readonly RiskMatrixDocument _doc;
    private readonly IUiService _ui;

    public OrganizationsViewModel(RiskMatrixDocument doc, IUiService ui)
    {
        _doc = doc;
        _ui = ui;
        Reload(doc.Project.Companies.FirstOrDefault()?.Id);
    }

    public ObservableCollection<CompanyRow> Companies { get; } = [];
    public ObservableCollection<UnitRow> Units { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelected), nameof(UnitsTitle))]
    private CompanyRow? _selected;

    [ObservableProperty]
    private string? _newCompany;

    [ObservableProperty]
    private string? _newUnit;

    [ObservableProperty]
    private string? _error;

    public bool HasSelected => Selected is not null;

    public string UnitsTitle => Selected is null ? Strings.Org_Units : string.Format(CultureInfo.CurrentCulture, Strings.Org_UnitsOf, Selected.OriginalName);

    partial void OnSelectedChanged(CompanyRow? oldValue, CompanyRow? newValue)
    {
        // Al pasar a otra empresa se aplican los nombres corregidos de las unidades de la anterior.
        if (oldValue is not null && !_reloading && !ApplyUnitRenames())
        {
            return;
        }

        LoadUnits();
    }

    private bool _reloading;

    private void Reload(Guid? select)
    {
        _reloading = true;
        Companies.Clear();
        foreach (var c in _doc.Project.Companies)
        {
            Companies.Add(new CompanyRow(c, _doc.CompanyUsage(c.Id), this));
        }

        Selected = Companies.FirstOrDefault(c => c.Id == select) ?? Companies.FirstOrDefault();
        _reloading = false;
        LoadUnits();
    }

    private void LoadUnits()
    {
        Units.Clear();
        foreach (var u in (Selected is null ? null : _doc.Project.FindCompany(Selected.Id))?.Units ?? [])
        {
            Units.Add(new UnitRow(u, _doc.UnitUsage(u.Id), this));
        }
    }

    [RelayCommand]
    private void AddCompany()
    {
        if (!string.IsNullOrWhiteSpace(NewCompany) && ApplyRenames() &&
            Run(() => _doc.AddCompany(NewCompany).Id) is { } id)
        {
            NewCompany = null;
            Reload(id);
        }
    }

    [RelayCommand]
    private void RemoveCompany(CompanyRow row)
    {
        if (ApplyRenames() && Run(() => { _doc.RemoveCompany(row.Id); return row.Id; }) is not null)
        {
            Reload(Selected?.Id == row.Id ? null : Selected?.Id);
        }
    }

    [RelayCommand]
    private async Task PickLogo(CompanyRow row)
    {
        var path = await _ui.PickOpenFileAsync(Strings.Project_PickLogo, FileKind.Image);
        if (path is null)
        {
            return;
        }

        if (new FileInfo(path).Length > MaxLogoBytes)
        {
            Error = Strings.Project_LogoTooLarge;
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        if (Brushes.Image(bytes) is null)
        {
            Error = Strings.Project_LogoInvalid;
            return;
        }

        if (ApplyRenames() && Run(() => { _doc.SetCompanyLogo(row.Id, bytes); return row.Id; }) is not null)
        {
            Reload(row.Id);
        }
    }

    [RelayCommand]
    private void ClearLogo(CompanyRow row)
    {
        if (ApplyRenames() && Run(() => { _doc.SetCompanyLogo(row.Id, null); return row.Id; }) is not null)
        {
            Reload(row.Id);
        }
    }

    [RelayCommand]
    private void AddUnit()
    {
        if (Selected is { } company && !string.IsNullOrWhiteSpace(NewUnit) && ApplyRenames() &&
            Run(() => { _doc.AddUnit(company.Id, NewUnit); return company.Id; }) is not null)
        {
            NewUnit = null;
            Reload(company.Id);
        }
    }

    [RelayCommand]
    private void RemoveUnit(UnitRow row)
    {
        if (ApplyRenames() && Run(() => { _doc.RemoveUnit(row.Id); return row.Id; }) is not null)
        {
            Reload(Selected?.Id);
        }
    }

    /// <summary>Al cerrar: aplica los nombres corregidos; si alguno falla, la ventana queda abierta con el motivo.</summary>
    public bool Close() => ApplyRenames();

    /// <summary>Aplica los nombres corregidos de empresas y unidades; si alguno no se puede, informa y devuelve false.</summary>
    private bool ApplyRenames()
    {
        foreach (var row in Companies.Where(r => r.Name.Trim() != r.OriginalName).ToList())
        {
            if (Run(() => { _doc.RenameCompany(row.Id, row.Name); return row.Id; }) is null)
            {
                return false;
            }
        }

        return ApplyUnitRenames();
    }

    private bool ApplyUnitRenames()
    {
        foreach (var row in Units.Where(r => r.Name.Trim() != r.OriginalName).ToList())
        {
            if (Run(() => { _doc.RenameUnit(row.Id, row.Name); return row.Id; }) is null)
            {
                return false;
            }
        }

        return true;
    }

    private Guid? Run(Func<Guid> action)
    {
        try
        {
            var id = action();
            Error = null;
            return id;
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
            return null;
        }
    }
}
