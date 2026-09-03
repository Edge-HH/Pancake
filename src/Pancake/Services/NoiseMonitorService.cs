using NAudio.Wave;

namespace Pancake.Services;

/// <summary>
/// Converts microphone PCM samples into a smoothed classroom noise estimate.
/// CalibrationOffsetDb lets each classroom align the estimate with a trusted sound meter.
/// </summary>
public sealed class NoiseMonitorService : IDisposable
{
    private WaveInEvent? _capture;
    private double? _smoothedDb;

    public event EventHandler<double>? LevelAvailable;
    public event EventHandler<string>? CaptureFailed;

    public double CalibrationOffsetDb { get; set; }
    public int SampleRate { get; private set; } = 16000;

    public void Start(int sampleRate)
    {
        Stop();
        SampleRate = sampleRate;
        try
        {
            _capture = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(sampleRate, 16, 1),
                BufferMilliseconds = 100,
                NumberOfBuffers = 3
            };
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

    public void Restart(int sampleRate) => Start(sampleRate);

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= Capture_DataAvailable;
        _capture.RecordingStopped -= Capture_RecordingStopped;
        try
        {
            _capture.StopRecording();
        }
        catch
        {
            // A device can disappear while recording; disposal is still required.
        }
        _capture.Dispose();
        _capture = null;
        _smoothedDb = null;
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        int sampleCount = e.BytesRecorded / 2;
        if (sampleCount == 0)
        {
            return;
        }

        double sumSquares = 0;
        for (int offset = 0; offset + 1 < e.BytesRecorded; offset += 2)
        {
            short sample = BitConverter.ToInt16(e.Buffer, offset);
            double normalized = sample / 32768d;
            sumSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);
        double dbfs = 20 * Math.Log10(Math.Max(rms, 0.0000001));
        double estimatedDb = Math.Clamp(94 + dbfs + CalibrationOffsetDb, 20, 120);
        _smoothedDb = _smoothedDb is null ? estimatedDb : (_smoothedDb.Value * 0.78) + (estimatedDb * 0.22);
        LevelAvailable?.Invoke(this, _smoothedDb.Value);
    }

    private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            CaptureFailed?.Invoke(this, e.Exception.Message);
        }
    }

    public void Dispose() => Stop();
}
