using System.Text;

namespace Pancake.Services;

/// <summary>
/// 学科补全与作业补全的纯逻辑实现：切词、计数、收录、过期、屏蔽与匹配。
/// 界面层只负责取值和展示，便于设置逻辑测试直接覆盖这里的行为。
/// </summary>
public sealed class AutofillService
{
    public const int MaxSuggestions = 8;
    public const int MaxQueryLength = 20;
    public const int PendingExpiryDays = 14;
    public const int PromotedExpiryDays = 90;
    public const int MaxAutoItems = 300;
    public const int MaxManualItems = 200;
    public const int MaxSubjectEntries = 200;
    public const int MaxTokenLength = 12;
    public const int MaxSubjectNameLength = 12;
    public const string PlaceholderHomework = "在这里输入作业内容";
    public const string GlobalScope = "Global";
    public const string BuiltInSource = "BuiltIn";
    public const string LearnedSource = "Learned";
    public const string ManualSource = "Manual";
    public const string AutoSource = "Auto";

    private static readonly string[] BuiltInSubjects =
    [
        "语文", "数学", "英语", "物理", "化学", "生物", "政治", "历史", "地理", "科学",
        "信息技术", "体育", "音乐", "美术", "劳动", "道德与法治"
    ];

    private static readonly string[] SubjectPalette =
    [
        "#FBBF24", "#4ADE80", "#7567FF", "#60A5FA", "#F472B6", "#2DD4BF",
        "#F87171", "#65D46E", "#818CF8", "#A8D5BA", "#E8D5A5", "#BCBCE3"
    ];

    private readonly Func<AutofillSettings> _accessor;
    private readonly PinyinIndex _pinyin;
    // 随机配色时记住每个学科上一次抽到的颜色，避免连续两次补全看起来像没生效。
    private readonly Dictionary<string, string> _lastRandomColors = new(StringComparer.OrdinalIgnoreCase);

    public AutofillService(Func<AutofillSettings> accessor, PinyinIndex? pinyin = null)
    {
        _accessor = accessor;
        _pinyin = pinyin ?? PinyinIndex.Shared;
    }

    public AutofillService(AutofillSettings settings, PinyinIndex? pinyin = null)
        : this(() => settings, pinyin)
    {
    }

    public AutofillSettings Settings => _accessor();

    public SubjectCompletionSettings Subject => Settings.Subject;

    public HomeworkCompletionSettings Homework => Settings.Homework;

    public static string NormalizeLevel(string? level) => level is "Loose" or "Normal" or "Strict" ? level : "Normal";

    /// <summary>作业收录阈值：宽松 2 次、正常 3 次、严格 5 次。</summary>
    public static int RecordThreshold(string? level) => NormalizeLevel(level) switch
    {
        "Loose" => 2,
        "Strict" => 5,
        _ => 3
    };

    /// <summary>保证内置学科清单存在，已有同名条目时保留用户配置。</summary>
    public bool EnsureBuiltIns()
    {
        SubjectCompletionSettings subject = Subject;
        bool changed = false;
        for (int index = 0; index < BuiltInSubjects.Length; index++)
        {
            string name = BuiltInSubjects[index];
            if (subject.Subjects.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            subject.Subjects.Add(new SubjectSuggestion
            {
                Name = name,
                Enabled = true,
                Color = SubjectPalette[index % SubjectPalette.Length],
                Source = BuiltInSource
            });
            changed = true;
        }

        return changed;
    }

    /// <param name="includeExact">
    /// 输入法上屏的文字本身就是某个学科时也给该候选，便于接着用键盘套用学科颜色；
    /// 普通输入保持“已经输入完整学科名就不再提示”的行为。
    /// </param>
    public IReadOnlyList<SubjectSuggestion> MatchSubjects(string? typed, int max = MaxSuggestions, bool includeExact = false)
    {
        SubjectCompletionSettings subject = Subject;
        if (!subject.Enabled) return [];
        string query = typed?.Trim() ?? string.Empty;
        if (query.Length is 0 or > MaxQueryLength) return [];
        string level = NormalizeLevel(subject.MatchLevel);
        if (query.Length < MinimumInput(level, query)) return [];

        List<(int Score, SubjectSuggestion Item, int Order)> matches = [];
        int order = 0;
        foreach (SubjectSuggestion item in subject.Subjects)
        {
            int current = order++;
            if (!item.Enabled || string.IsNullOrWhiteSpace(item.Name)) continue;
            if (!includeExact && item.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) continue;
            int score = Score(item.Name, query, level);
            if (score > 0) matches.Add((score, item, current));
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Item.LastUsedAt)
            .ThenBy(match => match.Order)
            .Take(max)
            .Select(match => match.Item)
            .ToList();
    }

    /// <summary>默认标题被清除后展示的推荐学科：最近使用的排在前面。</summary>
    public IReadOnlyList<SubjectSuggestion> RecommendSubjects(int max)
    {
        SubjectCompletionSettings subject = Subject;
        if (!subject.Enabled) return [];
        return subject.Subjects
            .Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Name))
            .OrderByDescending(item => item.LastUsedAt)
            .ThenBy(item => item.Source == BuiltInSource ? 0 : 1)
            .Take(max)
            .ToList();
    }

    public IReadOnlyList<HomeworkSuggestion> MatchHomework(string? typed, string? subjectName, int max = MaxSuggestions)
    {
        HomeworkCompletionSettings homework = Homework;
        if (!homework.Enabled) return [];
        string query = typed?.Trim() ?? string.Empty;
        if (query.Length is 0 or > MaxQueryLength) return [];
        if (query.Length < MinimumInput("Normal", query)) return [];
        string? subject = NormalizeSubject(subjectName);

        List<(int Score, HomeworkSuggestion Item, int Order)> matches = [];
        int order = 0;
        foreach (HomeworkSuggestion item in homework.Items)
        {
            int current = order++;
            if (!item.Promoted || string.IsNullOrWhiteSpace(item.Text)) continue;
            if (IsBlocked(item.Text)) continue;
            if (!Covers(item, subject)) continue;
            if (item.Text.Equals(query, StringComparison.OrdinalIgnoreCase)) continue;
            int score = Score(item.Text, query, "Normal");
            if (score > 0) matches.Add((score, item, current));
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Item.LastSeenAt)
            .ThenBy(match => match.Order)
            .Take(max)
            .Select(match => match.Item)
            .ToList();
    }

    /// <summary>一条作业编辑结束时统计一次；同一条内重复出现的词只算一次。</summary>
    public bool RecordHomework(string? content, string? subjectName)
    {
        HomeworkCompletionSettings homework = Homework;
        if (!homework.Enabled || !homework.AutoRecord) return false;
        if (string.IsNullOrWhiteSpace(content)) return false;

        DateTime now = DateTime.Now;
        string scope = CurrentScope(subjectName);
        int threshold = RecordThreshold(homework.RecordLevel);
        bool changed = false;
        foreach (string token in Tokenize(content))
        {
            if (!IsRecordable(token) || IsBlocked(token)) continue;
            HomeworkSuggestion? existing = homework.Items.FirstOrDefault(item =>
                item.Text.Equals(token, StringComparison.OrdinalIgnoreCase) && Covers(item, scope == GlobalScope ? null : scope));
            if (existing is not null)
            {
                existing.LastSeenAt = now;
                // 已收录后继续累计使用次数，列表里的次数才不会停在收录那一刻。
                if (existing.Source == AutoSource)
                {
                    existing.Count++;
                    if (!existing.Promoted && existing.Count >= threshold) existing.Promoted = true;
                }

                changed = true;
                continue;
            }

            bool global = scope == GlobalScope;
            homework.Items.Add(new HomeworkSuggestion
            {
                Text = token,
                Source = AutoSource,
                IsGlobal = global,
                Subjects = global ? [] : [scope],
                Count = 1,
                Promoted = 1 >= threshold,
                FirstSeenAt = now,
                LastSeenAt = now
            });
            changed = true;
        }

        if (changed) TrimAutoItems();
        return changed;
    }

    public void NotifySubjectCompleted(SubjectSuggestion item) => item.LastUsedAt = DateTime.Now;

    /// <summary>
    /// 学科补全实际套用的颜色：随机配色从预设色板抽一个（尽量不与上一次相同），
    /// 否则沿用学科自身的颜色。抽出的仍是预设原值，显示与套用时继续跟随当前色系。
    /// </summary>
    public string ResolveSubjectColor(SubjectSuggestion item)
    {
        if (!item.RandomColor) return item.Color;

        IReadOnlyList<string> presets = ColorPalette.Presets;
        if (presets.Count == 0) return item.Color;

        int index = Random.Shared.Next(presets.Count);
        if (presets.Count > 1 && _lastRandomColors.TryGetValue(item.Name, out string? previous)
            && presets[index].Equals(previous, StringComparison.OrdinalIgnoreCase))
            index = (index + 1 + Random.Shared.Next(presets.Count - 1)) % presets.Count;

        _lastRandomColors[item.Name] = presets[index];
        return presets[index];
    }

    public void NotifyHomeworkCompleted(HomeworkSuggestion item)
    {
        item.LastSeenAt = DateTime.Now;
        item.Promoted = true;
    }

    public void BlockHomework(string text)
    {
        HomeworkCompletionSettings homework = Homework;
        foreach (HomeworkSuggestion item in homework.Items.Where(item => item.Text.Equals(text, StringComparison.OrdinalIgnoreCase)).ToList())
            homework.Items.Remove(item);
        if (!homework.Blocked.Any(value => value.Equals(text, StringComparison.OrdinalIgnoreCase))) homework.Blocked.Add(text);
    }

    public void UnblockHomework(string text) =>
        Homework.Blocked.RemoveAll(value => value.Equals(text, StringComparison.OrdinalIgnoreCase));

    public void RemoveHomework(HomeworkSuggestion item) => Homework.Items.Remove(item);

    /// <summary>改写已收录条目；与同范围的同名条目合并计数，避免列表里出现重复项。</summary>
    public bool RenameHomework(HomeworkSuggestion item, string text)
    {
        string value = text.Trim();
        if (value.Length == 0 || value.Equals(item.Text, StringComparison.OrdinalIgnoreCase)) return false;
        HomeworkSuggestion? existing = Homework.Items.FirstOrDefault(other =>
            !ReferenceEquals(other, item) &&
            other.Text.Equals(value, StringComparison.OrdinalIgnoreCase) &&
            other.IsGlobal == item.IsGlobal &&
            other.Subjects.Count == item.Subjects.Count &&
            other.Subjects.SequenceEqual(item.Subjects, StringComparer.OrdinalIgnoreCase));
        if (existing is null)
        {
            item.Text = value;
            return true;
        }

        existing.Count += item.Count;
        existing.Promoted = true;
        existing.LastSeenAt = DateTime.Now;
        Homework.Items.Remove(item);
        return true;
    }

    public void ClearHomework()
    {
        Homework.Items.Clear();
        Homework.Blocked.Clear();
    }

    public bool IsBlocked(string text) =>
        Homework.Blocked.Any(value => value.Equals(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>清理过期条目：未收录 14 天、已收录 90 天，手动添加项永不过期。</summary>
    public bool Prune(DateTime now)
    {
        HomeworkCompletionSettings homework = Homework;
        bool changed = false;
        for (int index = homework.Items.Count - 1; index >= 0; index--)
        {
            HomeworkSuggestion item = homework.Items[index];
            if (item.Source != AutoSource) continue;
            if (AgeDays(item, now) <= ExpiryDays(item.Promoted)) continue;
            homework.Items.RemoveAt(index);
            changed = true;
        }

        bool trimmed = TrimAutoItems();
        return changed || trimmed;
    }

    /// <summary>待收录/已收录条目的剩余可保留天数，用于设置页展示。</summary>
    public static int RemainingDays(HomeworkSuggestion item, DateTime now) =>
        (int)Math.Ceiling(ExpiryDays(item.Promoted) - AgeDays(item, now));

    /// <summary>还差几次输入才进入补全候选；手动项返回 0。</summary>
    public static int RemainingCount(HomeworkSuggestion item, string? level) =>
        item.Source != AutoSource || item.Promoted ? 0 : Math.Max(0, RecordThreshold(level) - item.Count);

    /// <summary>按空白、标点、数字与换行切分词或短语。</summary>
    public static IReadOnlyList<string> Tokenize(string? content)
    {
        List<string> tokens = [];
        if (string.IsNullOrEmpty(content)) return tokens;
        StringBuilder current = new();
        int start = 0;
        for (int index = 0; index < content.Length; index++)
        {
            char character = content[index];
            if (IsWordSeparator(character))
            {
                FlushToken(tokens, current, content, start, index);
                continue;
            }

            if (current.Length == 0) start = index;
            current.Append(character);
        }

        FlushToken(tokens, current, content, start, content.Length);
        return tokens;
    }

    /// <summary>长度、字符种类与占位文案过滤，因此“P”“第 3 题”这类碎片不会入库。</summary>
    public static bool IsRecordable(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (token.Length is < 2 or > MaxTokenLength) return false;
        if (token.Contains(PlaceholderHomework, StringComparison.Ordinal)) return false;
        if (token.Any(IsHan)) return true;
        int run = 0;
        foreach (char character in token)
        {
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                run++;
                if (run >= 2) return true;
            }
            else
            {
                run = 0;
            }
        }

        return false;
    }

    /// <summary>当前输入对应的记录范围：分学科隔离时写入学科桶，否则写入全局桶。</summary>
    public string CurrentScope(string? subjectName)
    {
        if (Homework.Isolation != "Subject") return GlobalScope;
        return NormalizeSubject(subjectName) ?? GlobalScope;
    }

    private static double AgeDays(HomeworkSuggestion item, DateTime now)
    {
        DateTime reference = item.LastSeenAt == default ? item.FirstSeenAt : item.LastSeenAt;
        return reference == default ? 0 : Math.Max(0, (now - reference).TotalDays);
    }

    private static double ExpiryDays(bool promoted) => promoted ? PromotedExpiryDays : PendingExpiryDays;

    private bool TrimAutoItems()
    {
        HomeworkCompletionSettings homework = Homework;
        List<HomeworkSuggestion> auto = homework.Items
            .Where(item => item.Source == AutoSource)
            .OrderByDescending(item => item.LastSeenAt)
            .ToList();
        if (auto.Count <= MaxAutoItems) return false;
        foreach (HomeworkSuggestion item in auto.Skip(MaxAutoItems)) homework.Items.Remove(item);
        return true;
    }

    private static void FlushToken(List<string> tokens, StringBuilder current, string source, int start, int end)
    {
        if (current.Length == 0) return;
        string token = NormalizeToken(current.ToString(), source, start, end);
        current.Clear();
        if (token.Length == 0) return;
        if (!tokens.Any(value => value.Equals(token, StringComparison.OrdinalIgnoreCase))) tokens.Add(token);
    }

    /// <summary>
    /// 页码与序号不属于作业名称：把「双练一测P30」收成「双练一测」，把「双练一测第3页」收成「双练一测」。
    /// </summary>
    private static string NormalizeToken(string token, string source, int start, int end)
    {
        int from = 0;
        int to = token.Length;
        bool followedByDigit = end < source.Length && char.IsDigit(source[end]);
        bool afterDigit = start > 0 && char.IsDigit(source[start - 1]);
        if (followedByDigit && to > 1 && token[to - 1] == '第') to--;
        if (afterDigit)
        {
            while (from < to && token[from] is '页' or '题' or '课' or '章' or '节') from++;
        }

        // 汉字名称旁单独粘连的一个字母多半是页码标记（如“双练一测P”），不参与名称。
        bool hasHan = false;
        for (int index = from; index < to; index++)
        {
            if (IsHan(token[index]))
            {
                hasHan = true;
                break;
            }
        }

        if (hasHan)
        {
            int letterEnd = to;
            while (letterEnd > from && IsAsciiLetter(token[letterEnd - 1])) letterEnd--;
            if (to - letterEnd == 1) to = letterEnd;
            int letterStart = from;
            while (letterStart < to && IsAsciiLetter(token[letterStart])) letterStart++;
            if (letterStart - from == 1 && letterStart < to) from = letterStart;
        }

        return token[from..to];
    }

    /// <summary>词的边界：空白、标点、数字与控制符，标题框和正文框共用同一规则。</summary>
    public static bool IsWordSeparator(char character) =>
        // 撇号是输入法用来分隔音节的（yu'wen），它仍然属于正在输入的那个词。
        character is not ('\'' or '\u2019') &&
        (char.IsWhiteSpace(character) || char.IsDigit(character) ||
         char.IsPunctuation(character) || char.IsSymbol(character) || char.IsControl(character));

    private static bool IsHan(char character) =>
        character is >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';

    private static bool IsAsciiLetters(string value)
    {
        if (value.Length == 0) return false;
        foreach (char character in value)
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z'))
                return false;
        return true;
    }

    private static bool IsAsciiLetter(char character) => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>
    /// 输入法会用撇号分隔音节（yu'wen），也可能带全角字符；匹配拼音前统一成紧凑小写字母。
    /// </summary>
    private static string NormalizePinyinQuery(string query)
    {
        // FormKD 会把全角字符和带声调字母拆成基本字母，随后只保留 a-z。
        string normalized = query.Normalize(NormalizationForm.FormKD);
        StringBuilder builder = new(normalized.Length);
        foreach (char character in normalized)
        {
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z')) continue;
            builder.Append(character);
        }

        return builder.ToString().ToLowerInvariant();
    }

    private static bool IsSubsequence(string source, string query)
    {
        int index = 0;
        foreach (char character in source)
        {
            if (index >= query.Length) break;
            if (character == query[index]) index++;
        }

        return index >= query.Length;
    }

    /// <summary>中文前缀/包含优先，其次是全拼前缀、首字母前缀，宽松档允许首字母跳字。</summary>
    private int Score(string name, string query, string level)
    {
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 400;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 300;
        string lowered = NormalizePinyinQuery(query);
        if (!IsAsciiLetters(lowered)) return 0;
        (string full, string initials) = _pinyin.Expand(name);
        if (full.StartsWith(lowered, StringComparison.Ordinal)) return 260;
        if (initials.StartsWith(lowered, StringComparison.Ordinal)) return 240;
        if (NormalizeLevel(level) == "Loose" && IsSubsequence(initials, lowered)) return 200;
        return 0;
    }

    /// <summary>触发补全所需的最少输入：宽松 1 个字符，正常汉字 1 个或字母 2 个，严格 2 个。</summary>
    private static int MinimumInput(string level, string query) => NormalizeLevel(level) switch
    {
        "Loose" => 1,
        "Strict" => 2,
        _ => query.Any(IsHan) ? 1 : 2
    };

    private static string? NormalizeSubject(string? subjectName)
    {
        string value = subjectName?.Trim() ?? string.Empty;
        return value.Length == 0 ? null : value;
    }

    private static bool Covers(HomeworkSuggestion item, string? subject)
    {
        if (item.IsGlobal) return true;
        if (subject is null) return false;
        return item.Subjects.Any(value => value.Equals(subject, StringComparison.OrdinalIgnoreCase));
    }
}
