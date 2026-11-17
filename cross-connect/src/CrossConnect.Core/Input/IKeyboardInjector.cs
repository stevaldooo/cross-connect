namespace CrossConnect.Core.Input;

/// <summary>
/// Contract for replaying keyboard events into the local input stream
/// (Client role).
/// </summary>
public interface IKeyboardInjector : IDisposable
{
    /// <summary>Injects a key press. Returns true when the OS accepted the input.</summary>
    bool InjectKeyDown(int virtualKeyCode);

    /// <summary>Injects a key release. Returns true when the OS accepted the input.</summary>
    bool InjectKeyUp(int virtualKeyCode);

    /// <summary>
    /// Releases every key this injector has pressed and not yet released.
    /// Must be called whenever the connection is lost or forwarding stops, so
    /// modifiers like Shift or Ctrl do not stay logically pressed forever.
    /// </summary>
    void ReleaseAllPressedKeys();

    /// <summary>Number of keys currently held down by this injector.</summary>
    int PressedKeyCount { get; }
}
