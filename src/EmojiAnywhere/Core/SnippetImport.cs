using System.Text;
using System.Text.Json;

namespace EmojiAnywhere.Core;

// Reads snippets from other tools so switching takes one click.
// Everything here only reads text the person chose; nothing runs and nothing is sent anywhere.
public static class SnippetImport
{
    public const int MaxFileBytes = 20 * 1024 * 1024;

    // Picks the reader from the file's content: our JSON, Beeftext JSON, or espanso YAML
    public static List<Snippet> Read(string text)
    {
        string t = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (t.StartsWith('{') || t.StartsWith('['))
        {
            var ours = TryOurs(t);
            if (ours.Count > 0) return ours;
            return Beeftext(t);
        }
        return Espanso(text);
    }

    private static List<Snippet> TryOurs(string json)
    {
        try
        {
            var f = JsonSerializer.Deserialize<SnippetLibraryFile>(json);
            // Stored HTML from a file is never kept; it is rebuilt from the formatting when the snippet is used
            return f?.Snippets?.Where(s => s != null && !s.Deleted && s.Body != null)
                .Select(s => { var c = s.Clone(); c.Html = null; return c; }).ToList() ?? new();
        }
        catch { return new(); }
    }

    // Beeftext saves { "combos": [ { "name", "keyword", "snippet", "enabled" } ] }
    public static List<Snippet> Beeftext(string json)
    {
        var list = new List<Snippet>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement combos = default;
            if (doc.RootElement.ValueKind == JsonValueKind.Array) combos = doc.RootElement;
            else
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Name.Equals("combos", StringComparison.OrdinalIgnoreCase)) combos = p.Value;
            if (combos.ValueKind != JsonValueKind.Array) return list;
            foreach (var c in combos.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                string keyword = Str(c, "keyword"), body = Str(c, "snippet"), name = Str(c, "name");
                if (Str(c, "enabled") == "false") continue;
                var s = FromTrigger(keyword, body, name);
                if (s != null) list.Add(s);
            }
        }
        catch { }
        return list;
    }

    private static string Str(JsonElement o, string key)
    {
        foreach (var p in o.EnumerateObject())
            if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => p.Value.ToString(),
                };
        return "";
    }

    // espanso match files:  matches:  - trigger: ":addr"   replace: "text"  (or a | block for several lines)
    // Only simple trigger and replace pairs are read; scripts, forms and shell commands are skipped on purpose.
    public static List<Snippet> Espanso(string yaml)
    {
        var list = new List<Snippet>();
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        string? trigger = null, replace = null, label = null;

        void Flush()
        {
            if (trigger != null && replace != null && FromTrigger(trigger, replace, label ?? "") is Snippet s) list.Add(s);
            trigger = null; replace = null; label = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string line = raw.Trim();
            if (line.StartsWith("- ")) { Flush(); line = line[2..].Trim(); }
            if (line.StartsWith("trigger:")) trigger = Scalar(line["trigger:".Length..]);
            else if (line.StartsWith("label:")) label = Scalar(line["label:".Length..]);
            else if (line.StartsWith("replace:"))
            {
                string rest = line["replace:".Length..].Trim();
                if (rest.StartsWith('|') || rest.StartsWith('>'))
                {
                    // Block: every following line indented more than the "replace:" key
                    int keyIndent = raw.Length - raw.TrimStart().Length + (raw.TrimStart().StartsWith("- ") ? 2 : 0);
                    var sb = new StringBuilder();
                    int blockIndent = -1;
                    while (i + 1 < lines.Length)
                    {
                        string next = lines[i + 1];
                        if (next.Trim().Length == 0) { sb.Append('\n'); i++; continue; }
                        int ind = next.Length - next.TrimStart().Length;
                        if (ind <= keyIndent) break;
                        if (blockIndent < 0) blockIndent = ind;
                        sb.Append(next.Length >= blockIndent ? next[blockIndent..] : next.TrimStart()).Append('\n');
                        i++;
                    }
                    string block = sb.ToString();
                    replace = rest.StartsWith("|-") || rest.StartsWith(">-") ? block.TrimEnd('\n') : block.TrimEnd('\n') + "\n";
                    if (rest.StartsWith('>')) replace = replace.Replace("\n", " ").Trim();
                }
                else replace = Scalar(rest);
            }
        }
        Flush();
        return list;
    }

    private static string Scalar(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            s = s[1..^1];
            return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'') return s[1..^1].Replace("''", "'");
        int hash = s.IndexOf(" #", StringComparison.Ordinal);
        return hash >= 0 ? s[..hash].Trim() : s;
    }

    // ":addr" or ";addr" or "addr" becomes the abbreviation "addr"
    private static Snippet? FromTrigger(string trigger, string body, string label)
    {
        string abbr = new string(trigger.SkipWhile(c => !SnippetMatcher.IsAbbreviationChar(c)).ToArray());
        abbr = new string(abbr.TakeWhile(SnippetMatcher.IsAbbreviationChar).ToArray());
        if (!SnippetMatcher.IsValidAbbreviation(abbr) || body.Length > SnippetMatcher.MaxBody) return null;
        return new Snippet { Abbreviation = abbr, Body = body, Label = label.Length > 0 ? label : abbr };
    }
}
