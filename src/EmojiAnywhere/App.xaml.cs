using System.Threading;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one copy at a time
        _single = new Mutex(true, "EmojiAnywhere.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        // The popup is tiny. Drawing it without the graphics card avoids the driver
        // reserving a big chunk of memory for nothing.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var settings = Settings.Load();
        var usage = new UsageStore();
        var index = new EmojiIndex(usage);
        var popup = new SuggestionPopup();
        var controller = new AutocompleteController(index, usage, settings, popup);

        _hook = new KeyboardHook(controller.OnKey, controller.OnClick);
        _tray = new TrayHost(settings, controller, Shutdown);

        // Keep the "start with Windows" entry pointing at this exe, even if it was moved.
        // Skipped for a custom data folder (portable copies and tests should never register).
        if (!AppPaths.IsCustomFolder) SettingsWindow.ApplyStartup(settings.StartWithWindows);

        if (!settings.FirstRunDone)
        {
            _tray.ShowWelcome();
            settings.FirstRunDone = true;
            settings.Save();
        }

        // Loading the emoji list leaves temporary data behind. Clean it up once things settle.
        MemoryTrim.Schedule(TimeSpan.FromSeconds(3));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
