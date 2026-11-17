using System.Runtime.InteropServices;

namespace CrossConnect.Core.Input;

/// <summary>
/// Contract for replaying mouse events into the local input stream
/// (Client role).
/// </summary>
public interface IMouseInjector : IDisposable
{
    void InjectMoveRel(int dx, int dy);
    void InjectMoveAbs(int x, int y);
    void InjectButtonDown(int button);
    void InjectButtonUp(int button);
    void InjectWheel(int delta, bool horizontal);

    /// <summary>
    /// Releases every button this injector has pressed and not yet released —
    /// called when the connection drops so no button stays logically held.
    /// </summary>
    void ReleaseAllPressedButtons();

    int PressedButtonCount { get; }
}
