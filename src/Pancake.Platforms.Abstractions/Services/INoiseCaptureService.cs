using Pancake.Platforms.Abstraction.Models;

namespace Pancake.Platforms.Abstraction.Services;

/// <summary>一次采集到的 PCM 帧；音量换算由 Core 的 NoiseLevelMeter 负责。</summary>
public sealed class AudioSamplesEventArgs(float[] samples, int sampleRate, int channels) : EventArgs
{
    public float[] Samples { get; } = samples;
    public int SampleRate { get; } = sampleRate;
    public int Channels { get; } = channels;
}

/// <summary>麦克风采集能力。不支持的平台用 Stub 实现并把入口隐藏。</summary>
public interface INoiseCaptureService : IDisposable
{
    bool IsSupported { get; }

    /// <summary>请求的采集采样率；实际格式由设备共享模式决定。</summary>
    int SampleRate { get; set; }

    /// <summary>枚举输入设备；首项固定为“系统默认输入设备”。</summary>
    IReadOnlyList<AudioInputDevice> GetDevices();

    void Start(string deviceId);

    void Stop();

    event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;

    event EventHandler<string>? CaptureFailed;
}
