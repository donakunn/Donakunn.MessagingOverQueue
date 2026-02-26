namespace MessagingOverQueue.Test.Integration.RedisStreams.LoadTests.Metrics;

/// <summary>
/// Immutable statistics calculated from latency measurements.
/// </summary>
public sealed record LatencyStatistics
{
    public int Count { get; init; }
    public TimeSpan Min { get; init; }
    public TimeSpan Max { get; init; }
    public TimeSpan Mean { get; init; }
    public TimeSpan P50 { get; init; }
    public TimeSpan P95 { get; init; }
    public TimeSpan P99 { get; init; }
    public TimeSpan P999 { get; init; }

    public static LatencyStatistics Empty => new()
    {
        Count = 0,
        Min = TimeSpan.Zero,
        Max = TimeSpan.Zero,
        Mean = TimeSpan.Zero,
        P50 = TimeSpan.Zero,
        P95 = TimeSpan.Zero,
        P99 = TimeSpan.Zero,
        P999 = TimeSpan.Zero
    };
}

/// <summary>
/// Thread-safe histogram for latency measurements using reservoir sampling.
/// Maintains a fixed-size reservoir of samples to bound memory usage while
/// preserving statistical accuracy for percentile calculations.
/// </summary>
public sealed class LatencyHistogram
{
    private const int ReservoirSize = 10_000;
    private readonly long[] _reservoir = new long[ReservoirSize];
    private long _totalCount;
    private long _totalSum;
    private long _min = long.MaxValue;
    private long _max = long.MinValue;
    private readonly Lock _lock = new();

    /// <summary>
    /// Records a latency measurement using reservoir sampling (Algorithm R).
    /// Thread-safe. O(1) memory regardless of total message count.
    /// </summary>
    public void Record(TimeSpan latency)
    {
        var ms = (long)latency.TotalMilliseconds;

        lock (_lock)
        {
            var count = _totalCount;
            _totalCount = count + 1;
            _totalSum += ms;

            if (ms < _min) _min = ms;
            if (ms > _max) _max = ms;

            if (count < ReservoirSize)
            {
                // Fill the reservoir first
                _reservoir[count] = ms;
            }
            else
            {
                // Reservoir sampling: replace a random element with decreasing probability
                var index = Random.Shared.NextInt64(count + 1);
                if (index < ReservoirSize)
                {
                    _reservoir[index] = ms;
                }
            }
        }
    }

    /// <summary>
    /// Gets the total number of recorded samples.
    /// </summary>
    public long Count
    {
        get
        {
            lock (_lock)
            {
                return _totalCount;
            }
        }
    }

    /// <summary>
    /// Computes statistics from the reservoir sample.
    /// </summary>
    public LatencyStatistics GetStatistics()
    {
        long[] sample;
        long totalCount;
        long totalSum;
        long min;
        long max;

        lock (_lock)
        {
            totalCount = _totalCount;
            if (totalCount == 0)
                return LatencyStatistics.Empty;

            totalSum = _totalSum;
            min = _min;
            max = _max;

            var sampleSize = (int)Math.Min(totalCount, ReservoirSize);
            sample = new long[sampleSize];
            Array.Copy(_reservoir, sample, sampleSize);
        }

        Array.Sort(sample);

        return new LatencyStatistics
        {
            Count = (int)totalCount,
            Min = TimeSpan.FromMilliseconds(min),
            Max = TimeSpan.FromMilliseconds(max),
            Mean = TimeSpan.FromMilliseconds((double)totalSum / totalCount),
            P50 = TimeSpan.FromMilliseconds(GetPercentile(sample, 50)),
            P95 = TimeSpan.FromMilliseconds(GetPercentile(sample, 95)),
            P99 = TimeSpan.FromMilliseconds(GetPercentile(sample, 99)),
            P999 = TimeSpan.FromMilliseconds(GetPercentile(sample, 99.9))
        };
    }

    /// <summary>
    /// Clears all recorded latencies.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _totalCount = 0;
            _totalSum = 0;
            _min = long.MaxValue;
            _max = long.MinValue;
            Array.Clear(_reservoir);
        }
    }

    private static long GetPercentile(long[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Length - 1))];
    }
}
