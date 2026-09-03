namespace PowerX.Core.Telemetry;

/// <summary>
/// Fixed-capacity ring buffer for one metric's recent history. Bounded memory —
/// old samples are overwritten, never retained (docs/PRODUCT_SPEC.md §7, prompt §57).
/// </summary>
public sealed class MetricRing(int capacity)
{
    private readonly double[] _buf = new double[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private int _start;
    private int _count;

    public int Capacity => _buf.Length;
    public int Count => _count;
    public double Latest => _count == 0 ? 0 : _buf[(_start + _count - 1) % _buf.Length];

    public void Add(double value)
    {
        int end = (_start + _count) % _buf.Length;
        _buf[end] = value;
        if (_count < _buf.Length) _count++;
        else _start = (_start + 1) % _buf.Length;
    }

    /// <summary>
    /// Fill an empty ring with <paramref name="value"/> so a chart opens already full on the
    /// first sample instead of drawing in from the left over several seconds. No-op once samples exist.
    /// </summary>
    public void Seed(double value)
    {
        if (_count > 0) return;
        Array.Fill(_buf, value);
        _start = 0;
        _count = _buf.Length;
    }

    /// <summary>Oldest → newest.</summary>
    public double[] ToArray()
    {
        var result = new double[_count];
        for (int i = 0; i < _count; i++) result[i] = _buf[(_start + i) % _buf.Length];
        return result;
    }

    public double Max()
    {
        double m = 0;
        for (int i = 0; i < _count; i++) m = Math.Max(m, _buf[(_start + i) % _buf.Length]);
        return m;
    }

    public double Average()
    {
        if (_count == 0) return 0;
        double s = 0;
        for (int i = 0; i < _count; i++) s += _buf[(_start + i) % _buf.Length];
        return s / _count;
    }
}
