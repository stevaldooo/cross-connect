using CrossConnect.Core.Networking;
using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Input;

/// <summary>
/// Client-side controller for remote input (M3+M4+M5).
///
/// Passive: injects keyboard/mouse events received from the host. This matches
/// the moment the host forwards input while the cursor is on this machine.
///
/// Controlled: entered when SWITCH_DEVICE (ToClient=true) arrives. The client's
/// cursor jumps to the mapped entry position, local keyboard and mouse are
/// hooked and swallowed (the physical devices on this machine must not fight
/// the remote input), and received events are injected locally.
///
/// Leaving controlled mode — SWITCH_DEVICE (ToClient=false) or a dropped
/// connection — releases every key and button injected so nothing stays
/// logically pressed, then unhooks the local devices.
///
/// Escape hatch: Scroll Lock on the client raises ForceSwitchBackRequested;
/// the UI sends SWITCH_DEVICE (ToClient=false) to the host.
/// </summary>
public sealed class InputReceivingController : IDisposable
{
    private readonly ICrossConnectClient _client;
    private readonly IKeyboardHook _keyboardHook;
    private readonly IMouseHook _mouseHook;
    private readonly IScreenService _screen;
    private readonly IKeyboardInjector _keyboardInjector;
    private readonly IMouseInjector _mouseInjector;
    private readonly bool _ownsKeyboardInjector;
    private readonly bool _ownsMouseInjector;
    private readonly object _gate = new();
    private bool _disposed;

    public InputReceivingController(
        ICrossConnectClient client,
        IKeyboardHook keyboardHook,
        IMouseHook mouseHook,
        IScreenService screen,
        IKeyboardInjector? keyboardInjector = null,
        IMouseInjector? mouseInjector = null)
    {
        _client       = client;
        _keyboardHook = keyboardHook;
        _mouseHook    = mouseHook;
        _screen       = screen;

        _keyboardInjector = keyboardInjector ?? new KeyboardInjector();
        _mouseInjector    = mouseInjector ?? new MouseInjector();
        _ownsKeyboardInjector = keyboardInjector is null;
        _ownsMouseInjector    = mouseInjector is null;

        _keyboardHook.ProcessKey = HandleLocalKey;
        _mouseHook.ProcessMouse  = HandleLocalMouse;

        _client.MessageReceived += OnMessageReceived;
        _client.Disconnected    += OnDisconnected;
    }

    // ── Events ───────────────────────────────────────────────────────────────

    public event Action<bool>? ControlledChanged;
    public event Action? ForceSwitchBackRequested;
    public event Action<string>? StateMessage;

    public bool IsControlled { get; private set; }

    // ── Network events ───────────────────────────────────────────────────────

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        switch (e.Message)
        {
            case SwitchDeviceMessage s:
                if (s.ToClient) EnterControlled(s.X, s.Y);
                else            ExitControlled();
                break;

            case KeyDownMessage down:
                if (IsControlled) _keyboardInjector.InjectKeyDown(down.KeyCode);
                break;

            case KeyUpMessage up:
                if (IsControlled) _keyboardInjector.InjectKeyUp(up.KeyCode);
                break;

            case MouseMoveAbsMessage abs:
                if (IsControlled) _mouseInjector.InjectMoveAbs(abs.X, abs.Y);
                break;

            case MouseMoveRelMessage rel:
                if (IsControlled) _mouseInjector.InjectMoveRel(rel.Dx, rel.Dy);
                break;

            case MouseButtonMessage button:
                if (IsControlled)
                {
                    if (button.IsDown) _mouseInjector.InjectButtonDown(button.Button);
                    else               _mouseInjector.InjectButtonUp(button.Button);
                }
                break;

            case MouseScrollMessage scroll:
                if (IsControlled) _mouseInjector.InjectWheel(scroll.Delta, scroll.IsHorizontal);
                break;
        }
    }

    private void OnDisconnected(object? sender, ConnectionEventArgs e)
        => ExitControlled();

    // ── State transitions ────────────────────────────────────────────────────

    private void EnterControlled(int entryX, int entryY)
    {
        List<Action> events;
        lock (_gate)
        {
            if (_disposed || IsControlled) return;
            IsControlled = true;

            // Jump the cursor to where the host thinks it should appear. The
            // coordinates are already in this screen's pixel space.
            if (entryX >= 0 && entryY >= 0)
                _screen.TrySetCursorPos(entryX, entryY);

            try
            {
                _keyboardHook.Install();
                _mouseHook.Install();
            }
            catch (Exception ex)
            {
                IsControlled = false;
                events = new List<Action>
                {
                    () => StateMessage?.Invoke($"Could not install local hooks: {ex.Message}")
                };
                Raise(events);
                return;
            }

            events = new List<Action>
            {
                () => ControlledChanged?.Invoke(true),
                () => StateMessage?.Invoke(
                    "Remote control active — your keyboard and mouse feed the remote session. Scroll Lock takes control back.")
            };
        }
        Raise(events);
    }

    private void ExitControlled()
    {
        List<Action> events;
        lock (_gate)
        {
            if (!IsControlled) return;
            IsControlled = false;

            // A dropped socket must never leave keys or buttons logically held.
            _keyboardInjector.ReleaseAllPressedKeys();
            _mouseInjector.ReleaseAllPressedButtons();

            _mouseHook.Uninstall();
            _keyboardHook.Uninstall();

            events = new List<Action>
            {
                () => ControlledChanged?.Invoke(false),
                () => StateMessage?.Invoke("Remote control ended — local input restored.")
            };
        }
        Raise(events);
    }

    // ── Local input hooks (swallow everything while controlled) ──────────────

    private bool HandleLocalKey(KeyEvent e)
    {
        lock (_gate)
        {
            if (!IsControlled)
                return false;

            if (e.IsDown && e.VirtualKeyCode == VirtualKeyCodes.ScrollLock)
                ForceSwitchBackRequested?.Invoke();

            return true; // swallow all local keys while controlled
        }
    }

    private bool HandleLocalMouse(MouseEvent e)
    {
        lock (_gate)
            return IsControlled; // swallow all local mouse input while controlled
    }

    // ── Event dispatch ───────────────────────────────────────────────────────

    /// <summary>
    /// Raised outside the lock. Note ForceSwitchBackRequested fires on the
    /// keyboard hook thread — the UI handler must marshal and must not block
    /// on this controller's lock.
    /// </summary>
    private static void Raise(List<Action> events)
    {
        foreach (var e in events) e();
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _client.MessageReceived -= OnMessageReceived;
        _client.Disconnected    -= OnDisconnected;

        // Whether or not we were controlled, make sure nothing is left pressed.
        ExitControlled();

        _keyboardInjector.ReleaseAllPressedKeys();
        _mouseInjector.ReleaseAllPressedButtons();

        _keyboardHook.Uninstall();
        _mouseHook.Uninstall();
        _keyboardHook.Dispose();
        _mouseHook.Dispose();

        _keyboardHook.ProcessKey = null;
        _mouseHook.ProcessMouse  = null;

        if (_ownsKeyboardInjector) _keyboardInjector.Dispose();
        if (_ownsMouseInjector)    _mouseInjector.Dispose();
    }
}
