using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace EmojiAnywhere.Core;

// The app sits idle in the tray almost all the time. A few seconds after the last popup,
// tidy up memory and hand unused pages back to Windows so it stays small.
public static class MemoryTrim
{
    private static DispatcherTimer? _timer;

    public static void Schedule(TimeSpan delay)
    {
        _timer ??= new DispatcherTimer(DispatcherPriority.Background);
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        _timer!.Stop();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        try { SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1); } catch { }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
}
