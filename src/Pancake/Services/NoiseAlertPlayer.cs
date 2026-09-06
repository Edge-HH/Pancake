using NAudio.Wave;

namespace Pancake.Services;

public sealed class NoiseAlertPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private RawSourceWaveStream? _stream;
    public void Prepare()
    {
        if (_output is not null) return;
        var stream = new RawSourceWaveStream(new MemoryStream(NoiseAlertTone.CreatePcm()), new WaveFormat(NoiseAlertTone.SampleRate, 16, 1));
        var output = new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 2 };
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
