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
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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

        // Pegar lo hace esta clase: el de la biblioteca falla con lo copiado de otras aplicaciones (formato RTF o HTML).
        Editor.AddHandler(KeyDownEvent, OnPasteKey, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Enter también: el de la biblioteca a veces falla al partir el párrafo (error inesperado al escribir).
        Editor.AddHandler(KeyDownEvent, OnEnterKey, RoutingStrategies.Tunnel);

        // Cada cambio del usuario se pasa a Text (teclado, mouse, pegar).
        Editor.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
        Editor.AddHandler(TextInputEvent, (_, _) => Publish(), RoutingStrategies.Bubble, handledEventsToo: true);
        Editor.AddHandler(PointerReleasedEvent, (_, _) => Publish(), RoutingStrategies.Bubble, handledEventsToo: true);
        Editor.LostFocus += (_, _) => Publish();

        // La biblioteca toma el foco por su cuenta (al cargar un documento, cuando uno de sus párrafos lo pierde, al
        // actualizar el texto): lo que se escribía en el título terminaba en una descripción, o un clic en una descripción
        // no hacía nada porque la otra recuperaba el foco. Solo se acepta el foco que pide el usuario (clic o Tab) o esta
        // clase; el que toma la biblioteca vuelve al control que eligió el usuario.
        // El clic da el foco al editor: la biblioteca marca el clic como atendido y solo a veces toma el foco (más tarde).
        Editor.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _userFocus = true;
            if (!Editor.IsKeyboardFocusWithin)
            {
                Editor.Focus(NavigationMethod.Pointer);
            }

            // Un clic fuera del texto (en el margen de una línea o en el espacio libre de abajo) ubica el cursor en esa
            // línea o al final, como en un procesador de textos. La biblioteca solo ubica el cursor si el clic cae sobre
            // un párrafo; si no, lo deja donde estaba (al principio).
            if (!OverText(e.Source) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                var point = e.GetPosition(Editor);
                Dispatcher.UIThread.Post(() => CaretAt(point));
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);

        // Los párrafos internos de la biblioteca no toman el foco: lo tiene siempre el editor, que recibe el teclado.
        // Si un párrafo tuviera el foco, la biblioteca lo reclamaría para su editor en cualquier momento (al cargarse,
        // al perderlo), aunque el usuario esté escribiendo en otro lado.
        Editor.LayoutUpdated += (_, _) => KeepFocusOnEditor();
    }

    private static bool OverText(object? source) =>
        source is Visual v && v.GetSelfAndVisualAncestors().Any(a => a.GetType().Name == "EditableParagraph");

    /// <summary>Ubica el cursor en la línea a la altura del punto (del editor), o al final si está debajo del texto.</summary>
    private void CaretAt(Point point)
    {
        foreach (var block in Editor.GetVisualDescendants().OfType<TextBlock>())
        {
            if (block.GetType().Name != "EditableParagraph" || block.DataContext is not AvRichTextBox.Paragraph paragraph ||
                block.TranslatePoint(new Point(0, 0), Editor) is not { } origin ||
                point.Y < origin.Y || point.Y >= origin.Y + block.Bounds.Height)
            {
                continue;
            }

            var x = Math.Clamp(point.X - origin.X, 0, Math.Max(0, block.Bounds.Width - 1));
            var hit = block.TextLayout.HitTestPoint(new Point(x, point.Y - origin.Y));
            var start = Editor.FlowDocument.Blocks.TakeWhile(b => !ReferenceEquals(b, paragraph)).Sum(b => b.BlockLength);
            Editor.FlowDocument.Select(start + hit.TextPosition, 0);
            return;
        }

        CaretToEnd();
    }

    /// <summary>Lleva el cursor al final del texto.</summary>
    private void CaretToEnd()
    {
        var paragraphs = Editor.FlowDocument?.Blocks.OfType<AvRichTextBox.Paragraph>().ToList();
        if (paragraphs is not { Count: > 0 })
        {
            return;
        }

        var start = paragraphs.Take(paragraphs.Count - 1).Sum(p => p.BlockLength);
        Editor.FlowDocument!.Select(start + paragraphs[^1].TextLength, 0);
    }

    private void KeepFocusOnEditor()
    {
        foreach (var element in Editor.GetVisualDescendants().OfType<InputElement>())
        {
            if (element.Focusable && element.GetType().Name == "EditableParagraph")
            {
                element.Focusable = false;
            }
        }
    }

    /// <summary>El usuario acaba de pedir el foco para este editor (clic, o esta clase por él).</summary>
    private bool _userFocus;

    /// <summary>
    /// Control que eligió el usuario (con un clic, Tab o esta clase): es el único editor que puede tener el foco, y a él
    /// vuelve el foco que toma la biblioteca.
    /// </summary>
    private static WeakReference<IInputElement>? s_owner;

    /// <summary>El control elegido por el usuario está en este editor.</summary>
    private bool Owns => s_owner?.TryGetTarget(out var owner) == true && owner is Visual v &&
                         v.FindAncestorOfType<FormattedTextEditor>(includeSelf: true) == this;

    private static bool ByUser(NavigationMethod method) =>
        method is NavigationMethod.Tab or NavigationMethod.Directional or NavigationMethod.Pointer;

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

    // Se ejecuta después de OnGotFocus (el evento sube del editor a la ventana). Fuera de los editores, todo foco es del
    // usuario; dentro de uno, solo el que pidió el usuario o el que se mueve dentro del editor que ya eligió.
    private static void TrackFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is not Visual v || e.Source is not IInputElement input)
        {
            return;
        }

        var editor = v.FindAncestorOfType<FormattedTextEditor>(includeSelf: true);
        if (editor is null || ByUser(e.NavigationMethod) || editor._userFocus || editor.Owns)
        {
            s_owner = new WeakReference<IInputElement>(input);
            if (editor is not null)
            {
                editor._userFocus = false;
            }
        }
    }

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        // Un párrafo interno de la biblioteca tomó el foco (un clic sobre el texto): el foco pasa al editor. Si quedara
        // en el párrafo, al perderlo la biblioteca se lo devuelve al editor, aunque el usuario haya hecho clic en otro lado.
        if (e.Source is Visual source && !ReferenceEquals(source, Editor) && source.FindAncestorOfType<RichTextBox>() == Editor)
        {
            _userFocus |= ByUser(e.NavigationMethod);
            Editor.Focus(e.NavigationMethod);
            return;
        }

        if (ByUser(e.NavigationMethod) || _userFocus || Owns)
        {
            return;
        }

        // Foco que tomó la biblioteca: vuelve en el acto al control que eligió el usuario.
        if (s_owner?.TryGetTarget(out var owner) == true && owner is Visual visual && owner.IsEffectivelyEnabled &&
            TopLevel.GetTopLevel(visual) is { } top && top == TopLevel.GetTopLevel(this))
        {
            owner.Focus();
        }
    }

    /// <summary>Pone el foco en el texto (como un clic del usuario).</summary>
    public void FocusText()
    {
        _userFocus = true;
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

        // Al entrar al editor (con Tab o un clic fuera del texto) se sigue escribiendo al final.
        CaretToEnd();
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

    private void OnKeyUp(object? sender, KeyEventArgs e) => Publish();

    /// <summary>Enter parte el párrafo donde está el cursor (reemplaza lo seleccionado) y sigue la lista si hay una.</summary>
    private void OnEnterKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || (e.KeyModifiers & ~KeyModifiers.Shift) != 0 || Editor.IsReadOnly ||
            !_loaded)
        {
            return;
        }

        e.Handled = true;
        try
        {
            NewParagraph();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Editor.Enter");
        }
    }

    /// <summary>Lo que hace Enter: un párrafo nuevo en el cursor.</summary>
    public void NewParagraph()
    {
        PasteText("\n");
        ContinueList();
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

    private async void OnPasteKey(object? sender, KeyEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        var gestures = top?.PlatformSettings?.HotkeyConfiguration.Paste;
        var isPaste = gestures is { Count: > 0 }
            ? gestures.Any(g => g.Matches(e))
            : e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control;
        if (!isPaste || e.Handled || Editor.IsReadOnly)
        {
            return;
        }

        e.Handled = true;
        try
        {
            if (top?.Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { Length: > 0 } text)
            {
                PasteText(text);
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Editor.Paste");
        }
    }

    /// <summary>
    /// Pega texto sin formato donde está el cursor (reemplaza lo seleccionado). Cada línea del texto es un párrafo.
    /// </summary>
    public void PasteText(string text)
    {
        if (!_loaded || Editor.FlowDocument is null)
        {
            return;
        }

        var selection = Editor.FlowDocument.Selection;
        var (markup, paragraph, offset) = RichDocument.Paste(Editor.FlowDocument, selection.Start, selection.End, text);
        ApplyLines([.. markup.Split('\n')], paragraph, offset);
        Editor.ScrollToSelection();
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
