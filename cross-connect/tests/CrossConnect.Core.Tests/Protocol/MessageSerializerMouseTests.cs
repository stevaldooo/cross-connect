using CrossConnect.Core.Protocol;
using Xunit;

namespace CrossConnect.Core.Tests.Protocol;

public partial class MessageSerializerTests
{
    [Fact]
    public void Serialize_MouseMoveRel_RoundTrips()
    {
        byte[] data = MessageSerializer.Serialize(new MouseMoveRelMessage { Dx = -14, Dy = 33 });

        var message = Assert.IsType<MouseMoveRelMessage>(
            MessageSerializer.Deserialize(System.Text.Encoding.UTF8.GetString(data).TrimEnd('\n')));
        Assert.Equal(-14, message.Dx);
        Assert.Equal(33, message.Dy);
    }

    [Fact]
    public void Serialize_MouseMoveAbs_RoundTrips()
    {
        var message = Assert.IsType<MouseMoveAbsMessage>(
            MessageSerializer.Deserialize("""{"type":"MOUSE_MOVE_ABS","x":1200,"y":540}"""));
        Assert.Equal(1200, message.X);
        Assert.Equal(540, message.Y);
    }

    [Fact]
    public void Serialize_MouseButton_RoundTrips()
    {
        byte[] data = MessageSerializer.Serialize(new MouseButtonMessage { Button = 2, IsDown = true });

        string json = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\n');
        Assert.Contains("\"type\":\"MOUSE_BUTTON\"", json);
        Assert.Contains("\"button\":2", json);
        Assert.Contains("\"isDown\":true", json);

        var message = Assert.IsType<MouseButtonMessage>(MessageSerializer.Deserialize(json));
        Assert.Equal(2, message.Button);
        Assert.True(message.IsDown);
    }

    [Theory]
    [InlineData("""{"type":"MOUSE_SCROLL","delta":120,"isHorizontal":false}""", 120, false)]
    [InlineData("""{"type":"MOUSE_SCROLL","delta":-240,"isHorizontal":true}""", -240, true)]
    public void Serialize_MouseScroll_RoundTrips(string json, int expectedDelta, bool expectedHorizontal)
    {
        var message = Assert.IsType<MouseScrollMessage>(MessageSerializer.Deserialize(json));
        Assert.Equal(expectedDelta, message.Delta);
        Assert.Equal(expectedHorizontal, message.IsHorizontal);
    }
}
