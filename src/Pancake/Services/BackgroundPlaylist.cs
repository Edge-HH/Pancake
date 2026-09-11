namespace Pancake.Services;

/// <summary>独立于播放器的选曲逻辑；坏文件在本次配置中跳过，避免全部失败时无限重试。</summary>
public sealed class BackgroundPlaylist
{
    private string[] _paths = [];
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private int _index;
    public string Current => _paths.Length == 0 ? "" : _paths[_index];
    public static double NormalizeInterval(double seconds) => double.IsFinite(seconds) ? Math.Clamp(seconds, 1, 86400) : 60;
    public bool Configure(BackgroundSettings settings)
    {
        string[] paths = (settings.PlaylistEnabled ? settings.Playlist ?? [] : new List<string> { settings.ImagePath })
            .Where(path => MediaLibrary.IsSupported(path) && File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_paths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)) return false;
        string previous = Current;
        _paths = paths;
        _failed.Clear();
        _index = Math.Max(0, Array.FindIndex(paths, path => string.Equals(path, previous, StringComparison.OrdinalIgnoreCase)));
        return true;
    }
    public bool Advance(bool shuffle, bool failed = false)
    {
        if (failed) _failed.Add(Current);
        int[] candidates = Enumerable.Range(0, _paths.Length).Where(i => i != _index && !_failed.Contains(_paths[i]) && File.Exists(_paths[i])).ToArray();
        if (candidates.Length == 0) return false;
        _index = shuffle ? candidates[Random.Shared.Next(candidates.Length)]
            : candidates.FirstOrDefault(i => i > _index, candidates[0]);
        return true;
    }
}
