using NAudio.Wave;

namespace Pancake.Services;

public sealed class NoiseAlertPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private RawSourceWaveStream? _stream;
    private float _volume = 1;
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_output is not null) _output.Volume = _volume;
        }
    }

    public void Prepare()
    {
        if (_output is not null) return;
        var stream = new RawSourceWaveStream(new MemoryStream(NoiseAlertTone.CreatePcm()), new WaveFormat(NoiseAlertTone.SampleRate, 16, 1));
        var output = new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 2, Volume = _volume };
        try { output.Init(stream); }
        catch { output.Dispose(); stream.Dispose(); throw; }
        _stream = stream;
        _output = output;
    }
    public void Play()
    {
        Prepare();
        if (_output!.PlaybackState == PlaybackState.Playing) return;
        _stream!.Position = 0;
        _output.Play();
    }
    public void Dispose()
    {
        _output?.Dispose();
        _stream?.Dispose();
        _output = null;
        _stream = null;
    }
}
