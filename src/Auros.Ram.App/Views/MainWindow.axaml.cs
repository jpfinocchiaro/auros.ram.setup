using System.Windows.Input;
using Auros.Ram.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Auros.Ram.App.Views;

public partial class MainWindow : Window
{
    private bool _closingConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        // RNF-36: cualquier interacción reinicia el contador de inactividad.
        AddHandler(PointerMovedEvent, (_, _) => Vm?.NotifyActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => Vm?.NotifyActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => Vm?.NotifyActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);

        Opened += (_, _) => BindShortcuts();
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    /// <summary>RNF-44: un atajo para cada botón de la cinta, con el modificador de cada plataforma (Ctrl o Cmd).</summary>
    private void BindShortcuts()
    {
        if (Vm is not { } vm)
        {
            return;
        }

        var mod = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        KeyGesture Cmd(Key key) => new(key, mod);
        KeyGesture Shift(Key key) => new(key, mod | KeyModifiers.Shift);

        void Bind(string name, ICommand command, KeyGesture gesture, object? parameter = null, string? tipPath = null)
        {
            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command, CommandParameter = parameter! });
            MainRibbon.ShowShortcut(name, gesture, tipPath);
        }

        Bind("New", vm.NewCommand, Cmd(Key.N));
        Bind("Open", vm.OpenCommand, Cmd(Key.O));
        Bind("Save", vm.SaveCommand, Cmd(Key.S));
        Bind("SaveAs", vm.SaveAsCommand, Shift(Key.S));
        Bind("Close", vm.CloseCommand, Cmd(Key.W));
        Bind("Undo", vm.UndoCommand, Cmd(Key.Z), tipPath: "Active.UndoText");
        Bind("Redo", vm.RedoCommand, Cmd(Key.Y), tipPath: "Active.RedoText");
        KeyBindings.Add(new KeyBinding { Gesture = Shift(Key.Z), Command = vm.RedoCommand });
        Bind("NewRisk", vm.NewRiskCommand, Shift(Key.N));
        Bind("Lock", vm.LockNowCommand, Cmd(Key.L));
        Bind("Password", vm.ChangePasswordCommand, Shift(Key.K));

        Bind("Excel", vm.ExportCommand, Shift(Key.E), "Xlsx");
        Bind("PdfAll", vm.ExportCommand, Shift(Key.P), "PdfAll");
        Bind("PdfMatrix", vm.ExportCommand, Shift(Key.M), "PdfMatrix");
        Bind("PdfSummary", vm.ExportCommand, Shift(Key.R), "PdfSummary");
        Bind("PdfDetail", vm.ExportCommand, Shift(Key.D), "PdfDetail");
        Bind("PdfEvolution", vm.ExportCommand, Shift(Key.V), "PdfEvolution");
        Bind("Print", vm.ExportCommand, Cmd(Key.P), "Print");
        Bind("CsvRisks", vm.ExportCommand, Shift(Key.C), "CsvRisks");
        Bind("CsvMatrix", vm.ExportCommand, Shift(Key.T), "CsvMatrix");
        Bind("Json", vm.ExportCommand, Shift(Key.J), "Json");
        Bind("ImportJson", vm.ImportJsonCommand, Shift(Key.I));

        Bind("Project", vm.ShowSectionCommand, Cmd(Key.D1), "Project");
        Bind("Risks", vm.ShowSectionCommand, Cmd(Key.D2), "Risks");
        Bind("Matrix", vm.ShowSectionCommand, Cmd(Key.D3), "Matrix");
        Bind("Reports", vm.ShowSectionCommand, Cmd(Key.D4), "Reports");
        Bind("Scales", vm.ShowSectionCommand, Cmd(Key.D5), "Scales");

        Bind("Companies", vm.OrganizationsCommand, Shift(Key.U));
        Bind("Catalogs", vm.CatalogsCommand, Shift(Key.Q));
        Bind("Template", vm.SaveTemplateCommand, Shift(Key.G));
        Bind("Preferences", vm.PreferencesCommand, Cmd(Key.OemComma));
        Bind("License", vm.LicenseCommand, Shift(Key.L));
        Bind("About", vm.AboutCommand, new KeyGesture(Key.F1));
        Bind("Toggle", vm.ToggleRibbonCommand, Cmd(Key.F1), tipPath: nameof(MainWindowViewModel.RibbonTip));
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closingConfirmed || Vm is not { HasDocuments: true } vm)
        {
            return;
        }

        // RNF-43: confirmar antes de cerrar con cambios sin guardar.
        e.Cancel = true;
        if (await vm.CloseAllAsync())
        {
            _closingConfirmed = true;
            Close();
        }
    }
}
