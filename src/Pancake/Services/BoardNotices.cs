using Pancake.Shared;

namespace Pancake.Services;

/// <summary>班务条上的公告挑选与定时：只依赖服务器下发的班务信息和当前时间，便于测试。</summary>
public static class BoardNotices
{
    public const string AllLevels = "All";
    public const string ImportantOnly = "ImportantOnly";

    public static bool IsActive(AnnouncementItem item, DateTime utcNow) =>
        (item.StartsAt is null || item.StartsAt <= utcNow) && (item.ExpiresAt is null || item.ExpiresAt > utcNow);

    /// <summary>当前应显示的公告：生效中的公告加上已经到点的定时公告，紧急、重要、置顶依次靠前。</summary>
    public static List<AnnouncementItem> Visible(BoardInfo board, DateTime utcNow, string? levels)
    {
        bool importantOnly = levels == ImportantOnly;
        return board.Announcements.Concat(board.Upcoming ?? [])
            .DistinctBy(x => x.Id)
            .Where(x => IsActive(x, utcNow))
            .Where(x => !importantOnly || x.Pinned || x.Level != "info")
            .OrderBy(x => x.Level == "urgent" ? 0 : x.Level == "important" ? 1 : 2)
            .ThenByDescending(x => x.Pinned)
            .ThenByDescending(x => x.CreatedAt)
            .ToList();
    }

    /// <summary>下一个需要重新挑选的时间点：某条公告开始或结束的时刻；没有则返回 null。</summary>
    public static DateTime? NextBoundary(BoardInfo board, DateTime utcNow) =>
        board.Announcements.Concat(board.Upcoming ?? [])
            .SelectMany(x => new[] { x.StartsAt, x.ExpiresAt })
            .Where(x => x > utcNow)
            .Min();

    /// <summary>内容指纹：公告或值日变化后，被收起的班务条重新出现。</summary>
    public static string Key(IEnumerable<AnnouncementItem> notices, DutyToday? duty) =>
        string.Join("|", notices.Select(a => $"{a.Id}:{a.Title}:{a.Body}:{a.Level}")) + "|" +
        (duty is { } d ? d.Date + d.Label + string.Join(",", d.Tasks.SelectMany(t => t.Students)) : "");
}
