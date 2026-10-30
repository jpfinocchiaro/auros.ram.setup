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

    /// <summary>RNF-44: atajos estándar con el modificador de cada plataforma (Ctrl o Cmd).</summary>
    private void BindShortcuts()
    {
        var mod = PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        void Bind(MenuItem item, Key key, KeyModifiers extra = KeyModifiers.None)
        {
            var gesture = new KeyGesture(key, mod | extra);
            item.InputGesture = gesture;
            if (item.Command is { } command)
            {
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command, CommandParameter = item.CommandParameter! });
            }
        }

        Bind(MiNew, Key.N);
        Bind(MiOpen, Key.O);
        Bind(MiSave, Key.S);
        Bind(MiSaveAs, Key.S, KeyModifiers.Shift);
        Bind(MiPrint, Key.P);
        Bind(MiLock, Key.L);
        Bind(MiClose, Key.W);
        Bind(MiUndo, Key.Z);
        Bind(MiRedo, Key.Y);
        Bind(MiNewRisk, Key.N, KeyModifiers.Shift);
        Bind(MiProject, Key.D1);
        Bind(MiRisks, Key.D2);
        Bind(MiMatrix, Key.D3);
        Bind(MiReports, Key.D4);
        Bind(MiScales, Key.D5);
        if (MiRedo.Command is { } redo)
        {
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, mod | KeyModifiers.Shift), Command = redo });
        }
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

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
}
