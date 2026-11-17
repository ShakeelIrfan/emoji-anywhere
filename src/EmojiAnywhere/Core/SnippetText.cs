using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EmojiAnywhere.Core;

// A field the person fills in before a snippet is inserted: {input:Name} or {choice:Size|S,M,L}
public sealed record SnippetField(string Name, string[]? Choices);

// Placeholders inside snippet text:
//   {date} {date:dd MMM yyyy} {time} {time:HH:mm}   today's date or time, optional .NET format
//   {clipboard}                                       what is on the clipboard (text)
//   {cursor}                                          where the cursor ends up after inserting
//   {input:Name}  {choice:Size|S,M,L}                 asked for in a small form first
//   {snippet:abbr}                                    another snippet's text (up to 3 levels deep)
//   {{ and }}                                         a literal { or }
// Anything else in braces is left as it is.
public static class SnippetText
{
    public const int MaxNesting = 3;
    public const string CursorMarker = ""; // a private use character nobody types

    public sealed record Rendered(string Text, int CursorBack);

    private static readonly Regex Token = new(@"\{(?<name>[A-Za-z]+)(?::(?<arg>[^{}]*))?\}", RegexOptions.Compiled);
    // In RTF the braces a person typed are escaped as \{ and \}
    private static readonly Regex RtfToken = new(@"\\\{(?<name>[A-Za-z]+)(?::(?<arg>[^\\{}]*))?\\\}", RegexOptions.Compiled);

    public static bool HasPlaceholders(string s) => s.Contains('{');

    // How much one insert may add in total. Stops a snippet that includes another one many times
    // (which includes another one many times...) from building something huge and freezing the app.
    private sealed class Budget
    {
        public int Chars = SnippetMatcher.MaxBody;
        public int Refs = 50;

        public string Take(string text)
        {
            if (Chars <= 0) return "";
            if (text.Length > Chars) text = text[..Chars];
            Chars -= text.Length;
            return text;
        }
    }

    // Replaces {snippet:abbr} with that snippet's plain text, a few levels deep (so loops end)
    public static string ResolveNested(string body, Func<string, Snippet?> find, int depth = 0) => ResolveNested(body, find, depth, new Budget());

    private static string ResolveNested(string body, Func<string, Snippet?> find, int depth, Budget budget)
    {
        if (!body.Contains("{snippet:", StringComparison.OrdinalIgnoreCase)) return body;
        // At most 50 includes in total and every level cut to the size limit, so the work stays small
        string result = Token.Replace(body, m =>
        {
            if (!m.Groups["name"].Value.Equals("snippet", StringComparison.OrdinalIgnoreCase)) return m.Value;
            // Too deep (or a snippet that includes itself), or too many includes: stop here
            if (depth >= MaxNesting || budget.Refs <= 0) return "";
            budget.Refs--;
            var inner = find(m.Groups["arg"].Value.Trim());
            return inner == null ? "" : ResolveNested(inner.Body, find, depth + 1, budget);
        });
        return result.Length > SnippetMatcher.MaxBody ? result[..SnippetMatcher.MaxBody] : result;
    }

    // The fields to ask for, in order, each name once
    public static List<SnippetField> Fields(string body)
    {
        var list = new List<SnippetField>();
        foreach (Match m in Token.Matches(Unescape(body, keepEscapes: true)))
        {
            string name = m.Groups["name"].Value.ToLowerInvariant();
            string arg = m.Groups["arg"].Value;
            if (name is not ("input" or "choice")) continue;
            string label = (name == "choice" ? arg.Split('|')[0] : arg).Trim();
            if (label.Length == 0) label = name == "choice" ? "Choice" : "Text";
            if (list.Any(f => f.Name.Equals(label, StringComparison.OrdinalIgnoreCase))) continue;
            string[]? choices = null;
            if (name == "choice")
            {
                var parts = arg.Split('|', 2);
                choices = (parts.Length > 1 ? parts[1] : "").Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).Take(50).ToArray();
                if (choices.Length == 0) choices = null;
            }
            list.Add(new SnippetField(label, choices));
            if (list.Count >= 20) break;
        }
        return list;
    }

    // Plain text, ready to type or paste. CursorBack is how many times to press Left afterwards.
    public static Rendered Render(string body, IReadOnlyDictionary<string, string> values, Func<string> clipboard, DateTime now, Func<string, Snippet?>? find = null)
    {
        string text = Fill(body, values, clipboard, now, s => s, Token, plainEscapes: true, find);
        int at = text.IndexOf(CursorMarker, StringComparison.Ordinal);
        if (at < 0) return new Rendered(text, 0);
        text = text.Replace(CursorMarker, "");
        string after = text[at..].Replace("\r\n", "\n");
        return new Rendered(text, after.Length);
    }

    public static string RenderHtml(string html, IReadOnlyDictionary<string, string> values, Func<string> clipboard, DateTime now, Func<string, Snippet?>? find = null) =>
        Fill(html, values, clipboard, now, HtmlEscape, Token, plainEscapes: false, find).Replace(CursorMarker, "");

    public static string RenderRtf(string rtf, IReadOnlyDictionary<string, string> values, Func<string> clipboard, DateTime now, Func<string, Snippet?>? find = null) =>
        Fill(rtf, values, clipboard, now, RtfEscape, RtfToken, plainEscapes: false, find).Replace(CursorMarker, "");

    private static string Fill(string body, IReadOnlyDictionary<string, string> values, Func<string> clipboard, DateTime now,
        Func<string, string> escape, Regex token, bool plainEscapes, Func<string, Snippet?>? find)
    {
        // Doubled braces mean a literal brace (plain text only; RTF uses its own escapes)
        if (plainEscapes) body = Unescape(body, keepEscapes: true);
        var budget = new Budget(); // everything filled in counts, so 1000 copies of {clipboard} can't blow up either
        string result = token.Replace(body, m =>
        {
            string name = m.Groups["name"].Value.ToLowerInvariant();
            string arg = m.Groups["arg"].Value;
            switch (name)
            {
                case "date": return escape(budget.Take(Format(now, arg, "d")));
                case "time": return escape(budget.Take(Format(now, arg, "t")));
                case "clipboard": return escape(budget.Take(SafeClipboard(clipboard)));
                case "cursor": return CursorMarker;
                case "snippet":
                    if (find == null) return m.Value;
                    if (budget.Refs <= 0 || budget.Chars <= 0) return "";
                    budget.Refs--;
                    var inner = find(arg.Trim());
                    if (inner == null) return "";
                    // Its own budget for the includes inside it; this outer budget then caps what actually goes in
                    string nested = Fill(ResolveNested(inner.Body, find, 1), values, clipboard, now, x => x, Token, plainEscapes: true, null);
                    return escape(budget.Take(nested.Replace(CursorMarker, "")));
                case "input":
                case "choice":
                    string label = (name == "choice" ? arg.Split('|')[0] : arg).Trim();
                    if (label.Length == 0) label = name == "choice" ? "Choice" : "Text";
                    return escape(budget.Take(values.TryGetValue(label, out var v) ? v : ""));
                default: return m.Value;
            }
        });
        return plainEscapes ? result.Replace(OpenEsc, "{").Replace(CloseEsc, "}") : result;
    }

    // Placeholders stand in for {{ and }} while the tokens are replaced
    private const string OpenEsc = "", CloseEsc = "";
    private static string Unescape(string s, bool keepEscapes) => s.Replace("{{", OpenEsc).Replace("}}", CloseEsc);

    private static string SafeClipboard(Func<string> clipboard)
    {
        try { var t = clipboard() ?? ""; return t.Length > SnippetMatcher.MaxBody ? t[..SnippetMatcher.MaxBody] : t; }
        catch { return ""; }
    }

    private static string Format(DateTime now, string format, string fallback)
    {
        if (string.IsNullOrWhiteSpace(format)) return now.ToString(fallback, CultureInfo.CurrentCulture);
        try { return now.ToString(format.Trim(), CultureInfo.CurrentCulture); }
        catch (FormatException) { return now.ToString(fallback, CultureInfo.CurrentCulture); }
    }

    public static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
         .Replace("\r\n", "\n").Replace("\n", "<br>");

    public static string RtfEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Replace("\r\n", "\n"))
        {
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\par ");
            else if (c > 127) sb.Append("\\u").Append((int)(short)c).Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // Windows' clipboard HTML format: a small header with byte offsets, then the page
    public static string ClipboardHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string pre = "<html><body>\r\n<!--StartFragment-->";
        const string post = "<!--EndFragment-->\r\n</body></html>";
        int headerLen = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startHtml = headerLen;
        int startFrag = startHtml + Encoding.UTF8.GetByteCount(pre);
        int endFrag = startFrag + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFrag + Encoding.UTF8.GetByteCount(post);
        return string.Format(header, startHtml, endHtml, startFrag, endFrag) + pre + fragment + post;
    }
}
