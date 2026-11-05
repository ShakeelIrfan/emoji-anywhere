using System.Runtime.InteropServices;
using System.Text;
using EmojiAnywhere.Native;

namespace EmojiAnywhere.Input;

public readonly record struct KeyPress(int Vk, char? Char, bool Ctrl, bool Alt, bool Win);

// Global low level keyboard hook on its own thread.
// Windows waits for this callback before every keypress reaches any app, so it must never be
// blocked by drawing or other slow work. That is why it does not run on the UI thread.
// Return true from the handler to swallow the key so the focused app never sees it.
public sealed class KeyboardHook : IDisposable
{
    private readonly Func<KeyPress, bool> _handler;
    private readonly Action _onClick;
    private readonly Thread _thread;
    private Win32.LowLevelKeyboardProc? _proc, _mouseProc; // keep references so the GC never frees them
    private IntPtr _hook, _mouseHook;
    private uint _threadId;
    private readonly byte[] _state = new byte[256];
    private readonly StringBuilder _buf = new(8);

    public KeyboardHook(Func<KeyPress, bool> handler, Action onClick)
    {
        _handler = handler;
        _onClick = onClick;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Keyboard hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait();
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Could not install keyboard hook");
    }

    private void Loop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _proc = Callback;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        // Mouse clicks only tell us "the cursor may have moved, start fresh". Nothing else is read.
        _mouseProc = MouseCallback;
        _mouseHook = Win32.SetWindowsHookEx(14 /* WH_MOUSE_LL */, _mouseProc, Win32.GetModuleHandle(null), 0);
        ready.Set();
        if (_hook == IntPtr.Zero) return;

        // The hook only fires while this thread pumps messages
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        Win32.UnhookWindowsHookEx(_hook);
        if (_mouseHook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_mouseHook);
        _hook = _mouseHook = IntPtr.Zero;
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Button down messages only: left 0x201, right 0x204, middle 0x207. Moves are ignored instantly.
        if (nCode >= 0 && (wParam == 0x201 || wParam == 0x204 || wParam == 0x207))
        {
            try { _onClick(); } catch { }
        }
        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == Win32.WM_KEYDOWN || wParam == Win32.WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            // Skip only the keys we send ourselves. Keys from on-screen keyboards and
            // other input tools still count, so they work with the popup too.
            if (info.dwExtraInfo != Win32.OurMarker)
            {
                bool ctrl = Win32.IsDown(Win32.VK_CONTROL), alt = Win32.IsDown(Win32.VK_MENU);
                bool win = Win32.IsDown(Win32.VK_LWIN) || Win32.IsDown(Win32.VK_RWIN);
                try
                {
                    var press = new KeyPress((int)info.vkCode, ToChar(info, ctrl, alt), ctrl, alt, win);
                    if (_handler(press)) return (IntPtr)1;
                }
                catch { /* never let a bug here break the user's keyboard */ }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private char? ToChar(Win32.KBDLLHOOKSTRUCT info, bool ctrl, bool alt)
    {
        // On-screen keyboards and some tools send the character itself (VK_PACKET)
        if (info.vkCode == 0xE7) return (char)info.scanCode;

        Array.Clear(_state);
        if (Win32.IsDown(Win32.VK_SHIFT)) _state[Win32.VK_SHIFT] = 0x80;
        if (ctrl) _state[Win32.VK_CONTROL] = 0x80;
        if (alt) _state[Win32.VK_MENU] = 0x80;
        if ((Win32.GetKeyState(Win32.VK_CAPITAL) & 1) != 0) _state[Win32.VK_CAPITAL] = 0x01;

        uint thread = Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out _);
        IntPtr layout = Win32.GetKeyboardLayout(thread);
        _buf.Clear();
        // Flag 0x4: do not change the keyboard state, so dead keys keep working in the app
        int n = Win32.ToUnicodeEx(info.vkCode, info.scanCode, _state, _buf, _buf.Capacity, 0x4, layout);
        if (n != 1) return null;
        char ch = _buf[0];
        // Esc, Backspace and friends come back as invisible control characters. They are not typing.
        if (ch < 0x20 && ch != '\r' && ch != '\t' && ch != '\n') return null;
        return ch;
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
