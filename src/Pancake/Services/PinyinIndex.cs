using System.Text;

namespace Pancake.Services;

/// <summary>
/// 汉字到无声调拼音的只读索引，数据来自随包分发的 Assets/Pinyin/pinyin.txt。
/// 未收录的字符按原字符参与匹配，因此缺数据时仍能使用汉字前缀匹配。
/// </summary>
public sealed class PinyinIndex
{
    private static readonly Lazy<PinyinIndex> SharedIndex = new(() => new PinyinIndex(DefaultPath));

    public static PinyinIndex Shared => SharedIndex.Value;

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Pinyin", "pinyin.txt");

    private readonly Dictionary<char, string> _readings = [];

    public PinyinIndex(string path)
    {
        Load(path);
    }

    public int Count => _readings.Count;

    public bool TryGetReading(char character, out string pinyin) => _readings.TryGetValue(character, out pinyin!);

    /// <summary>把文本展开为“全拼串 + 首字母串”，供拼音前缀与首字母匹配使用。</summary>
    public (string Full, string Initials) Expand(string text)
    {
        StringBuilder full = new(text.Length * 3);
        StringBuilder initials = new(text.Length);
        foreach (char character in text)
        {
            if (_readings.TryGetValue(character, out string? pinyin) && pinyin.Length > 0)
            {
                full.Append(pinyin);
                initials.Append(pinyin[0]);
            }
            else
            {
                full.Append(character);
                initials.Append(character);
            }
        }

        return (full.ToString().ToLowerInvariant(), initials.ToString().ToLowerInvariant());
    }

    private void Load(string path)
    {
        // 资源缺失只降级，不影响看板启动；拼音匹配会退化为汉字匹配。
        try
        {
            if (!File.Exists(path)) return;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.TrimStart('\uFEFF');
                int separator = line.IndexOf('\t');
                if (separator != 1) continue;
                string pinyin = line[(separator + 1)..].Trim();
                if (pinyin.Length == 0) continue;
                _readings[line[0]] = pinyin;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
