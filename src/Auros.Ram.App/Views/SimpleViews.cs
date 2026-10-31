using Auros.Ram.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Auros.Ram.App.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();
}

public partial class DocumentView : UserControl
{
    public DocumentView() => InitializeComponent();

    private void OnUnlockKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DocumentViewModel vm)
        {
            vm.UnlockCommand.Execute(null);
            e.Handled = true;
        }
    }
}

public partial class ProjectView : UserControl
{
    public ProjectView() => InitializeComponent();
}

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();
}

public partial class ScalesView : UserControl
{
    public ScalesView() => InitializeComponent();
}

public partial class MatrixView : UserControl
{
    public MatrixView() => InitializeComponent();

    private async void OnCellDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Un doble clic sobre un riesgo ya lo abre su botón; acá solo cuenta el espacio libre de la celda.
        if (e.Source is Avalonia.Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (sender is Control { DataContext: MatrixCell cell } && DataContext is MatrixViewModel vm)
        {
            e.Handled = true;
            await vm.AddAt(cell);
        }
    }
}

public partial class RisksView : UserControl
{
    public RisksView() => InitializeComponent();

    private void OnRiskDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RisksViewModel vm && vm.Selected is { } row)
        {
            vm.EditCommand.Execute(row);
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RisksViewModel vm || vm.Selected is not { } row)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.EditCommand.Execute(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            vm.DeleteCommand.Execute(row);
            e.Handled = true;
        }
    }
}
