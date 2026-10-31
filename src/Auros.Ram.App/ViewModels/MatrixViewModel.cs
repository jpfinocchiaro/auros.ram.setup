using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Views;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record MatrixItem(Guid RiskId, string Title, string Tooltip);

/// <param name="Level">Nivel de riesgo de la celda según el mapa de calor: da el color de fondo y de fuente.</param>
/// <param name="ImpactId">Nivel de impacto de la fila (para agregar un riesgo con doble clic).</param>
/// <param name="ProbabilityId">Nivel de probabilidad de la columna.</param>
public sealed record MatrixCell(IReadOnlyList<MatrixItem> Items, double MinHeight, LevelChip? Level, Guid ImpactId, Guid ProbabilityId, string Tooltip);

public sealed record MatrixRow(LevelChip Level, IReadOnlyList<MatrixCell> Cells, double MinHeight);

/// <summary>Un riesgo para elegir de qué riesgo se dibuja el recorrido (Id vacío = ninguno).</summary>
public sealed record PathOption(Guid RiskId, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Matriz de riesgo en pantalla (F4, RF-13 a RF-15): impacto × probabilidad, coloreada como mapa de calor.</summary>
public sealed partial class MatrixViewModel : ObservableObject
{
    public const double ItemHeight = 30;
    public const double CellHeaderHeight = 20;
    private readonly DocumentViewModel _owner;

    public MatrixViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public ObservableCollection<LevelChip> Columns { get; } = [];
    public ObservableCollection<MatrixRow> Rows { get; } = [];
    public ObservableCollection<LevelChip> Legend { get; } = [];

    [ObservableProperty] private int _columnCount = 1;
    [ObservableProperty] private Bitmap? _companyLogo;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private int _rowCount = 1;

    /// <summary>Fecha a la que se mira la matriz (null = hoy, el estado actual).</summary>
    [ObservableProperty] private DateTime? _asOfDate;

    /// <summary>Riesgo cuyo recorrido se dibuja sobre la matriz.</summary>
    [ObservableProperty] private PathOption? _pathRisk;

    [ObservableProperty] private IReadOnlyList<PathMarker> _pathMarkers = [];

    public ObservableCollection<PathOption> PathRisks { get; } = [];

    public bool IsHistorical => AsOfDate is not null;

    partial void OnAsOfDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(IsHistorical));
        Reload();
    }

    partial void OnPathRiskChanged(PathOption? value) => UpdatePath();

    /// <summary>Vuelve a la matriz de hoy.</summary>
    [RelayCommand]
    private void ShowToday() => AsOfDate = null;

    private DateOnly? AsOf => AsOfDate is { } d ? DateOnly.FromDateTime(d) : null;

    public void Reload()
    {
        var doc = _owner.Document;
        var m = PriorityMatrix.Build(doc, AsOf);
        // RF-14: todas las celdas con la altura de la que más riesgos tiene, más el rótulo del nivel de la celda.
        var height = (Math.Max(1, m.MaxCellCount) * ItemHeight) + 12 + CellHeaderHeight;

        Columns.Clear();
        foreach (var c in m.Columns)
        {
            Columns.Add(new LevelChip(c));
        }

        ColumnCount = Math.Max(1, m.Columns.Count);
        RowCount = Math.Max(1, m.Rows.Count);
        Rows.Clear();
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var cells = new List<MatrixCell>();
            for (var c = 0; c < m.Columns.Count; c++)
            {
                var level = LevelChip.From(m.LevelAt(r, c));
                cells.Add(new MatrixCell([.. m[r, c].Select(x =>
                {
                    var tip = string.Format(CultureInfo.CurrentCulture, Strings.Matrix_Tooltip, x.Title, level?.Name ?? "—");
                    return new MatrixItem(x.Id, x.Title, tip);
                })], height, level, m.Rows[r].Id, m.Columns[c].Id,
                    string.Format(CultureInfo.CurrentCulture, Strings.Matrix_CellTip, m.Rows[r].Name, m.Columns[c].Name)));
            }

            Rows.Add(new MatrixRow(new LevelChip(m.Rows[r]), cells, height));
        }

        Legend.Clear();
        foreach (var l in doc.ProjectScale(ScaleType.Risk)?.OrderedLevels.Reverse() ?? [])
        {
            Legend.Add(new LevelChip(l));
        }

        CompanyLogo = Brushes.Image(doc.Project.CompanyLogo);
        var asOf = AsOf is { } date ? string.Format(CultureInfo.CurrentCulture, Strings.Matrix_AsOfSubtitle, date.ToString("d", CultureInfo.CurrentCulture)) : null;
        Subtitle = string.Join(" · ", new[] { doc.Project.Name, doc.Project.Company, asOf }.Where(s => !string.IsNullOrWhiteSpace(s)));
        IsEmpty = doc.Risks.Count == 0;

        // Lista para elegir el recorrido, conservando el elegido si sigue existiendo.
        var selected = PathRisk?.RiskId ?? Guid.Empty;
        PathRisks.Clear();
        PathRisks.Add(new PathOption(Guid.Empty, Strings.Matrix_PathNone));
        foreach (var r in doc.Risks.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            PathRisks.Add(new PathOption(r.Id, r.Title));
        }

        PathRisk = PathRisks.FirstOrDefault(p => p.RiskId == selected) ?? PathRisks[0];
        UpdatePath();
    }

    /// <summary>Celdas por las que pasó el riesgo elegido (hasta la fecha de la matriz), en orden.</summary>
    private void UpdatePath()
    {
        var doc = _owner.Document;
        if (PathRisk is not { RiskId: var id } || doc.FindRisk(id) is not { } risk)
        {
            PathMarkers = [];
            return;
        }

        var rows = doc.ProjectScale(ScaleType.Impact)?.OrderedLevels.Reverse().ToList() ?? [];
        var columns = doc.ProjectScale(ScaleType.Probability)?.OrderedLevels.ToList() ?? [];
        PathMarkers = [.. RiskHistory.Steps(doc, risk, AsOf)
            .Select(s => new PathMarker(
                rows.FindIndex(l => l.Id == s.Impact.Id),
                columns.FindIndex(l => l.Id == s.Probability.Id),
                s.Assessment.Date.ToString("dd/MM/yy", CultureInfo.CurrentCulture)))
            .Where(p => p.Row >= 0 && p.Column >= 0)];
    }

    public DisplayOptions Display => DisplayOptions.Current;

    [RelayCommand]
    private Task Open(MatrixItem item) => _owner.EditRiskAsync(item.RiskId);

    /// <summary>Clic derecho en un riesgo de la matriz: nueva evaluación.</summary>
    [RelayCommand]
    private Task Reassess(MatrixItem item) => _owner.ReassessRiskAsync(item.RiskId);

    /// <summary>Doble clic en una celda: riesgo nuevo con el impacto y la probabilidad de esa celda ya elegidos.</summary>
    public Task AddAt(MatrixCell cell) => _owner.NewRiskAtAsync(cell.ImpactId, cell.ProbabilityId);
}
