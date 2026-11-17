namespace CrossConnect.Core.Input;

/// <summary>
/// Contract for the low-level keyboard capture component (Host role).
///
/// The hook observes every key event on the local machine, including events
/// destined for other applications, which is what allows the host to intercept
/// keys and forward them instead of applying them locally.
/// </summary>
public interface IKeyboardHook : IDisposable
{
    /// <summary>
    /// Assigned by the owner of the hook. Invoked on the hook thread for every
    /// non-injected key event. Return true to swallow the event (prevent local
    /// processing); return false to let it continue to local applications.
    /// Must return quickly — Windows removes hooks that stall too long.
    /// </summary>
    Func<KeyEvent, bool>? ProcessKey { get; set; }

    /// <summary>Installs the hook. Must be called before any events arrive.</summary>
    void Install();

    /// <summary>Removes the hook and stops the event thread.</summary>
    void Uninstall();

    bool IsInstalled { get; }
}
