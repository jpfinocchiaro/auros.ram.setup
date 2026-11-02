using System.Collections.ObjectModel;
using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core;
using Auros.Ram.Core.Model;
using Auros.Ram.Core.Services;
using Auros.Ram.Core.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

public enum PasswordMode
{
    /// <summary>Abrir o desbloquear: una sola contraseña.</summary>
    Enter,

    /// <summary>Archivo o plantilla nueva: contraseña + confirmación + advertencia (RNF-34, RNF-35). Vacía = sin contraseña.</summary>
    Create,

    /// <summary>Cambio de contraseña: actual + nueva + confirmación. Nueva vacía = quitar la contraseña.</summary>
    Change,
}

public sealed partial class PasswordDialogViewModel : ObservableObject
{
    public PasswordDialogViewModel(PasswordMode mode, string title, string fileName)
    {
        Mode = mode;
        Title = title;
        FileName = fileName;
    }

    public PasswordMode Mode { get; }
    public string Title { get; }
    public string FileName { get; }

    public bool IsEnter => Mode == PasswordMode.Enter;
    public bool IsCreate => Mode == PasswordMode.Create;
    public bool IsChange => Mode == PasswordMode.Change;
    public bool NeedsNew => Mode != PasswordMode.Enter;
    public string FirstLabel => Mode == PasswordMode.Change ? Strings.Password_Current : Strings.Password_Label;

    /// <summary>Valida y aplica la contraseña. Devuelve un mensaje de error o null si se puede cerrar.</summary>
    public Func<PasswordDialogViewModel, Task<string?>>? Submit { get; set; }

    [ObservableProperty]
    private string _current = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthText), nameof(StrengthValue), nameof(StrengthBrush), nameof(WithoutPassword), nameof(ShowNoRecovery))]
    private string _newPassword = "";

    [ObservableProperty]
    private string _confirmation = "";

    [ObservableProperty]
    private bool _acknowledged;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Por ahora la contraseña es opcional: si se deja en blanco el archivo no se cifra.</summary>
    public bool WithoutPassword => NeedsNew && NewPassword.Length == 0;

    /// <summary>La advertencia de no recuperación solo aplica si se eligió una contraseña.</summary>
    public bool ShowNoRecovery => IsCreate && NewPassword.Length > 0;

    public string StrengthText => NewPassword.Length == 0
        ? Strings.Password_MinLength
        : PasswordPolicy.Describe(PasswordPolicy.Evaluate(NewPassword));

    public double StrengthValue => NewPassword.Length == 0 ? 0 : ((int)PasswordPolicy.Evaluate(NewPassword) + 1) * 20;

    public Avalonia.Media.IBrush StrengthBrush => PasswordPolicy.Evaluate(NewPassword) switch
    {
        PasswordStrength.VeryWeak or PasswordStrength.Weak => Brushes.FromHex("#D93025"),
        PasswordStrength.Fair => Brushes.FromHex("#F28C28"),
        _ => Brushes.FromHex("#2E9E44"),
    };

    /// <summary>La contraseña resultante: la nueva al crear o cambiar, la ingresada al abrir.</summary>
    public string Result => Mode == PasswordMode.Enter ? Current : NewPassword;

    /// <summary>Reglas locales (largo, coincidencia, advertencia leída). Null si todo está bien.</summary>
    public string? LocalValidation()
    {
        if (Mode == PasswordMode.Enter)
        {
            return string.IsNullOrEmpty(Current) ? Strings.Password_Required : null;
        }

        if (Mode == PasswordMode.Change && string.IsNullOrEmpty(Current))
        {
            return Strings.Password_CurrentRequired;
        }

        if (NewPassword.Length == 0 && Confirmation.Length == 0)
        {
            return null;
        }

        if (!PasswordPolicy.IsAcceptable(NewPassword))
        {
            return Strings.Password_TooShort;
        }

        if (NewPassword != Confirmation)
        {
            return Strings.Password_Mismatch;
        }

        if (Mode == PasswordMode.Create && !Acknowledged)
        {
            return Strings.Password_AckRequired;
        }

        return null;
    }
}

/// <summary>Un tamaño de matriz para elegir al crearla, con la miniatura de su mapa de calor por omisión.</summary>
public sealed partial class MatrixSizeOption : ObservableObject
{
    public const double PreviewSize = 64;

    public MatrixSizeOption(ScalePreset preset)
    {
        Preset = preset;
        Size = (int)preset;
        Label = $"{Size} × {Size}";
        var m = PriorityMatrix.Build(RiskMatrixDocument.CreateNew(scales: DefaultScales.Create(preset)));
        Cells = [.. Enumerable.Range(0, m.Rows.Count).SelectMany(r => Enumerable.Range(0, m.Columns.Count)
            .Select(c => m.LevelAt(r, c) is { } level ? Brushes.FromHex(level.Color) : Avalonia.Media.Brushes.Transparent))];
    }

    public ScalePreset Preset { get; }
    public int Size { get; }
    public string Label { get; }
    public IReadOnlyList<Avalonia.Media.IBrush> Cells { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class NewMatrixViewModel : ObservableObject
{
    public NewMatrixViewModel()
    {
        SizeOptions = [.. DefaultScales.Presets.Select(p => new MatrixSizeOption(p))];
        SizeOptions.First(o => o.Preset == ScalePreset.Matrix5x5).IsSelected = true;
    }

    /// <summary>De 2 × 2 a 10 × 10; junto con "plantilla" forman un único grupo de opciones.</summary>
    public IReadOnlyList<MatrixSizeOption> SizeOptions { get; }

    /// <summary>Tamaño elegido, o null si se cargan las escalas de una plantilla.</summary>
    public ScalePreset? SelectedPreset => UseTemplate ? null : SizeOptions.FirstOrDefault(o => o.IsSelected)?.Preset ?? ScalePreset.Matrix5x5;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _company = "";

    [ObservableProperty]
    private bool _useTemplate;

    [ObservableProperty]
    private string? _error;
}

public sealed partial class ExportConfirmViewModel(bool isPdf) : ObservableObject
{
    public bool IsPdf { get; } = isPdf;

    [ObservableProperty]
    private bool _protectPdf;

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _confirmation = "";

    [ObservableProperty]
    private string? _error;
}

public sealed partial class PreferencesViewModel : ObservableObject
{
    [ObservableProperty]
    private decimal? _autosaveMinutes;

    [ObservableProperty]
    private decimal? _lockMinutes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HideAbbreviations))]
    private bool _showAbbreviations = true;

    /// <summary>Opción "No" del par Sí / No.</summary>
    public bool HideAbbreviations
    {
        get => !ShowAbbreviations;
        set => ShowAbbreviations = !value;
    }

    /// <summary>Fechas de evaluación anteriores a hoy al dar de alta un riesgo.</summary>
    [ObservableProperty]
    private bool _allowPastDateOnCreate = true;

    /// <summary>Al modificar un riesgo, fechas anteriores a una evaluación ya registrada.</summary>
    [ObservableProperty]
    private bool _allowDateBeforeRecorded = true;

    /// <summary>Fechas anteriores a hoy al modificar un riesgo o corregir una evaluación.</summary>
    [ObservableProperty]
    private bool _allowPastDateOnEdit = true;

    public IReadOnlyList<Choice<EntryMode>> ImpactEntries { get; } =
    [
        new(Strings.Prefs_EntryLevel, EntryMode.Level),
        new(Strings.Prefs_EntryAmount, EntryMode.Value),
        new(Strings.Prefs_EntryEither, EntryMode.Either),
    ];

    public IReadOnlyList<Choice<EntryMode>> ProbabilityEntries { get; } =
    [
        new(Strings.Prefs_EntryLevel, EntryMode.Level),
        new(Strings.Prefs_EntryPercent, EntryMode.Value),
        new(Strings.Prefs_EntryEither, EntryMode.Either),
    ];

    /// <summary>Impacto: qué dato es obligatorio al cargar un riesgo.</summary>
    [ObservableProperty]
    private Choice<EntryMode> _impactEntry = null!;

    /// <summary>Impacto: el nivel se ajusta solo según el monto (y se puede cambiar).</summary>
    [ObservableProperty]
    private bool _impactAuto = true;

    [ObservableProperty]
    private Choice<EntryMode> _probabilityEntry = null!;

    [ObservableProperty]
    private bool _probabilityAuto = true;

    public PreferencesViewModel()
    {
        _impactEntry = ImpactEntries[0];
        _probabilityEntry = ProbabilityEntries[0];
        RefreshLanguages(null);
    }

    // ---- Idioma ----------------------------------------------------------------

    /// <summary>Selectores de archivo y avisos (null en las pruebas sin ventana).</summary>
    public IUiService? Ui { get; init; }

    public ObservableCollection<LanguageOption> Languages { get; } = [];

    [ObservableProperty]
    private LanguageOption? _language;

    /// <summary>Elige el idioma por código (el español si no está).</summary>
    public void SelectLanguage(string? code) =>
        Language = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];

    private void RefreshLanguages(string? select)
    {
        Languages.Clear();
        foreach (var option in Services.Languages.List())
        {
            Languages.Add(option);
        }

        SelectLanguage(select);
    }

    /// <summary>
    /// Guarda todos los textos con su id para traducirlos. Con un idioma ya traducido elegido, conserva sus
    /// traducciones y marca las que faltan (para completar un idioma después de actualizar la aplicación).
    /// </summary>
    [RelayCommand]
    private async Task GenerateLanguage()
    {
        if (Ui is null)
        {
            return;
        }

        var chosen = Language is { Code: not Localization.BaseCode } l ? l : null;
        var path = await Ui.PickSaveFileAsync(Strings.Language_SaveTitle, (chosen?.Code ?? Strings.Language_DefaultName) + ".txt", FileKind.Language);
        if (path is null)
        {
            return;
        }

        var existing = chosen is null ? null : Services.Languages.Load(chosen.Code);
        var content = chosen is null
            ? Localization.Template("", "")
            : Localization.Template(chosen.Code, chosen.Name, existing, existing?.Culture ?? "");
        await File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(true));
        await Ui.AlertAsync(Strings.Language_SavedTitle, string.Format(CultureInfo.CurrentCulture, Strings.Language_Saved, Path.GetFileName(path)));
    }

    /// <summary>Agrega un archivo traducido a la lista de idiomas, con un resumen de lo que trae.</summary>
    [RelayCommand]
    private async Task AddLanguage()
    {
        if (Ui is null || await Ui.PickOpenFileAsync(Strings.Language_OpenTitle, FileKind.Language) is not { } path)
        {
            return;
        }

        try
        {
            var (file, check) = Services.Languages.Install(path);
            RefreshLanguages(file.Code);
            var message = new System.Text.StringBuilder(string.Format(CultureInfo.CurrentCulture, Strings.Language_Added,
                file.Name, file.Code, check.Translated, check.Total));
            if (check.Missing.Count > 0)
            {
                message.Append(string.Format(CultureInfo.CurrentCulture, Strings.Language_Missing, check.Missing.Count));
            }

            if (check.BadPlaceholders.Count > 0)
            {
                message.Append(string.Format(CultureInfo.CurrentCulture, Strings.Language_BadPlaceholders, check.BadPlaceholders.Count,
                    string.Join(", ", check.BadPlaceholders.Take(10))));
            }

            if (check.Unknown.Count > 0)
            {
                message.Append(string.Format(CultureInfo.CurrentCulture, Strings.Language_Unknown, check.Unknown.Count));
            }

            await Ui.AlertAsync(Strings.Language_AddedTitle, message.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Ui.AlertAsync(Strings.Language_ErrorTitle, Strings.Language_ErrorMessage);
        }
    }
}
