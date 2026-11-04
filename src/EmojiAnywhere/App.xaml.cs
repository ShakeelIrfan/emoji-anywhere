using System.Windows;

namespace EmojiAnywhere;

// Entry point. Lives in the tray, no main window.
// The keyboard hook, popup and tray menu get wired up here as they are built.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
    }
}
