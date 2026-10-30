using Auros.Ram.App.Resources;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Auros.Ram.App.ViewModels;

public enum PasswordMode
{
    /// <summary>Abrir o desbloquear: una sola contraseña.</summary>
    Enter,

    /// <summary>Archivo o plantilla nueva: contraseña + confirmación + advertencia (RNF-34, RNF-35).</summary>
    Create,

    /// <summary>Cambio de contraseña: actual + nueva + confirmación.</summary>
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
    [NotifyPropertyChangedFor(nameof(StrengthText), nameof(StrengthValue), nameof(StrengthBrush))]
    private string _newPassword = "";

    [ObservableProperty]
    private string _confirmation = "";

    [ObservableProperty]
    private bool _acknowledged;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    public string StrengthText => NewPassword.Length == 0
        ? Strings.Password_MinLength
        : PasswordPolicy.Describe(PasswordPolicy.Evaluate(NewPassword));

    public double StrengthValue => ((int)PasswordPolicy.Evaluate(NewPassword) + 1) * 20;

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

public sealed partial class NewMatrixViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _client = "";

    [ObservableProperty]
    private string _consultancy = "";

    [ObservableProperty]
    private bool _useStandardScales = true;

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
}
