namespace Pancake.Platforms.Abstraction.Services;

/// <summary>噪音提示音播放能力。</summary>
public interface INoiseAlertPlayback : IDisposable
{
    bool IsSupported { get; }

    /// <summary>0~1 的音量，超出范围由实现自行截断。</summary>
    double Volume { get; set; }

    /// <summary>播放一段 16 位单声道 PCM 音频。</summary>
    void Play(byte[] pcm16Mono, int sampleRate);
}
