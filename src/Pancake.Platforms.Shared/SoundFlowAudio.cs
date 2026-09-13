using System.Collections.Concurrent;
using Pancake.Platforms.Abstraction.Models;
using Pancake.Platforms.Abstraction.Services;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace Pancake.Platforms.Shared;

/// <summary>
/// 采集与播放共用一个 MiniAudio 引擎。引擎初始化会枚举系统设备，因此放在首次使用时懒加载，
/// 未使用噪音检测的用户不会为此付出启动开销。
/// </summary>
internal static class AudioEngineHost
{
    private static readonly Lazy<MiniAudioEngine> Shared = new(() => new MiniAudioEngine(), isThreadSafe: true);

    public static MiniAudioEngine Engine => Shared.Value;

    public static AudioFormat Format(int sampleRate, int channels) => new()
    {
        SampleRate = sampleRate,
        Channels = channels,
        Layout = channels == 1 ? ChannelLayout.Mono : ChannelLayout.Stereo,
        Format = SampleFormat.F32
    };

    /// <summary>设备标识用指针表示，这里统一转成字符串供设置页保存与比较。</summary>
    public static string Id(DeviceInfo info) => info.Id.ToInt64().ToString("x");
}

/// <summary>
/// 基于 SoundFlow(MiniAudio) 的麦克风采集：按设备格式连续读取浮点采样，
/// 只上报原始 PCM，音量换算交给核心层的 NoiseLevelMeter。
/// </summary>
public sealed class SoundFlowNoiseCaptureService : INoiseCaptureService
{
    private AudioCaptureDevice? _device;
    private int _sampleRate = 16000;

    public bool IsSupported => true;

    /// <summary>采集采样率；Windows 上实际格式由系统共享模式决定，这里作为请求值。</summary>
    public int SampleRate
    {
        get => _sampleRate;
        set => _sampleRate = value is >= 8000 and <= 96000 ? value : 16000;
    }

    public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;

    public event EventHandler<string>? CaptureFailed;

    public IReadOnlyList<AudioInputDevice> GetDevices()
    {
        List<AudioInputDevice> devices = [new(string.Empty, "系统默认输入设备")];
        try
        {
            foreach (DeviceInfo info in AudioEngineHost.Engine.CaptureDevices)
            {
                devices.Add(new AudioInputDevice(AudioEngineHost.Id(info), info.Name ?? "输入设备"));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or DllNotFoundException)
        {
            // 没有可用音频后端时只保留默认项，设置页会说明设备不可用。
        }

        return devices;
    }

    public void Start(string deviceId)
    {
        Stop();
        try
        {
            MiniAudioEngine engine = AudioEngineHost.Engine;
            DeviceInfo? target = null;
            if (!string.IsNullOrEmpty(deviceId))
            {
                foreach (DeviceInfo info in engine.CaptureDevices)
                {
                    if (AudioEngineHost.Id(info) == deviceId)
                    {
                        target = info;
                        break;
                    }
                }
            }

            _device = engine.InitializeCaptureDevice(target, AudioEngineHost.Format(_sampleRate, 1), new MiniAudioDeviceConfig());
            _device.OnAudioProcessed += OnAudioProcessed;
            _device.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or DllNotFoundException or ArgumentException)
        {
            Stop();
            CaptureFailed?.Invoke(this, ex.Message);
        }
    }

    public void Stop()
    {
        AudioCaptureDevice? device = _device;
        _device = null;
        if (device is null) return;
        device.OnAudioProcessed -= OnAudioProcessed;
        try
        {
            device.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 设备被拔出时仍要继续释放句柄。
        }

        device.Dispose();
    }

    /// <summary>
    /// 采集回调在音频线程触发，这里只做一次复制；音量统计与报警判断都在核心层完成。
    /// </summary>
    private void OnAudioProcessed(Span<float> samples, Capability capability)
    {
        if (_device is null || samples.IsEmpty) return;
        AudioFormat format = _device.Format;
        SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples.ToArray(), format.SampleRate, format.Channels));
    }

    public void Dispose() => Stop();
}

/// <summary>
/// 基于 SoundFlow(MiniAudio) 的提示音播放。每次播放创建一个播放器，播完即释放，
/// 避免长时间运行后混音器里堆积组件。
/// </summary>
public sealed class SoundFlowNoiseAlertPlayback : INoiseAlertPlayback
{
    private readonly object _gate = new();
    private readonly ConcurrentBag<SoundPlayer> _finished = [];
    private AudioPlaybackDevice? _device;
    private double _volume = 1;

    public bool IsSupported => true;

    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, 1);
    }

    public void Play(byte[] pcm16Mono, int sampleRate)
    {
        if (pcm16Mono.Length == 0 || sampleRate <= 0) return;
        try
        {
            AudioPlaybackDevice device = EnsureDevice(sampleRate);
            MemoryStream stream = new(pcm16Mono, writable: false);
            AudioFormat format = new()
            {
                SampleRate = sampleRate,
                Channels = 1,
                Layout = ChannelLayout.Mono,
                Format = SampleFormat.S16
            };
            SoundPlayer player = new(AudioEngineHost.Engine, format, new StreamDataProvider(AudioEngineHost.Engine, format, stream));
            player.Volume = (float)_volume;
            player.PlaybackEnded += (_, _) => Release(player);
            device.MasterMixer.AddComponent(player);
            player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or DllNotFoundException or ArgumentException)
        {
            // 播放失败不应打断噪音检测本身。
        }
    }

    /// <summary>按提示音自己的采样率创建播放设备；采样率变化时重建。</summary>
    private AudioPlaybackDevice EnsureDevice(int sampleRate)
    {
        lock (_gate)
        {
            if (_device is not null && _device.Format.SampleRate == sampleRate) return _device;
            AudioPlaybackDevice? previous = _device;
            _device = null;
            previous?.Dispose();

            MiniAudioEngine engine = AudioEngineHost.Engine;
            DeviceInfo? defaultDevice = null;
            foreach (DeviceInfo info in engine.PlaybackDevices)
            {
                if (info.IsDefault)
                {
                    defaultDevice = info;
                    break;
                }
            }

            AudioPlaybackDevice device = engine.InitializePlaybackDevice(
                defaultDevice,
                AudioEngineHost.Format(sampleRate, 1),
                new MiniAudioDeviceConfig());
            device.MasterMixer.Volume = 1f;
            device.Start();
            _device = device;
            return device;
        }
    }

    /// <summary>播放结束后把组件从混音器移除并释放。</summary>
    private void Release(SoundPlayer player)
    {
        try
        {
            _device?.MasterMixer.RemoveComponent(player);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 设备已重建时组件会随旧设备一起释放。
        }

        player.Dispose();
        _finished.Add(player);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _device?.Dispose();
            _device = null;
        }

        while (_finished.TryTake(out SoundPlayer? player)) player.Dispose();
    }
}
