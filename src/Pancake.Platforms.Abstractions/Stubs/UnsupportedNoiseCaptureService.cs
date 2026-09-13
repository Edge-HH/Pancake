using Pancake.Platforms.Abstraction.Models;
using Pancake.Platforms.Abstraction.Services;

namespace Pancake.Platforms.Abstraction.Stubs;

/// <summary>平台尚未实现麦克风采集时的占位实现；IsSupported 为 false，界面会隐藏噪音检测。</summary>
public sealed class UnsupportedNoiseCaptureService : INoiseCaptureService
{
    public bool IsSupported => false;

    public int SampleRate { get; set; } = 16000;

    // 占位实现不会产生采样与失败事件；显式空访问器避免出现“事件从未使用”的编译警告。
    public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable
    {
        add { }
        remove { }
    }

    public event EventHandler<string>? CaptureFailed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<AudioInputDevice> GetDevices() => [new("", "系统默认输入设备")];

    public void Start(string deviceId)
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
        // 占位实现不持有任何非托管资源。
        GC.SuppressFinalize(this);
    }
}
