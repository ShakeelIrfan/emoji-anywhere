using System.IO;
using System.Text.Json;

namespace EmojiAnywhere.Core;

// Text snippets: type ";addr" and get your full address.
// Saved in snippets.json in the data folder, or in a sync folder (OneDrive, Dropbox) the person picks.
// The file only holds what the person saved on purpose. Typed text is never saved.

public sealed class SnippetFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool Deleted { get; set; }
}

public sealed class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? FolderId { get; set; }
    public string Abbreviation { get; set; } = ""; // without the trigger, so "addr" for ";addr"
    public string Label { get; set; } = "";
    public string Body { get; set; } = "";         // plain text (always set, also for rich snippets)
    public string Format { get; set; } = "plain";  // "plain" or "rich"
    public string? Rtf { get; set; }               // rich only: for Word and other desktop apps
    public string? Html { get; set; }              // rich only: for browsers, Gmail, Outlook
    public bool PopupOnly { get; set; }            // only insert from the search popup, never on its own
    public List<string>? Apps { get; set; }        // only expand in these apps (process names), null means everywhere
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool Deleted { get; set; }              // kept as a marker so a delete also syncs to other PCs

    // The RTF is the source of truth for formatting. HTML is rebuilt from it when inserting, never trusted as stored.
    public bool IsRich => Format == "rich" && !string.IsNullOrEmpty(Rtf);

    public Snippet Clone()
    {
        var c = (Snippet)MemberwiseClone();
        c.Apps = Apps?.ToList();
        return c;
    }
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

    private readonly Dictionary<string, Snippet> _exact;      // auto expanding ones
    private readonly Dictionary<string, Snippet> _any;        // also popup only ones (for nesting and the popup)
    private readonly HashSet<string> _prefixesOfLonger;       // every proper prefix of every auto abbreviation
    private readonly List<Snippet> _all;                      // everything usable, for the search popup

    public SnippetMatcher(IEnumerable<Snippet> snippets, IEnumerable<SnippetFolder> folders)
    {
        var off = folders.Where(f => !f.Enabled || f.Deleted).Select(f => f.Id).ToHashSet();
        _exact = new(StringComparer.OrdinalIgnoreCase);
        _any = new(StringComparer.OrdinalIgnoreCase);
        _prefixesOfLonger = new(StringComparer.OrdinalIgnoreCase);
        _all = new();
        foreach (var s in snippets)
        {
            if (s.Deleted || !IsValidAbbreviation(s.Abbreviation)) continue;
            if (s.FolderId != null && off.Contains(s.FolderId)) continue;
            _all.Add(s);
            _any.TryAdd(s.Abbreviation, s);
            if (s.PopupOnly) continue;
            _exact.TryAdd(s.Abbreviation, s); // the first one wins if two share an abbreviation
            for (int i = 1; i < s.Abbreviation.Length; i++) _prefixesOfLonger.Add(s.Abbreviation[..i]);
        }
    }

    public int Count => _all.Count;

    public Snippet? Find(string abbreviation) => _exact.TryGetValue(abbreviation, out var s) ? s : null;

    // Includes popup only snippets. Used for {snippet:name} and for picking from the popup.
    public Snippet? FindAny(string abbreviation) => _any.TryGetValue(abbreviation, out var s) ? s : null;

    // True when a longer abbreviation starts with this text, so ";ad" must wait in case you meant ";addr"
    public bool HasLonger(string abbreviation) => _prefixesOfLonger.Contains(abbreviation);

    // True when this text could still become an abbreviation
    public bool CouldMatch(string typed) => _exact.ContainsKey(typed) || _prefixesOfLonger.Contains(typed);

    // Search popup: exact abbreviation first, then abbreviations that start with it, then names that contain it
    public List<Snippet> Search(string query, int max)
    {
        if (query.Length == 0) return new();
        return _all
            .Select(s => (s, score: Score(s, query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.s.Abbreviation.Length).ThenBy(x => x.s.Abbreviation, StringComparer.OrdinalIgnoreCase)
            .Take(max).Select(x => x.s).ToList();
    }

    private static int Score(Snippet s, string q)
    {
        if (s.Abbreviation.Equals(q, StringComparison.OrdinalIgnoreCase)) return 100;
        if (s.Abbreviation.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 80;
        if (s.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 60;
        if (s.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return 50;
        if (q.Length >= 2 && s.Abbreviation.Contains(q, StringComparison.OrdinalIgnoreCase)) return 40;
        if (q.Length >= 3 && s.Label.Contains(q, StringComparison.OrdinalIgnoreCase)) return 30;
        return 0;
    }

    public static bool IsAbbreviationChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    public static bool IsValidAbbreviation(string a) =>
        a.Length is > 0 and <= MaxAbbreviation && a.All(IsAbbreviationChar);
}

public static class SnippetMerge
{
    // Two copies of the library (this PC and the sync folder) become one: for each snippet and folder
    // the newest change wins, and deletes win only if they are newer than the last edit.
    public static SnippetLibraryFile Merge(SnippetLibraryFile a, SnippetLibraryFile b)
    {
        return new SnippetLibraryFile
        {
            Version = Math.Max(a.Version, b.Version),
            Folders = MergeBy(a.Folders, b.Folders, f => f.Id, f => f.UpdatedUtc),
            Snippets = MergeBy(a.Snippets, b.Snippets, s => s.Id, s => s.UpdatedUtc),
        };
    }

    private static List<T> MergeBy<T>(List<T> a, List<T> b, Func<T, string> id, Func<T, DateTime> when)
    {
        var result = new Dictionary<string, T>();
        var order = new List<string>();
        foreach (var x in a.Concat(b))
        {
            string key = id(x);
            if (!result.TryGetValue(key, out var have)) { result[key] = x; order.Add(key); }
            else if (when(x) > when(have)) result[key] = x;
        }
        return order.Select(k => result[k]).ToList();
    }
}

public sealed class SnippetLibrary : IDisposable
{
    public const string LocalFileName = "snippets.json";
    public const string SyncFileName = "EmojiAnywhere-snippets.json";

    private readonly object _lock = new();
    private readonly Settings _settings;
    private SnippetLibraryFile _file;
    private FileSystemWatcher? _watcher;
    private string _lastWritten = "";
    private System.Threading.Timer? _reloadTimer;

    // Swapped as a whole on every change, so the keyboard thread always sees a complete matcher
    public SnippetMatcher Matcher { get; private set; }

    // Raised (on a background thread) when the library changes, also when another PC changed the sync file
    public event Action? Changed;

    // Set when the sync folder could not be read or written, shown in the editor
    public string? SyncProblem { get; private set; }

    public SnippetLibrary(Settings settings)
    {
        _settings = settings;
        _file = LoadLocal();
        if (SyncPath is string sp) { MergeFromDisk(sp); WriteNow(); }
        Matcher = Build(_file);
        Watch();
    }

    public string FilePath => SyncPath ?? Path.Combine(AppPaths.Folder, LocalFileName);

    private string? SyncPath =>
        !string.IsNullOrWhiteSpace(_settings.SnippetSyncFolder) && Directory.Exists(_settings.SnippetSyncFolder)
            ? Path.Combine(_settings.SnippetSyncFolder, SyncFileName) : null;

    public IReadOnlyList<Snippet> Snippets { get { lock (_lock) return _file.Snippets.Where(s => !s.Deleted).Select(s => s.Clone()).ToList(); } }
    public IReadOnlyList<SnippetFolder> Folders { get { lock (_lock) return _file.Folders.Where(f => !f.Deleted).OrderBy(f => f.Order).ThenBy(f => f.Name).ToList(); } }

    public void Upsert(Snippet s)
    {
        lock (_lock)
        {
            s = s.Clone();
            s.UpdatedUtc = DateTime.UtcNow;
            int i = _file.Snippets.FindIndex(x => x.Id == s.Id);
            if (i >= 0) _file.Snippets[i] = s; else _file.Snippets.Add(s);
            Commit();
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            var s = _file.Snippets.FirstOrDefault(x => x.Id == id);
            if (s == null) return;
            s.Deleted = true; s.Body = ""; s.Rtf = null; s.Html = null; s.UpdatedUtc = DateTime.UtcNow;
            Commit();
        }
    }

    public SnippetFolder AddFolder(string name)
    {
        lock (_lock)
        {
            var existing = _file.Folders.FirstOrDefault(f => !f.Deleted && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var f = new SnippetFolder { Name = name.Trim(), Order = _file.Folders.Count };
            _file.Folders.Add(f);
            Commit();
            return f;
        }
    }

    public void UpdateFolder(string id, string? name = null, bool? enabled = null)
    {
        lock (_lock)
        {
            var f = _file.Folders.FirstOrDefault(x => x.Id == id);
            if (f == null) return;
            if (name != null) f.Name = name.Trim();
            if (enabled != null) f.Enabled = enabled.Value;
            f.UpdatedUtc = DateTime.UtcNow;
            Commit();
        }
    }

    // Deleting a folder keeps its snippets: they move out to "no folder"
    public void DeleteFolder(string id)
    {
        lock (_lock)
        {
            var f = _file.Folders.FirstOrDefault(x => x.Id == id);
            if (f == null) return;
            f.Deleted = true; f.UpdatedUtc = DateTime.UtcNow;
            foreach (var s in _file.Snippets.Where(s => s.FolderId == id)) { s.FolderId = null; s.UpdatedUtc = DateTime.UtcNow; }
            Commit();
        }
    }

    // Adds imported snippets. Skips ones that already exist with the same abbreviation and text.
    public int Import(IEnumerable<Snippet> incoming, string? folderName = null)
    {
        lock (_lock)
        {
            string? folderId = null;
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                var f = _file.Folders.FirstOrDefault(x => !x.Deleted && x.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
                if (f == null) { f = new SnippetFolder { Name = folderName, Order = _file.Folders.Count }; _file.Folders.Add(f); }
                folderId = f.Id;
            }
            int added = 0;
            foreach (var s in incoming)
            {
                if (_file.Snippets.Count >= 10_000) break;
                if (!SnippetMatcher.IsValidAbbreviation(s.Abbreviation) || s.Body.Length > SnippetMatcher.MaxBody) continue;
                if (_file.Snippets.Any(x => !x.Deleted && x.Abbreviation.Equals(s.Abbreviation, StringComparison.OrdinalIgnoreCase) && x.Body == s.Body)) continue;
                var copy = s.Clone();
                copy.Id = Guid.NewGuid().ToString("N");
                copy.FolderId = folderId ?? copy.FolderId;
                copy.UpdatedUtc = DateTime.UtcNow;
                _file.Snippets.Add(copy);
                added++;
            }
            if (added > 0) Commit();
            return added;
        }
    }

    public string ExportJson()
    {
        lock (_lock)
        {
            var clean = new SnippetLibraryFile
            {
                Folders = _file.Folders.Where(f => !f.Deleted).ToList(),
                Snippets = _file.Snippets.Where(s => !s.Deleted).ToList(),
            };
            return JsonSerializer.Serialize(clean, AppPaths.Json);
        }
    }

    // Called after the sync folder setting changed: merge this PC's copy into the folder (or back out of it)
    public void SyncFolderChanged()
    {
        lock (_lock)
        {
            if (SyncPath is string sp) MergeFromDisk(sp);
            Commit();
        }
        Watch();
    }

    private void Commit()
    {
        Matcher = Build(_file);
        WriteNow();
        var changed = Changed;
        if (changed != null) Task.Run(() => changed());
    }

    private void WriteNow()
    {
        string json = JsonSerializer.Serialize(_file, AppPaths.Json);
        _lastWritten = json;
        // This PC always keeps its own copy too, so turning sync off or losing the folder loses nothing
        AppPaths.SaveText(LocalFileName, json);
        if (SyncPath is string sp)
        {
            try
            {
                File.WriteAllText(sp + ".tmp", json);
                File.Move(sp + ".tmp", sp, overwrite: true);
                SyncProblem = null;
            }
            catch (Exception ex) { SyncProblem = "Could not save to the sync folder: " + ex.Message; }
        }
    }

    private void MergeFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path)) { SyncProblem = null; return; }
            var other = Clean(JsonSerializer.Deserialize<SnippetLibraryFile>(File.ReadAllText(path)));
            if (other != null) _file = SnippetMerge.Merge(_file, other);
            SyncProblem = null;
        }
        catch (Exception ex) { SyncProblem = "Could not read the sync file: " + ex.Message; }
    }

    // Another PC changed the sync file: merge it in a moment later (cloud apps write files in several steps)
    private void Watch()
    {
        _watcher?.Dispose(); _watcher = null;
        if (SyncPath is not string sp) return;
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(sp)!, SyncFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler onChange = (_, _) => (_reloadTimer ??= new System.Threading.Timer(_ => ReloadFromSync())).Change(700, Timeout.Infinite);
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (s, e) => onChange(s, e);
        }
        catch { }
    }

    private void ReloadFromSync()
    {
        lock (_lock)
        {
            if (SyncPath is not string sp) return;
            string text;
            try { text = File.ReadAllText(sp); } catch { return; }
            if (text == _lastWritten) return; // our own write
            MergeFromDisk(sp);
            Commit();
        }
    }

    private static SnippetMatcher Build(SnippetLibraryFile f) => new(f.Snippets, f.Folders);

    // A hand edited or damaged file can't break typing: drop anything out of bounds
    private static SnippetLibraryFile? Clean(SnippetLibraryFile? f)
    {
        if (f == null) return null;
        f.Folders = (f.Folders ?? new()).Where(x => x != null).Take(1000).ToList();
        foreach (var x in f.Folders) { x.Id ??= Guid.NewGuid().ToString("N"); x.Name ??= ""; }
        f.Snippets = (f.Snippets ?? new()).Where(s => s != null && (s.Body ?? "").Length <= SnippetMatcher.MaxBody).Take(10_000).ToList();
        foreach (var s in f.Snippets) { s.Abbreviation ??= ""; s.Label ??= ""; s.Body ??= ""; s.Id ??= Guid.NewGuid().ToString("N"); s.Format ??= "plain"; }
        return f;
    }

    private static SnippetLibraryFile LoadLocal()
    {
        string path = Path.Combine(AppPaths.Folder, LocalFileName);
        bool exists = File.Exists(path);
        SnippetLibraryFile? f = null;
        if (exists)
        {
            try { f = Clean(JsonSerializer.Deserialize<SnippetLibraryFile>(File.ReadAllText(path))); }
            catch
            {
                // Keep the broken file for the person to look at, and start from the last backup if there is one
                try { File.Copy(path, path + ".broken", overwrite: true); } catch { }
                try { f = Clean(JsonSerializer.Deserialize<SnippetLibraryFile>(File.ReadAllText(path + ".bak"))); } catch { }
            }
            // One backup per start, so a bad change can always be undone by hand
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
        }
        f ??= new SnippetLibraryFile();
        if (!exists)
        {
            // A few examples so the feature is easy to discover; the person can edit or delete them
            f.Snippets.Add(new Snippet { Abbreviation = "shrug", Label = "Shrug", Body = "¯\\_(ツ)_/¯" });
            f.Snippets.Add(new Snippet { Abbreviation = "ty", Label = "Thank you", Body = "Thank you so much!" });
            f.Snippets.Add(new Snippet { Abbreviation = "today", Label = "Today's date", Body = "{date:d MMMM yyyy}" });
            f.Snippets.Add(new Snippet { Abbreviation = "time", Label = "Time right now", Body = "{time}" });
            AppPaths.SaveText(LocalFileName, JsonSerializer.Serialize(f, AppPaths.Json));
        }
        return f;
    }

    public void Dispose() { _watcher?.Dispose(); _reloadTimer?.Dispose(); }
}
