using System.IO;
using System.Text.Json;

namespace EmojiAnywhere.Core;

// Text snippets: type ";addr" and get your full address. Saved in snippets.json in the data folder.
// The file only holds what the person saved on purpose. Typed text is never saved.

public sealed class SnippetFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? FolderId { get; set; }
    public string Abbreviation { get; set; } = ""; // without the trigger, so "addr" for ";addr"
    public string Label { get; set; } = "";
    public string Body { get; set; } = "";
    public string Format { get; set; } = "plain"; // "plain" or "rich"
    public string? Rtf { get; set; }
    public bool PopupOnly { get; set; }            // only insert from the search popup, never on its own
    public List<string>? Apps { get; set; }        // only expand in these apps (process names), null means everywhere
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool Deleted { get; set; }              // kept as a marker so a delete also syncs to other PCs
}

public sealed class SnippetLibraryFile
{
    public int Version { get; set; } = 1;
    public List<SnippetFolder> Folders { get; set; } = new();
    public List<Snippet> Snippets { get; set; } = new();
}

// Fast lookups for the keyboard thread. Built once per change and then only read, so it needs no locks.
public sealed class SnippetMatcher
{
    public const int MaxAbbreviation = 32;
    public const int MaxBody = 100_000;

    private readonly Dictionary<string, Snippet> _exact;
    private readonly HashSet<string> _prefixesOfLonger; // every proper prefix of every abbreviation

    public SnippetMatcher(IEnumerable<Snippet> snippets, IEnumerable<SnippetFolder> folders)
    {
        var off = folders.Where(f => !f.Enabled).Select(f => f.Id).ToHashSet();
        _exact = new(StringComparer.OrdinalIgnoreCase);
        _prefixesOfLonger = new(StringComparer.OrdinalIgnoreCase);
        foreach (var s in snippets)
        {
            if (s.Deleted || s.PopupOnly || !IsValidAbbreviation(s.Abbreviation)) continue;
            if (s.FolderId != null && off.Contains(s.FolderId)) continue;
            _exact.TryAdd(s.Abbreviation, s); // the first one wins if two share an abbreviation
            for (int i = 1; i < s.Abbreviation.Length; i++) _prefixesOfLonger.Add(s.Abbreviation[..i]);
        }
    }

    public int Count => _exact.Count;

    public Snippet? Find(string abbreviation) => _exact.TryGetValue(abbreviation, out var s) ? s : null;

    // True when a longer abbreviation starts with this text, so ";ad" must wait in case you meant ";addr"
    public bool HasLonger(string abbreviation) => _prefixesOfLonger.Contains(abbreviation);

    // True when this text could still become an abbreviation (worth keeping track of)
    public bool CouldMatch(string typed) => _exact.ContainsKey(typed) || _prefixesOfLonger.Contains(typed);

    public static bool IsAbbreviationChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    public static bool IsValidAbbreviation(string a) =>
        a.Length is > 0 and <= MaxAbbreviation && a.All(IsAbbreviationChar);
}

public sealed class SnippetLibrary
{
    private const string FileName = "snippets.json";
    private readonly object _lock = new();
    private SnippetLibraryFile _file;

    // Swapped as a whole on every change, so the keyboard thread always sees a complete matcher
    public SnippetMatcher Matcher { get; private set; }

    public SnippetLibrary()
    {
        _file = Load();
        Matcher = Build(_file);
    }

    public IReadOnlyList<Snippet> Snippets { get { lock (_lock) return _file.Snippets.Where(s => !s.Deleted).ToList(); } }

    public void Upsert(Snippet s)
    {
        lock (_lock)
        {
            s.UpdatedUtc = DateTime.UtcNow;
            int i = _file.Snippets.FindIndex(x => x.Id == s.Id);
            if (i >= 0) _file.Snippets[i] = s; else _file.Snippets.Add(s);
            Changed();
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            var s = _file.Snippets.FirstOrDefault(x => x.Id == id);
            if (s == null) return;
            s.Deleted = true; s.Body = ""; s.Rtf = null; s.UpdatedUtc = DateTime.UtcNow;
            Changed();
        }
    }

    private void Changed()
    {
        Matcher = Build(_file);
        var json = JsonSerializer.Serialize(_file, AppPaths.Json);
        Task.Run(() => { lock (_saveLock) AppPaths.SaveText(FileName, json); });
    }

    private static readonly object _saveLock = new();

    private static SnippetMatcher Build(SnippetLibraryFile f) => new(f.Snippets, f.Folders);

    private static SnippetLibraryFile Load()
    {
        bool exists = File.Exists(Path.Combine(AppPaths.Folder, FileName));
        var f = AppPaths.Load<SnippetLibraryFile>(FileName);
        f.Folders ??= new(); f.Snippets ??= new();
        // A hand edited or damaged file can't break typing: drop anything out of bounds
        f.Snippets = f.Snippets.Where(s => s != null && s.Body != null && s.Body.Length <= SnippetMatcher.MaxBody)
            .Take(10_000).ToList();
        foreach (var s in f.Snippets) { s.Abbreviation ??= ""; s.Label ??= ""; s.Id ??= Guid.NewGuid().ToString("N"); }
        if (!exists)
        {
            // A few examples so the feature is easy to discover; the person can edit or delete them
            f.Snippets.Add(new Snippet { Abbreviation = "shrug", Label = "Shrug", Body = "¯\\_(ツ)_/¯" });
            f.Snippets.Add(new Snippet { Abbreviation = "ty", Label = "Thank you", Body = "Thank you so much!" });
            f.Snippets.Add(new Snippet { Abbreviation = "brb", Label = "Be right back", Body = "Be right back, give me a minute." });
            AppPaths.SaveText(FileName, JsonSerializer.Serialize(f, AppPaths.Json));
        }
        return f;
    }
}
