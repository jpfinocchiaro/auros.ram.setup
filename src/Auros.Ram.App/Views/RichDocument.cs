using System.Globalization;
using System.Security;
using System.Text;
using Auros.Ram.Core.Text;
using AvRichTextBox;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>
/// Pasa una descripción entre el formato guardado (marcas de <see cref="RichText"/>) y el documento del
/// editor con formato directo. Las listas son párrafos que empiezan con "• " o "1. ".
/// </summary>
public static class RichDocument
{
    public const double SmallSize = 12;
    public const double NormalSize = 14;
    public const double LargeSize = 18;

    /// <summary>Fuente de la aplicación para el texto del editor (la biblioteca trae otra por omisión).</summary>
    public const string FontFamilyName = "fonts:Plex#IBM Plex Sans";

    /// <summary>Reemplaza el contenido del editor por el texto con marcas (a través de su carga de XAML).</summary>
    public static void Load(RichTextBox box, string? markup) => box.LoadXamlString(ToXaml(markup));

    /// <summary>El texto con marcas como XAML de documento: un párrafo por línea y un Run por tramo.</summary>
    public static string ToXaml(string? markup)
    {
        var sb = new StringBuilder("<Section xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">");
        foreach (var block in RichText.Parse(markup))
        {
            sb.Append(CultureInfo.InvariantCulture, $"<Paragraph FontFamily=\"{FontFamilyName}\" FontSize=\"{NormalSize}\">");
            var prefix = RichText.Prefix(block);
            var runs = new List<RichRun>();
            if (prefix.Length > 0)
            {
                runs.Add(new RichRun(prefix, false, false, false, RichSize.Normal));
            }

            runs.AddRange(block.Runs);
            if (runs.Count == 0)
            {
                runs.Add(new RichRun("", false, false, false, RichSize.Normal));
            }

            foreach (var run in runs)
            {
                var size = run.Size switch { RichSize.Small => SmallSize, RichSize.Large => LargeSize, _ => NormalSize };
                sb.Append(CultureInfo.InvariantCulture,
                    $"<Run FontFamily=\"{FontFamilyName}\" FontSize=\"{size}\" FontWeight=\"{(run.Bold ? "Bold" : "Normal")}\" FontStyle=\"{(run.Italic ? "Italic" : "Normal")}\"");
                if (run.Underline)
                {
                    sb.Append(" TextDecorations=\"Underline\"");
                }

                sb.Append('>').Append(SecurityElement.Escape(run.Text)).Append("</Run>");
            }

            sb.Append("</Paragraph>");
        }

        return sb.Append("</Section>").ToString();
    }

    /// <summary>El contenido del documento como texto con marcas.</summary>
    public static string Save(FlowDocument doc)
    {
        var lines = new List<string>();
        foreach (var p in doc.Blocks.OfType<Paragraph>())
        {
            var runs = p.Inlines.OfType<EditableRun>()
                .Select(r => new RichRun(
                    (r.Text ?? "").Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal),
                    r.FontWeight >= FontWeight.SemiBold,
                    r.FontStyle == FontStyle.Italic,
                    r.TextDecorations is { Count: > 0 },
                    r.FontSize < (SmallSize + NormalSize) / 2 ? RichSize.Small
                        : r.FontSize > (NormalSize + LargeSize) / 2 ? RichSize.Large : RichSize.Normal))
                .Where(r => r.Text.Length > 0)
                .ToList();
            lines.Add(Line(Merge(runs)));
        }

        // Los párrafos vacíos del final no aportan nada.
        while (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    /// <summary>Une tramos seguidos con el mismo formato, para no generar marcas de más.</summary>
    private static List<RichRun> Merge(List<RichRun> runs)
    {
        var merged = new List<RichRun>();
        foreach (var r in runs)
        {
            if (merged.Count > 0 && merged[^1] is var last && last.Bold == r.Bold && last.Italic == r.Italic &&
                last.Underline == r.Underline && last.Size == r.Size)
            {
                merged[^1] = last with { Text = last.Text + r.Text };
            }
            else
            {
                merged.Add(r);
            }
        }

        return merged;
    }

    private static string Line(List<RichRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var r in runs)
        {
            var text = r.Text;

            // Una viñeta del editor ("• ") se guarda como "- ".
            if (sb.Length == 0 && text.StartsWith("• ", StringComparison.Ordinal))
            {
                sb.Append(RichText.BulletPrefix);
                text = text[2..];
            }

            if (text.Length == 0)
            {
                continue;
            }

            // Los espacios del borde quedan fuera de las marcas (así "**texto**" se reconoce siempre).
            var core = text.Trim();
            if (core.Length == 0 || (!r.Bold && !r.Italic && !r.Underline && r.Size == RichSize.Normal))
            {
                sb.Append(text);
                continue;
            }

            var lead = text[..text.IndexOf(core, StringComparison.Ordinal)];
            var trail = text[(lead.Length + core.Length)..];
            var (open, close) = Marks(r);
            sb.Append(lead).Append(open).Append(core).Append(close).Append(trail);
        }

        return sb.ToString();
    }

    private static (string Open, string Close) Marks(RichRun r)
    {
        var open = new StringBuilder();
        var close = new StringBuilder();
        if (r.Size == RichSize.Large)
        {
            open.Append(RichText.LargeOpen);
            close.Insert(0, RichText.LargeClose);
        }
        else if (r.Size == RichSize.Small)
        {
            open.Append(RichText.SmallOpen);
            close.Insert(0, RichText.SmallClose);
        }

        if (r.Underline)
        {
            open.Append(RichText.Underline);
            close.Insert(0, RichText.Underline);
        }

        if (r.Bold)
        {
            open.Append(RichText.Bold);
            close.Insert(0, RichText.Bold);
        }

        if (r.Italic)
        {
            open.Append(RichText.Italic);
            close.Insert(0, RichText.Italic);
        }

        return (open.ToString(), close.ToString());
    }
}
