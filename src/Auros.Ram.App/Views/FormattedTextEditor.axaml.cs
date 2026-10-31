using System.Collections.ObjectModel;
using System.Reflection;
using Auros.Ram.Core.Text;
using AvRichTextBox;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Auros.Ram.App.Views;

/// <summary>
/// Caja de texto con formato directo (negrita, itálica, subrayado, tamaños y listas). El texto se
/// guarda con las marcas de <see cref="RichText"/>, así archivos, informes, PDF y Excel no cambian.
/// </summary>
public partial class FormattedTextEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<FormattedTextEditor, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> EditorHeightProperty =
        AvaloniaProperty.Register<FormattedTextEditor, double>(nameof(EditorHeight), 80);

    private bool _syncing;
    private bool _loaded;
    private Rectangle? _caret;

    public FormattedTextEditor()
    {
        InitializeComponent();
        Editor.Height = EditorHeight;

        // En la biblioteca, la lista de párrafos por omisión es una sola colección compartida por todos los
        // documentos: cada editor tiene la suya desde el comienzo (si no, la descripción corta y la larga mostrarían
        // el mismo texto, y dos editores vacíos a la vez hacían fallar la carga). Arranca con un párrafo vacío,
        // que la biblioteca necesita para iniciarse; el contenido real se carga al mostrarse.
        // El párrafo se agrega después de asignar la lista: la biblioteca actualiza su índice interno de párrafos
        // solo cuando la lista cambia, y sin él falla al iniciarse.
        var document = new FlowDocument();
        document.SetValue(FlowDocument.BlocksProperty, new ObservableCollection<Block>());
        var empty = new AvRichTextBox.Paragraph();
        empty.Inlines.Add(new EditableRun(""));
        document.Blocks.Add(empty);
        Editor.FlowDocument = document;

        // El contenido se carga cuando el editor ya está en pantalla (la carga necesita su árbol visual).
        Editor.Loaded += (_, _) =>
        {
            Reload();
            HookCaret();
        };

        // Cada cambio del usuario se pasa a Text (teclado, mouse, pegar).
        Editor.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
        Editor.AddHandler(TextInputEvent, (_, _) => Publish(), RoutingStrategies.Bubble, handledEventsToo: true);
        Editor.AddHandler(PointerReleasedEvent, (_, _) => Publish(), RoutingStrategies.Bubble, handledEventsToo: true);
        Editor.LostFocus += (_, _) => Publish();

        // La biblioteca toma el foco por su cuenta al terminar de cargar un documento: al abrir la ventana del riesgo,
        // lo que se escribía en el título terminaba en una descripción. Solo se acepta el foco que pide el usuario
        // (clic o Tab) o esta clase; el que toma la biblioteca vuelve al control que lo tenía.
        Editor.AddHandler(PointerPressedEvent, (_, _) => _focusAllowed = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private bool _focusAllowed;

    /// <summary>
    /// Último control que recibió el foco por decisión del usuario (fuera de los editores, o dentro de uno con
    /// permiso): a él vuelve el foco que toma la biblioteca.
    /// </summary>
    private static WeakReference<IInputElement>? s_lastLegit;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TopLevel.GetTopLevel(this)?.AddHandler(GotFocusEvent, TrackFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        TopLevel.GetTopLevel(this)?.RemoveHandler(GotFocusEvent, TrackFocus);
        base.OnDetachedFromVisualTree(e);
    }

    // Se ejecuta después de OnGotFocus (el evento sube del editor a la ventana): el foco bloqueado no se registra.
    private static void TrackFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is Visual v && e.Source is IInputElement input &&
            v.FindAncestorOfType<FormattedTextEditor>(includeSelf: true) is not { _focusAllowed: false })
        {
            s_lastLegit = new WeakReference<IInputElement>(input);
        }
    }

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        // El permiso dura mientras el foco siga dentro del editor (la biblioteca lo pasa entre sus partes).
        _focusAllowed |= e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional or NavigationMethod.Pointer;
        if (_focusAllowed)
        {
            return;
        }

        if (s_lastLegit?.TryGetTarget(out var previous) == true && previous is Visual visual && TopLevel.GetTopLevel(visual) is not null &&
            visual.FindAncestorOfType<FormattedTextEditor>(includeSelf: true) != this &&
            TopLevel.GetTopLevel(visual) == TopLevel.GetTopLevel(this))
        {
            // Se revisa al ejecutarse: si mientras tanto el usuario entró al editor (o salió), no se toca el foco.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (IsKeyboardFocusWithin && !_focusAllowed)
                {
                    previous.Focus();
                }
            });
        }
    }

    /// <summary>Pone el foco en el texto (como un clic del usuario).</summary>
    public void FocusText()
    {
        _focusAllowed = true;
        Editor.Focus();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double EditorHeight
    {
        get => GetValue(EditorHeightProperty);
        set => SetValue(EditorHeightProperty, value);
    }

    /// <summary>El editor, para las pruebas.</summary>
    public RichTextBox Box => Editor;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorHeightProperty && Editor is not null)
        {
            Editor.Height = EditorHeight;
        }
        else if (change.Property == IsKeyboardFocusWithinProperty)
        {
            UpdateCaret();
            if (!IsKeyboardFocusWithin)
            {
                _focusAllowed = false;
            }
        }
        else if (change.Property == TextProperty && !_syncing && Editor?.FlowDocument is not null &&
                 (Text ?? "") != RichDocument.Save(Editor.FlowDocument))
        {
            Reload();
        }
    }

    private void Reload()
    {
        if (Editor.FlowDocument is null)
        {
            return;
        }

        RichDocument.Load(Editor, Text);
        _loaded = true;
    }

    /// <summary>
    /// La biblioteca hace titilar el cursor aunque el editor no tenga el foco (se veía en las dos descripciones).
    /// Su visibilidad y su recorte los manejan la biblioteca y la capa donde se dibuja, así que el cursor se vuelve
    /// transparente (con una máscara de opacidad) mientras no se esté escribiendo acá.
    /// </summary>
    private void HookCaret()
    {
        _caret ??= typeof(RichTextBox).GetField("_CaretRect", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(Editor) as Rectangle;
        UpdateCaret();
    }

    private void UpdateCaret()
    {
        if (_caret is not null)
        {
            _caret.OpacityMask = IsKeyboardFocusWithin ? null : Brushes.Transparent;
        }
    }

    /// <summary>El cursor está oculto (el editor no tiene el foco); para las pruebas.</summary>
    public bool CaretHidden => _caret?.OpacityMask is not null;

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _loaded)
        {
            ContinueList();
        }

        Publish();
    }

    /// <summary>
    /// Enter dentro de una lista: el párrafo nuevo sigue la lista. Enter sobre un ítem vacío termina la lista.
    /// </summary>
    private void ContinueList()
    {
        var (index, offset) = CaretSpot();
        if (index == 0)
        {
            return;
        }

        var lines = Lines();
        var previous = RichText.Parse(lines[index - 1])[0];
        if (previous.Kind == RichBlockKind.Paragraph || RichText.Parse(lines[index])[0].Kind != RichBlockKind.Paragraph)
        {
            return;
        }

        if (previous.Runs.All(r => r.Text.Trim().Length == 0))
        {
            // Ítem vacío: se quita la viñeta y el párrafo nuevo sobra.
            lines[index - 1] = "";
            lines.RemoveAt(index);
            ApplyLines(lines, index - 1, 0);
            return;
        }

        var prefix = previous.Kind == RichBlockKind.Numbered ? $"{previous.Number + 1}. " : RichText.BulletPrefix;
        lines[index] = prefix + lines[index];
        ApplyLines(lines, index, offset + prefix.Length);
    }

    /// <summary>Las líneas con marcas, una por párrafo del editor.</summary>
    private List<string> Lines()
    {
        var lines = RichDocument.Save(Editor.FlowDocument).Split('\n').ToList();
        var count = Editor.FlowDocument.Blocks.OfType<AvRichTextBox.Paragraph>().Count();
        while (lines.Count < count)
        {
            lines.Add("");
        }

        return lines;
    }

    /// <summary>Párrafo y posición dentro de él donde está el cursor.</summary>
    private (int Paragraph, int Offset) CaretSpot()
    {
        var paragraphs = Editor.FlowDocument.Blocks.OfType<AvRichTextBox.Paragraph>().ToList();
        var position = Editor.FlowDocument.Selection.End;
        var start = 0;
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (position < start + paragraphs[i].BlockLength || i == paragraphs.Count - 1)
            {
                return (i, Math.Max(0, position - start));
            }

            start += paragraphs[i].BlockLength;
        }

        return (0, 0);
    }

    /// <summary>Recarga el editor con las líneas dadas y deja el cursor en el párrafo y la posición indicados.</summary>
    private void ApplyLines(List<string> lines, int paragraph, int offset)
    {
        _syncing = true;
        try
        {
            SetCurrentValue(TextProperty, string.Join('\n', lines));
            RichDocument.Load(Editor, Text);
        }
        finally
        {
            _syncing = false;
        }

        var paragraphs = Editor.FlowDocument.Blocks.OfType<AvRichTextBox.Paragraph>().ToList();
        if (paragraphs.Count == 0)
        {
            return;
        }

        paragraph = Math.Clamp(paragraph, 0, paragraphs.Count - 1);
        var start = paragraphs.Take(paragraph).Sum(p => p.BlockLength);
        Editor.FlowDocument.Select(start + Math.Clamp(offset, 0, paragraphs[paragraph].TextLength), 0);
    }

    /// <summary>Pasa el contenido del editor a <see cref="Text"/> (con marcas).</summary>
    public void Publish()
    {
        // Antes de cargarse el contenido el editor está vacío: no debe pisar el texto del riesgo.
        if (!_loaded || Editor.FlowDocument is null)
        {
            return;
        }

        var markup = RichDocument.Save(Editor.FlowDocument);
        if (markup == (Text ?? ""))
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(TextProperty, markup);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnBold(object? sender, RoutedEventArgs e) =>
        Toggle(TextElement.FontWeightProperty, v => v is FontWeight w && w >= FontWeight.SemiBold, FontWeight.Bold, FontWeight.Normal);

    private void OnItalic(object? sender, RoutedEventArgs e) =>
        Toggle(TextElement.FontStyleProperty, v => v is FontStyle.Italic, FontStyle.Italic, FontStyle.Normal);

    private void OnUnderline(object? sender, RoutedEventArgs e) =>
        Toggle(Inline.TextDecorationsProperty, v => v is TextDecorationCollection { Count: > 0 }, TextDecorations.Underline, null);

    private void OnSmall(object? sender, RoutedEventArgs e) => Format(TextElement.FontSizeProperty, RichDocument.SmallSize);

    private void OnNormal(object? sender, RoutedEventArgs e) => Format(TextElement.FontSizeProperty, RichDocument.NormalSize);

    private void OnLarge(object? sender, RoutedEventArgs e) => Format(TextElement.FontSizeProperty, RichDocument.LargeSize);

    private void OnBullets(object? sender, RoutedEventArgs e) => ToggleList(numbered: false);

    private void OnNumbers(object? sender, RoutedEventArgs e) => ToggleList(numbered: true);

    /// <summary>Activa o desactiva un formato en la selección, según cómo esté su comienzo.</summary>
    private void Toggle(AvaloniaProperty property, Func<object?, bool> isOn, object? on, object? off)
    {
        var selection = Editor.FlowDocument.Selection;
        if (selection.Length == 0)
        {
            return;
        }

        Format(property, isOn(selection.GetFormatting(property)) ? off : on);
    }

    private void Format(AvaloniaProperty property, object? value)
    {
        var selection = Editor.FlowDocument.Selection;
        if (selection.Length == 0)
        {
            return;
        }

        // La biblioteca declara el valor como no nulo, pero null es lo que quita el subrayado.
        selection.ApplyFormatting(property, value!);
        Publish();
        FocusText();
    }

    /// <summary>Convierte los párrafos seleccionados en lista (o los devuelve a párrafo si ya lo eran).</summary>
    private void ToggleList(bool numbered)
    {
        var doc = Editor.FlowDocument;
        var paragraphs = doc.Blocks.OfType<AvRichTextBox.Paragraph>().ToList();
        var selected = doc.GetSelectedParagraphs.Select(p => paragraphs.IndexOf(p)).Where(i => i >= 0).ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }

        var (caretParagraph, caretOffset) = CaretSpot();
        var lines = Lines();
        var target = numbered ? RichBlockKind.Numbered : RichBlockKind.Bullet;
        var blocks = lines.Select(l => RichText.Parse(l)[0]).ToList();
        var remove = selected.All(i => blocks[i].Kind == target);
        var n = 0;
        foreach (var i in selected.Order())
        {
            var oldPrefix = RichText.Prefix(blocks[i]).Length;
            var body = lines[i][oldPrefix..];
            n++;
            lines[i] = remove ? body : (numbered ? $"{n}. " : RichText.BulletPrefix) + body;
            if (i == caretParagraph)
            {
                // El cursor queda en el mismo lugar del texto, después de la viñeta (no antes).
                var newPrefix = lines[i].Length - body.Length;
                caretOffset = Math.Max(newPrefix, caretOffset - oldPrefix + newPrefix);
            }
        }

        ApplyLines(lines, caretParagraph, caretOffset);
        FocusText();
    }
}
