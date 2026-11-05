using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EmojiAnywhere.Core;
using EmojiAnywhere.Input;
using EmojiAnywhere.UI;

namespace EmojiAnywhere;

// Entry point. Lives in the tray, no main window.
public partial class App : Application
{
    private Mutex? _single;
    private KeyboardHook? _hook;
    private TrayHost? _tray;
    private bool _showingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Problems get a clear message instead of the app silently vanishing
        DispatcherUnhandledException += (_, args) => { args.Handled = true; ShowCrash(); };
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        // Only one copy at a time
        _single = new Mutex(true, "EmojiAnywhere.SingleInstance", out bool first);
        if (!first)
        {
            NoticeWindow.Show("😉", "Emoji Anywhere is already running",
                "Look for the :) icon in your system tray, next to the clock. Click it for settings.", "Got it");
            Shutdown();
            return;
        }

        // The popup is tiny. Drawing it without the graphics card avoids the driver
        // reserving a big chunk of memory for nothing.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var settings = Settings.Load();
        var usage = new UsageStore();
        var index = new EmojiIndex(usage);
        var popup = new SuggestionPopup();
        var controller = new AutocompleteController(index, usage, settings, popup);

        try
        {
            _hook = new KeyboardHook(controller.OnKey, controller.OnClick);
        }
        catch (InvalidOperationException)
        {
            NoticeWindow.Show("🔒", "Emoji Anywhere can't see the keyboard",
                "Windows did not let it listen for the colon key. A security or anti-cheat app may be blocking it. Try restarting your PC, or allow Emoji Anywhere in that app.",
                "Quit", reportLink: true);
            Shutdown();
            return;
        }
        _tray = new TrayHost(settings, controller, Shutdown);

        // Keep the "start with Windows" entry pointing at this exe, even if it was moved.
        // Skipped for a custom data folder (portable copies and tests should never register).
        if (!AppPaths.IsCustomFolder) Settings.ApplyStartup(settings.StartWithWindows);

        if (!settings.FirstRunDone)
        {
            _tray.ShowWelcome();
            settings.FirstRunDone = true;
            settings.Save();
        }

        // Loading the emoji list leaves temporary data behind. Clean it up once things settle.
        MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
    }

    private void ShowCrash()
    {
        if (_showingError) return; // one message at a time, never a pile of them
        _showingError = true;
        string choice = NoticeWindow.Show("😵", "Something went wrong",
            "Emoji Anywhere hit a problem it didn't expect. Restarting it usually fixes this. Nothing was sent anywhere.",
            "Restart", "Quit", reportLink: true);
        if (choice == "Restart")
        {
            _hook?.Dispose();
            _single?.Dispose();
            _single = null;
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }); } catch { }
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
