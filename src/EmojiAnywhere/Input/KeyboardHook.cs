using System.Runtime.InteropServices;
using System.Text;
using EmojiAnywhere.Native;

namespace EmojiAnywhere.Input;

public readonly record struct KeyPress(int Vk, char? Char, bool Ctrl, bool Alt, bool Win);

// Global low level keyboard hook. Runs on the UI thread, so the handler must be fast.
// Return true from the handler to swallow the key so the focused app never sees it.
public sealed class KeyboardHook : IDisposable
{
    private readonly Win32.LowLevelKeyboardProc _proc; // keep a reference so the GC never frees it
    private readonly Func<KeyPress, bool> _handler;
    private IntPtr _hook;
    private readonly byte[] _state = new byte[256];
    private readonly StringBuilder _buf = new(8);

    public KeyboardHook(Func<KeyPress, bool> handler)
    {
        _handler = handler;
        _proc = Callback;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Could not install keyboard hook");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == Win32.WM_KEYDOWN || wParam == Win32.WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            // Ignore keys we (or other tools) inject, so our own typing never loops back
            if ((info.flags & Win32.LLKHF_INJECTED) == 0)
            {
                bool ctrl = Win32.IsDown(Win32.VK_CONTROL), alt = Win32.IsDown(Win32.VK_MENU);
                bool win = Win32.IsDown(Win32.VK_LWIN) || Win32.IsDown(Win32.VK_RWIN);
                var press = new KeyPress((int)info.vkCode, ToChar(info, ctrl, alt), ctrl, alt, win);
                try
                {
                    if (_handler(press)) return (IntPtr)1;
                }
                catch { /* never let a bug here break the user's keyboard */ }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private char? ToChar(Win32.KBDLLHOOKSTRUCT info, bool ctrl, bool alt)
    {
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
        return n == 1 ? _buf[0] : null;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
