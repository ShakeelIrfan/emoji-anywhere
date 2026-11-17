using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using EmojiAnywhere.Core;

namespace EmojiAnywhere.UI;

// Turns the editor's formatted text into the formats other apps understand:
// HTML (browsers, Gmail, Outlook web), RTF (Word, WordPad) and plain text.
public static class RichTextConvert
{
    public static string Plain(FlowDocument doc)
    {
        string t = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        if (t.EndsWith("\r\n")) t = t[..^2];
        return t.Replace("\r\n", "\n");
    }

    public static string Rtf(FlowDocument doc)
    {
        using var ms = new MemoryStream();
        new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.Rtf);
        return Encoding.ASCII.GetString(ms.ToArray()); // RTF is 7 bit; other characters are already escaped
    }

    public static void LoadRtf(FlowDocument doc, string rtf)
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.Rtf);
    }

    public static void LoadPlain(FlowDocument doc, string text)
    {
        doc.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            doc.Blocks.Add(new Paragraph(new Run(line)));
    }

    // True when anything is bold, italic, underlined, a link or a list. Otherwise the snippet is saved as plain text.
    public static bool HasFormatting(FlowDocument doc) => doc.Blocks.Any(BlockHasFormatting);

    private static bool BlockHasFormatting(Block b) => b switch
    {
        List => true,
        Paragraph p => p.Inlines.Any(InlineHasFormatting),
        Section s => s.Blocks.Any(BlockHasFormatting),
        _ => false,
    };

    private static bool InlineHasFormatting(Inline i)
    {
        if (i is Hyperlink) return true;
        if (i.TextDecorations is { Count: > 0 }) return true;
        if (i is Run r) return r.FontWeight.ToOpenTypeWeight() >= 600 || r.FontStyle == FontStyles.Italic || Underlined(r);
        if (i is Span sp) return sp.FontWeight.ToOpenTypeWeight() >= 600 || sp.FontStyle == FontStyles.Italic || sp.Inlines.Any(InlineHasFormatting);
        return false;
    }

    public static string Html(FlowDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var b in doc.Blocks) Block(sb, b);
        return sb.ToString();
    }

    private static void Block(StringBuilder sb, Block b)
    {
        switch (b)
        {
            case Paragraph p:
                sb.Append("<div>");
                int before = sb.Length;
                foreach (var i in p.Inlines) Inline(sb, i);
                if (sb.Length == before) sb.Append("<br>");
                sb.Append("</div>");
                break;
            case List l:
                bool ordered = l.MarkerStyle is TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                    or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
                sb.Append(ordered ? "<ol>" : "<ul>");
                foreach (var item in l.ListItems)
                {
                    sb.Append("<li>");
                    foreach (var inner in item.Blocks)
                    {
                        if (inner is Paragraph ip) foreach (var i in ip.Inlines) Inline(sb, i);
                        else Block(sb, inner);
                    }
                    sb.Append("</li>");
                }
                sb.Append(ordered ? "</ol>" : "</ul>");
                break;
            case Section s:
                foreach (var inner in s.Blocks) Block(sb, inner);
                break;
        }
    }

    private static void Inline(StringBuilder sb, Inline i)
    {
        switch (i)
        {
            case Run r:
                if (r.Text.Length == 0) return;
                bool bold = r.FontWeight.ToOpenTypeWeight() >= 600, italic = r.FontStyle == FontStyles.Italic, under = Underlined(r);
                if (bold) sb.Append("<b>");
                if (italic) sb.Append("<i>");
                if (under) sb.Append("<u>");
                sb.Append(SnippetText.HtmlEscape(r.Text));
                if (under) sb.Append("</u>");
                if (italic) sb.Append("</i>");
                if (bold) sb.Append("</b>");
                break;
            case LineBreak:
                sb.Append("<br>");
                break;
            case Hyperlink h:
                string href = h.NavigateUri?.ToString() ?? "";
                bool safe = href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
                if (safe) sb.Append("<a href=\"").Append(SnippetText.HtmlEscape(href)).Append("\">");
                foreach (var inner in h.Inlines) Inline(sb, inner);
                if (safe) sb.Append("</a>");
                break;
            case Span s:
                foreach (var inner in s.Inlines) Inline(sb, inner);
                break;
        }
    }

    // Underline is not inherited like bold is, so look at the run and the spans around it.
    // Links are underlined by default; that is not the person's formatting, so links are skipped.
    private static bool Underlined(Inline i)
    {
        for (DependencyObject? d = i; d is Inline x; d = x.Parent)
            if (x is not Hyperlink && x.TextDecorations is { Count: > 0 } td && td.Any(t => t.Location == TextDecorationLocation.Underline)) return true;
        return false;
    }
}
