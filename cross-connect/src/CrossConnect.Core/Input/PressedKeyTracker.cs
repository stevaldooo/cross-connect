namespace CrossConnect.Core.Input;

/// <summary>
/// Thread-safe set of keys currently held down. Both sides of the forwarding
/// pipeline track pressed keys for the same reason: when forwarding stops or
/// the connection drops, every tracked key must receive a synthetic KEY_UP so
/// no key stays logically pressed on the receiving computer.
///
/// The host uses it to know which KEY_UP messages to send when forwarding is
/// disabled; the client uses it to know which keys to release locally when
/// the connection is lost.
/// </summary>
public sealed class PressedKeyTracker
{
    private readonly object _lock = new();
    private readonly HashSet<int> _pressed = new();

    /// <summary>Records that a key is being held down. Duplicate downs (key repeats) are ignored.</summary>
    public void MarkPressedDown(int virtualKeyCode)
    {
        lock (_lock)
        {
            _pressed.Add(virtualKeyCode);
        }
    }

    /// <summary>Records that a key was released. Releasing an untracked key is a no-op.</summary>
    public void MarkReleased(int virtualKeyCode)
    {
        lock (_lock)
        {
            _pressed.Remove(virtualKeyCode);
        }
    }

    /// <summary>Returns a snapshot of the currently pressed keys without clearing them.</summary>
    public IReadOnlyList<int> SnapshotPressed()
    {
        lock (_lock)
        {
            return _pressed.ToArray();
        }
    }

    /// <summary>
    /// Atomically returns every pressed key and clears the tracker.
    /// Used when all pressed keys are about to be force-released.
    /// </summary>
    public IReadOnlyList<int> DrainAllPressed()
    {
        lock (_lock)
        {
            var pressed = _pressed.ToArray();
            _pressed.Clear();
            return pressed;
        }
    }
}
