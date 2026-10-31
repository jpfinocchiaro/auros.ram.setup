using Auros.Ram.Core.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Auros.Ram.App.Views;

/// <summary>Muestra una descripción con su formato (negrita, itálica, subrayado, tamaños y listas).</summary>
public class RichTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<RichTextBlock, string?>(nameof(Markup));

    static RichTextBlock()
    {
        MarkupProperty.Changed.AddClassHandler<RichTextBlock>((b, _) => b.Rebuild());
        FontSizeProperty.Changed.AddClassHandler<RichTextBlock>((b, _) => b.Rebuild());
    }

    public RichTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    /// <summary>El texto con marcas, tal como se guarda en el riesgo.</summary>
    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        var blocks = RichText.Parse(Markup);
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                inlines.Add(new LineBreak());
            }

            var prefix = RichText.Prefix(blocks[i]);
            if (prefix.Length > 0)
            {
                inlines.Add(new Run("   " + prefix));
            }

            foreach (var run in blocks[i].Runs)
            {
                var r = new Run(run.Text);
                if (run.Bold)
                {
                    r.FontWeight = FontWeight.SemiBold;
                }

                if (run.Italic)
                {
                    r.FontStyle = FontStyle.Italic;
                }

                if (run.Underline)
                {
                    r.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                }

                if (run.Size != RichSize.Normal)
                {
                    r.FontSize = FontSize * (run.Size == RichSize.Large ? 1.3 : 0.85);
                }

                inlines.Add(r);
            }
        }

        Inlines = inlines;
    }
}
