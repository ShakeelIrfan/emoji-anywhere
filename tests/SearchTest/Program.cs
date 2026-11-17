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

    var lib = new SnippetLibrary();
    Check("snippet library: first run adds examples", lib.Snippets.Count == 3 && lib.Matcher.Find("shrug") != null);
    var mine = new Snippet { Abbreviation = "mail", Body = "me@example.com" };
    lib.Upsert(mine);
    Check("snippet library: new snippet works right away", lib.Matcher.Find("mail")?.Body == "me@example.com");
    lib.Delete(mine.Id);
    Check("snippet library: deleted snippet stops right away", lib.Matcher.Find("mail") == null);
    Thread.Sleep(300); // saves happen in the background
    var again = new SnippetLibrary();
    Check("snippet library: changes are saved", again.Matcher.Find("shrug") != null && again.Matcher.Find("mail") == null && again.Snippets.Count == 3);
}

try { Directory.Delete(data, true); } catch { }
Console.WriteLine($"RESULT pass={pass} fail={fail}");
return fail == 0 ? 0 : 1;
