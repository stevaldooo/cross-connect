namespace CrossConnect.Core.Input;

/// <summary>Kind of mouse event captured by the hook or delivered for injection.</summary>
public enum MouseEventType
{
    Move,
    ButtonDown,
    ButtonUp,
    Wheel,
    HorizontalWheel
}

/// <summary>
/// A single mouse event. For moves, X/Y are screen coordinates in pixels.
/// For wheel events, WheelDelta follows the Win32 convention (±120 per notch).
/// Button uses MouseCodes values; zero for non-button events.
/// </summary>
public sealed record MouseEvent(
    int X,
    int Y,
    MouseEventType Type,
    int Button = 0,
    int WheelDelta = 0);

/// <summary>Win32 mouse button identifiers used across the protocol and injectors.</summary>
public static class MouseCodes
{
    public const int Left   = 1;
    public const int Right  = 2;
    public const int Middle = 3;
    public const int X1     = 4;
    public const int X2     = 5;
}

/// <summary>
/// Contract for the low-level mouse capture component (Host role).
/// </summary>
public interface IMouseHook : IDisposable
{
    /// <summary>
    /// Assigned by the owner. Invoked on the hook thread for every non-injected
    /// mouse event. Return true to swallow the event; false to pass it through.
    /// Must return quickly.
    /// </summary>
    Func<MouseEvent, bool>? ProcessMouse { get; set; }

    void Install();
    void Uninstall();
    bool IsInstalled { get; }
}
