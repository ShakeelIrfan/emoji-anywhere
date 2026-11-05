using System.IO;
using System.Reflection;
using System.Text.Json;

namespace EmojiAnywhere.Core;

public sealed record Emoji(string Hex, string Unicode, string Label, string[] Shortcodes, string[] Tags, int Order)
{
    // Prefer a word over symbols, so 👍 shows as :thumbsup: and not :+1:
    public string PrimaryShortcode =>
        Shortcodes.FirstOrDefault(s => char.IsLetter(s[0])) ?? (Shortcodes.Length > 0 ? Shortcodes[0] : Label.Replace(' ', '_'));
}

// All emoji data is built into the app. Nothing is downloaded.
public sealed class EmojiIndex
{
    private readonly List<Emoji> _all = new();
    private readonly Dictionary<string, Emoji> _byShortcode = new(StringComparer.OrdinalIgnoreCase);
    private readonly UsageStore _usage;

    public EmojiIndex(UsageStore usage)
    {
        _usage = usage;
        Load();
    }

    public int Count => _all.Count;

    public Emoji? FindExact(string shortcode) =>
        _byShortcode.TryGetValue(shortcode, out var e) ? e : null;

    public List<Emoji> Search(string query, int max)
    {
        query = query.ToLowerInvariant();
        var scored = new List<(Emoji e, double score)>();
        // Skin tones only show up when you ask for them, so :thumb is not five thumbs in a row
        bool wantSkins = query.Contains("tone");

        foreach (var e in _all)
        {
            if (!wantSkins && _skinHex.Contains(e.Hex) && !_usage.HasUsed(e.Hex)) continue;
            double s = MatchScore(e, query);
            if (s <= 0) continue;
            s += _usage.Bonus(e.Hex);
            if (Popular.Contains(e.Unicode)) s += 30;
            scored.Add((e, s));
        }

        return scored
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.e.Order)
            .Take(max)
            .Select(x => x.e)
            .ToList();
    }

    // Emojis most people reach for. A small nudge so :sad shows 😢 😭 before rare ones,
    // until the app learns what you personally use.
    private static readonly HashSet<string> Popular = new()
    {
        "😂", "❤️", "🤣", "👍", "😭", "🙏", "😘", "🥰", "😍", "😊", "🎉", "😁", "💕", "🥺", "😅", "🔥",
        "🤦", "🤷", "🙄", "😆", "🤗", "😉", "🤔", "👏", "🙂", "😳", "🥳", "😎", "👌", "💜", "😔", "💪",
        "✨", "💖", "👀", "😋", "😏", "😢", "💯", "😩", "😡", "😜", "🙈", "🤞", "😄", "🤤", "🙌", "🤪",
        "😀", "💀", "💔", "😌", "🤩", "🙃", "😬", "😱", "😴", "🤭", "😐", "😇", "😈", "😞", "🖤", "💩",
        "✅", "👋", "🤮", "😤", "🤢", "🌟", "😥", "🌈", "😝", "😫", "😲", "🤯", "💃", "👊", "🤬", "😕",
        "⚡", "☕", "⭐", "🤨", "🤘", "🤝", "😓", "😟", "😣", "🧐", "😠", "🤠", "😛", "😑", "🫡", "🫠", "😮‍💨"
    };

    private static double MatchScore(Emoji e, string q)
    {
        double best = 0;
        foreach (var sc in e.Shortcodes)
        {
            if (sc == q) return 1000;
            if (sc.StartsWith(q)) best = Math.Max(best, 600 - sc.Length);
            else if (sc.Contains("_" + q)) best = Math.Max(best, 400 - sc.Length);
        }
        foreach (var t in e.Tags)
        {
            // An exact meaning match (:lol finds 😂) beats a longer name that only starts the same (lollipop)
            if (t == q) best = Math.Max(best, 590 - e.PrimaryShortcode.Length / 10.0);
            else if (t.StartsWith(q)) best = Math.Max(best, 250);
        }
        foreach (var word in e.Label.Split(' '))
        {
            if (word.StartsWith(q, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 200);
        }
        return best;
    }

    private void Load()
    {
        var shortcodes = new Dictionary<string, List<string>>();
        // iamcal is what Slack and Discord use. GitHub fills the gaps.
        foreach (var file in new[] { "iamcal.json", "github.json" })
        {
            using var doc = JsonDocument.Parse(ReadResource(file));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!shortcodes.TryGetValue(prop.Name, out var list))
                    shortcodes[prop.Name] = list = new List<string>();
                if (prop.Value.ValueKind == JsonValueKind.Array)
                    foreach (var v in prop.Value.EnumerateArray()) AddUnique(list, v.GetString());
                else
                    AddUnique(list, prop.Value.GetString());
            }
        }

        using var data = JsonDocument.Parse(ReadResource("compact.json"));
        foreach (var el in data.RootElement.EnumerateArray())
        {
            string hex = el.GetProperty("hexcode").GetString()!;
            string uni = el.GetProperty("unicode").GetString()!;
            string label = el.GetProperty("label").GetString()!;

            if (!el.TryGetProperty("order", out var orderEl))
            {
                // Letter emojis 🇦 to 🇿, named like Discord does
                if (label.StartsWith("regional indicator "))
                {
                    string letter = label[^1..].ToLowerInvariant();
                    Add(new Emoji(hex, uni, label, new[] { "regional_indicator_" + letter }, new[] { "letter" }, 100000 + letter[0]), skin: false);
                }
                continue;
            }

            var tags = el.TryGetProperty("tags", out var t)
                ? t.EnumerateArray().Select(x => x.GetString()!.ToLowerInvariant()).ToArray()
                : Array.Empty<string>();
            var codes = shortcodes.TryGetValue(hex, out var c) ? c.ToArray() : Array.Empty<string>();
            if (codes.Length == 0) codes = new[] { Slug(label) }; // every emoji gets at least one shortcode

            var emoji = new Emoji(hex, uni, label, codes, tags, orderEl.GetInt32());
            Add(emoji, skin: false);

            // Skin tone versions, named like Discord: thumbsup_tone2
            if (el.TryGetProperty("skins", out var skins))
            {
                foreach (var s in skins.EnumerateArray())
                {
                    string shex = s.GetProperty("hexcode").GetString()!;
                    string tones = string.Concat(shex.Split('-').Select(ToneNumber).Where(n => n > 0).Select(n => "_tone" + n));
                    var scodes = codes.Select(x => x + tones).ToArray();
                    Add(new Emoji(shex, s.GetProperty("unicode").GetString()!, s.GetProperty("label").GetString()!,
                        scodes, tags, s.TryGetProperty("order", out var so) ? so.GetInt32() : emoji.Order), skin: true);
                }
            }
        }
    }

    private readonly HashSet<string> _skinHex = new();

    private void Add(Emoji e, bool skin)
    {
        _all.Add(e);
        if (skin) _skinHex.Add(e.Hex);
        foreach (var sc in e.Shortcodes) _byShortcode.TryAdd(sc, e);
    }

    private static int ToneNumber(string part) => part switch
    {
        "1F3FB" => 1, "1F3FC" => 2, "1F3FD" => 3, "1F3FE" => 4, "1F3FF" => 5, _ => 0
    };

    private static string Slug(string label) =>
        new string(label.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');

    private static void AddUnique(List<string> list, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !list.Contains(value)) list.Add(value.ToLowerInvariant());
    }

    private static string ReadResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        string full = asm.GetManifestResourceNames().First(n => n.EndsWith("." + name));
        using var stream = asm.GetManifestResourceStream(full)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
