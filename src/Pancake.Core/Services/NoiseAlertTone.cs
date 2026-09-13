namespace Pancake.Services;

public static class NoiseAlertTone
{
    public const int SampleRate = 24000;
    public const double DurationSeconds = 0.52;
    // 三个 120 ms 的 1000 Hz 短音，间隔 80 ms；淡入淡出避免爆音。
    public static byte[] CreatePcm()
    {
        byte[] pcm = new byte[(int)(SampleRate * DurationSeconds) * 2];
        for (int i = 0; i < pcm.Length / 2; i++)
        {
            double time = i / (double)SampleRate;
            double phase = time % 0.2;
            double envelope = phase < 0.12 ? Math.Min(1, Math.Min(phase / 0.005, (0.12 - phase) / 0.005)) : 0;
            short sample = (short)(Math.Sin(2 * Math.PI * 1000 * time) * envelope * 0.3 * short.MaxValue);
            pcm[i * 2] = (byte)sample;
            pcm[i * 2 + 1] = (byte)(sample >> 8);
        }
        return pcm;
    }
}
