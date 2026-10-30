using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public sealed record ScaleItem(Scale Scale, IReadOnlyList<LevelChip> Levels, bool IsAssigned)
{
    public string Name => Scale.Name;
    public string LevelCount => string.Format(CultureInfo.CurrentCulture, Strings.Scales_LevelCount, Scale.Levels.Count);
}

public sealed record ScaleGroup(ScaleType Type, string Title, string Purpose, IReadOnlyList<ScaleItem> Scales);

/// <summary>Configuración → Escalas (RF-01 a RF-06), agrupadas por tipo.</summary>
public sealed partial class ScalesViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;

    public ScalesViewModel(DocumentViewModel owner)
    {
        _owner = owner;
        Reload();
    }

    public ObservableCollection<ScaleGroup> Groups { get; } = [];

    public void Reload()
    {
        var doc = _owner.Document;
        Groups.Clear();
        foreach (var type in Enum.GetValues<ScaleType>())
        {
            var purpose = type switch
            {
                ScaleType.Risk => Strings.Scales_PurposeRisk,
                ScaleType.Value => Strings.Scales_PurposeValue,
                _ => Strings.Scales_PurposeDifficulty,
            };
            Groups.Add(new ScaleGroup(type, type.DisplayName(), purpose, [.. doc.Scales
                .Where(s => s.Type == type)
                .Select(s => new ScaleItem(s, [.. s.OrderedLevels.Select(l => new LevelChip(l))], doc.IsScaleAssigned(s.Id)))]));
        }
    }

    [RelayCommand]
    private Task Add(ScaleGroup group) => _owner.EditScaleAsync(null, group.Type);

    [RelayCommand]
    private Task Edit(ScaleItem item) => _owner.EditScaleAsync(item.Scale, item.Scale.Type);

    [RelayCommand]
    private async Task Delete(ScaleItem item)
    {
        if (item.IsAssigned)
        {
            await _owner.Ui.AlertAsync(Strings.Scales_DeleteTitle,
                string.Format(CultureInfo.CurrentCulture, Strings.Scales_DeleteAssigned, item.Name));
            return;
        }

        var ok = await _owner.Ui.ConfirmAsync(Strings.Scales_DeleteTitle,
            string.Format(CultureInfo.CurrentCulture, Strings.Scales_DeleteMessage, item.Name), Strings.Common_Delete);
        if (ok)
        {
            _owner.Run(() => _owner.Document.RemoveScale(item.Scale.Id));
        }
    }

    [RelayCommand]
    private Task SaveTemplate() => _owner.SaveTemplateAsync();
}
