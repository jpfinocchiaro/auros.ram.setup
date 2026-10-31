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
    private bool _ownBlocks;
    private Rectangle? _caret;

    public FormattedTextEditor()
    {
        InitializeComponent();
        Editor.Height = EditorHeight;

        Editor.FlowDocument = new FlowDocument();

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

        // En la biblioteca, la lista de párrafos por omisión es una sola colección compartida por todos los
        // documentos: sin una propia, la descripción corta y la larga mostrarían (y guardarían) el mismo texto.
        // Se reemplaza recién acá, con el editor ya iniciado (al iniciarse espera al menos un párrafo).
        if (!_ownBlocks)
        {
            Editor.FlowDocument.SetValue(FlowDocument.BlocksProperty, new ObservableCollection<Block>());
            _ownBlocks = true;
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
        Editor.Focus();
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
        Editor.Focus();
    }
}
