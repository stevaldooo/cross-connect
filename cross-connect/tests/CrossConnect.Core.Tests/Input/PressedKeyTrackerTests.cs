using CrossConnect.Core.Input;
using Xunit;

namespace CrossConnect.Core.Tests.Input;

public class PressedKeyTrackerTests
{
    [Fact]
    public void SnapshotPressed_ReturnsHeldKeys()
    {
        var tracker = new PressedKeyTracker();

        tracker.MarkPressedDown(0x10); // Shift
        tracker.MarkPressedDown(0x41); // A

        Assert.Equal(new[] { 0x10, 0x41 }, tracker.SnapshotPressed().OrderBy(k => k));
    }

    [Fact]
    public void MarkReleased_RemovesKeyFromSnapshot()
    {
        var tracker = new PressedKeyTracker();
        tracker.MarkPressedDown(0x41);

        tracker.MarkReleased(0x41);

        Assert.Empty(tracker.SnapshotPressed());
    }

    [Fact]
    public void MarkPressedDown_IgnoresKeyRepeats()
    {
        var tracker = new PressedKeyTracker();

        tracker.MarkPressedDown(0x41);
        tracker.MarkPressedDown(0x41);
        tracker.MarkPressedDown(0x41);

        var drained = tracker.DrainAllPressed();
        Assert.Single(drained);
    }

    [Fact]
    public void MarkReleased_OfUntrackedKey_IsNoOp()
    {
        var tracker = new PressedKeyTracker();
        tracker.MarkPressedDown(0x41);

        tracker.MarkReleased(0x42); // never pressed

        Assert.Single(tracker.SnapshotPressed());
    }

    [Fact]
    public void SnapshotPressed_DoesNotClearTracker()
    {
        var tracker = new PressedKeyTracker();
        tracker.MarkPressedDown(0x41);

        tracker.SnapshotPressed();

        Assert.Single(tracker.SnapshotPressed());
    }

    [Fact]
    public void DrainAllPressed_ReturnsKeysAndClearsTracker()
    {
        var tracker = new PressedKeyTracker();
        tracker.MarkPressedDown(0x10);
        tracker.MarkPressedDown(0x11);

        var drained = tracker.DrainAllPressed();

        Assert.Equal(new[] { 0x10, 0x11 }, drained.OrderBy(k => k));
        Assert.Empty(tracker.SnapshotPressed());
    }

    [Fact]
    public async Task Tracker_IsSafeForConcurrentUse()
    {
        var tracker = new PressedKeyTracker();
        var codes = Enumerable.Range(1, 200).ToArray();

        await Task.WhenAll(
            Task.Run(() => { foreach (int vk in codes) tracker.MarkPressedDown(vk); }),
            Task.Run(() => { foreach (int vk in codes) tracker.MarkReleased(vk); }),
            Task.Run(() => { for (int i = 0; i < 1000; i++) tracker.SnapshotPressed(); }));

        // No exception, no corruption — drain returns a valid subset.
        Assert.Subset(codes.ToHashSet(), tracker.DrainAllPressed().ToHashSet());
    }
}
