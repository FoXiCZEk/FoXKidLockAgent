using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FoXKidLockAgent.Native;

public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private readonly HookProc _callback;
    private IntPtr _hook;
    private bool _enabled;
    public LowLevelKeyboardHook() => _callback = Callback;
    public void EnableHook()
    {
        _enabled = true;
        if (_hook != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(process.MainModule?.ModuleName), 0);
    }
    public void DisableHook() { _enabled = false; if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; } }
    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _enabled)
        {
            var key = (Keys)Marshal.ReadInt32(data);
            var alt = (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;
            var ctrl = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;
            if (key is Keys.LWin or Keys.RWin or Keys.Apps || alt && key is Keys.Tab or Keys.Escape or Keys.F4 || ctrl && key == Keys.Escape) return (IntPtr)1;
        }
        return CallNextHookEx(_hook, code, message, data);
    }
    public void Dispose() => DisableHook();
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? name);
}
