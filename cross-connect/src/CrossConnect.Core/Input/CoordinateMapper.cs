namespace CrossConnect.Core.Input;

/// <summary>
/// Pure coordinate mapping between the host and client screen spaces.
/// Both screens are treated as primary monitors in physical pixels
/// (PerMonitorV2 aware); scaling is proportional.
/// </summary>
public static class CoordinateMapper
{
    /// <summary>
    /// Where the client cursor should appear when control transfers to the
    /// client: at the client edge that faces the host, at the proportionally
    /// mapped vertical position.
    /// </summary>
    public static (int X, int Y) ClientEntry(
        ScreenArrangement arrangement, int hostY, int hostHeight, int clientWidth, int clientHeight)
    {
        int y = Map(hostY, sourceMax: hostHeight, destMax: clientHeight);
        return arrangement == ScreenArrangement.ClientRight
            ? (0, y)
            : (clientWidth - 1, y);
    }

    /// <summary>
    /// Where the host cursor should appear when control returns to the host:
    /// at the host edge that faces the client, mapped from the virtual
    /// cursor's client-space position.
    /// </summary>
    public static (int X, int Y) HostReturn(
        ScreenArrangement arrangement, int clientY, int hostWidth, int hostHeight, int clientHeight)
    {
        int y = Map(clientY, sourceMax: clientHeight, destMax: hostHeight);
        return arrangement == ScreenArrangement.ClientRight
            ? (hostWidth - 1, y)
            : (0, y);
    }

    /// <summary>Proportionally maps a coordinate from source to dest extent, clamped in range.</summary>
    public static int Map(int value, int sourceMax, int destMax)
    {
        if (sourceMax <= 0 || destMax <= 0) return 0;
        int mapped = (int)Math.Round((double)value * destMax / sourceMax);
        return Math.Clamp(mapped, 0, destMax - 1);
    }
}
