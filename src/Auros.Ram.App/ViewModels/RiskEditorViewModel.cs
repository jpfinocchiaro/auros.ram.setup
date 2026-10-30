using System.Collections.ObjectModel;
using Auros.Ram.App.Resources;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un botón de color por nivel de la escala del proyecto (RF-10).</summary>
public sealed partial class LevelOption(LevelChip chip, DimensionEditor owner) : ObservableObject
{
    public LevelChip Chip { get; } = chip;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => owner.Select(this);
}

/// <summary>Una dimensión (Riesgo, Valor o Dificultad): nivel elegido y justificación.</summary>
public sealed partial class DimensionEditor : ObservableObject
{
    public DimensionEditor(ScaleType type, Scale? scale, Guid selected, string justification)
    {
        Type = type;
        Label = type.DisplayName();
        ScaleName = scale?.Name ?? "";
        Options = [.. (scale?.OrderedLevels ?? []).Select(l => new LevelOption(new LevelChip(l), this))];
        foreach (var o in Options)
        {
            o.IsSelected = o.Chip.Id == selected;
        }

        _justification = justification;
    }

    public ScaleType Type { get; }
    public string Label { get; }
    public string ScaleName { get; }
    public IReadOnlyList<LevelOption> Options { get; }

    public Guid SelectedId => Options.FirstOrDefault(o => o.IsSelected)?.Chip.Id ?? Guid.Empty;

    public string SelectedText => Options.FirstOrDefault(o => o.IsSelected)?.Chip.Name ?? Strings.Risk_NoLevel;

    [ObservableProperty]
    private string _justification;

    public void Select(LevelOption option)
    {
        foreach (var o in Options)
        {
            o.IsSelected = ReferenceEquals(o, option);
        }

        OnPropertyChanged(nameof(SelectedText));
    }
}

/// <summary>Alta y modificación de un riesgo (F3). Los errores se muestran sin cerrar el diálogo.</summary>
public sealed partial class RiskEditorViewModel : ObservableObject
{
    private readonly RiskMatrixDocument _doc;
    private readonly Risk? _original;

    public RiskEditorViewModel(RiskMatrixDocument doc, Risk? risk)
    {
        _doc = doc;
        _original = risk;
        _title = risk?.Title ?? "";
        _shortDescription = risk?.ShortDescription ?? "";
        _longDescription = risk?.LongDescription ?? "";
        Dimensions = [.. Enum.GetValues<ScaleType>().Select(t =>
            new DimensionEditor(t, doc.ProjectScale(t), risk?.LevelIdFor(t) ?? Guid.Empty, risk?.JustificationFor(t) ?? ""))];
    }

    public string WindowTitle => _original is null ? Strings.Risk_New : Strings.Risk_Edit;
    public ObservableCollection<DimensionEditor> Dimensions { get; }
    public int TitleMax => Limits.RiskTitleMax;
    public int ShortMax => Limits.RiskShortDescriptionMax;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleCounter))]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortCounter))]
    private string _shortDescription;

    [ObservableProperty]
    private string _longDescription;

    [ObservableProperty]
    private string? _error;

    public string TitleCounter => $"{Title.Length}/{Limits.RiskTitleMax}";
    public string ShortCounter => $"{ShortDescription.Length}/{Limits.RiskShortDescriptionMax}";

    /// <summary>Se dispara cuando el riesgo quedó guardado en el documento.</summary>
    public event EventHandler? Committed;

    [RelayCommand]
    private void Save()
    {
        var risk = _original?.Clone() ?? new Risk();
        risk.Title = Title;
        risk.ShortDescription = ShortDescription;
        risk.LongDescription = LongDescription;
        risk.RiskLevelId = Dimensions[0].SelectedId;
        risk.ValueLevelId = Dimensions[1].SelectedId;
        risk.DifficultyLevelId = Dimensions[2].SelectedId;
        risk.RiskJustification = Dimensions[0].Justification;
        risk.ValueJustification = Dimensions[1].Justification;
        risk.DifficultyJustification = Dimensions[2].Justification;

        try
        {
            if (_original is null)
            {
                _doc.AddRisk(risk);
            }
            else
            {
                _doc.UpdateRisk(risk);
            }

            Error = null;
            Committed?.Invoke(this, EventArgs.Empty);
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }
    }
}
