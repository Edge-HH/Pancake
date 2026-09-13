namespace Pancake.Services;

/// <summary>
/// 把麦克风 PCM 采样换算成教室噪音估算值。算法与平台无关：
/// 先按检测窗口累计均方根，再换算成 dBFS 并叠加用户校准偏移。
/// </summary>
public sealed class NoiseLevelMeter
{
    /// <summary>换算基准：满量程对应 94 dB SPL，因此结果为“估算声压级”。</summary>
    private const double FullScaleDbSpl = 94;

    private const double SilenceFloor = 0.0000001;

    private readonly object _gate = new();
    private double _sumSquares;
    private long _sampleCount;
    private double? _rawDb;
    private DateTime _lastSampleUtc;

    /// <summary>统计窗口长度（秒），由设置页的检测间隔决定。</summary>
    public double IntervalSeconds { get; set; } = 0.1;

    /// <summary>用户校准偏移，叠加在原始估算值上。</summary>
    public double CalibrationOffsetDb { get; set; }

    /// <summary>最近一次原始估算值，用于校准；超过 3 秒未更新即视为失效。</summary>
    public double? RawDb
    {
        get { lock (_gate) return _rawDb; }
    }

    /// <summary>
    /// 累积一批采样。返回本窗口的显示音量（已叠加校准）；样本尚未填满一个窗口时返回 null。
    /// </summary>
    public double? Add(ReadOnlySpan<float> samples, int sampleRate, int channels)
    {
        if (samples.IsEmpty || sampleRate <= 0) return null;

        double blockSquares = 0;
        lock (_gate)
        {
            foreach (float sample in samples)
            {
                double value = sample;
                blockSquares += value * value;
                _sumSquares += value * value;
                _sampleCount++;
            }

            // 采样率与声道数决定一个统计窗口需要多少个样本。
            long windowSamples = (long)Math.Max(1, sampleRate * Math.Max(1, channels) * IntervalSeconds);
            if (_sampleCount < windowSamples || _sampleCount <= 0) return null;

            _rawDb = FullScaleDbSpl + 20 * Math.Log10(Math.Max(Math.Sqrt(_sumSquares / _sampleCount), SilenceFloor));
            _lastSampleUtc = DateTime.UtcNow;
            double level = Math.Clamp(_rawDb.Value + CalibrationOffsetDb, 20, 120);
            _sumSquares = 0;
            _sampleCount = 0;
            return level;
        }
    }

    /// <summary>
    /// 按单个采集块立即估算音量，供报警使用，不等待完整的显示统计窗口。
    /// </summary>
    public double FastLevel(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 20;
        double sumSquares = 0;
        foreach (float sample in samples) sumSquares += (double)sample * sample;
        double db = FullScaleDbSpl + 20 * Math.Log10(Math.Max(Math.Sqrt(sumSquares / samples.Length), SilenceFloor));
        return Math.Clamp(db + CalibrationOffsetDb, 20, 120);
    }

    /// <summary>把当前环境对齐到用户设定的目标音量；样本过期或目标越界时返回 false。</summary>
    public bool TryCalibrate(double targetDb)
    {
        lock (_gate)
        {
            if (!double.IsFinite(targetDb) || targetDb < 20 || targetDb > 120 || _rawDb is null ||
                DateTime.UtcNow - _lastSampleUtc > TimeSpan.FromSeconds(3))
            {
                return false;
            }

            CalibrationOffsetDb = targetDb - _rawDb.Value;
            return true;
        }
    }

    /// <summary>丢弃未完成的统计窗口；设备切换或暂停检测时调用。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _sumSquares = 0;
            _sampleCount = 0;
            _rawDb = null;
        }
    }
}
