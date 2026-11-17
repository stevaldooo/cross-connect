namespace CrossConnect.Core.Input;

/// <summary>
/// Which side of the host screen the client monitor sits on.
/// </summary>
public enum ScreenArrangement
{
    /// <summary>The client screen is to the right of the host screen.</summary>
    ClientRight,

    /// <summary>The client screen is to the left of the host screen.</summary>
    ClientLeft
}

/// <summary>
/// The host-side virtual copy of the client's cursor. While forwarding, the
/// host applies its physical mouse deltas to this cursor in the client's
/// coordinate space, injects the position remotely, and uses it to detect
/// when the cursor reaches the client's opposite edge (returning control).
///
/// Because the host computes everything, the client needs no switching logic
/// of its own — the standard server-computes model used by Barrier.
/// </summary>
public sealed class VirtualCursor
{
    public int X { get; private set; }
    public int Y { get; private set; }

    public void SetPosition(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Applies pixel deltas, clamping to the client screen bounds.</summary>
    public void ApplyDelta(int dx, int dy, int clientWidth, int clientHeight)
    {
        X = Math.Clamp(X + dx, 0, Math.Max(clientWidth - 1, 0));
        Y = Math.Clamp(Y + dy, 0, Math.Max(clientHeight - 1, 0));
    }

    /// <summary>
    /// True when the virtual cursor reached the client edge that faces back
    /// toward the host — the cue to switch control back.
    /// </summary>
    public bool IsAtReturnEdge(ScreenArrangement arrangement, int clientWidth)
        => arrangement == ScreenArrangement.ClientRight
            ? X <= 0
            : X >= clientWidth - 1;
}
