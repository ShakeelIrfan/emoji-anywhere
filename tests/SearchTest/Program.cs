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

try { Directory.Delete(data, true); } catch { }
Console.WriteLine($"RESULT pass={pass} fail={fail}");
return fail == 0 ? 0 : 1;
