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
    public ReportsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ReportsViewModel vm)
            {
                vm.Image.Render = RenderImage;
            }
        };
    }

    /// <summary>
    /// Dibuja la imagen de la matriz (la de la vista previa, a su tamaño real de 1280 de ancho) multiplicada por
    /// <paramref name="scale"/>: PNG para guardar y píxeles para el portapapeles. Null si la pestaña no está a la vista.
    /// </summary>
    public RenderedImage? RenderImage(int scale)
    {
        var size = ImageRoot.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
        {
            return null;
        }

        var pixels = new Avalonia.PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(pixels, new Avalonia.Vector(96 * scale, 96 * scale));
        bitmap.Render(ImageRoot);

        using var png = new MemoryStream();
        bitmap.Save(png);

        var stride = pixels.Width * 4;
        var bgra = new byte[stride * pixels.Height];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bgra, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(pixels), pin.AddrOfPinnedObject(), bgra.Length, stride);
        }
        finally
        {
            pin.Free();
        }

        return new RenderedImage(png.ToArray(), Services.ImageClipboard.ToDib(bgra, pixels.Width, pixels.Height), pixels.Width, pixels.Height);
    }
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
