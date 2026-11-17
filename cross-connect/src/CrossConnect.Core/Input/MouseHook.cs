using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Win32 low-level mouse hook (WH_MOUSE_LL).
///
/// Injected events (LLMHF_INJECTED — produced by a client replaying forwarded
/// input) are passed through unprocessed so a dual-role machine never
/// re-captures its own injections.
/// </summary>
public sealed class MouseHook : LowLevelHookBase, IMouseHook
{
    private const int  WH_MOUSE_LL = 14;
    private const uint LLMHF_INJECTED = 0x00000001;

    private const int WM_MOUSEMOVE      = 0x0200;
    private const int WM_LBUTTONDOWN    = 0x0201;
    private const int WM_LBUTTONUP      = 0x0202;
    private const int WM_RBUTTONDOWN    = 0x0204;
    private const int WM_RBUTTONUP      = 0x0205;
    private const int WM_MBUTTONDOWN    = 0x0207;
    private const int WM_MBUTTONUP      = 0x0208;
    private const int WM_MOUSEWHEEL     = 0x020A;
    private const int WM_XBUTTONDOWN    = 0x020B;
    private const int WM_XBUTTONUP      = 0x020C;
    private const int WM_MOUSEHWHEEL    = 0x020E;

    private const ushort HIWORD_XBUTTON1 = 1;
    private const ushort HIWORD_XBUTTON2 = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT  Pt;
        public uint   MouseData;
        public uint   Flags;
        public uint   Time;
        public IntPtr ExtraInfo;
    }

    protected override int HookId => WH_MOUSE_LL;

    public Func<MouseEvent, bool>? ProcessMouse { get; set; }

    protected override bool OnEvent(int msg, IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if ((data.Flags & LLMHF_INJECTED) != 0)
            return false;

        MouseEvent? ev = msg switch
        {
            WM_MOUSEMOVE   => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.Move),
            WM_LBUTTONDOWN => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonDown, MouseCodes.Left),
            WM_LBUTTONUP   => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonUp,   MouseCodes.Left),
            WM_RBUTTONDOWN => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonDown, MouseCodes.Right),
            WM_RBUTTONUP   => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonUp,   MouseCodes.Right),
            WM_MBUTTONDOWN => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonDown, MouseCodes.Middle),
            WM_MBUTTONUP   => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonUp,   MouseCodes.Middle),
            WM_XBUTTONDOWN => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonDown, XButtonFrom(data.MouseData)),
            WM_XBUTTONUP   => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.ButtonUp,   XButtonFrom(data.MouseData)),
            WM_MOUSEWHEEL  => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.Wheel,           WheelDelta: ToSignedDelta(data.MouseData)),
            WM_MOUSEHWHEEL => new MouseEvent(data.Pt.X, data.Pt.Y, MouseEventType.HorizontalWheel, WheelDelta: ToSignedDelta(data.MouseData)),
            _              => null
        };

        if (ev is null)
            return false;

        var handler = ProcessMouse;
        return handler?.Invoke(ev) ?? false;
    }

    private static int XButtonFrom(uint mouseData)
        => (ushort)(mouseData >> 16) == HIWORD_XBUTTON2 ? MouseCodes.X2 : MouseCodes.X1;

    /// <summary>Wheel deltas travel in the hi-word of MouseData.</summary>
    private static int ToSignedDelta(uint mouseData)
        => unchecked((short)(mouseData >> 16));
}
