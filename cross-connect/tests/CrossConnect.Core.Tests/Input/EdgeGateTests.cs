using CrossConnect.Core.Input;
using Xunit;

namespace CrossConnect.Core.Tests.Input;

public class EdgeGateTests
{
    [Fact]
    public void RequiresConsecutiveSamplesBeforeFiring()
    {
        var gate = new EdgeGate(requiredSamples: 3);

        Assert.False(gate.Record(true));
        Assert.False(gate.Record(true));
        Assert.True(gate.Record(true)); // third consecutive sample fires
    }

    [Fact]
    public void BreakInStreak_RestartsTheCount()
    {
        var gate = new EdgeGate(requiredSamples: 3);

        Assert.False(gate.Record(true));
        Assert.False(gate.Record(true));
        Assert.False(gate.Record(false)); // streak broken
        Assert.False(gate.Record(true));
        Assert.False(gate.Record(true));
        Assert.True(gate.Record(true));   // new streak of 3
    }

    [Fact]
    public void FiresOncePerDwell_ThenRequiresAFullStreakAgain()
    {
        var gate = new EdgeGate(requiredSamples: 2);

        Assert.False(gate.Record(true));
        Assert.True(gate.Record(true));   // fires
        Assert.False(gate.Record(true));  // latch reset — needs a new streak
        Assert.True(gate.Record(true));
    }

    [Fact]
    public void Reset_ClearsTheStreak()
    {
        var gate = new EdgeGate(requiredSamples: 2);
        gate.Record(true);

        gate.Reset();

        Assert.False(gate.Record(true));
        Assert.True(gate.Record(true));
    }

    [Fact]
    public void RejectsNonPositiveSampleCounts()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeGate(0));
}
