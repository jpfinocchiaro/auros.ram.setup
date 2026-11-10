using System.Globalization;
using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Auros.Ram.App.ViewModels;

/// <summary>Configuración → Licencia: edición en uso, código de este equipo y activación de la clave de licencia.</summary>
public sealed partial class LicenseViewModel : ObservableObject
{
    public string MachineCode => Edition.MachineCode;

    public bool IsLicensed => Edition.IsLicensed;

    public string Status => Edition.Current is { } license
        ? string.Format(CultureInfo.CurrentCulture, Strings.License_LicensedStatus, license.Customer,
            license.IssuedOn.ToString("d", CultureInfo.CurrentCulture))
        : string.Format(CultureInfo.CurrentCulture, Strings.License_FreeStatus,
            Edition.Limits.MaxCompanies ?? 0, Edition.Limits.MaxRisks ?? 0);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private string _key = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _success;

    private bool CanActivate() => !string.IsNullOrWhiteSpace(Key);

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private void Activate()
    {
        Error = null;
        Success = null;
        try
        {
            Edition.Activate(Key);
            Key = "";
            Success = Strings.License_Activated;
            OnPropertyChanged(nameof(IsLicensed));
            OnPropertyChanged(nameof(Status));
        }
        catch (DomainException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "License.Activate");
            Error = Strings.License_SaveError;
        }
    }
}
