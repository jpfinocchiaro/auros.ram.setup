using System.Collections.ObjectModel;
using Auros.Ram.App.Resources;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un nivel en edición: nombre, color de fondo, color de fuente y orden (RF-03).</summary>
public sealed partial class LevelEditViewModel : ObservableObject
{
    public LevelEditViewModel(Level level, ScaleEditorViewModel owner)
    {
        Id = level.Id;
        Owner = owner;
        _name = level.Name;
        _hex = level.Color;
        _textHex = level.TextColor;
        _order = level.Order;
        _rangeFrom = level.RangeFrom;
        _rangeTo = level.RangeTo;
        _namedColor = Find(Palette.Named, level.Color);
        _namedTextColor = Find(Palette.TextNamed, level.TextColor);
    }

    public Guid Id { get; }
    public ScaleEditorViewModel Owner { get; }
    public IReadOnlyList<NamedColor> Colors => Palette.Named;
    public IReadOnlyList<NamedColor> TextColors => Palette.TextNamed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(LowContrast))]
    private string _hex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText), nameof(LowContrast))]
    private string _textHex;

    [ObservableProperty]
    private decimal? _order;

    /// <summary>Rango del nivel: monto (Impacto) o porcentaje (Probabilidad), con 2 decimales.</summary>
    [ObservableProperty]
    private decimal? _rangeFrom;

    [ObservableProperty]
    private decimal? _rangeTo;

    [ObservableProperty]
    private NamedColor? _namedColor;

    [ObservableProperty]
    private NamedColor? _namedTextColor;

    public IBrush Preview => Brushes.FromHex(Hex);
    public IBrush PreviewText => Brushes.FromHex(TextHex);

    /// <summary>RNF-46: avisa si la fuente no se lee bien sobre el fondo (menos de ~3:1, mínimo para etiquetas en negrita).</summary>
    public bool LowContrast =>
        Palette.IsValidHex(Hex) && Palette.IsValidHex(TextHex) && Palette.ContrastRatio(Hex, TextHex) < Palette.WhiteTextMinContrast;

    partial void OnNamedColorChanged(NamedColor? value)
    {
        if (value is not null)
        {
            Hex = value.Hex;
        }
    }

    partial void OnNamedTextColorChanged(NamedColor? value)
    {
        if (value is not null)
        {
            TextHex = value.Hex;
        }
    }

    // Si la fuente era la automática (blanco o negro según el fondo), acompaña el cambio de fondo.
    partial void OnHexChanged(string? oldValue, string newValue)
    {
        if (oldValue is not null && Palette.IsValidHex(newValue) &&
            string.Equals(TextHex, Palette.ContrastingText(oldValue), StringComparison.OrdinalIgnoreCase))
        {
            TextHex = Palette.ContrastingText(newValue);
            NamedTextColor = Find(Palette.TextNamed, TextHex);
        }
    }

    public Level ToLevel() => new()
    {
        Id = Id,
        Name = Name,
        Color = Palette.Normalize(Hex),
        TextColor = Palette.Normalize(TextHex),
        Order = (int)(Order ?? -1),
        RangeFrom = Owner.HasRanges ? RangeFrom : null,
        RangeTo = Owner.HasRanges ? RangeTo : null,
    };

    private static NamedColor? Find(IReadOnlyList<NamedColor> list, string hex) =>
        list.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Alta y edición de una escala (RF-01 a RF-05).</summary>
public sealed partial class ScaleEditorViewModel : ObservableObject
{
    private readonly RiskMatrixDocument _doc;
    private readonly Scale? _original;

    public ScaleEditorViewModel(RiskMatrixDocument doc, Scale? scale, ScaleType type)
    {
        _doc = doc;
        _original = scale;
        _name = scale?.Name ?? "";
        _selectedType = Types.First(t => t.Value == (scale?.Type ?? type));
        Levels = [.. (scale?.OrderedLevels ?? []).Select(l => new LevelEditViewModel(l, this))];
        if (scale is null)
        {
            AddLevel();
        }
    }

    public bool IsNew => _original is null;
    public string WindowTitle => IsNew ? Strings.Scale_New : Strings.Scale_Edit;
    public IReadOnlyList<Choice<ScaleType>> Types { get; } =
        [.. Enum.GetValues<ScaleType>().Select(t => new Choice<ScaleType>(t.DisplayName(), t))];

    public ObservableCollection<LevelEditViewModel> Levels { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRanges), nameof(IsProbability), nameof(RangeHint), nameof(RangeMax))]
    private Choice<ScaleType> _selectedType;

    /// <summary>Impacto y Probabilidad tienen rango por nivel (monto o porcentaje); el nivel de riesgo no.</summary>
    public bool HasRanges => SelectedType.Value != ScaleType.Risk;

    public bool IsProbability => SelectedType.Value == ScaleType.Probability;

    public decimal RangeMax => IsProbability ? 100m : 999_999_999_999.99m;

    public string RangeHint => IsProbability ? Strings.Scale_RangeHintPercent : Strings.Scale_RangeHintAmount;

    /// <summary>Probabilidad: reparte 0–100 % en partes iguales según el orden de los niveles.</summary>
    [RelayCommand]
    private void SplitPercent()
    {
        var levels = Levels.OrderBy(l => l.Order ?? 0).ToList();
        for (var i = 0; i < levels.Count; i++)
        {
            levels[i].RangeFrom = i == 0 ? 0m : Math.Round(100m * i / levels.Count, 2) + 0.01m;
            levels[i].RangeTo = i == levels.Count - 1 ? 100m : Math.Round(100m * (i + 1) / levels.Count, 2);
        }
    }

    [ObservableProperty]
    private string? _error;

    public event EventHandler? Committed;

    /// <summary>La vista confirma antes de quitar un nivel (RF-05); aquí solo se valida su uso.</summary>
    public Func<string, Task<bool>>? ConfirmRemove { get; set; }

    [RelayCommand]
    private void AddLevel()
    {
        var next = Levels.Count == 0 ? 1 : (int)Math.Min(Limits.LevelOrderMax, (Levels.Max(l => l.Order) ?? 0) + 1);
        var color = Palette.Named[Levels.Count % Palette.Named.Count].Hex;
        Levels.Add(new LevelEditViewModel(new Level { Name = "", Color = color, TextColor = Palette.ContrastingText(color), Order = next }, this));
    }

    [RelayCommand]
    private async Task RemoveLevel(LevelEditViewModel level)
    {
        if (_doc.IsLevelInUse(level.Id))
        {
            Error = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Scale_LevelInUse, level.Name);
            return;
        }

        if (ConfirmRemove is not null && !await ConfirmRemove(level.Name))
        {
            return;
        }

        Levels.Remove(level);
        Error = null;
    }

    [RelayCommand]
    private void Save()
    {
        var scale = new Scale
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            Name = Name,
            Type = SelectedType.Value,
            Levels = [.. Levels.Select(l => l.ToLevel())],
        };

        try
        {
            if (_original is null)
            {
                _doc.AddScale(scale);
            }
            else
            {
                _doc.UpdateScale(scale);
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
