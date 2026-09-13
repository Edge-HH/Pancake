namespace Pancake.Platforms.Abstraction.Models;

/// <summary>可用的录音输入设备；Id 为空字符串表示使用系统默认设备。</summary>
public sealed record AudioInputDevice(string Id, string Name);
