using CrossConnect.Core.Protocol;
using Xunit;

namespace CrossConnect.Core.Tests.Protocol;

public partial class MessageSerializerTests
{
    [Fact]
    public void Serialize_KeyDownMessage_EndsWithNewlineAndRoundTrips()
    {
        byte[] data = MessageSerializer.Serialize(new KeyDownMessage { KeyCode = 0x41 });

        string line = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\n');
        Assert.EndsWith("\n", System.Text.Encoding.UTF8.GetString(data));

        var message = Assert.IsType<KeyDownMessage>(MessageSerializer.Deserialize(line));
        Assert.Equal(0x41, message.KeyCode);
    }

    [Fact]
    public void Serialize_KeyUpMessage_ProducesExpectedWireFormat()
    {
        byte[] data = MessageSerializer.Serialize(new KeyUpMessage { KeyCode = 0x10 });

        string json = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\n');
        Assert.Contains("\"type\":\"KEY_UP\"", json);
        Assert.Contains("\"keyCode\":16", json);
    }

    [Fact]
    public void Deserialize_KeyDownMessage_PreservesKeyCode()
    {
        var message = Assert.IsType<KeyDownMessage>(
            MessageSerializer.Deserialize("""{"type":"KEY_DOWN","keyCode":162}"""));

        Assert.Equal(162, message.KeyCode); // VK_LCONTROL
    }

    [Fact]
    public void Deserialize_KeyUpMessage_PreservesKeyCode()
    {
        var message = Assert.IsType<KeyUpMessage>(
            MessageSerializer.Deserialize("""{"type":"KEY_UP","keyCode":32}"""));

        Assert.Equal(32, message.KeyCode); // VK_SPACE
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"type\":\"BOGUS\"}")]
    [InlineData("{\"noTypeField\":1}")]
    [InlineData("[1,2,3]")]
    public void Deserialize_InvalidOrUnknownInput_ReturnsNull(string line)
        => Assert.Null(MessageSerializer.Deserialize(line));

    [Fact]
    public void Deserialize_TruncatedJson_ReturnsNullInsteadOfThrowing()
        => Assert.Null(MessageSerializer.Deserialize("""{"type":"KEY_DOWN","keyCode":"""));
}
