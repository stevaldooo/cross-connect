namespace CrossConnect.Core.Input;

/// <summary>
/// Dwell detector for screen-edge switching: requires the cursor to sit at
/// the edge for N consecutive samples before a switch fires, so flying past
/// an edge never steals control and parking on it switches reliably.
///
/// Record returns true exactly once per triggered sequence, then the counter
/// resets — another switch requires another full dwell.
/// </summary>
public sealed class EdgeGate
{
    private readonly int _requiredSamples;
    private int _consecutive;

    public EdgeGate(int requiredSamples = 3)
    {
        if (requiredSamples < 1)
            throw new ArgumentOutOfRangeException(nameof(requiredSamples));
        _requiredSamples = requiredSamples;
    }

    /// <summary>Feeds one sample. Returns true when the dwell condition is met.</summary>
    public bool Record(bool atEdge)
    {
        if (!atEdge)
        {
            _consecutive = 0;
            return false;
        }

        _consecutive++;
        if (_consecutive < _requiredSamples)
            return false;

        _consecutive = 0;
        return true;
    }

    public void Reset() => _consecutive = 0;
}
