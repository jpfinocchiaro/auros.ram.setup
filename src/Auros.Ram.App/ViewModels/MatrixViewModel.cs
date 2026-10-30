using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record MatrixItem(Guid RiskId, string Title, LevelChip? RiskLevel, string Tooltip);

public sealed record MatrixCell(IReadOnlyList<MatrixItem> Items, double MinHeight);

public sealed record MatrixRow(LevelChip Level, IReadOnlyList<MatrixCell> Cells, double MinHeight);

/// <summary>Matriz de priorización en pantalla (F4, RF-13 a RF-15).</summary>
public sealed partial class MatrixViewModel : ObservableObject
{
    public const double ItemHeight = 30;
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
    [ObservableProperty] private Bitmap? _consultancyLogo;
    [ObservableProperty] private Bitmap? _clientLogo;
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isEmpty;

    public void Reload()
    {
        var doc = _owner.Document;
        var m = PriorityMatrix.Build(doc);
        var riskLevels = doc.ProjectScale(ScaleType.Risk)?.Levels.ToDictionary(l => l.Id, l => new LevelChip(l)) ?? [];

        // RF-14: todas las celdas con la altura de la que más riesgos tiene.
        var height = (Math.Max(1, m.MaxCellCount) * ItemHeight) + 12;

        Columns.Clear();
        foreach (var c in m.Columns)
        {
            Columns.Add(new LevelChip(c));
        }

        ColumnCount = Math.Max(1, m.Columns.Count);
        Rows.Clear();
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var cells = new List<MatrixCell>();
            for (var c = 0; c < m.Columns.Count; c++)
            {
                cells.Add(new MatrixCell([.. m[r, c].Select(x =>
                {
                    var chip = riskLevels.GetValueOrDefault(x.RiskLevelId);
                    var tip = string.Format(CultureInfo.CurrentCulture, Strings.Matrix_Tooltip, x.Title, chip?.Name ?? "—");
                    return new MatrixItem(x.Id, x.Title, chip, tip);
                })], height));
            }

            Rows.Add(new MatrixRow(new LevelChip(m.Rows[r]), cells, height));
        }

        Legend.Clear();
        foreach (var l in doc.ProjectScale(ScaleType.Risk)?.OrderedLevels.Reverse() ?? [])
        {
            Legend.Add(new LevelChip(l));
        }

        ConsultancyLogo = Brushes.Image(doc.Project.ConsultancyLogo);
        ClientLogo = Brushes.Image(doc.Project.ClientLogo);
        Subtitle = string.Join(" · ", new[] { doc.Project.Name, doc.Project.Client }.Where(s => !string.IsNullOrWhiteSpace(s)));
        IsEmpty = doc.Risks.Count == 0;
    }

    [RelayCommand]
    private Task Open(MatrixItem item) => _owner.EditRiskAsync(item.RiskId);
}
