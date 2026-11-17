using CrossConnect.Core.Input;
using CrossConnect.Core.Networking;
using CrossConnect.Core.Protocol;
using Xunit;

namespace CrossConnect.Core.Tests.Input;

/// <summary>
/// Tests the client receiving brain with fakes — no real hooks, injection or
/// display are touched. Covers controlled-mode entry/exit, injection, and
/// the Scroll Lock escape hatch.
/// </summary>
public class InputReceivingControllerTests
{
    private sealed class FakeClient : ICrossConnectClient
    {
        public event EventHandler<ConnectionEventArgs>? Connected;
        public event EventHandler<ConnectionEventArgs>? Disconnected;
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        public Task ConnectAsync(string host, int port, string localDeviceName, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(BaseMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public bool IsConnected => true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Raise(BaseMessage message)
            => MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message, "fake:1"));
        public void RaiseDisconnected()
            => Disconnected?.Invoke(this, new ConnectionEventArgs("fake:1"));
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public Func<KeyEvent, bool>? ProcessKey { get; set; }
        public bool IsInstalled { get; set; }
        public void Install() => IsInstalled = true;
        public void Uninstall() => IsInstalled = false;
        public void Dispose() => IsInstalled = false;
        public bool Raise(int vk, bool isDown) => ProcessKey!.Invoke(new KeyEvent(vk, isDown));
    }

    private sealed class FakeMouseHook : IMouseHook
    {
        public Func<MouseEvent, bool>? ProcessMouse { get; set; }
        public bool IsInstalled { get; set; }
        public void Install() => IsInstalled = true;
        public void Uninstall() => IsInstalled = false;
        public void Dispose() => IsInstalled = false;
        public bool Raise(MouseEvent e) => ProcessMouse!.Invoke(e);
    }

    private sealed class FakeKeyboardInjector : IKeyboardInjector
    {
        public List<string> Calls { get; } = new();
        public int ReleaseAllCount { get; private set; }
        public bool InjectKeyDown(int vk) { Calls.Add($"kd {vk}"); return true; }
        public bool InjectKeyUp(int vk) { Calls.Add($"ku {vk}"); return true; }
        public void ReleaseAllPressedKeys() => ReleaseAllCount++;
        public int PressedKeyCount => 0;
        public void Dispose() { }
    }

    private sealed class FakeMouseInjector : IMouseInjector
    {
        public List<string> Calls { get; } = new();
        public int ReleaseAllCount { get; private set; }
        public void InjectMoveRel(int dx, int dy) => Calls.Add($"rel {dx},{dy}");
        public void InjectMoveAbs(int x, int y) => Calls.Add($"abs {x},{y}");
        public void InjectButtonDown(int b) => Calls.Add($"bd {b}");
        public void InjectButtonUp(int b) => Calls.Add($"bu {b}");
        public void InjectWheel(int d, bool h) => Calls.Add($"wh {d}");
        public void ReleaseAllPressedButtons() => ReleaseAllCount++;
        public int PressedButtonCount => 0;
        public void Dispose() { }
    }

    private sealed class FakeScreenService : IScreenService
    {
        public List<(int X, int Y)> SetCursorCalls { get; } = new();
        public (int Width, int Height) GetPrimaryScreenSize() => (2560, 1440);
        public bool TryGetCursorPos(out int x, out int y) { x = y = 0; return true; }
        public bool TrySetCursorPos(int x, int y) { SetCursorCalls.Add((x, y)); return true; }
    }

    private readonly FakeClient _client = new();
    private readonly FakeKeyboardHook _kb = new();
    private readonly FakeMouseHook _mouse = new();
    private readonly FakeKeyboardInjector _kbInjector = new();
    private readonly FakeMouseInjector _mouseInjector = new();
    private readonly FakeScreenService _screen = new();

    private InputReceivingController CreateController()
        => new(_client, _kb, _mouse, _screen, _kbInjector, _mouseInjector);

    [Fact]
    public void SwitchToClient_EnterControlled_PositionsCursorAndCapturesLocalInput()
    {
        using var controller = CreateController();

        _client.Raise(new SwitchDeviceMessage { ToClient = true, X = 10, Y = 20 });

        Assert.True(controller.IsControlled);
        Assert.True(_kb.IsInstalled);
        Assert.True(_mouse.IsInstalled);
        Assert.Equal([(10, 20)], _screen.SetCursorCalls);

        // Local physical input is swallowed while controlled.
        Assert.True(_kb.Raise(0x41, isDown: true));
        Assert.True(_mouse.Raise(new MouseEvent(5, 5, MouseEventType.Move)));
    }

    [Fact]
    public void InputMessages_WhileControlled_AreInjected()
    {
        using var controller = CreateController();
        _client.Raise(new SwitchDeviceMessage { ToClient = true, X = -1, Y = -1 });

        _client.Raise(new KeyDownMessage { KeyCode = 0x41 });
        _client.Raise(new KeyUpMessage { KeyCode = 0x41 });
        _client.Raise(new MouseMoveAbsMessage { X = 100, Y = 200 });
        _client.Raise(new MouseButtonMessage { Button = MouseCodes.Right, IsDown = true });
        _client.Raise(new MouseScrollMessage { Delta = 120, IsHorizontal = false });

        Assert.Equal(new[] { "kd 65", "ku 65" }, _kbInjector.Calls);
        Assert.Equal(new[] { "abs 100,200", "bd 2", "wh 120" }, _mouseInjector.Calls);
    }

    [Fact]
    public void InputMessages_WhileNotControlled_AreIgnored()
    {
        using var controller = CreateController();

        _client.Raise(new KeyDownMessage { KeyCode = 0x41 });
        _client.Raise(new MouseMoveAbsMessage { X = 100, Y = 200 });

        Assert.Empty(_kbInjector.Calls);
        Assert.Empty(_mouseInjector.Calls);
    }

    [Fact]
    public void SwitchBackToHost_ExitsControlledAndReleasesEverything()
    {
        using var controller = CreateController();
        _client.Raise(new SwitchDeviceMessage { ToClient = true, X = 10, Y = 20 });
        _client.Raise(new KeyDownMessage { KeyCode = 0x10 });
        _client.Raise(new MouseButtonMessage { Button = MouseCodes.Left, IsDown = true });
        _kbInjector.Calls.Clear();
        _mouseInjector.Calls.Clear();

        _client.Raise(new SwitchDeviceMessage { ToClient = false });

        Assert.False(controller.IsControlled);
        Assert.False(_kb.IsInstalled);
        Assert.False(_mouse.IsInstalled);
        Assert.Equal(1, _kbInjector.ReleaseAllCount);
        Assert.Equal(1, _mouseInjector.ReleaseAllCount);
        Assert.Empty(_kbInjector.Calls); // only release-all, no new injections
    }

    [Fact]
    public void Disconnect_ExitsControlledAndReleasesEverything()
    {
        using var controller = CreateController();
        _client.Raise(new SwitchDeviceMessage { ToClient = true, X = 10, Y = 20 });

        _client.RaiseDisconnected();

        Assert.False(controller.IsControlled);
        Assert.Equal(1, _kbInjector.ReleaseAllCount);
        Assert.Equal(1, _mouseInjector.ReleaseAllCount);
    }

    [Fact]
    public void ScrollLock_WhileControlled_RaisesForceSwitchBack_AndIsSwallowed()
    {
        using var controller = CreateController();
        _client.Raise(new SwitchDeviceMessage { ToClient = true, X = 10, Y = 20 });

        int requests = 0;
        controller.ForceSwitchBackRequested += () => requests++;

        bool swallowed = _kb.Raise(0x91, isDown: true);

        Assert.True(swallowed);
        Assert.Equal(1, requests);
        Assert.True(controller.IsControlled); // controller waits for the network round trip
    }

    [Fact]
    public void LocalInput_WhileNotControlled_PassesThrough()
    {
        using var controller = CreateController();

        Assert.False(_kb.Raise(0x41, isDown: true));
        Assert.False(_mouse.Raise(new MouseEvent(5, 5, MouseEventType.Move)));
    }
}
