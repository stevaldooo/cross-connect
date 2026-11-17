using CrossConnect.Core.Input;
using CrossConnect.Core.Protocol;
using Xunit;

namespace CrossConnect.Core.Tests.Input;

/// <summary>
/// Tests the host switching brain with fakes — no real hooks, cursor or
/// display are touched. Covers the full switch lifecycle: arm → edge/Scroll
/// Lock → forward (ABS moves) → switch back (release everything).
/// </summary>
public class InputSwitchControllerTests
{
    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public Func<KeyEvent, bool>? ProcessKey { get; set; }
        public bool IsInstalled { get; set; }
        public void Install() => IsInstalled = true;
        public void Uninstall() => IsInstalled = false;
        public void Dispose() => IsInstalled = false;
        public bool Raise(int vk, bool isDown) => ProcessKey!.Invoke(new KeyEvent(vk, isDown));
        public bool Raise(KeyEvent e) => ProcessKey!.Invoke(e);
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

    private sealed class FakeScreenService : IScreenService
    {
        public int ScreenWidth = 1920;
        public int ScreenHeight = 1080;
        public int CursorX = 100;
        public int CursorY = 100;
        public List<(int X, int Y)> SetCursorCalls { get; } = new();

        public (int Width, int Height) GetPrimaryScreenSize() => (ScreenWidth, ScreenHeight);
        public bool TryGetCursorPos(out int x, out int y) { x = CursorX; y = CursorY; return true; }
        public bool TrySetCursorPos(int x, int y) { SetCursorCalls.Add((x, y)); return true; }
    }

    private readonly FakeKeyboardHook _kb = new();
    private readonly FakeMouseHook _mouse = new();
    private readonly FakeScreenService _screen = new();
    private readonly List<BaseMessage> _sent = new();

    private const string ClientEndpoint = "c1";
    private const int ClientW = 2560;
    private const int ClientH = 1440;

    private InputSwitchController CreateController()
        => new(_kb, _mouse, _screen, message => { _sent.Add(message); return Task.CompletedTask; }, pollIntervalMs: 1);

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "condition not met within timeout");
    }

    private void SetupClient(InputSwitchController controller)
        => controller.SetClientScreen(ClientEndpoint, ClientW, ClientH);

    private static KeyEvent Down(int vk) => new(vk, IsDown: true);
    private static KeyEvent Up(int vk)   => new(vk, IsDown: false);

    // ── Arming ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Arm_WithoutClient_PollDoesNotSwitch()
    {
        using var controller = CreateController();
        controller.Arm();

        _screen.CursorX = _screen.ScreenWidth - 1; // parked at right edge
        await Task.Delay(80);

        Assert.True(controller.IsArmed);
        Assert.False(controller.IsForwarding);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Arm_WithClient_ParkingOnEdgeTransfersControl()
    {
        using var controller = CreateController();
        SetupClient(controller);
        controller.Arm();

        _screen.CursorX = _screen.ScreenWidth - 1; // parked at right edge
        await WaitForAsync(() => controller.IsForwarding);

        // Entry point: client left edge, mapped Y from host Y=100 (of 1080 → 1440)
        var switchMsg = Assert.IsType<SwitchDeviceMessage>(
            Assert.Single(_sent, m => m is SwitchDeviceMessage));
        Assert.True(switchMsg.ToClient);
        Assert.Equal(0, switchMsg.X);
        Assert.Equal(CoordinateMapper.Map(100, 1080, 1440), switchMsg.Y);
    }

    [Fact]
    public void ScrollLock_WhileArmed_ForcesManualSwitch()
    {
        using var controller = CreateController();
        SetupClient(controller);
        controller.Arm();

        _screen.CursorX = 500; // nowhere near an edge
        bool swallowed = _kb.Raise(0x91, isDown: true);

        Assert.True(swallowed);
        Assert.True(controller.IsForwarding);
    }

    [Fact]
    public void KeysPassThroughLocally_WhileArmed()
    {
        using var controller = CreateController();
        SetupClient(controller);
        controller.Arm();

        Assert.False(_kb.Raise(Down(0x41)));
        Assert.False(_kb.Raise(Up(0x41)));
        Assert.Empty(_sent);
    }

    // ── Forwarding ───────────────────────────────────────────────────────────

    private InputSwitchController StartForwarding()
    {
        var controller = CreateController();
        SetupClient(controller);
        controller.Arm();
        _kb.Raise(0x91, isDown: true); // manual switch
        _sent.Clear();
        return controller;
    }

    [Fact]
    public void MouseMoves_WhileForwarding_SendAbsoluteClientCoordinates()
    {
        using var controller = StartForwarding();

        _mouse.Raise(new MouseEvent(_screen.CursorX + 50, _screen.CursorY, MouseEventType.Move));

        var abs = Assert.IsType<MouseMoveAbsMessage>(Assert.Single(_sent));
        Assert.Equal(50, abs.X);
        Assert.Equal(CoordinateMapper.Map(100, 1080, 1440), abs.Y);
    }

    [Fact]
    public void KeysAndButtons_WhileForwarding_AreSwallowedAndSent()
    {
        using var controller = StartForwarding();

        bool keySwallowed = _kb.Raise(Down(0x41));
        bool mouseSwallowed = _mouse.Raise(new MouseEvent(100, 100, MouseEventType.ButtonDown, MouseCodes.Left));

        Assert.True(keySwallowed);
        Assert.True(mouseSwallowed);
        Assert.Contains(_sent, m => m is KeyDownMessage { KeyCode: 0x41 });
        Assert.Contains(_sent, m => m is MouseButtonMessage { Button: MouseCodes.Left, IsDown: true });
    }

    [Fact]
    public async Task ReachingClientReturnEdge_SwitchesBackAndRepositionsHostCursor()
    {
        var controller = StartForwarding();

        // Entry X = 0 (client sits right). First move right to get off the
        // edge, then park on X=0 — four consecutive at-edge samples trigger
        // the return.
        _mouse.Raise(new MouseEvent(_screen.CursorX + 400, _screen.CursorY, MouseEventType.Move));
        for (int i = 0; i < 4; i++)
            _mouse.Raise(new MouseEvent(0, _screen.CursorY, MouseEventType.Move));

        await WaitForAsync(() => !controller.IsForwarding);

        Assert.False(controller.IsForwarding);
        Assert.True(controller.IsArmed); // still watching for the next crossing

        var back = Assert.IsType<SwitchDeviceMessage>(
            Assert.Single(_sent, m => m is SwitchDeviceMessage));
        Assert.False(back.ToClient);

        // Host cursor reappears at the right edge (client sits right), mapped Y.
        var expectedY = CoordinateMapper.Map(
            CoordinateMapper.Map(100, 1080, 1440), 1440, 1080);
        Assert.Contains((X: _screen.ScreenWidth - 1, Y: expectedY), _screen.SetCursorCalls);
    }

    [Fact]
    public void SwitchBack_ReleasesHeldKeysAndButtonsOnClient()
    {
        using var controller = StartForwarding();

        _kb.Raise(Down(0x10));                                              // Shift
        _mouse.Raise(new MouseEvent(100, 100, MouseEventType.ButtonDown, MouseCodes.Left));
        _sent.Clear();

        _kb.Raise(0x91, isDown: true); // Scroll Lock — force return

        Assert.Contains(_sent, m => m is KeyUpMessage { KeyCode: 0x10 });
        Assert.Contains(_sent, m => m is MouseButtonMessage { Button: MouseCodes.Left, IsDown: false });
        Assert.Contains(_sent, m => m is SwitchDeviceMessage { ToClient: false });
    }

    [Fact]
    public void ClientRequestingControlBack_EndsForwarding()
    {
        using var controller = StartForwarding();

        controller.HandleMessage(new SwitchDeviceMessage { ToClient = false });

        Assert.False(controller.IsForwarding);
        // The client initiated the switch, so the host must NOT send a
        // switch-back message of its own — only key/button releases happen.
        Assert.DoesNotContain(_sent, m => m is SwitchDeviceMessage);
    }

    [Fact]
    public void ClientDisconnect_WhileForwarding_EndsCleanlyWithoutSwitchMessage()
    {
        var controller = StartForwarding();

        _kb.Raise(Down(0x10));
        _sent.Clear();

        controller.ClientDisconnected(ClientEndpoint);

        Assert.False(controller.IsForwarding);
        Assert.False(controller.IsArmed);
        Assert.Contains(_sent, m => m is KeyUpMessage { KeyCode: 0x10 });
        Assert.DoesNotContain(_sent, m => m is SwitchDeviceMessage);
    }

    [Fact]
    public void StaleClientDisconnect_IsIgnored()
    {
        using var controller = CreateController();
        SetupClient(controller);
        controller.Arm();

        controller.ClientDisconnected("old-client:999");

        Assert.True(controller.IsArmed);
        Assert.Equal(ClientEndpoint, controller.ClientEndpoint);
    }

    [Fact]
    public void Disarm_WhileForwarding_SwitchesBackAndDisarms()
    {
        using var controller = StartForwarding();

        controller.Disarm();

        Assert.False(controller.IsForwarding);
        Assert.False(controller.IsArmed);
        Assert.Contains(_sent, m => m is SwitchDeviceMessage { ToClient: false });
    }

    [Fact]
    public void ArrangementLeft_MapsToMirroredEdges()
    {
        using var controller = CreateController();
        controller.Arrangement = ScreenArrangement.ClientLeft;
        SetupClient(controller);
        controller.Arm();

        _screen.CursorX = 1; // parked at LEFT edge (client is left)
        _kb.Raise(0x91, isDown: true); // manual switch to skip poll timing

        var switchMsg = Assert.IsType<SwitchDeviceMessage>(Assert.Single(_sent));
        Assert.Equal(ClientW - 1, switchMsg.X); // enters client at its right edge
    }
}
