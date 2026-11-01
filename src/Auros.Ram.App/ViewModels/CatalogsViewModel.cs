using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Una opción de un catálogo en edición: su nombre (se puede corregir) y cuántos riesgos la usan.</summary>
public sealed partial class CatalogRow(CatalogItem item, int usage, CatalogListViewModel owner) : ObservableObject
{
    public Guid Id { get; } = item.Id;
    public string OriginalName { get; } = item.Name;
    public CatalogListViewModel Owner { get; } = owner;

    [ObservableProperty]
    private string _name = item.Name;

    public string Usage => usage == 0 ? Strings.Catalog_Unused : string.Format(CultureInfo.CurrentCulture, Strings.Catalog_Usage, usage);
}

/// <summary>Un catálogo (consecuencias o criterios de materialidad): agregar, corregir y quitar opciones.</summary>
public sealed partial class CatalogListViewModel : ObservableObject
{
    private readonly RiskMatrixDocument _doc;
    private readonly Action<string?> _report;

    public CatalogListViewModel(RiskMatrixDocument doc, CatalogKind kind, Action<string?> report)
    {
        _doc = doc;
        _report = report;
        Kind = kind;
        Title = kind == CatalogKind.Consequence ? Strings.Risk_Consequences : Strings.Risk_Materiality;
        Reload();
    }

    public CatalogKind Kind { get; }
    public string Title { get; }
    public ObservableCollection<CatalogRow> Items { get; } = [];

    [ObservableProperty]
    private string? _newName;

    public void Reload()
    {
        Items.Clear();
        foreach (var item in _doc.Project.CatalogFor(Kind))
        {
            Items.Add(new CatalogRow(item, _doc.CatalogItemUsage(Kind, item.Id), this));
        }
    }

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewName) || !ApplyRenames())
        {
            return;
        }

        if (Run(() => _doc.AddCatalogItem(Kind, NewName)))
        {
            NewName = null;
        }
    }

    [RelayCommand]
    private void Remove(CatalogRow row)
    {
        if (ApplyRenames())
        {
            Run(() => _doc.RemoveCatalogItem(Kind, row.Id));
        }
    }

    /// <summary>
    /// Aplica los nombres corregidos. Si alguno no se puede (vacío o repetido), se informa y la lista queda como
    /// está para corregirlo; devuelve false.
    /// </summary>
    public bool ApplyRenames()
    {
        foreach (var row in Items.Where(r => r.Name.Trim() != r.OriginalName).ToList())
        {
            try
            {
                _doc.RenameCatalogItem(Kind, row.Id, row.Name);
            }
            catch (DomainException ex)
            {
                _report(ex.Message);
                return false;
            }
        }

        return true;
    }

    /// <summary>Hace el cambio y vuelve a leer la lista (con los nombres ya aplicados).</summary>
    private bool Run(Action action)
    {
        try
        {
            action();
            _report(null);
            return true;
        }
        catch (DomainException ex)
        {
            _report(ex.Message);
            return false;
        }
        finally
        {
            Reload();
        }
    }
}

/// <summary>
/// Catálogos de la matriz (Configuración → Consecuencias y criterios): las opciones de los combos del riesgo.
/// Los cambios se aplican en el documento y se pueden deshacer.
/// </summary>
public sealed partial class CatalogsViewModel : ObservableObject
{
    public CatalogsViewModel(RiskMatrixDocument doc)
    {
        Consequences = new CatalogListViewModel(doc, CatalogKind.Consequence, m => Error = m);
        Materiality = new CatalogListViewModel(doc, CatalogKind.Materiality, m => Error = m);
    }

    public CatalogListViewModel Consequences { get; }
    public CatalogListViewModel Materiality { get; }

    [ObservableProperty]
    private string? _error;

    /// <summary>Al cerrar: aplica los nombres corregidos; si alguno falla, la ventana queda abierta con el motivo.</summary>
    public bool Close() => Consequences.ApplyRenames() && Materiality.ApplyRenames();
}
