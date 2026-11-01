using Auros.Ram.App.Resources;
using Auros.Ram.App.ViewModels;
using Auros.Ram.Persistence;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Auros.Ram.App.Views;

public partial class RiskEditorWindow : Window
{
    public RiskEditorWindow() => InitializeComponent();

    public RiskEditorWindow(RiskEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.Committed += (_, _) => Close();

        // El editor de descripciones toma el foco por su cuenta al terminar de cargarse: se devuelve al título
        // (o a la fecha, en una nueva evaluación) cuando la ventana queda inactiva, después de esa carga.
        Opened += (_, _) => Dispatcher.UIThread.Post(
            () => (vm.CanEditTexts ? (Control)TitleBox : AssessmentDatePicker).Focus(), DispatcherPriority.ApplicationIdle);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

public partial class ScaleEditorWindow : Window
{
    public ScaleEditorWindow() => InitializeComponent();

    public ScaleEditorWindow(ScaleEditorViewModel vm) : this()
    {
        DataContext = vm;
        vm.Committed += (_, _) => Close();
        Opened += (_, _) => NameBox.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

public partial class PasswordWindow : Window
{
    public PasswordWindow() => InitializeComponent();

    public PasswordWindow(PasswordDialogViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) =>
        {
            if (vm.IsCreate)
            {
                NewBox.Focus();
            }
            else
            {
                CurrentBox.Focus();
            }
        };
    }

    private PasswordDialogViewModel Vm => (PasswordDialogViewModel)DataContext!;

    private async void OnAccept(object? sender, RoutedEventArgs e)
    {
        var vm = Vm;
        vm.Error = vm.LocalValidation();
        if (vm.Error is not null)
        {
            return;
        }

        vm.IsBusy = true;
        try
        {
            vm.Error = vm.Submit is null ? null : await vm.Submit(vm);
        }
        finally
        {
            vm.IsBusy = false;
        }

        if (vm.Error is null)
        {
            Close(vm.Result);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}

public partial class MessageWindow : Window
{
    public MessageWindow() => InitializeComponent();

    /// <summary>Mensaje con botones; devuelve el índice del botón elegido o -1 si se cerró la ventana.</summary>
    public MessageWindow(string title, string message, IReadOnlyList<string> buttons, int defaultIndex = 0) : this()
    {
        Title = title;
        Heading.Text = title;
        Body.Text = message;
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], IsDefault = i == defaultIndex };
            ButtonIcon.SetIconByKey(button, MessageIcons.IconFor(buttons[i]));
            if (i == defaultIndex)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) => Close(index);
            Buttons.Children.Add(button);
        }

        Opened += (_, _) => Buttons.Children[defaultIndex].Focus();
    }
}

public partial class NewMatrixWindow : Window
{
    public NewMatrixWindow() => InitializeComponent();

    public NewMatrixWindow(NewMatrixViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) => NameBox.Focus();
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var vm = (NewMatrixViewModel)DataContext!;
        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            vm.Error = Strings.New_NameRequired;
            return;
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

public partial class ExportConfirmWindow : Window
{
    public ExportConfirmWindow() => InitializeComponent();

    public ExportConfirmWindow(ExportConfirmViewModel vm) : this() => DataContext = vm;

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var vm = (ExportConfirmViewModel)DataContext!;
        if (vm.IsPdf && vm.ProtectPdf)
        {
            if (vm.Password.Length < 6)
            {
                vm.Error = Strings.Export_PdfPasswordShort;
                return;
            }

            if (vm.Password != vm.Confirmation)
            {
                vm.Error = Strings.Password_Mismatch;
                return;
            }
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

/// <summary>Catálogos de la matriz: consecuencias potenciales y criterios de materialidad.</summary>
public partial class CatalogsWindow : Window
{
    public CatalogsWindow() => InitializeComponent();

    public CatalogsWindow(CatalogsViewModel vm) : this() => DataContext = vm;

    // Al cerrar se aplican los nombres corregidos; si alguno no se puede, queda abierta con el motivo.
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CatalogsViewModel vm || vm.Close())
        {
            Close();
        }
    }
}

/// <summary>Maestro de empresas y unidades de negocio.</summary>
public partial class OrganizationsWindow : Window
{
    public OrganizationsWindow() => InitializeComponent();

    public OrganizationsWindow(OrganizationsViewModel vm) : this() => DataContext = vm;

    // Al cerrar se aplican los nombres corregidos; si alguno no se puede, queda abierta con el motivo.
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OrganizationsViewModel vm || vm.Close())
        {
            Close();
        }
    }
}

/// <summary>Íconos de los botones de los mensajes, según su texto.</summary>
internal static class MessageIcons
{
    public static string IconFor(string text) =>
        text == Strings.Common_Save ? "IconSave"
        : text == Strings.Common_Delete ? "IconDelete"
        : text == Strings.Common_Discard || text == Strings.Close_DontSave ? "IconUndo"
        : text == Strings.Common_Cancel ? "IconClose"
        : "IconCheck";
}

public partial class PreferencesWindow : Window
{
    public PreferencesWindow() => InitializeComponent();

    public PreferencesWindow(PreferencesViewModel vm) : this() => DataContext = vm;

    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

/// <summary>Pantalla de inicio breve con la portada de la marca.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.About_Version, AppInfo.Version);
    }
}

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.About_Version, AppInfo.Version);
        using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("THIRD-PARTY-NOTICES.txt");
        NoticesText.Text = stream is null ? "" : new StreamReader(stream).ReadToEnd();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
