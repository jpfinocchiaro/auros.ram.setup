using Auros.Ram.Core.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Auros.Ram.App.Views;

/// <summary>
/// Caja de texto de las descripciones, sobre el editor de AvaloniaEdit. Lo que se escribe es el texto con las marcas de
/// <see cref="RichText"/> (<c>**negrita**</c>, <c>*itálica*</c>, <c>__subrayado__</c>, tamaños y listas), que es lo que se
/// guarda: el editor lo muestra con su formato y deja las marcas en gris tenue. La barra pone y quita las marcas.
/// </summary>
public partial class FormattedTextEditor : UserControl
{
    public const double SmallSize = 12;
    public const double NormalSize = 14;
    public const double LargeSize = 18;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<FormattedTextEditor, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> EditorHeightProperty =
        AvaloniaProperty.Register<FormattedTextEditor, double>(nameof(EditorHeight), 80);

    private bool _syncing;

    public FormattedTextEditor()
    {
        InitializeComponent();
        Editor.Height = EditorHeight;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = false;
        Editor.Options.AllowScrollBelowDocument = false;
        Editor.TextArea.TextView.LineTransformers.Add(new MarkupColorizer());
        Editor.TextArea.TextView.ElementGenerators.Add(new BulletGenerator());

        // Cada cambio del texto pasa a Text (teclado, pegar, deshacer, la barra).
        Editor.Document.TextChanged += (_, _) => Publish();

        // Las marcas no se ven: el cursor no se detiene entre sus caracteres, y borrar las salta.
        Editor.TextArea.Caret.PositionChanged += (_, _) => SnapCaret();
        Editor.Document.Changed += OnDocumentChanged;

        // Enter (listas), Retroceso y Suprimir (saltan las marcas).
        Editor.AddHandler(KeyDownEvent, OnEnterKey, RoutingStrategies.Tunnel);
        Editor.AddHandler(KeyDownEvent, OnDeleteKey, RoutingStrategies.Tunnel);

        ActualThemeVariantChanged += (_, _) => Editor.TextArea.TextView.Redraw();
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
    public TextEditor Box => Editor;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorHeightProperty && Editor is not null)
        {
            Editor.Height = EditorHeight;
        }
        else if (change.Property == TextProperty && !_syncing && Editor is not null && (Text ?? "") != DocumentText())
        {
            // Texto nuevo desde afuera (al abrir el riesgo, o al repetir el título): se carga con el cursor al final.
            _syncing = true;
            try
            {
                Editor.Document.Text = (Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
                Editor.Document.UndoStack.ClearAll();
                Editor.CaretOffset = Editor.Document.TextLength;
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    /// <summary>Pone el foco en el texto.</summary>
    public void FocusText() => Editor.TextArea.Focus();

    /// <summary>El texto del editor como se guarda: saltos de línea "\n" y sin líneas vacías al final.</summary>
    private string DocumentText() =>
        Editor.Document.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');

    /// <summary>Pasa el contenido del editor a <see cref="Text"/>.</summary>
    public void Publish()
    {
        if (_syncing)
        {
            return;
        }

        var text = DocumentText();
        if (text == (Text ?? ""))
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Pega texto sin formato donde está el cursor (reemplaza lo seleccionado), como Ctrl+V.</summary>
    public void PasteText(string text) =>
        Replace(Editor.SelectionStart, Editor.SelectionLength, text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));

    private void Replace(int start, int length, string text)
    {
        Editor.Document.Replace(start, length, text);
        Editor.Select(start + text.Length, 0);
        Editor.TextArea.Caret.BringCaretToView();
    }

    // ---- Marcas ocultas ------------------------------------------------------------------------------

    private int _lastCaret;
    private bool _cleaning;

    private static readonly string[] PairedMarks =
        [RichText.Bold, RichText.Underline, RichText.LargeOpen, RichText.LargeClose, RichText.SmallOpen, RichText.SmallClose];

    /// <summary>Las marcas de una línea (inicio y fin en el documento).</summary>
    private List<(int Start, int End)> MarksOf(DocumentLine line)
    {
        var text = Editor.Document.GetText(line);
        var prefix = RichText.PrefixLength(text);
        return [.. RichText.Tokens(text[prefix..]).Where(t => t.IsMark)
            .Select(t => (line.Offset + prefix + t.Start, line.Offset + prefix + t.Start + t.Length))];
    }

    /// <summary>El cursor no queda entre los caracteres de una marca: pasa al borde, en el sentido en que se movía.</summary>
    private void SnapCaret()
    {
        var offset = Editor.CaretOffset;
        if (Editor.SelectionLength == 0)
        {
            foreach (var (start, end) in MarksOf(Editor.Document.GetLineByOffset(offset)))
            {
                if (start < offset && offset < end)
                {
                    Editor.CaretOffset = offset >= _lastCaret ? end : start;
                    return;
                }
            }
        }

        _lastCaret = offset;
    }

    /// <summary>Retroceso y Suprimir borran el carácter visible de al lado, no una marca oculta.</summary>
    private void OnDeleteKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Back or Key.Delete) || e.KeyModifiers != KeyModifiers.None || Editor.SelectionLength > 0 ||
            Editor.IsReadOnly)
        {
            return;
        }

        var back = e.Key == Key.Back;
        var offset = Editor.CaretOffset;
        var marks = MarksOf(Editor.Document.GetLineByOffset(offset));
        var moved = true;
        while (moved)
        {
            moved = false;
            foreach (var (start, end) in marks)
            {
                if (back ? start < offset && offset <= end : start <= offset && offset < end)
                {
                    offset = back ? start : end;
                    moved = true;
                }
            }
        }

        _lastCaret = offset;
        Editor.CaretOffset = offset;
    }

    private void OnDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        // Al borrar puede quedar una marca sin su pareja (y se vería como texto): se quita después del cambio.
        if (!_syncing && !_cleaning && e.RemovalLength > 0)
        {
            var offset = e.Offset;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => CleanOrphans(offset));
        }
    }

    /// <summary>Quita de la línea las marcas que quedaron sin pareja (no las "*" sueltas, que pueden ser texto: 2*3).</summary>
    public void CleanOrphans(int offset)
    {
        var doc = Editor.Document;
        if (offset > doc.TextLength)
        {
            return;
        }

        var line = doc.GetLineByOffset(offset);
        var text = doc.GetText(line);
        var prefix = RichText.PrefixLength(text);
        var body = text[prefix..];
        var marks = RichText.Tokens(body).Where(t => t.IsMark).ToList();
        var orphans = new List<(int Start, int Length)>();
        foreach (var mark in PairedMarks)
        {
            for (var i = body.IndexOf(mark, StringComparison.Ordinal); i >= 0; i = body.IndexOf(mark, i + mark.Length, StringComparison.Ordinal))
            {
                var at = i;
                if (!marks.Any(t => t.Start <= at && at + mark.Length <= t.Start + t.Length) &&
                    !orphans.Any(o => o.Start < at + mark.Length && at < o.Start + o.Length))
                {
                    orphans.Add((at, mark.Length));
                }
            }
        }

        if (orphans.Count == 0)
        {
            return;
        }

        _cleaning = true;
        try
        {
            var caret = Editor.CaretOffset;
            doc.BeginUpdate();
            foreach (var (start, length) in orphans.OrderByDescending(o => o.Start))
            {
                var at = line.Offset + prefix + start;
                doc.Remove(at, length);
                if (caret > at)
                {
                    caret -= Math.Min(length, caret - at);
                }
            }

            doc.EndUpdate();
            Editor.CaretOffset = Math.Min(caret, doc.TextLength);
        }
        finally
        {
            _cleaning = false;
        }
    }

    // ---- Enter y listas ------------------------------------------------------------------------------

    private void OnEnterKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || (e.KeyModifiers & ~KeyModifiers.Shift) != 0 || Editor.IsReadOnly)
        {
            return;
        }

        e.Handled = true;
        NewParagraph();
    }

    /// <summary>
    /// Lo que hace Enter: un salto de línea en el cursor. En una lista, la línea nueva sigue la lista; sobre un ítem
    /// vacío, la lista termina (se quita la viñeta).
    /// </summary>
    public void NewParagraph()
    {
        var doc = Editor.Document;
        var line = doc.GetLineByOffset(Editor.SelectionStart);
        var text = doc.GetText(line);
        var block = RichText.Parse(text)[0];
        var prefix = RichText.PrefixLength(text);
        if (block.Kind != RichBlockKind.Paragraph && Editor.SelectionLength == 0 && text[prefix..].Trim().Length == 0)
        {
            Replace(line.Offset, line.Length, "");
            return;
        }

        var next = block.Kind switch
        {
            RichBlockKind.Bullet => RichText.BulletPrefix,
            RichBlockKind.Numbered => $"{block.Number + 1}. ",
            _ => "",
        };

        // Antes de la viñeta, el salto deja la línea entera abajo (sin agregar otra viñeta).
        if (Editor.SelectionStart - line.Offset < prefix)
        {
            next = "";
        }

        // Un formato abierto en el cursor (p. ej., en medio de una negrita) se cierra antes del salto y se abre de nuevo
        // en la línea nueva: una marca vale solo dentro de su línea.
        var (close, open) = OpenMarks(text, prefix, Editor.SelectionStart - line.Offset);
        Replace(Editor.SelectionStart, Editor.SelectionLength, close + "\n" + next + open);
    }

    /// <summary>Las marcas para cerrar y para volver a abrir el formato que está abierto en una posición de la línea.</summary>
    private static (string Close, string Open) OpenMarks(string line, int prefix, int column)
    {
        if (column <= prefix)
        {
            return ("", "");
        }

        // Solo si el cursor está dentro de un tramo con formato (no en su borde).
        var position = column - prefix;
        var current = RichText.Tokens(line[prefix..])
            .FirstOrDefault(t => !t.IsMark && t.Start < position && position < t.Start + t.Length)?.Format;
        if (current is null)
        {
            return ("", "");
        }

        var open = new System.Text.StringBuilder();
        var close = new System.Text.StringBuilder();
        void Add(bool on, string o, string c)
        {
            if (on)
            {
                open.Append(o);
                close.Insert(0, c);
            }
        }

        Add(current.Size == RichSize.Large, RichText.LargeOpen, RichText.LargeClose);
        Add(current.Size == RichSize.Small, RichText.SmallOpen, RichText.SmallClose);
        Add(current.Underline, RichText.Underline, RichText.Underline);
        Add(current.Bold, RichText.Bold, RichText.Bold);
        Add(current.Italic, RichText.Italic, RichText.Italic);
        return (close.ToString(), open.ToString());
    }

    private void OnBullets(object? sender, RoutedEventArgs e) => ToggleList(numbered: false);

    private void OnNumbers(object? sender, RoutedEventArgs e) => ToggleList(numbered: true);

    /// <summary>Convierte las líneas seleccionadas en lista (o las devuelve a párrafo si ya lo eran).</summary>
    private void ToggleList(bool numbered)
    {
        var doc = Editor.Document;
        var first = doc.GetLineByOffset(Editor.SelectionStart).LineNumber;
        var last = doc.GetLineByOffset(Editor.SelectionStart + Editor.SelectionLength).LineNumber;
        var lines = Enumerable.Range(first, last - first + 1).Select(doc.GetLineByNumber).ToList();
        var target = numbered ? RichBlockKind.Numbered : RichBlockKind.Bullet;
        var remove = lines.All(l => RichText.Parse(doc.GetText(l))[0].Kind == target);
        var caretLine = doc.GetLineByOffset(Editor.CaretOffset).LineNumber;
        var caretColumn = Editor.CaretOffset - doc.GetLineByNumber(caretLine).Offset;

        doc.BeginUpdate();
        try
        {
            var n = 0;
            foreach (var line in lines)
            {
                var text = doc.GetText(line);
                var oldPrefix = RichText.PrefixLength(text);
                n++;
                var newPrefix = remove ? "" : numbered ? $"{n}. " : RichText.BulletPrefix;
                doc.Replace(line.Offset, oldPrefix, newPrefix);
                if (line.LineNumber == caretLine)
                {
                    // El cursor queda en el mismo lugar del texto, después de la viñeta (no antes).
                    caretColumn = Math.Max(newPrefix.Length, caretColumn - oldPrefix + newPrefix.Length);
                }
            }
        }
        finally
        {
            doc.EndUpdate();
        }

        var caret = doc.GetLineByNumber(caretLine);
        Editor.Select(caret.Offset + Math.Min(caretColumn, caret.Length), 0);
        FocusText();
    }

    // ---- Formato -------------------------------------------------------------------------------------

    private void OnBold(object? sender, RoutedEventArgs e) => ToggleMarks(RichText.Bold, RichText.Bold);

    private void OnItalic(object? sender, RoutedEventArgs e) => ToggleMarks(RichText.Italic, RichText.Italic);

    private void OnUnderline(object? sender, RoutedEventArgs e) => ToggleMarks(RichText.Underline, RichText.Underline);

    private void OnSmall(object? sender, RoutedEventArgs e) => SetSize(RichSize.Small);

    private void OnNormal(object? sender, RoutedEventArgs e) => SetSize(RichSize.Normal);

    private void OnLarge(object? sender, RoutedEventArgs e) => SetSize(RichSize.Large);

    /// <summary>
    /// Pone las marcas alrededor de lo seleccionado, o las quita si ya estaban. Sin selección, pone las dos marcas y deja
    /// el cursor entre ellas para escribir con ese formato.
    /// </summary>
    public void ToggleMarks(string open, string close)
    {
        var doc = Editor.Document;
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        var selected = doc.GetText(start, length);

        if (Surrounded(start, length, open, close))
        {
            doc.BeginUpdate();
            doc.Remove(start + length, close.Length);
            doc.Remove(start - open.Length, open.Length);
            doc.EndUpdate();
            Editor.Select(start - open.Length, length);
        }
        else if (length > open.Length + close.Length && selected.StartsWith(open, StringComparison.Ordinal) &&
                 selected.EndsWith(close, StringComparison.Ordinal) && !(open == RichText.Italic && selected.StartsWith(RichText.Bold, StringComparison.Ordinal)))
        {
            var inner = selected[open.Length..^close.Length];
            doc.Replace(start, length, inner);
            Editor.Select(start, inner.Length);
        }
        else if (length == 0)
        {
            doc.Insert(start, open + close);
            Editor.Select(start + open.Length, 0);
        }
        else
        {
            var wrapped = Wrap(selected, open, close);
            doc.Replace(start, length, wrapped);
            Editor.Select(start, wrapped.Length);
        }

        FocusText();
    }

    /// <summary>Cambia el tamaño de lo seleccionado: quita las marcas de tamaño que tenga y pone las nuevas.</summary>
    private void SetSize(RichSize size)
    {
        var doc = Editor.Document;
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        foreach (var (open, close) in new[] { (RichText.LargeOpen, RichText.LargeClose), (RichText.SmallOpen, RichText.SmallClose) })
        {
            if (Surrounded(start, length, open, close))
            {
                doc.BeginUpdate();
                doc.Remove(start + length, close.Length);
                doc.Remove(start - open.Length, open.Length);
                doc.EndUpdate();
                start -= open.Length;
            }
        }

        var text = doc.GetText(start, length);
        foreach (var mark in new[] { RichText.LargeOpen, RichText.LargeClose, RichText.SmallOpen, RichText.SmallClose })
        {
            text = text.Replace(mark, "", StringComparison.Ordinal);
        }

        if (size != RichSize.Normal && text.Length > 0)
        {
            text = size == RichSize.Large ? Wrap(text, RichText.LargeOpen, RichText.LargeClose) : Wrap(text, RichText.SmallOpen, RichText.SmallClose);
        }

        doc.Replace(start, length, text);
        Editor.Select(start, text.Length);
        FocusText();
    }

    /// <summary>Las marcas están justo antes y justo después de lo seleccionado.</summary>
    private bool Surrounded(int start, int length, string open, string close)
    {
        var doc = Editor.Document;
        if (start < open.Length || start + length + close.Length > doc.TextLength ||
            doc.GetText(start - open.Length, open.Length) != open || doc.GetText(start + length, close.Length) != close)
        {
            return false;
        }

        // Un "*" pegado a otro "*" es parte de una negrita, no una itálica.
        return open != RichText.Italic ||
               ((start - 2 < 0 || doc.GetCharAt(start - 2) != '*') && (start + length + 1 >= doc.TextLength || doc.GetCharAt(start + length + 1) != '*'));
    }

    /// <summary>Pone las marcas en cada línea de lo seleccionado (una marca se cierra en su línea), pegadas al texto.</summary>
    private static string Wrap(string text, string open, string close) =>
        string.Join('\n', text.Split('\n').Select(line =>
        {
            var prefix = RichText.PrefixLength(line);
            var body = line[prefix..];
            var core = body.Trim();
            if (core.Length == 0)
            {
                return line;
            }

            var lead = body[..body.IndexOf(core, StringComparison.Ordinal)];
            var trail = body[(lead.Length + core.Length)..];
            return line[..prefix] + lead + open + core + close + trail;
        }));

    /// <summary>Tamaño de las marcas ocultas: casi nada, para que no ocupen lugar.</summary>
    private const double HiddenSize = 0.01;

    /// <summary>Muestra el formato de cada tramo y oculta las marcas (sin ancho ni color).</summary>
    private sealed class MarkupColorizer : DocumentColorizingTransformer
    {
        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            var prefix = RichText.PrefixLength(text);
            foreach (var token in RichText.Tokens(text[prefix..]))
            {
                var start = line.Offset + prefix + token.Start;
                var end = start + token.Length;
                var format = token.Format;
                if (token.IsMark)
                {
                    ChangeLinePart(start, end, e =>
                    {
                        e.TextRunProperties.SetForegroundBrush(Brushes.Transparent);
                        e.TextRunProperties.SetFontRenderingEmSize(HiddenSize);
                    });
                    continue;
                }

                if (!format.Bold && !format.Italic && !format.Underline && format.Size == RichSize.Normal)
                {
                    continue;
                }

                ChangeLinePart(start, end, e =>
                {
                    var face = e.TextRunProperties.Typeface;
                    e.TextRunProperties.SetTypeface(new Typeface(face.FontFamily,
                        format.Italic ? FontStyle.Italic : face.Style,
                        format.Bold ? FontWeight.Bold : face.Weight));
                    if (format.Underline)
                    {
                        e.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                    }

                    if (format.Size != RichSize.Normal)
                    {
                        e.TextRunProperties.SetFontRenderingEmSize(format.Size == RichSize.Small ? SmallSize : LargeSize);
                    }
                });
            }
        }
    }

    /// <summary>La viñeta "- " del comienzo de una línea se ve como "• ".</summary>
    private sealed class BulletGenerator : VisualLineElementGenerator
    {
        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            return startOffset == line.Offset && line.Length >= 2 &&
                   CurrentContext.Document.GetText(line.Offset, 2) == RichText.BulletPrefix
                ? startOffset
                : -1;
        }

        public override VisualLineElement ConstructElement(int offset) => new FormattedTextElement("\u2022 ", 2);
    }
}
