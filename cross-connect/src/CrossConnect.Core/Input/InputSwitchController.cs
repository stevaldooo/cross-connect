using CrossConnect.Core.Protocol;

namespace CrossConnect.Core.Input;

/// <summary>
/// Host-side controller for screen-edge switching (M5) — the brain that ties
/// keyboard and mouse forwarding together with automatic control transfer.
///
/// States:
///   Armed      — the keyboard hook watches passively (Scroll Lock only); a
///                poll loop checks whether the local cursor parks on the
///                configured edge. A short dwell transfers control.
///   Forwarding — keyboard and mouse are swallowed locally and forwarded;
///                physical deltas drive a VirtualCursor in the client's screen
///                space (MOUSE_MOVE_ABS). When the virtual cursor reaches the
///                client's opposite edge — or the user presses Scroll Lock, or
///                the client requests control back — control returns to the
///                host and the cursor reappears at the mapped position.
///
/// Because the host computes the client cursor entirely, the client only
/// injects absolute positions and needs no switching logic of its own.
///
/// Concurrency design:
/// - All state transitions run under _gate; events are collected and raised
///   AFTER the lock is released so a subscriber that marshals to the UI can
///   never deadlock against this lock.
/// - Hook Install/Uninstall run on a single serialized operations queue. This
///   keeps ordering (off-then-on cannot interleave) and lets a hook-thread
///   callback end forwarding without ever joining its own thread, which would
///   stall input for up to two seconds.
///
/// Escape hatches, in order of escalation:
///   Scroll Lock on the host  — force a switch in either direction.
///   Scroll Lock on the client — arrives as SWITCH_DEVICE (ToClient=false).
///   Disconnect — ClientDisconnected ends forwarding safely.
/// </summary>
public sealed class InputSwitchController : IDisposable
{
    private readonly IKeyboardHook _keyboardHook;
    private readonly IMouseHook _mouseHook;
    private readonly IScreenService _screen;
    private readonly Func<BaseMessage, Task> _sendToClient;
    private readonly int _pollIntervalMs;

    private readonly EdgeGate _enterGate = new(requiredSamples: 3);
    private readonly EdgeGate _exitGate  = new(requiredSamples: 4);
    private readonly VirtualCursor _virtualCursor = new();
    private readonly PressedKeyTracker _pressedKeys    = new();
    private readonly PressedKeyTracker _pressedButtons = new();
    private readonly object _gate = new();

    private CancellationTokenSource? _pollCts;
    private (int Width, int Height)? _clientScreen;
    private bool _hasLastHostPos;
    private int  _lastHostX, _lastHostY;

    // Serialized hook operations — see class doc.
    private Task _hookOps = Task.CompletedTask;

    private bool _disposed;

    public InputSwitchController(
        IKeyboardHook keyboardHook,
        IMouseHook mouseHook,
        IScreenService screen,
        Func<BaseMessage, Task> sendToClient,
        int pollIntervalMs = 15)
    {
        _keyboardHook   = keyboardHook;
        _mouseHook      = mouseHook;
        _screen         = screen;
        _sendToClient   = sendToClient;
        _pollIntervalMs = pollIntervalMs;

        _keyboardHook.ProcessKey = HandleKey;
        _mouseHook.ProcessMouse  = HandleMouse;
    }

    // ── Events (always raised outside _gate) ─────────────────────────────────

    public event Action<bool>? ForwardingChanged;
    public event Action<bool>? ArmedChanged;
    public event Action<string>? StateMessage;

    // ── Observable state ─────────────────────────────────────────────────────

    public ScreenArrangement Arrangement { get; set; } = ScreenArrangement.ClientRight;
    public bool IsArmed      { get; private set; }
    public bool IsForwarding { get; private set; }
    public string? ClientEndpoint { get; private set; }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Begins watching the configured screen edge.</summary>
    public void Arm()
    {
        List<Action> events;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsArmed || IsForwarding)
                return;

            events = SetArmedLocked(true);
        }
        Raise(events);
    }

    /// <summary>Stops watching. If currently forwarding, control returns to the host.</summary>
    public void Disarm()
    {
        List<Action> events;
        lock (_gate)
        {
            if (IsForwarding)
                events = EndForwardingLocked(switchBack: true, disarmAfter: true);
            else if (IsArmed)
                events = SetArmedLocked(false);
            else
                return;
        }
        Raise(events);
    }

    /// <summary>Records the client's screen size (from SCREEN_INFO).</summary>
    public void SetClientScreen(string endpoint, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        List<Action> events;
        lock (_gate)
        {
            ClientEndpoint = endpoint;
            _clientScreen  = (width, height);
            events = new List<Action>
            {
                () => StateMessage?.Invoke($"Client ready ({width}×{height}) — edge switching active.")
            };
        }
        Raise(events);
    }

    /// <summary>
    /// Called when the client connection drops. Ends forwarding without a
    /// switch-back message (the socket is gone) and disarms.
    /// </summary>
    public void ClientDisconnected(string endpoint)
    {
        List<Action> events;
        lock (_gate)
        {
            if (ClientEndpoint is not null && ClientEndpoint != endpoint)
                return; // a stale client we are not tracking

            ClientEndpoint = null;
            _clientScreen  = null;

            if (IsForwarding)
                events = EndForwardingLocked(switchBack: false, disarmAfter: true);
            else if (IsArmed)
                events = SetArmedLocked(false);
            else
                return;
        }
        Raise(events);
    }

    /// <summary>Processes SWITCH_DEVICE messages received from the client.</summary>
    public void HandleMessage(BaseMessage message)
    {
        if (message is not SwitchDeviceMessage { ToClient: false })
            return;

        List<Action> events;
        lock (_gate)
        {
            if (!IsForwarding)
                return;
            // Client pressed Scroll Lock — it wants control back.
            events = EndForwardingLocked(switchBack: false, disarmAfter: false);
        }
        Raise(events);
    }

    // ── Key pipeline (keyboard hook thread) ──────────────────────────────────

    private bool HandleKey(KeyEvent e)
    {
        List<Action> events;

        lock (_gate)
        {
            // Scroll Lock is the universal manual toggle; swallowed in every state.
            if (e.IsDown && e.VirtualKeyCode == VirtualKeyCodes.ScrollLock)
            {
                if (IsForwarding)
                    events = EndForwardingLocked(switchBack: true, disarmAfter: false);
                else if (IsArmed && CanForwardLocked())
                    events = BeginForwardingLocked();
                else
                    return true; // swallow even when nothing to do
                RaiseOnHookThread(events);
                return true;
            }

            if (!IsForwarding)
                return false; // armed/idle: keys flow to local applications

            if (e.IsDown)
                _pressedKeys.MarkPressedDown(e.VirtualKeyCode);
            else
                _pressedKeys.MarkReleased(e.VirtualKeyCode);

            BaseMessage message = e.IsDown
                ? new KeyDownMessage { KeyCode = e.VirtualKeyCode }
                : new KeyUpMessage   { KeyCode = e.VirtualKeyCode };

            _ = SendBestEffortAsync(message);
            return true;
        }
    }

    // ── Mouse pipeline (mouse hook thread) ───────────────────────────────────

    private bool HandleMouse(MouseEvent e)
    {
        lock (_gate)
        {
            if (!IsForwarding)
                return false; // hook is only installed while forwarding

            switch (e.Type)
            {
                case MouseEventType.Move:
                    if (_hasLastHostPos)
                    {
                        (int cw, int ch) = _clientScreen!.Value;
                        _virtualCursor.ApplyDelta(e.X - _lastHostX, e.Y - _lastHostY, cw, ch);
                        _ = SendBestEffortAsync(new MouseMoveAbsMessage { X = _virtualCursor.X, Y = _virtualCursor.Y });

                        if (_exitGate.Record(_virtualCursor.IsAtReturnEdge(Arrangement, cw)))
                        {
                            var events = EndForwardingLocked(switchBack: true, disarmAfter: false);
                            RaiseOnHookThread(events);
                        }
                    }
                    _lastHostX = e.X;
                    _lastHostY = e.Y;
                    _hasLastHostPos = true;
                    break;

                case MouseEventType.ButtonDown:
                    _pressedButtons.MarkPressedDown(e.Button);
                    _ = SendBestEffortAsync(new MouseButtonMessage { Button = e.Button, IsDown = true });
                    break;

                case MouseEventType.ButtonUp:
                    _pressedButtons.MarkReleased(e.Button);
                    _ = SendBestEffortAsync(new MouseButtonMessage { Button = e.Button, IsDown = false });
                    break;

                case MouseEventType.Wheel:
                    _ = SendBestEffortAsync(new MouseScrollMessage { Delta = e.WheelDelta, IsHorizontal = false });
                    break;

                case MouseEventType.HorizontalWheel:
                    _ = SendBestEffortAsync(new MouseScrollMessage { Delta = e.WheelDelta, IsHorizontal = true });
                    break;
            }

            return true;
        }
    }

    // ── Transitions (must be called while holding _gate) ─────────────────────
    // Each returns the events to raise after the lock is released.

    private bool CanForwardLocked() => ClientEndpoint is not null && _clientScreen is not null;

    private List<Action> BeginForwardingLocked()
    {
        if (!CanForwardLocked())
            return new List<Action>();

        (int _, int hostH) = _screen.GetPrimaryScreenSize();
        if (!_screen.TryGetCursorPos(out int hostX, out int hostY))
            return new List<Action>();

        (int cw, int ch) = _clientScreen!.Value;
        (int entryX, int entryY) = CoordinateMapper.ClientEntry(Arrangement, hostY, hostH, cw, ch);

        IsArmed      = false;
        IsForwarding = true;
        _virtualCursor.SetPosition(entryX, entryY);
        _lastHostX = hostX;
        _lastHostY = hostY;
        _hasLastHostPos = true;
        _enterGate.Reset();
        _exitGate.Reset();

        EnqueueHookOp(() => _mouseHook.Install());

        _ = SendBestEffortAsync(new SwitchDeviceMessage { ToClient = true, X = entryX, Y = entryY });

        return new List<Action>
        {
            () => ForwardingChanged?.Invoke(true),
            () => StateMessage?.Invoke("Forwarding keyboard and mouse to the client. Scroll Lock returns control.")
        };
    }

    private List<Action> EndForwardingLocked(bool switchBack, bool disarmAfter)
    {
        IsForwarding    = false;
        _hasLastHostPos = false;

        // Everything we pressed on the client must be released before control
        // transfers back, or modifiers would stay logically held there.
        foreach (int vk in _pressedKeys.DrainAllPressed())
            _ = SendBestEffortAsync(new KeyUpMessage { KeyCode = vk });
        foreach (int button in _pressedButtons.DrainAllPressed())
            _ = SendBestEffortAsync(new MouseButtonMessage { Button = button, IsDown = false });

        if (switchBack)
            _ = SendBestEffortAsync(new SwitchDeviceMessage { ToClient = false });

        // Put the local cursor where the user expects it: the edge facing the
        // client, at the mapped vertical position of the virtual cursor.
        (int hostW, int hostH) = _screen.GetPrimaryScreenSize();
        if (_clientScreen is { } cs)
        {
            (int hx, int hy) = CoordinateMapper.HostReturn(Arrangement, _virtualCursor.Y, hostW, hostH, cs.Height);
            _screen.TrySetCursorPos(hx, hy);
        }

        EnqueueHookOp(() => _mouseHook.Uninstall());

        var events = new List<Action>
        {
            () => ForwardingChanged?.Invoke(false)
        };

        if (disarmAfter)
        {
            events.AddRange(SetArmedLocked(false));
        }
        else
        {
            // Resume edge watching right away — the user may immediately want
            // to cross back to the client.
            events.AddRange(SetArmedLocked(true));
        }

        return events;
    }

    private List<Action> SetArmedLocked(bool armed)
    {
        IsArmed = armed;
        if (armed)
        {
            _enterGate.Reset();
            EnqueueHookOp(() => _keyboardHook.Install());
            StartPollLoop();
            return new List<Action>
            {
                () => ArmedChanged?.Invoke(true),
                () => StateMessage?.Invoke($"Watching the {EdgeName()} edge — park the cursor there to switch.")
            };
        }

        StopPollLoop();
        EnqueueHookOp(() => _keyboardHook.Uninstall());
        return new List<Action>
        {
            () => ArmedChanged?.Invoke(false),
            () => StateMessage?.Invoke("Edge switching off.")
        };
    }

    private string EdgeName()
        => Arrangement == ScreenArrangement.ClientRight ? "right" : "left";

    // ── Hook operations queue ────────────────────────────────────────────────

    private void EnqueueHookOp(Action op)
    {
        // Serialize onto the previous operation so off/on ordering is preserved.
        _hookOps = _hookOps.ContinueWith(_ => op(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void FlushHookOps()
    {
        try { _hookOps.Wait(TimeSpan.FromSeconds(3)); } catch { /* disposal races */ }
    }

    // ── Event dispatch ───────────────────────────────────────────────────────

    /// <summary>
    /// Raises events collected under the lock. Hook threads take this path too;
    /// subscribers must never block on this controller's lock (UI handlers use
    /// BeginInvoke, not Invoke).
    /// </summary>
    private static void RaiseOnHookThread(List<Action> events)
    {
        foreach (var e in events) e();
    }

    private static void Raise(List<Action> events)
    {
        foreach (var e in events) e();
    }

    // ── Edge poll loop ───────────────────────────────────────────────────────

    private void StartPollLoop()
    {
        if (_pollCts is not null) return;

        _pollCts = new CancellationTokenSource();
        var ct   = _pollCts.Token;
        _ = PollLoopAsync(ct);
    }

    private void StopPollLoop()
    {
        _pollCts?.Cancel();
        _pollCts = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                List<Action>? events = null;
                lock (_gate)
                {
                    if (_disposed || !IsArmed || !CanForwardLocked())
                        continue;

                    (int hostW, int _) = _screen.GetPrimaryScreenSize();
                    if (!_screen.TryGetCursorPos(out int x, out int y))
                        continue;

                    bool atEdge = Arrangement == ScreenArrangement.ClientRight
                        ? x >= hostW - 2
                        : x <= 1;

                    if (_enterGate.Record(atEdge))
                        events = BeginForwardingLocked();
                }

                if (events is not null)
                    Raise(events);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (ObjectDisposedException)    { /* timer raced disposal */ }
    }

    // ── Send helper ──────────────────────────────────────────────────────────

    private async Task SendBestEffortAsync(BaseMessage message)
    {
        try
        {
            await _sendToClient(message);
        }
        catch
        {
            // Connection dying — the networking layer raises its disconnect
            // event, which routes to ClientDisconnected().
        }
    }

    // ── IDisposable ──────────────────────────────────────────────────────────

    public void Dispose()
    {
        List<Action> events;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            events = IsForwarding
                ? EndForwardingLocked(switchBack: true, disarmAfter: false)
                : new List<Action>();

            StopPollLoop();
        }

        // Wait for queued hook operations, then remove the hooks for good.
        FlushHookOps();
        _mouseHook.Uninstall();
        _keyboardHook.Uninstall();

        Raise(events);

        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _keyboardHook.ProcessKey = null;
        _mouseHook.ProcessMouse  = null;
    }
}
