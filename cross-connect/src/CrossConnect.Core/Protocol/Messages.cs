using System.Text.Json.Serialization;

namespace CrossConnect.Core.Protocol;

/// <summary>
/// Base record for all protocol messages. Every message carries a type discriminator
/// so the receiver can deserialize into the correct concrete type.
/// </summary>
public abstract record BaseMessage
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

/// <summary>
/// Sent by the connecting device to identify itself.
/// </summary>
public record HelloMessage : BaseMessage
{
    public override string Type => MessageType.Hello;

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = ProtocolConstants.Version;

    [JsonPropertyName("deviceName")]
    public required string DeviceName { get; init; }
}

/// <summary>
/// Sent by the host in reply to a HELLO message to confirm the connection.
/// </summary>
public record HelloAckMessage : BaseMessage
{
    public override string Type => MessageType.HelloAck;

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = ProtocolConstants.Version;

    [JsonPropertyName("deviceName")]
    public required string DeviceName { get; init; }
}

/// <summary>
/// Heartbeat request — sent periodically to verify the connection is alive.
/// </summary>
public record PingMessage : BaseMessage
{
    public override string Type => MessageType.Ping;
}

/// <summary>
/// Heartbeat response — sent in reply to a PING.
/// </summary>
public record PongMessage : BaseMessage
{
    public override string Type => MessageType.Pong;
}

/// <summary>
/// Sent by either side before closing the connection intentionally.
/// </summary>
public record DisconnectMessage : BaseMessage
{
    public override string Type => MessageType.Disconnect;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// A keyboard key was pressed on the host. Carries the Win32 virtual-key code
/// (a plain integer) so the payload is independent of keyboard layouts.
/// </summary>
public record KeyDownMessage : BaseMessage
{
    public override string Type => MessageType.KeyDown;

    [JsonPropertyName("keyCode")]
    public int KeyCode { get; init; }
}

/// <summary>
/// A keyboard key was released on the host.
/// </summary>
public record KeyUpMessage : BaseMessage
{
    public override string Type => MessageType.KeyUp;

    [JsonPropertyName("keyCode")]
    public int KeyCode { get; init; }
}

/// <summary>
/// Relative mouse movement (pixel deltas) captured on the host.
/// Used before screen-edge switching (M5) provides absolute mapping.
/// </summary>
public record MouseMoveRelMessage : BaseMessage
{
    public override string Type => MessageType.MouseMoveRel;

    [JsonPropertyName("dx")]
    public int Dx { get; init; }

    [JsonPropertyName("dy")]
    public int Dy { get; init; }
}

/// <summary>
/// Absolute mouse position in CLIENT-screen pixels. The client normalizes
/// these to its own screen when injecting, so the values must already be in
/// the client's coordinate space.
/// </summary>
public record MouseMoveAbsMessage : BaseMessage
{
    public override string Type => MessageType.MouseMoveAbs;

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }
}

/// <summary>
/// A mouse button press or release. Button values are the MouseCodes
/// constants (1 = left, 2 = right, 3 = middle, 4/5 = X buttons).
/// </summary>
public record MouseButtonMessage : BaseMessage
{
    public override string Type => MessageType.MouseButton;

    [JsonPropertyName("button")]
    public int Button { get; init; }

    [JsonPropertyName("isDown")]
    public bool IsDown { get; init; }
}

/// <summary>
/// A wheel tick. Delta follows the Win32 convention: multiples of +/-120 per
/// notch. IsHorizontal distinguishes the horizontal (tilt) wheel.
/// </summary>
public record MouseScrollMessage : BaseMessage
{
    public override string Type => MessageType.MouseScroll;

    [JsonPropertyName("delta")]
    public int Delta { get; init; }

    [JsonPropertyName("isHorizontal")]
    public bool IsHorizontal { get; init; }
}

/// <summary>
/// Announces the sender's primary screen size in pixels. The client sends it
/// right after the handshake so the host can map coordinates into the
/// client's screen space.
/// </summary>
public record ScreenInfoMessage : BaseMessage
{
    public override string Type => MessageType.ScreenInfo;

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }
}

/// <summary>
/// Requests a control transfer between the two machines.
/// ToClient=true hands control to the client; X/Y (in client-screen pixels)
/// is where the cursor should appear on the client. ToClient=false returns
/// control to the host.
/// </summary>
public record SwitchDeviceMessage : BaseMessage
{
    public override string Type => MessageType.SwitchDevice;

    [JsonPropertyName("toClient")]
    public bool ToClient { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; } = -1;

    [JsonPropertyName("y")]
    public int Y { get; init; } = -1;
}
