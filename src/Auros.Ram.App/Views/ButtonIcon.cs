using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>
/// Ícono de línea para un botón con texto: <c>ui:ButtonIcon.Icon="{StaticResource IconSave}"</c> muestra el ícono a la
/// izquierda del texto, en el color del texto del botón. El contenido (texto o enlace) no cambia.
/// </summary>
public static class ButtonIcon
{
    public static readonly AttachedProperty<Geometry?> IconProperty =
        AvaloniaProperty.RegisterAttached<Button, Geometry?>("Icon", typeof(ButtonIcon));

    static ButtonIcon()
    {
        IconProperty.Changed.AddClassHandler<Button>((button, e) =>
            button.ContentTemplate = e.NewValue is Geometry icon ? Template(icon) : null);
        RibbonProperty.Changed.AddClassHandler<Button>((button, e) =>
            button.ContentTemplate = e.NewValue is Geometry icon ? RibbonTemplate(icon) : null);
    }

    /// <summary>Botón de la cinta de opciones: ícono grande arriba y texto abajo.</summary>
    public static readonly AttachedProperty<Geometry?> RibbonProperty =
        AvaloniaProperty.RegisterAttached<Button, Geometry?>("Ribbon", typeof(ButtonIcon));

    public static Geometry? GetRibbon(Button button) => button.GetValue(RibbonProperty);

    public static void SetRibbon(Button button, Geometry? value) => button.SetValue(RibbonProperty, value);

    public static Geometry? GetIcon(Button button) => button.GetValue(IconProperty);

    public static void SetIcon(Button button, Geometry? value) => button.SetValue(IconProperty, value);

    /// <summary>Ícono para los botones que se arman por código (p. ej., los de los mensajes), según su clave de recurso.</summary>
    public static void SetIconByKey(Button button, string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey, out var value) == true && value is Geometry icon)
        {
            SetIcon(button, icon);
        }
    }

    private static FuncDataTemplate<object?> RibbonTemplate(Geometry icon) => new((_, _) =>
    {
        var text = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 14,
            MinHeight = 28,   // dos renglones: la cinta mide lo mismo en todas las pestañas
        };
        text.Bind(TextBlock.TextProperty, new Binding("."));
        var path = new Avalonia.Controls.Shapes.Path { Data = icon, StrokeThickness = 1.1 };
        path.Classes.Add("nav");
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new Viewbox { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, Child = path },
                text,
            },
        };
    }, supportsRecycling: true);

    // El texto se enlaza al contenido, así un botón cuyo texto cambia (p. ej., Reproducir / Pausa) lo sigue mostrando.
    private static FuncDataTemplate<object?> Template(Geometry icon) => new((_, _) =>
    {
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.TextProperty, new Binding("."));
        var path = new Avalonia.Controls.Shapes.Path { Data = icon };
        path.Classes.Add("nav");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { path, text },
        };
    }, supportsRecycling: true);
}
