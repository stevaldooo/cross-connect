using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Win32 SendInput-based keyboard injector.
///
/// Sends events as virtual-key codes and lets the receiving OS map them
/// through the local keyboard layout — the standard approach used by remote
/// input tools. Scancode-based injection (needed by some games that read
/// hardware scancodes) is a future refinement and does not change this API.
/// </summary>
public sealed class KeyboardInjector : IKeyboardInjector
{
    // ── Win32 interop ────────────────────────────────────────────────────────

    private const uint INPUT_KEYBOARD         = 1;
    private const uint KEYEVENTF_KEYUP        = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY  = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public uint    Vk;
        public uint    Scan;
        public uint    Flags;
        public uint    Time;
        public nuint   ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int    Dx;
        public int    Dy;
        public uint   MouseData;
        public uint   Flags;
        public uint   Time;
        public nuint  ExtraInfo;
    }

    // Explicit union mirroring the native INPUT struct; MOUSEINPUT is the
    // largest arm and keeps Marshal.SizeOf<INPUT>() at the value SendInput expects.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint     Type;
        public InputUnion U;

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT Mouse;
            [FieldOffset(0)] public KEYBDINPUT Ki;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── State ────────────────────────────────────────────────────────────────

    private readonly PressedKeyTracker _pressed = new();
    private readonly object _sendLock = new();
    private bool _disposed;

    public int PressedKeyCount => _pressed.SnapshotPressed().Count;

    // ── IKeyboardInjector ────────────────────────────────────────────────────

    public bool InjectKeyDown(int virtualKeyCode)
    {
        bool sent = SendKey(virtualKeyCode, keyUp: false);
        if (sent)
            _pressed.MarkPressedDown(virtualKeyCode);
        return sent;
    }

    public bool InjectKeyUp(int virtualKeyCode)
    {
        bool sent = SendKey(virtualKeyCode, keyUp: true);
        // Mark as released even if SendInput failed — retrying a phantom
        // release later would be worse than assuming the OS dropped the event.
        _pressed.MarkReleased(virtualKeyCode);
        return sent;
    }

    public void ReleaseAllPressedKeys()
    {
        foreach (int vk in _pressed.DrainAllPressed())
            SendKey(vk, keyUp: true);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private bool SendKey(int virtualKeyCode, bool keyUp)
    {
        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new INPUT.InputUnion
            {
                Ki = new KEYBDINPUT
                {
                    Vk    = (uint)virtualKeyCode,
                    Flags = (IsExtendedKey(virtualKeyCode) ? KEYEVENTF_EXTENDEDKEY : 0)
                            | (keyUp ? KEYEVENTF_KEYUP : 0)
                }
            }
        };

        lock (_sendLock)
        {
            return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
        }
    }

    /// <summary>
    /// Keys whose synthetic events must carry KEYEVENTF_EXTENDEDKEY to be
    /// interpreted correctly (right-hand modifiers, navigation block, numpad
    /// divide/enter). Matches the set documented for SendInput.
    /// </summary>
    private static bool IsExtendedKey(int vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 or            // PRIOR, NEXT, END, HOME
        0x25 or 0x26 or 0x27 or 0x28 or            // LEFT, UP, RIGHT, DOWN
        0x2D or 0x2E or                            // INSERT, DELETE
        0x5B or 0x5C or 0x5D or                    // LWIN, RWIN, APPS
        0xA3 or 0xA5 or                            // RCONTROL, RMENU
        0x6F or 0x8D or                            // NUMPAD_DIVIDE, NUMPAD_ENTER
        0xB5 or 0xB6 => true,                      // browser Back/Forward (X1/X2 style)
        _ => false
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAllPressedKeys();
    }
}
