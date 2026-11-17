using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Win32 SendInput-based mouse injector.
///
/// Absolute moves take client-screen pixel coordinates and normalize them to
/// the SendInput 0..65535 range using the local primary screen size — so a
/// position reported in the client's own coordinate space lands on the same
/// spot here.
/// </summary>
public sealed class MouseInjector : IMouseInjector
{
    // ── Win32 interop ────────────────────────────────────────────────────────

    private const uint INPUT_MOUSE               = 0;
    private const uint MOUSEEVENTF_MOVE          = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN      = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP        = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN     = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP       = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN    = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP      = 0x0040;
    private const uint MOUSEEVENTF_XDOWN         = 0x0080;
    private const uint MOUSEEVENTF_XUP           = 0x0100;
    private const uint MOUSEEVENTF_WHEEL         = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL        = 0x1000;
    private const uint MOUSEEVENTF_ABSOLUTE      = 0x8000;

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
        public uint Type;
        public InputUnion U;

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT Mouse;
            [FieldOffset(0)] public KEYBDINPUT Keyboard;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public uint   Vk;
        public uint   Scan;
        public uint   Flags;
        public uint   Time;
        public nuint  ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ── State ────────────────────────────────────────────────────────────────

    private readonly PressedKeyTracker _pressedButtons = new();
    private readonly object _sendLock = new();
    private bool _disposed;

    public int PressedButtonCount => _pressedButtons.SnapshotPressed().Count;

    // ── IMouseInjector ───────────────────────────────────────────────────────

    public void InjectMoveRel(int dx, int dy)
        => Send(new MOUSEINPUT { Dx = dx, Dy = dy, Flags = MOUSEEVENTF_MOVE });

    public void InjectMoveAbs(int x, int y)
    {
        (int width, int height) = ScreenInterop.GetPrimaryScreenSize();
        // Normalize to the 0..65535 device range SendInput expects.
        int nx = (int)Math.Clamp((long)x * 65536 / Math.Max(width, 1), 0, 65535);
        int ny = (int)Math.Clamp((long)y * 65536 / Math.Max(height, 1), 0, 65535);
        Send(new MOUSEINPUT { Dx = nx, Dy = ny, Flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE });
    }

    public void InjectButtonDown(int button)
    {
        if (SendButton(button, isDown: true))
            _pressedButtons.MarkPressedDown(button);
    }

    public void InjectButtonUp(int button)
    {
        // Mark released even if SendInput failed — retrying a phantom release
        // later would be worse than assuming the OS dropped the event.
        _pressedButtons.MarkReleased(button);
        SendButton(button, isDown: false);
    }

    public void InjectWheel(int delta, bool horizontal)
        => Send(new MOUSEINPUT { MouseData = unchecked((uint)delta), Flags = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL });

    public void ReleaseAllPressedButtons()
    {
        foreach (int button in _pressedButtons.DrainAllPressed())
            SendButton(button, isDown: false);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static uint ButtonDownFlag(int button) => button switch
    {
        MouseCodes.Left   => MOUSEEVENTF_LEFTDOWN,
        MouseCodes.Right  => MOUSEEVENTF_RIGHTDOWN,
        MouseCodes.Middle => MOUSEEVENTF_MIDDLEDOWN,
        MouseCodes.X1 or MouseCodes.X2 => MOUSEEVENTF_XDOWN,
        _ => 0
    };

    private static uint ButtonUpFlag(int button) => button switch
    {
        MouseCodes.Left   => MOUSEEVENTF_LEFTUP,
        MouseCodes.Right  => MOUSEEVENTF_RIGHTUP,
        MouseCodes.Middle => MOUSEEVENTF_MIDDLEUP,
        MouseCodes.X1 or MouseCodes.X2 => MOUSEEVENTF_XUP,
        _ => 0
    };

    private bool SendButton(int button, bool isDown)
    {
        uint flag = isDown ? ButtonDownFlag(button) : ButtonUpFlag(button);
        if (flag == 0) return false;

        // X buttons carry their identity (1 or 2) in the hi-word of MouseData.
        uint data = flag is MOUSEEVENTF_XDOWN or MOUSEEVENTF_XUP
            ? (uint)(button == MouseCodes.X2 ? 2 : 1) << 16
            : 0;

        return Send(new MOUSEINPUT { MouseData = data, Flags = flag });
    }

    private bool Send(MOUSEINPUT mouseInput)
    {
        var input = new INPUT { Type = INPUT_MOUSE, U = new INPUT.InputUnion { Mouse = mouseInput } };

        lock (_sendLock)
        {
            return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAllPressedButtons();
    }
}
