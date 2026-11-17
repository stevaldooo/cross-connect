namespace CrossConnect.Core.Protocol;

/// <summary>
/// All known message type identifiers in the CrossConnect protocol.
/// </summary>
public static class MessageType
{
    public const string Hello        = "HELLO";
    public const string HelloAck     = "HELLO_ACK";
    public const string Ping         = "PING";
    public const string Pong         = "PONG";
    public const string Disconnect   = "DISCONNECT";
    public const string KeyDown      = "KEY_DOWN";
    public const string KeyUp        = "KEY_UP";
    public const string MouseMoveRel = "MOUSE_MOVE_REL";
    public const string MouseMoveAbs = "MOUSE_MOVE_ABS";
    public const string MouseButton  = "MOUSE_BUTTON";
    public const string MouseScroll  = "MOUSE_SCROLL";
    public const string ScreenInfo   = "SCREEN_INFO";
    public const string SwitchDevice = "SWITCH_DEVICE";
}
