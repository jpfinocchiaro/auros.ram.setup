using System.Globalization;
using System.Text;

namespace Auros.Ram.Core.Text;

public enum RichSize
{
    Small,
    Normal,
    Large,
}

public enum RichBlockKind
{
    Paragraph,
    Bullet,
    Numbered,
}

/// <summary>Un tramo de texto con el mismo formato.</summary>
public sealed record RichRun(string Text, bool Bold, bool Italic, bool Underline, RichSize Size);

/// <summary>Una parte de una línea: una marca de formato o un tramo de texto (Format dice el formato del tramo).</summary>
public sealed record RichToken(int Start, int Length, bool IsMark, RichRun Format);

/// <summary>Una línea: párrafo, ítem con viñeta o ítem numerado (con su número).</summary>
public sealed record RichBlock(RichBlockKind Kind, int Number, IReadOnlyList<RichRun> Runs);

/// <summary>
/// Formato de las descripciones de los riesgos. Se guarda como texto con marcas simples:
/// <c>**negrita**</c>, <c>*itálica*</c>, <c>__subrayado__</c>, <c>{g}grande{/g}</c>, <c>{p}chico{/p}</c>,
/// y líneas que empiezan con <c>- </c> (viñeta) o <c>1. </c> (numeradas).
/// Una marca solo cuenta si se cierra en la misma línea; si no, queda como texto (p. ej., "2*3").
/// </summary>
public static class RichText
{
    public const string Bold = "**";
    public const string Italic = "*";
    public const string Underline = "__";
    public const string LargeOpen = "{g}";
    public const string LargeClose = "{/g}";
    public const string SmallOpen = "{p}";
    public const string SmallClose = "{/p}";
    public const string BulletPrefix = "- ";

    public static IReadOnlyList<RichBlock> Parse(string? text)
    {
        var blocks = new List<RichBlock>();
        foreach (var raw in (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var (kind, number, body) = LineKind(raw);
            blocks.Add(new RichBlock(kind, number, ParseInline(body)));
        }

        return blocks;
    }

    /// <summary>El texto sin marcas (viñetas como "• " y números como "1. "), para buscar, contar y exportar a CSV.</summary>
    public static string ToPlain(string? text)
    {
        var sb = new StringBuilder();
        var blocks = Parse(text);
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(Prefix(blocks[i]));
            foreach (var run in blocks[i].Runs)
            {
                sb.Append(run.Text);
            }
        }

        return sb.ToString();
    }

    /// <summary>Lo que se antepone a la línea al mostrarla: "• ", "1. " o nada.</summary>
    public static string Prefix(RichBlock block) => block.Kind switch
    {
        RichBlockKind.Bullet => "• ",
        RichBlockKind.Numbered => block.Number.ToString(CultureInfo.InvariantCulture) + ". ",
        _ => "",
    };

    private static (RichBlockKind Kind, int Number, string Body) LineKind(string line)
    {
        if (line.StartsWith(BulletPrefix, StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
        {
            return (RichBlockKind.Bullet, 0, line[2..]);
        }

        var digits = 0;
        while (digits < line.Length && digits < 4 && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }

        if (digits > 0 && line.Length > digits + 1 && line[digits] == '.' && line[digits + 1] == ' ')
        {
            return (RichBlockKind.Numbered, int.Parse(line.AsSpan(0, digits), CultureInfo.InvariantCulture), line[(digits + 2)..]);
        }

        return (RichBlockKind.Paragraph, 0, line);
    }

    // Cada tramo de texto entre marcas es un tramo con su formato.
    private static List<RichRun> ParseInline(string line) =>
        [.. Tokens(line).Where(t => !t.IsMark).Select(t => t.Format with { Text = line.Substring(t.Start, t.Length) })];

    /// <summary>
    /// Las partes de una línea (sin la viñeta o el número del comienzo): marcas y tramos de texto, con su posición
    /// en la línea y el formato que tiene cada tramo. Es lo que usa el editor para mostrar el formato mientras se escribe.
    /// </summary>
    public static IReadOnlyList<RichToken> Tokens(string line)
    {
        var tokens = new List<RichToken>();
        bool bold = false, italic = false, underline = false;
        var size = RichSize.Normal;
        var textStart = 0;

        void Flush(int end)
        {
            if (end > textStart)
            {
                tokens.Add(new RichToken(textStart, end - textStart, false, new RichRun("", bold, italic, underline, size)));
            }
        }

        void Mark(int at, int length)
        {
            Flush(at);
            tokens.Add(new RichToken(at, length, true, new RichRun("", bold, italic, underline, size)));
            textStart = at + length;
        }

        var i = 0;
        while (i < line.Length)
        {
            // El orden importa: "**" antes que "*".
            if (Toggle(RichText.Bold, ref bold) || Toggle(RichText.Underline, ref underline) || Toggle(RichText.Italic, ref italic) ||
                Size(LargeOpen, LargeClose, RichSize.Large) || Size(SmallOpen, SmallClose, RichSize.Small))
            {
                continue;
            }

            i++;
        }

        Flush(line.Length);
        return tokens;

        // Abre solo si hay un cierre más adelante en la línea; cierra siempre que esté abierta.
        bool Toggle(string mark, ref bool state)
        {
            if (string.CompareOrdinal(line, i, mark, 0, mark.Length) != 0)
            {
                return false;
            }

            // Un "*" pegado a otro es parte de un "**" sin cerrar: queda como texto.
            if (mark == RichText.Italic && i + 1 < line.Length && line[i + 1] == '*')
            {
                return false;
            }

            if (!state && line.IndexOf(mark, i + mark.Length, StringComparison.Ordinal) < 0)
            {
                return false;
            }

            Flush(i);
            state = !state;
            tokens.Add(new RichToken(i, mark.Length, true, new RichRun("", bold, italic, underline, size)));
            i += mark.Length;
            textStart = i;
            return true;
        }

        bool Size(string open, string close, RichSize target)
        {
            if (string.CompareOrdinal(line, i, open, 0, open.Length) == 0 && line.IndexOf(close, i + open.Length, StringComparison.Ordinal) >= 0)
            {
                Mark(i, open.Length);
                size = target;
                i += open.Length;
                return true;
            }

            if (size == target && string.CompareOrdinal(line, i, close, 0, close.Length) == 0)
            {
                Mark(i, close.Length);
                size = RichSize.Normal;
                i += close.Length;
                return true;
            }

            return false;
        }
    }

    /// <summary>Largo de la viñeta o el número del comienzo de una línea ("- ", "1. "), o 0 si es un párrafo.</summary>
    public static int PrefixLength(string line)
    {
        var (kind, _, body) = LineKind(line);
        return kind == RichBlockKind.Paragraph ? 0 : line.Length - body.Length;
    }
}
