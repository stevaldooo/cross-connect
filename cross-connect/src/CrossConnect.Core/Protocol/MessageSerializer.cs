using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrossConnect.Core.Protocol;

/// <summary>
/// Handles serialization and deserialization of CrossConnect protocol messages.
///
/// Wire format: UTF-8 JSON followed by a newline character (\n).
/// Each message occupies exactly one line so TCP stream framing is straightforward.
///
/// Deserialization is done in two steps:
///   1. Parse the raw JSON to read the "type" field.
///   2. Deserialize into the matching concrete record type.
/// This avoids a polymorphic JSON converter while remaining strongly typed.
/// </summary>
public static class MessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes a message to a UTF-8 newline-terminated byte sequence ready to send over TCP.
    /// </summary>
    public static byte[] Serialize(BaseMessage message)
    {
        string json = JsonSerializer.Serialize(message, message.GetType(), Options);
        return Encoding.UTF8.GetBytes(json + "\n");
    }

    /// <summary>
    /// Deserializes a single line of JSON (without the trailing newline) into the
    /// appropriate message type. Returns null when the line is empty, malformed,
    /// not a JSON object, or carries an unknown type discriminator — a single
    /// bad line must never kill the connection.
    /// </summary>
    public static BaseMessage? Deserialize(string line)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            if (JsonNode.Parse(line) is not JsonObject obj)
                return null;

            string? type = obj["type"]?.GetValue<string>();

            return type switch
            {
                MessageType.Hello      => JsonSerializer.Deserialize<HelloMessage>(line, Options),
                MessageType.HelloAck   => JsonSerializer.Deserialize<HelloAckMessage>(line, Options),
                MessageType.Ping       => JsonSerializer.Deserialize<PingMessage>(line, Options),
                MessageType.Pong       => JsonSerializer.Deserialize<PongMessage>(line, Options),
                MessageType.Disconnect => JsonSerializer.Deserialize<DisconnectMessage>(line, Options),
                MessageType.KeyDown    => JsonSerializer.Deserialize<KeyDownMessage>(line, Options),
                MessageType.KeyUp      => JsonSerializer.Deserialize<KeyUpMessage>(line, Options),
                MessageType.MouseMoveRel => JsonSerializer.Deserialize<MouseMoveRelMessage>(line, Options),
                MessageType.MouseMoveAbs => JsonSerializer.Deserialize<MouseMoveAbsMessage>(line, Options),
                MessageType.MouseButton  => JsonSerializer.Deserialize<MouseButtonMessage>(line, Options),
                MessageType.MouseScroll  => JsonSerializer.Deserialize<MouseScrollMessage>(line, Options),
                MessageType.ScreenInfo   => JsonSerializer.Deserialize<ScreenInfoMessage>(line, Options),
                MessageType.SwitchDevice => JsonSerializer.Deserialize<SwitchDeviceMessage>(line, Options),
                _                      => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
