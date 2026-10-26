namespace Gpc.HostWin.Timing;

/// <summary>Fixed-size histogram of tick start error in microseconds. Allocation-free after construction.</summary>
public sealed class TickStats
{
    const int BucketUs = 50;
    readonly long[] _buckets;
    long _count, _maxUs;

    public TickStats(int maxErrorUs = 50_000) => _buckets = new long[maxErrorUs / BucketUs + 1];

    public long Count => _count;
    public long MaxUs => _maxUs;

    public void Record(long errorUs)
    {
        if (errorUs < 0) errorUs = 0;
        int i = (int)Math.Min(errorUs / BucketUs, _buckets.Length - 1);
        _buckets[i]++;
        _count++;
        if (errorUs > _maxUs) _maxUs = errorUs;
    }

    /// <summary>Upper bound of the bucket holding the given percentile, in microseconds.</summary>
    public long PercentileUs(double p)
    {
        if (_count == 0) return 0;
        long target = (long)Math.Ceiling(_count * p / 100.0);
        long seen = 0;
        for (int i = 0; i < _buckets.Length; i++)
        {
            seen += _buckets[i];
            if (seen >= target) return (i + 1) * (long)BucketUs;
        }
        return _maxUs;
    }

    public override string ToString() =>
        $"ticks={_count} p50<={PercentileUs(50)}us p99<={PercentileUs(99)}us p99.9<={PercentileUs(99.9)}us max={_maxUs}us";
}
