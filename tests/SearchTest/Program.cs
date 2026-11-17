using System.Diagnostics;
using EmojiAnywhere.Core;

// Checks the emoji list and search ranking. Exit code 1 if anything fails.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Use an empty, temporary data folder so personal usage never changes the results
string data = Path.Combine(Path.GetTempPath(), "EmojiAnywhereSearchTest-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", data);

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) pass++; else fail++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {detail}");
}

var sw = Stopwatch.StartNew();
var index = new EmojiIndex(new UsageStore());
Console.WriteLine($"Loaded {index.Count} emojis in {sw.ElapsedMilliseconds} ms");
Check("loads every emoji", index.Count >= 3900, $"({index.Count})");

foreach (var (code, emoji) in new[]
{
    ("fire", "🔥"), ("sob", "😭"), ("skull", "💀"), ("joy", "😂"), ("thumbsup", "👍"), ("+1", "👍"),
    ("thumbsup_tone3", "👍🏽"), ("shinto_shrine", "⛩️"), ("regional_indicator_a", "🇦"), ("heart", "❤️"), ("100", "💯"),
})
{
    Check($"exact :{code}:", index.FindExact(code)?.Unicode == emoji, $"-> {index.FindExact(code)?.Unicode ?? "missing"}");
}

string Top(string q, int n) => string.Concat(index.Search(q, n).Select(e => e.Unicode));
Check(":fire first result is 🔥", Top("fire", 1) == "🔥", Top("fire", 6));
Check(":sad starts with 😢 😭", Top("sad", 2) is "😢😭" or "😭😢", Top("sad", 6));
Check(":lol finds 😂 first", Top("lol", 1) == "😂", Top("lol", 6));
Check(":thumb shows 👍 as :thumbsup:", index.Search("thumb", 1)[0].PrimaryShortcode == "thumbsup");
Check("skin tones hidden unless asked", !Top("thumb", 6).Contains("👍🏽"), Top("thumb", 6));

var times = new List<double>();
foreach (var q in new[] { "fi", "fir", "sa", "hea", "thu", "lo", "sk" })
{
    sw.Restart();
    index.Search(q, 6);
    times.Add(sw.Elapsed.TotalMilliseconds);
}
Check("search is fast (under 20 ms after warm up)", times.Skip(1).Max() < 20, $"max {times.Skip(1).Max():0.0} ms");

// Window title rules (issue #5)
Check("Jira tab in Chrome is a picker site", TitleRules.IsShortcodeSite("ABC-12 Fix login - Jira - Google Chrome"));
Check("Jira tab in Edge with more tabs", TitleRules.IsShortcodeSite("[ABC-12] Fix login - Jira and 3 more pages - Work - Microsoft​ Edge"));
Check("Teams on the web", TitleRules.IsShortcodeSite("Chat | Alex Doe | Microsoft Teams - Google Chrome"));
Check("Slack on the web", TitleRules.IsShortcodeSite("general (Channel) - Acme - Slack - Google Chrome"));
Check("Discord on the web", TitleRules.IsShortcodeSite("Discord | #general | My Server - Google Chrome"));
Check("an article about Jira is not", !TitleRules.IsShortcodeSite("Notes on Jira pricing - Google Chrome"));
Check("a plain page is not", !TitleRules.IsShortcodeSite("Google Docs - Google Chrome"));
Check("title rule matches any case", TitleRules.Ignored("Board - JIRA - Google Chrome", new[] { "jira" }));
Check("title rule needs a match", !TitleRules.Ignored("YouTube - Google Chrome", new[] { "jira" }));
Check("no rules, nothing ignored", !TitleRules.Ignored("Jira", Array.Empty<string>()));

// Snippets
{
    var folderOff = new SnippetFolder { Name = "Off", Enabled = false };
    var list = new List<Snippet>
    {
        new() { Abbreviation = "addr", Body = "123 Example Street" },
        new() { Abbreviation = "ad", Body = "short" },
        new() { Abbreviation = "ty", Body = "Thank you" },
        new() { Abbreviation = "gone", Body = "x", Deleted = true },
        new() { Abbreviation = "pop", Body = "x", PopupOnly = true },
        new() { Abbreviation = "hid", Body = "x", FolderId = folderOff.Id },
        new() { Abbreviation = "bad word", Body = "x" },
        new() { Abbreviation = "TY", Body = "duplicate" },
    };
    var m = new SnippetMatcher(list, new[] { folderOff });
    Check("snippet: exact match", m.Find("addr")?.Body == "123 Example Street");
    Check("snippet: case does not matter", m.Find("ADDR")?.Body == "123 Example Street");
    Check("snippet: first of two duplicates wins", m.Find("ty")?.Body == "Thank you");
    Check("snippet: ;ad waits because ;addr exists", m.Find("ad") != null && m.HasLonger("ad"));
    Check("snippet: ;addr has nothing longer", !m.HasLonger("addr"));
    Check("snippet: ;a could still match", m.CouldMatch("a") && !m.CouldMatch("ax"));
    Check("snippet: deleted ones never expand", m.Find("gone") == null);
    Check("snippet: popup only ones never auto expand", m.Find("pop") == null);
    Check("snippet: turned off folder never expands", m.Find("hid") == null);
    Check("snippet: abbreviation with a space is rejected", m.Find("bad word") == null && !SnippetMatcher.IsValidAbbreviation("bad word"));
    Check("snippet: too long abbreviation rejected", !SnippetMatcher.IsValidAbbreviation(new string('a', 33)));

    var lib = new SnippetLibrary(new Settings());
    Check("snippet library: first run adds examples", lib.Snippets.Count == 3 && lib.Matcher.Find("shrug") != null);
    var mine = new Snippet { Abbreviation = "mail", Body = "me@example.com" };
    lib.Upsert(mine);
    Check("snippet library: new snippet works right away", lib.Matcher.Find("mail")?.Body == "me@example.com");
    lib.Delete(mine.Id);
    Check("snippet library: deleted snippet stops right away", lib.Matcher.Find("mail") == null);
    Thread.Sleep(300); // saves happen in the background
    var again = new SnippetLibrary(new Settings());
    Check("snippet library: changes are saved", again.Matcher.Find("shrug") != null && again.Matcher.Find("mail") == null && again.Snippets.Count == 3);
}

// Snippet placeholders, forms, search, merge, import and sync
{
    var now = new DateTime(2026, 11, 18, 14, 5, 0);
    var none = new Dictionary<string, string>();
    Func<string> clip = () => "COPIED";
    string R(string body, Dictionary<string, string>? v = null, Func<string, Snippet?>? find = null) =>
        SnippetText.Render(body, v ?? none, clip, now, find).Text;

    Check("placeholder: date with format", R("{date:yyyy-MM-dd}") == "2026-11-18");
    Check("placeholder: time with format", R("{time:HH:mm}") == "14:05");
    Check("placeholder: clipboard", R("Re: {clipboard}") == "Re: COPIED");
    Check("placeholder: bad date format falls back", R("{date:%}").Length > 0);
    Check("placeholder: unknown stays as typed", R("{nope} and {Name}") == "{nope} and {Name}");
    Check("placeholder: doubled braces are literal", R("{{date}} costs {{5}}") == "{date} costs {5}");
    var cur = SnippetText.Render("Hi {cursor}!\nBye", none, clip, now);
    Check("placeholder: cursor removed and counted", cur.Text == "Hi !\nBye" && cur.CursorBack == 5, $"'{cur.Text}' back={cur.CursorBack}");
    var cur2 = SnippetText.Render("Hi {cursor}!\r\nBye", none, clip, now);
    Check("placeholder: CRLF counts as one step", cur2.CursorBack == 5, $"back={cur2.CursorBack}");

    var fields = SnippetText.Fields("Hi {input:Name}, size {choice:Size|S, M ,L}. Bye {input:name} {input:}");
    Check("fields: found in order, each once", fields.Count == 3 && fields[0].Name == "Name" && fields[1].Name == "Size" && fields[2].Name == "Text",
        string.Join(",", fields.Select(f => f.Name)));
    Check("fields: choices trimmed", fields[1].Choices is { Length: 3 } c && c[1] == "M");
    var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Sam", ["Size"] = "L" };
    Check("fields: filled in", R("Hi {input:Name}, size {choice:Size|S,M,L}", vals) == "Hi Sam, size L");

    var lookup = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase)
    {
        ["sig"] = new() { Abbreviation = "sig", Body = "Best,\n{snippet:name}" },
        ["name"] = new() { Abbreviation = "name", Body = "Sam" },
        ["loop"] = new() { Abbreviation = "loop", Body = "x{snippet:loop}" },
    };
    Func<string, Snippet?> find = k => lookup.TryGetValue(k, out var v) ? v : null;
    Check("nesting: snippet inside snippet", R("Thanks!\n{snippet:sig}", null, find) == "Thanks!\nBest,\nSam");
    Check("nesting: loops stop", R("{snippet:loop}", null, find).StartsWith("xxx") && R("{snippet:loop}", null, find).Length < 10);
    Check("nesting: missing snippet is empty", R("a{snippet:ghost}b", null, find) == "ab");
    Check("nesting: fields inside nested snippets are asked for",
        SnippetText.Fields(SnippetText.ResolveNested("{snippet:f}", k => k == "f" ? new Snippet { Body = "{input:City}" } : null)).Any(f => f.Name == "City"));

    Check("html: values are escaped", SnippetText.RenderHtml("<b>{input:Name}</b>", new Dictionary<string, string> { ["Name"] = "<x>&" }, clip, now) == "<b>&lt;x&gt;&amp;</b>");
    Check("rtf: placeholder found and escaped", SnippetText.RenderRtf(@"{\rtf1 Hi \{input:Name\}}", new Dictionary<string, string> { ["Name"] = "a{b}é" }, clip, now)
        == @"{\rtf1 Hi a\{b\}\u233?}");
    string cf = SnippetText.ClipboardHtml("<b>é</b>");
    int startFrag = int.Parse(cf.Split("StartFragment:")[1][..10]);
    int endFrag = int.Parse(cf.Split("EndFragment:")[1][..10]);
    var bytes = System.Text.Encoding.UTF8.GetBytes(cf);
    Check("clipboard html: offsets point at the fragment", System.Text.Encoding.UTF8.GetString(bytes, startFrag, endFrag - startFrag) == "<b>é</b>");

    var m2 = new SnippetMatcher(new List<Snippet>
    {
        new() { Abbreviation = "addr", Label = "Home address", Body = "1" },
        new() { Abbreviation = "ad", Label = "Short", Body = "2" },
        new() { Abbreviation = "hello", Label = "Greeting for addresses", Body = "3", PopupOnly = true },
        new() { Abbreviation = "zz", Label = "Work address", Body = "4" },
    }, Array.Empty<SnippetFolder>());
    var found = m2.Search("ad", 6).Select(x => x.Abbreviation).ToList();
    Check("search: exact, then prefix", found.Count >= 2 && found[0] == "ad" && found[1] == "addr", string.Join(",", found));
    Check("search: words in the name", m2.Search("work", 6).Any(x => x.Abbreviation == "zz"));
    Check("search: popup only ones show in search", m2.Search("hel", 6).Any(x => x.Abbreviation == "hello") && m2.Find("hello") == null && m2.FindAny("hello") != null);

    var t0 = new DateTime(2026, 1, 1);
    var pcA = new SnippetLibraryFile { Snippets = { new() { Id = "a", Abbreviation = "x", Body = "old", UpdatedUtc = t0 }, new() { Id = "b", Abbreviation = "y", Body = "keep", UpdatedUtc = t0 } } };
    var pcB = new SnippetLibraryFile { Snippets = { new() { Id = "a", Abbreviation = "x", Body = "new", UpdatedUtc = t0.AddMinutes(5) }, new() { Id = "b", Abbreviation = "y", Deleted = true, UpdatedUtc = t0.AddMinutes(1) }, new() { Id = "c", Abbreviation = "z", Body = "only B", UpdatedUtc = t0 } } };
    var merged = SnippetMerge.Merge(pcA, pcB);
    Check("merge: newest edit wins", merged.Snippets.First(x => x.Id == "a").Body == "new");
    Check("merge: newer delete wins", merged.Snippets.First(x => x.Id == "b").Deleted);
    Check("merge: new on either side kept", merged.Snippets.Any(x => x.Id == "c") && merged.Snippets.Count == 3);
    Check("merge: same result either way round", SnippetMerge.Merge(pcB, pcA).Snippets.First(x => x.Id == "a").Body == "new");

    string yaml = "matches:\n  - trigger: \":addr\"\n    replace: \"123 Example Street\"\n  - trigger: ;sig\n    label: Sign off\n    replace: |\n      Best,\n      Sam\n  - trigger: \":shell\"\n    replace: \"{{output}}\"\n    vars:\n      - name: output\n        type: shell\n  - regex: \"x\"\n    replace: no\n";
    var esp = SnippetImport.Read(yaml);
    Check("import espanso: simple and block text", esp.Any(x => x.Abbreviation == "addr" && x.Body == "123 Example Street") && esp.Any(x => x.Abbreviation == "sig" && x.Body == "Best,\nSam\n" && x.Label == "Sign off"),
        string.Join(" | ", esp.Select(x => x.Abbreviation + "=" + x.Body.Replace("\n", "\\n"))));
    Check("import espanso: regex matches skipped", esp.All(x => x.Abbreviation != "x"));
    var bee = SnippetImport.Read("{\"combos\":[{\"name\":\"Mail\",\"keyword\":\"!mail\",\"snippet\":\"me@example.com\",\"enabled\":true},{\"keyword\":\"off\",\"snippet\":\"x\",\"enabled\":false}]}");
    Check("import beeftext", bee.Count == 1 && bee[0].Abbreviation == "mail" && bee[0].Body == "me@example.com" && bee[0].Label == "Mail");
    var ours = SnippetImport.Read(System.Text.Json.JsonSerializer.Serialize(new SnippetLibraryFile { Snippets = { new() { Abbreviation = "a1", Body = "b1" }, new() { Abbreviation = "gone", Deleted = true } } }));
    Check("import our own export", ours.Count == 1 && ours[0].Abbreviation == "a1");
    Check("import junk is empty, never a crash", SnippetImport.Read("{not json").Count == 0 && SnippetImport.Read("random words").Count == 0);

    // Security: a snippet bomb can't build something huge
    var big = new string('x', 10_000);
    var bomb = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = new() { Body = string.Concat(Enumerable.Repeat("{snippet:b}", 100)) },
        ["b"] = new() { Body = string.Concat(Enumerable.Repeat("{snippet:c}", 100)) },
        ["c"] = new() { Body = big },
    };
    var swb = Stopwatch.StartNew();
    string blown = SnippetText.Render("{snippet:a}", none, clip, now, k => bomb.TryGetValue(k, out var v) ? v : null).Text;
    Check("security: nested snippet bomb is capped", blown.Length <= SnippetMatcher.MaxBody && blown.Length > 0 && swb.ElapsedMilliseconds < 2000, $"{blown.Length} chars in {swb.ElapsedMilliseconds} ms");
    string manyClips = SnippetText.Render(string.Concat(Enumerable.Repeat("{clipboard}", 1000)), none, () => new string('y', 100_000), now).Text;
    Check("security: repeated clipboard is capped", manyClips.Length <= SnippetMatcher.MaxBody, $"{manyClips.Length} chars");
    var importedRich = SnippetImport.Read(System.Text.Json.JsonSerializer.Serialize(new SnippetLibraryFile { Snippets = { new() { Abbreviation = "r", Body = "safe", Format = "rich", Html = "<a href=\"javascript:x\">evil</a>" } } }));
    Check("security: imported HTML is never kept", importedRich.Count == 1 && importedRich[0].Html == null && !importedRich[0].IsRich);

    // Two PCs sharing one sync folder
    string syncDir = Path.Combine(data, "sync");
    Directory.CreateDirectory(syncDir);
    var dataA = Path.Combine(data, "pcA"); var dataB = Path.Combine(data, "pcB");
    Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", dataA);
    var libA = new SnippetLibrary(new Settings { SnippetSyncFolder = syncDir });
    libA.Upsert(new Snippet { Abbreviation = "fromA", Body = "made on A" });
    Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", dataB);
    var libB = new SnippetLibrary(new Settings { SnippetSyncFolder = syncDir });
    Check("sync: second PC gets the first PC's snippets", libB.Matcher.Find("fromA")?.Body == "made on A");
    libB.Upsert(new Snippet { Abbreviation = "fromB", Body = "made on B" });
    var sw2 = Stopwatch.StartNew();
    while (libA.Matcher.Find("fromB") == null && sw2.ElapsedMilliseconds < 5000) Thread.Sleep(100);
    Check("sync: first PC picks up the change by itself", libA.Matcher.Find("fromB") != null, $"after {sw2.ElapsedMilliseconds} ms");
    Check("sync: each PC keeps a local copy too", File.Exists(Path.Combine(dataA, "snippets.json")) && File.Exists(Path.Combine(dataB, "snippets.json")));
    libA.Dispose(); libB.Dispose();
    Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", data);

    // A damaged file never stops the app
    string dataC = Path.Combine(data, "pcC");
    Directory.CreateDirectory(dataC);
    File.WriteAllText(Path.Combine(dataC, "snippets.json"), "{ broken");
    Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", dataC);
    var libC = new SnippetLibrary(new Settings());
    Check("damaged file: app still starts and keeps the broken copy", libC.Matcher != null && File.Exists(Path.Combine(dataC, "snippets.json.broken")));
    Environment.SetEnvironmentVariable("EMOJIANYWHERE_DATA", data);
}

try { Directory.Delete(data, true); } catch { }
Console.WriteLine($"RESULT pass={pass} fail={fail}");
return fail == 0 ? 0 : 1;
