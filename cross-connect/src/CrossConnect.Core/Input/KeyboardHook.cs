using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Win32 low-level keyboard hook (WH_KEYBOARD_LL).
///
/// Events flagged LLKHF_INJECTED (produced by SendInput, i.e. by a client
/// replaying forwarded keys) are passed through unprocessed, so a machine
/// acting as both host and client can never re-capture its own injections.
/// </summary>
public sealed class KeyboardHook : LowLevelHookBase, IKeyboardHook
{
    private const int  WH_KEYBOARD_LL = 13;
    private const int  WM_KEYDOWN     = 0x0100;
    private const int  WM_KEYUP       = 0x0101;
    private const int  WM_SYSKEYDOWN  = 0x0104;
    private const int  WM_SYSKEYUP    = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint   VkCode;
        public uint   ScanCode;
        public uint   Flags;
        public uint   Time;
        public IntPtr ExtraInfo;
    }

    protected override int HookId => WH_KEYBOARD_LL;

    public Func<KeyEvent, bool>? ProcessKey { get; set; }

    protected override bool OnEvent(int msg, IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        bool injected = (data.Flags & LLKHF_INJECTED) != 0;

        bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isUp   = msg is WM_KEYUP   or WM_SYSKEYUP;

        if (injected || !(isDown || isUp))
            return false;

        var handler = ProcessKey;
        return handler?.Invoke(new KeyEvent((int)data.VkCode, isDown)) ?? false;
    }
}
