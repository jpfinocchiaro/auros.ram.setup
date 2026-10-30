using System.Collections.ObjectModel;
using Auros.Ram.App.Resources;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Un nivel en edición: nombre, color y orden (RF-03).</summary>
public sealed partial class LevelEditViewModel : ObservableObject
{
    public LevelEditViewModel(Level level, ScaleEditorViewModel owner)
    {
        Id = level.Id;
        Owner = owner;
        _name = level.Name;
        _hex = level.Color;
        _order = level.Order;
        _namedColor = Palette.Named.FirstOrDefault(c => string.Equals(c.Hex, level.Color, StringComparison.OrdinalIgnoreCase));
    }

    public Guid Id { get; }
    public ScaleEditorViewModel Owner { get; }
    public IReadOnlyList<NamedColor> Colors => Palette.Named;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview), nameof(PreviewText))]
    private string _hex;

    [ObservableProperty]
    private decimal? _order;

    [ObservableProperty]
    private NamedColor? _namedColor;

    public IBrush Preview => Brushes.FromHex(Hex);
    public IBrush PreviewText => Brushes.FromHex(Palette.ContrastingText(Palette.IsValidHex(Hex) ? Hex : Palette.Gray));

    partial void OnNamedColorChanged(NamedColor? value)
    {
        if (value is not null)
        {
            Hex = value.Hex;
        }
    }

    public Level ToLevel() => new()
    {
        Id = Id,
        Name = Name,
        Color = Palette.Normalize(Hex),
        Order = (int)(Order ?? -1),
    };
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
    private Choice<ScaleType> _selectedType;

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
        Levels.Add(new LevelEditViewModel(new Level { Name = "", Color = color, Order = next }, this));
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
