using Auros.Ram.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Auros.Ram.App.Views;

/// <summary>Cinta de opciones: las acciones de la aplicación, agrupadas en pestañas, con Archivo a la izquierda.</summary>
public partial class Ribbon : UserControl
{
    public Ribbon()
    {
        InitializeComponent();

        // Con la cinta contraída, un clic en una pestaña la vuelve a mostrar.
        Tabs.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (DataContext is MainWindowViewModel { RibbonCollapsed: true } vm
                && (e.Source as Avalonia.Visual)?.FindAncestorOfType<TabItem>(includeSelf: true) is not null)
            {
                vm.RibbonCollapsed = false;
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Muestra el atajo de teclado en la descripción emergente del botón y, si está, en su opción de Archivo.
    /// Con <paramref name="tipPath"/>, el texto de la descripción sale de esa propiedad del modelo (p. ej., «Deshacer …»).
    /// </summary>
    public void ShowShortcut(string name, KeyGesture gesture, string? tipPath = null)
    {
        var shortcut = gesture.ToString("p", null);
        if (this.FindControl<Button>("Rb" + name) is { } button)
        {
            var text = ToolTip.GetTip(button)?.ToString();
            if (tipPath is null)
            {
                ToolTip.SetTip(button, $"{text} ({shortcut})");
            }
            else
            {
                button.Bind(ToolTip.TipProperty, new Binding(tipPath)
                {
                    StringFormat = "{0} (" + shortcut + ")",
                    FallbackValue = $"{text} ({shortcut})",
                });
            }
        }

        if (this.FindControl<MenuItem>("Fm" + name) is { } item)
        {
            item.InputGesture = gesture;
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
