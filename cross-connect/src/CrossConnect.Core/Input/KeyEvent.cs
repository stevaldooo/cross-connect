namespace CrossConnect.Core.Input;

/// <summary>
/// A single keyboard event captured by the hook or produced for injection.
/// Virtual-key codes are Win32 VK_* values (e.g. 0x41 = 'A' key), which are
/// independent of the active keyboard layout — the receiving side lets the OS
/// map them through its own layout.
/// </summary>
public sealed record KeyEvent(int VirtualKeyCode, bool IsDown);

/// <summary>
/// The subset of Win32 virtual-key codes CrossConnect needs by name.
/// </summary>
public static class VirtualKeyCodes
{
    public const int ScrollLock = 0x91; // VK_SCROLL — the forwarding toggle key
}
