using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Pancake.Services;

public sealed record MicrophoneDevice(string Id, string Name);

/// <summary>使用 Windows 共享模式采集，按检测窗口累计能量，不保存音频。</summary>
public sealed class NoiseMonitorService : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private double _sumSquares;
    private long _sampleCount;
    private double? _rawDb;
    private DateTime _lastSampleUtc;
    public event EventHandler<double>? LevelAvailable;
    public event EventHandler<double>? FastLevelAvailable;
    public event EventHandler<string>? CaptureFailed;
    public double CalibrationOffsetDb { get; set; }
    public double IntervalSeconds { get; set; } = 0.1;

    public static List<MicrophoneDevice> GetDevices()
    {
        using MMDeviceEnumerator enumerator = new();
        List<MicrophoneDevice> devices = [new("", "系统默认输入设备")];
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device) devices.Add(new(device.ID, device.FriendlyName));
        }
        return devices;
    }

    public void Start(string deviceId)
    {
        Stop();
        try
        {
            using MMDeviceEnumerator enumerator = new();
            _device = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                : enumerator.GetDevice(deviceId);
            _capture = new WasapiCapture(_device, true, 50);
            _capture.DataAvailable += Capture_DataAvailable;
            _capture.RecordingStopped += Capture_RecordingStopped;
            _capture.StartRecording();
        }
        catch (Exception exception)
        {
            Stop();
            CaptureFailed?.Invoke(this, exception.Message);
        }
    }

    public bool TryCalibrate(double targetDb)
    {
        lock (_gate)
        {
            if (!double.IsFinite(targetDb) || targetDb < 20 || targetDb > 120 || _rawDb is null ||
                DateTime.UtcNow - _lastSampleUtc > TimeSpan.FromSeconds(3)) return false;
            CalibrationOffsetDb = targetDb - _rawDb.Value;
            return true;
        }
    }

    public void Stop()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= Capture_DataAvailable;
            capture.RecordingStopped -= Capture_RecordingStopped;
            try { capture.StopRecording(); }
            catch { /* 设备被拔出时仍须释放句柄。 */ }
            capture.Dispose();
        }
        _device?.Dispose();
        _device = null;
        lock (_gate) { _sumSquares = 0; _sampleCount = 0; _rawDb = null; }
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiCapture capture || capture != _capture) return;
        WaveFormat format = capture.WaveFormat;
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible && extensible.SubFormat == NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float;
        int bytesPerSample = format.BitsPerSample / 8;
        double? level = null;
        double blockSquares = 0;
        int blockSamples = 0;
        lock (_gate)
        {
            for (int offset = 0; offset + bytesPerSample <= e.BytesRecorded; offset += bytesPerSample)
            {
                double sample = floating ? BitConverter.ToSingle(e.Buffer, offset) : bytesPerSample switch
                {
                    2 => BitConverter.ToInt16(e.Buffer, offset) / 32768d,
                    3 => ((e.Buffer[offset] | e.Buffer[offset + 1] << 8 | e.Buffer[offset + 2] << 16) << 8 >> 8) / 8388608d,
                    4 => BitConverter.ToInt32(e.Buffer, offset) / 2147483648d,
                    _ => (e.Buffer[offset] - 128) / 128d
                };
                blockSquares += sample * sample;
                blockSamples++;
                _sumSquares += sample * sample;
                _sampleCount++;
            }
            if (_sampleCount >= format.SampleRate * format.Channels * IntervalSeconds)
            {
                _rawDb = 94 + 20 * Math.Log10(Math.Max(Math.Sqrt(_sumSquares / _sampleCount), 0.0000001));
                _lastSampleUtc = DateTime.UtcNow;
                level = Math.Clamp(_rawDb.Value + CalibrationOffsetDb, 20, 120);
                _sumSquares = 0;
                _sampleCount = 0;
            }
        }
        // 报警按每个采集块计算，不等待用户设置的最长 2 秒显示统计窗口。
        if (blockSamples > 0)
        {
            double fastDb = 94 + 20 * Math.Log10(Math.Max(Math.Sqrt(blockSquares / blockSamples), 0.0000001));
            FastLevelAvailable?.Invoke(this, Math.Clamp(fastDb + CalibrationOffsetDb, 20, 120));
        }
        if (level is double value) LevelAvailable?.Invoke(this, value);
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate) _rawDb = null;
        if (e.Exception is not null) CaptureFailed?.Invoke(this, e.Exception.Message);
    }

    public void Dispose() => Stop();
}
