using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>平台尚未实现提示音时的占位实现。</summary>
public sealed class SilentNoiseAlertPlayback : INoiseAlertPlayback
{
    public bool IsSupported => false;

    public double Volume { get; set; } = 1;

    public void Play(byte[] pcm16Mono, int sampleRate)
    {
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
