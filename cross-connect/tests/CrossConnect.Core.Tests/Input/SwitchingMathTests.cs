using CrossConnect.Core.Input;
using Xunit;

namespace CrossConnect.Core.Tests.Input;

public class VirtualCursorTests
{
    [Fact]
    public void ApplyDelta_ClampsToClientBounds()
    {
        var cursor = new VirtualCursor();
        cursor.SetPosition(1900, 1000);

        cursor.ApplyDelta(200, 200, clientWidth: 1920, clientHeight: 1080);

        Assert.Equal(1919, cursor.X);
        Assert.Equal(1079, cursor.Y);
    }

    [Fact]
    public void ApplyDelta_ClampsToLowerBound()
    {
        var cursor = new VirtualCursor();
        cursor.SetPosition(2, 2);

        cursor.ApplyDelta(-50, -50, 1920, 1080);

        Assert.Equal(0, cursor.X);
        Assert.Equal(0, cursor.Y);
    }

    [Fact]
    public void ReturnEdge_IsOppositeSideOfArrangement()
    {
        var cursor = new VirtualCursor();

        cursor.SetPosition(0, 500);
        Assert.True(cursor.IsAtReturnEdge(ScreenArrangement.ClientRight, clientWidth: 1920));
        Assert.False(cursor.IsAtReturnEdge(ScreenArrangement.ClientLeft, clientWidth: 1920));

        cursor.SetPosition(1919, 500);
        Assert.False(cursor.IsAtReturnEdge(ScreenArrangement.ClientRight, clientWidth: 1920));
        Assert.True(cursor.IsAtReturnEdge(ScreenArrangement.ClientLeft, clientWidth: 1920));
    }
}

public class CoordinateMapperTests
{
    // Host 1920×1080, client 2560×1440.

    [Fact]
    public void ClientEntry_ClientRight_EntersAtLeftEdgeWithMappedY()
    {
        var (x, y) = CoordinateMapper.ClientEntry(
            ScreenArrangement.ClientRight, hostY: 540, hostHeight: 1080, clientWidth: 2560, clientHeight: 1440);

        Assert.Equal(0, x);
        Assert.Equal(720, y); // 540 * 1440 / 1080
    }

    [Fact]
    public void ClientEntry_ClientLeft_EntersAtRightEdge()
    {
        var (x, y) = CoordinateMapper.ClientEntry(
            ScreenArrangement.ClientLeft, hostY: 270, hostHeight: 1080, clientWidth: 2560, clientHeight: 1440);

        Assert.Equal(2559, x);
        Assert.Equal(360, y);
    }

    [Fact]
    public void HostReturn_ClientRight_EntersAtRightEdgeWithMappedY()
    {
        var (x, y) = CoordinateMapper.HostReturn(
            ScreenArrangement.ClientRight, clientY: 1440 - 1, hostWidth: 1920, hostHeight: 1080, clientHeight: 1440);

        Assert.Equal(1919, x);
        Assert.Equal(1079, y);
    }

    [Fact]
    public void HostReturn_ClientLeft_EntersAtLeftEdge()
    {
        var (x, y) = CoordinateMapper.HostReturn(
            ScreenArrangement.ClientLeft, clientY: 720, hostWidth: 1920, hostHeight: 1080, clientHeight: 1440);

        Assert.Equal(0, x);
        Assert.Equal(540, y);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(540, 720)]
    [InlineData(1079, 1439)]
    public void Map_IsProportionalAndClamped(int hostY, int expectedClientY)
        => Assert.Equal(expectedClientY, CoordinateMapper.Map(hostY, sourceMax: 1080, destMax: 1440));

    [Fact]
    public void Map_GuardsAgainstZeroExtents()
    {
        Assert.Equal(0, CoordinateMapper.Map(100, sourceMax: 0, destMax: 100));
        Assert.Equal(0, CoordinateMapper.Map(100, sourceMax: 100, destMax: 0));
    }
}
