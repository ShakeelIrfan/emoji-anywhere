using System.IO;
using System.Text.Json;

namespace EmojiAnywhere.Core;

// Everything we save lives in %AppData%\EmojiAnywhere.
// We never save typed text. Only settings and which emojis were picked.
internal static class AppPaths
{
    public static bool IsCustomFolder => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EMOJIANYWHERE_DATA"));

    // True when installed from the Microsoft Store (MSIX). Windows then handles "start with Windows" itself.
    public static readonly bool IsPackaged = DetectPackage();
    private static bool DetectPackage()
    {
        try { int len = 0; return GetCurrentPackageFullName(ref len, null) != 15700; } // 15700 = APPMODEL_ERROR_NO_PACKAGE
        catch { return false; }
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    public static string Folder
    {
        get
        {
            // EMOJIANYWHERE_DATA lets you keep settings somewhere else, like a USB stick or a test folder
            string? custom = Environment.GetEnvironmentVariable("EMOJIANYWHERE_DATA");
            string dir = !string.IsNullOrWhiteSpace(custom)
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EmojiAnywhere");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static T Load<T>(string file) where T : new()
    {
        try
        {
            string path = Path.Combine(Folder, file);
            if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        }
        catch { /* a broken file should never stop the app */ }
        return new T();
    }

    public static void Save<T>(string file, T value)
    {
        try
        {
            string path = Path.Combine(Folder, file);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch { }
    }
}

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; } = true; // a tray helper is only useful if it is always running
    public bool FirstRunDone { get; set; } = false;
    public int MaxResults { get; set; } = 6;
    public double PopupScale { get; set; } = 1.0;

    // Process names without .exe
    public List<string> IgnoredApps { get; set; } = new()
    {
        "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "mintty", "putty", "OpenConsole"
    };

    // Apps that reject typed unicode and need the clipboard paste method
    public List<string> PasteModeApps { get; set; } = new();

    public static Settings Load() => AppPaths.Load<Settings>("settings.json").Sanitized();

    // A hand edited or damaged settings file can't break the app: values are pulled back into safe ranges
    private Settings Sanitized()
    {
        MaxResults = Math.Clamp(MaxResults, 1, 12);
        PopupScale = double.IsFinite(PopupScale) ? Math.Clamp(PopupScale, 0.7, 2.0) : 1.0;
        IgnoredApps = CleanList(IgnoredApps);
        PasteModeApps = CleanList(PasteModeApps);
        return this;
    }

    private static List<string> CleanList(List<string>? list) =>
        (list ?? new()).Where(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 100)
            .Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToList();

    // Adds or removes the "start with Windows" entry for the current user
    public static void ApplyStartup(bool on)
    {
        if (AppPaths.IsPackaged) return; // the Store version starts through its own startup task
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (on) key?.SetValue("EmojiAnywhere", $"\"{Environment.ProcessPath}\"");
            else key?.DeleteValue("EmojiAnywhere", false);
        }
        catch { }
    }
    public void Save() => AppPaths.Save("settings.json", this);
}

public sealed class UsageStore
{
    public sealed class Entry
    {
        public int Count { get; set; }
        public DateTime LastUsed { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries;

    public UsageStore() => _entries = AppPaths.Load<Dictionary<string, Entry>>("usage.json");

    public void Record(string hex)
    {
        if (!_entries.TryGetValue(hex, out var e)) _entries[hex] = e = new Entry();
        e.Count++;
        e.LastUsed = DateTime.UtcNow;
        // Copy now, write to disk in the background so the keyboard never waits on the disk
        var snapshot = _entries.ToDictionary(x => x.Key, x => new Entry { Count = x.Value.Count, LastUsed = x.Value.LastUsed });
        Task.Run(() => { lock (_saveLock) AppPaths.Save("usage.json", snapshot); });
    }

    private static readonly object _saveLock = new();

    public bool HasUsed(string hex) => _entries.ContainsKey(hex);

    // Small boost so favorites float up without beating a much better text match
    public double Bonus(string hex)
    {
        if (!_entries.TryGetValue(hex, out var e)) return 0;
        double freq = Math.Min(e.Count, 50) * 3;
        double hours = (DateTime.UtcNow - e.LastUsed).TotalHours;
        double recent = hours < 1 ? 40 : hours < 24 ? 25 : hours < 168 ? 10 : 0;
        return freq + recent;
    }
}
