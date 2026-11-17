namespace CrossConnect.Core.Input;

/// <summary>
/// Abstraction over local screen metrics and cursor control, so switching
/// logic can be tested without touching a real display.
/// </summary>
public interface IScreenService
{
    /// <summary>Primary screen size in physical pixels.</summary>
    (int Width, int Height) GetPrimaryScreenSize();

    bool TryGetCursorPos(out int x, out int y);
    bool TrySetCursorPos(int x, int y);
}

/// <summary>Real implementation backed by Win32 (ScreenInterop).</summary>
public sealed class ScreenService : IScreenService
{
    public (int Width, int Height) GetPrimaryScreenSize()
        => ScreenInterop.GetPrimaryScreenSize();

    public bool TryGetCursorPos(out int x, out int y)
        => ScreenInterop.TryGetCursorPos(out x, out y);

    public bool TrySetCursorPos(int x, int y)
        => ScreenInterop.TrySetCursorPos(x, y);
}
