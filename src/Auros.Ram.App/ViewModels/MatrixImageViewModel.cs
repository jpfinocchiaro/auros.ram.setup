using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>La imagen ya generada: PNG y los píxeles para el portapapeles de Windows (formato DIB).</summary>
public sealed record RenderedImage(byte[] Png, byte[] Dib, int Width, int Height);

/// <summary>
/// Imagen de la matriz para pegar en presentaciones y documentos: la matriz (a hoy o a una fecha) sobre fondo
/// blanco, con título y leyenda opcionales, que se guarda como PNG o se copia al portapapeles.
/// </summary>
public sealed partial class MatrixImageViewModel : ObservableObject
{
    public const double ImageWidth = 1280;
    private readonly DocumentViewModel _owner;

    public MatrixImageViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        _showAbbreviations = DisplayOptions.Current.ShowLevelAbbreviations;
        _showAmounts = DisplayOptions.Current.ShowImpactAmount;
        Scale = Scales[1];
        Reload();
    }

    /// <summary>Fecha de la matriz (null = hoy).</summary>
    [ObservableProperty] private DateTime? _asOfDate;
    // Partes de la imagen que se muestran: cada una se puede quitar para copiar o guardar solo lo que haga falta.
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasHeader))] private bool _showTitle = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasHeader))] private bool _showSubtitle = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasHeader))] private bool _showLogo = true;
    [ObservableProperty] private bool _showLegend = true;
    [ObservableProperty] private bool _showAxisNames = true;
    [ObservableProperty] private bool _showLevelLabels = true;
    [ObservableProperty] private bool _showRisks = true;
    [ObservableProperty] private bool _showAbbreviations;
    [ObservableProperty] private bool _showAmounts;

    /// <summary>Encabezado: título, proyecto y fecha, o logo (si no queda nada, no ocupa lugar).</summary>
    public bool HasHeader => ShowTitle || ShowSubtitle || (ShowLogo && CompanyLogo is not null);

    // Sin los riesgos, las celdas quedan del mismo alto entre sí, sin el lugar de los títulos.
    partial void OnShowRisksChanged(bool value) => Reload();
    [ObservableProperty] private Choice<int> _scale;
    [ObservableProperty] private string? _status;

    /// <summary>Resolución: 1280 px de ancho multiplicado por el factor (2× por omisión, nítido en una presentación).</summary>
    public IReadOnlyList<Choice<int>> Scales { get; } =
    [
        new(Strings.Image_Scale1, 1),
        new(Strings.Image_Scale2, 2),
        new(Strings.Image_Scale3, 3),
    ];

    public ObservableCollection<PlaybackRow> Rows { get; } = [];
    public ObservableCollection<LevelChip> Columns { get; } = [];
    public ObservableCollection<LevelChip> Legend { get; } = [];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasHeader))] private Bitmap? _companyLogo;

    /// <summary>La vista genera la imagen (necesita el control en pantalla) con el factor de escala indicado.</summary>
    public Func<int, RenderedImage?>? Render { get; set; }

    partial void OnAsOfDateChanged(DateTime? value) => Reload();

    public void Reload()
    {
        var doc = _owner.Document;
        var asOf = AsOfDate is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var m = PriorityMatrix.Build(doc, asOf);
        var height = ShowRisks ? (Math.Max(1, m.MaxCellCount) * 24) + 34 : 64;

        Rows.Clear();
        for (var r = 0; r < m.Rows.Count; r++)
        {
            var cells = new List<PlaybackCell>();
            for (var c = 0; c < m.Columns.Count; c++)
            {
                IReadOnlyList<PlaybackItem> items = ShowRisks
                    ? [.. m[r, c].Select(x => new PlaybackItem(x.Title, false, x.Title, RangeFormat.Value(m.AmountOf(x.Id))))]
                    : [];
                cells.Add(new PlaybackCell(items, LevelChip.From(m.LevelAt(r, c)), height));
            }

            Rows.Add(new PlaybackRow(new LevelChip(m.Rows[r]), cells, height));
        }

        Columns.Clear();
        foreach (var c in m.Columns)
        {
            Columns.Add(new LevelChip(c));
        }

        Legend.Clear();
        foreach (var l in doc.ProjectScale(ScaleType.Risk)?.OrderedLevels.Reverse() ?? [])
        {
            Legend.Add(new LevelChip(l));
        }

        Title = Strings.Section_Matrix;
        var date = (asOf ?? Auros.Ram.Core.RiskMatrixDocument.Today).ToString("d", CultureInfo.CurrentCulture);
        Subtitle = string.Join(" · ", new[] { doc.Project.Name, doc.Project.Company, string.Format(CultureInfo.CurrentCulture, Strings.Matrix_AsOfSubtitle, date) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        CompanyLogo = Brushes.Image(doc.Project.CompanyLogo);
    }

    /// <summary>Guarda la imagen como PNG donde elija el usuario.</summary>
    [RelayCommand]
    private async Task SaveImage()
    {
        if (Render?.Invoke(Scale.Value) is not { } image)
        {
            return;
        }

        var baseName = Path.GetFileNameWithoutExtension(_owner.Path);
        var target = await _owner.Ui.PickSaveFileAsync(Strings.Image_SaveTitle, $"{baseName} - {Strings.Export_SuffixMatrix}.png", FileKind.Image);
        if (target is null)
        {
            return;
        }

        if (!target.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            target += ".png";
        }

        try
        {
            await File.WriteAllBytesAsync(target, image.Png);
            Status = string.Format(CultureInfo.CurrentCulture, Strings.Image_Saved, Path.GetFileName(target), image.Width, image.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Image");
            Status = Strings.Image_SaveError;
        }
    }

    /// <summary>Copia la imagen al portapapeles (PNG y mapa de bits) para pegarla en PowerPoint, Word u otro programa.</summary>
    [RelayCommand]
    private void CopyImage()
    {
        if (Render?.Invoke(Scale.Value) is not { } image)
        {
            return;
        }

        Status = ImageClipboard.SetImage(image.Png, image.Dib)
            ? string.Format(CultureInfo.CurrentCulture, Strings.Image_Copied, image.Width, image.Height)
            : Strings.Image_CopyError;
    }
}
