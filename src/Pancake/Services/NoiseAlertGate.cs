namespace Pancake.Services;

/// <summary>新一轮超阈值立即提醒；短暂屏蔽扬声器回授，持续吵闹每两秒提醒。</summary>
public sealed class NoiseAlertGate
{
    private double _lastAlert = double.NegativeInfinity;
    private bool _noisy;
    public bool ShouldPlay(double level, double threshold, bool enabled, double seconds)
    {
        if (!enabled) { Reset(); return false; }
        if (seconds - _lastAlert < 0.75) return false;
        if (level < threshold - 3) _noisy = false;
        if (level < threshold) return false;
        if (_noisy && seconds - _lastAlert < 2) return false;
        _noisy = true;
        _lastAlert = seconds;
        return true;
    }
    public void Reset() { _noisy = false; _lastAlert = double.NegativeInfinity; }
}
