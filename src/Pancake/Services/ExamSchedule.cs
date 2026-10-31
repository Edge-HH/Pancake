using Pancake.Shared;

namespace Pancake.Services;

public enum ExamPhase { Upcoming, Examining, Break }

/// <summary>
/// 考试画面当前应显示的内容。Remaining 的含义随 Phase 变化：
/// Upcoming=距开考；Examining=本场剩余；Break=距下一场开始（没有下一场时为休息段结束）。
/// Total 是当前阶段的总时长，用于绘制胶囊进度环；为空或为零时进度环显示为满。
/// </summary>
public sealed record ExamDisplay(
    ExamPhase Phase, string PlanName, DateOnly Date, string Title, TimeSpan Remaining,
    DateTime? SegmentStart, DateTime? SegmentEnd, string? NextSubject, DateTime? NextStart,
    TimeSpan? Total = null);

/// <summary>
/// 考试时间表的到点评估：只依赖时间表与当前时间，便于测试。服务器下发的计划与本机计划共用同一套逻辑。
/// </summary>
public static class ExamSchedule
{
    public const string LocalPlanName = "本机考试安排";
    public const string DefaultBreakLabel = "休息时间";

    /// <summary>
    /// 用校正后的本地时间评估当前应显示的分段；null 表示不在考试模式。
    /// 规则：开考前 EnterMinutesBefore 分钟进入 Upcoming；段区间左闭右开；段间空隙按休息处理；
    /// 当天最后一段结束后回到 null；不同日期的窗口互不影响。非法段（结束不晚于开始、考试段缺科目）
    /// 与重叠段会被忽略，保证时间轴单调。
    /// </summary>
    public static ExamDisplay? Evaluate(ExamScheduleDto? schedule, DateTime now)
    {
        if (schedule?.Days is not { Count: > 0 } days) return null;
        int enter = Math.Clamp(schedule.EnterMinutesBefore, 0, 120);
        string planName = string.IsNullOrWhiteSpace(schedule.PlanName) ? LocalPlanName : schedule.PlanName;
        foreach (ExamDayDto day in days.OrderBy(x => x.Date))
        {
            List<ExamSegmentDto> timeline = BuildTimeline(day.Segments);
            if (timeline.Count == 0) continue;
            ExamSegmentDto? firstExam = timeline.FirstOrDefault(x => x.Kind == ExamSegmentKinds.Exam);
            if (firstExam is null) continue;
            DateTime dayStart = day.Date.ToDateTime(TimeOnly.MinValue);
            DateTime firstExamStart = dayStart.AddMinutes(firstExam.StartMinute);
            if (now < firstExamStart.AddMinutes(-enter)) continue;
            if (now >= dayStart.AddMinutes(timeline[^1].EndMinute)) continue;
            if (now < firstExamStart)
                return new ExamDisplay(ExamPhase.Upcoming, planName, day.Date, SubjectOf(firstExam),
                    firstExamStart - now, firstExamStart, dayStart.AddMinutes(firstExam.EndMinute), null, firstExamStart,
                    TimeSpan.FromMinutes(enter));
            for (int index = 0; index < timeline.Count; index++)
            {
                ExamSegmentDto segment = timeline[index];
                DateTime start = dayStart.AddMinutes(segment.StartMinute);
                DateTime end = dayStart.AddMinutes(segment.EndMinute);
                if (now < start)
                {
                    // 上一段结束到这一段开始之间的空隙：按休息处理，倒计时指向当天后续第一场考试。
                    ExamSegmentDto? nextExam = timeline.Skip(index).FirstOrDefault(x => x.Kind == ExamSegmentKinds.Exam);
                    DateTime breakStart = index > 0 ? dayStart.AddMinutes(timeline[index - 1].EndMinute) : start;
                    return nextExam is null
                        ? new ExamDisplay(ExamPhase.Break, planName, day.Date, DefaultBreakLabel, start - now, null, null, null, null, start - breakStart)
                        : BreakDisplay(planName, day, dayStart, DefaultBreakLabel, now, nextExam, null, null, breakStart);
                }
                if (now >= end) continue;
                if (segment.Kind == ExamSegmentKinds.Break)
                {
                    ExamSegmentDto? nextExam = timeline.Skip(index + 1).FirstOrDefault(x => x.Kind == ExamSegmentKinds.Exam);
                    return nextExam is null
                        ? new ExamDisplay(ExamPhase.Break, planName, day.Date, LabelOf(segment), end - now, start, end, null, null, end - start)
                        : BreakDisplay(planName, day, dayStart, LabelOf(segment), now, nextExam, start, end, start);
                }
                ExamSegmentDto? following = timeline.Skip(index + 1).FirstOrDefault(x => x.Kind == ExamSegmentKinds.Exam);
                return new ExamDisplay(ExamPhase.Examining, planName, day.Date, SubjectOf(segment), end - now, start, end,
                    following is null ? null : SubjectOf(following),
                    following is null ? null : dayStart.AddMinutes(following.StartMinute), end - start);
            }
        }
        return null;
    }

    /// <summary>本机设置转成与服务器下发相同的 DTO，复用同一套评估逻辑。</summary>
    public static ExamScheduleDto FromLocal(LocalExamSettings local) => new(
        Guid.Empty, LocalPlanName,
        local.Days.OrderBy(day => day.Date).Select(day => new ExamDayDto(day.Date, day.Segments.Select(segment => new ExamSegmentDto(
            segment.Kind, segment.Subject ?? "", segment.StartMinute, segment.EndMinute,
            string.IsNullOrWhiteSpace(segment.Label) ? null : segment.Label)).ToList())).ToList(),
        Math.Clamp(local.EnterMinutesBefore, 0, 120), DateTime.UtcNow,
        local.ShowSeconds, string.IsNullOrWhiteSpace(local.RoomName) ? null : local.RoomName.Trim());

    /// <summary>本机安排的校验提示；空列表表示没有发现问题。</summary>
    public static List<string> ValidateLocal(LocalExamSettings local)
    {
        List<string> problems = [];
        if (local.Days.Count == 0)
        {
            problems.Add("至少需要一天考试安排。");
            return problems;
        }
        foreach (LocalExamDay day in local.Days)
        {
            string date = day.Date.ToString("M月d日");
            if (day.Segments.Count == 0)
            {
                problems.Add($"{date}：还没有时间段。");
                continue;
            }
            if (!day.Segments.Any(x => x.Kind == ExamSegmentKinds.Exam)) problems.Add($"{date}：至少需要一场考试。");
            foreach (LocalExamSegment segment in day.Segments)
            {
                if (segment.Kind is not (ExamSegmentKinds.Exam or ExamSegmentKinds.Break))
                {
                    problems.Add($"{date}：时间段类型无效。");
                    continue;
                }
                if (segment.StartMinute is < 0 or > 1439 || segment.EndMinute is < 1 or > 1440 || segment.EndMinute <= segment.StartMinute)
                    problems.Add($"{date}：{SegmentName(segment)} 的起止时间无效。");
                if (segment.Kind == ExamSegmentKinds.Exam && string.IsNullOrWhiteSpace(segment.Subject))
                    problems.Add($"{date}：有考试场次没有填写科目。");
            }
            List<LocalExamSegment> ordered = day.Segments.OrderBy(x => x.StartMinute).ToList();
            for (int index = 1; index < ordered.Count; index++)
            {
                if (ordered[index].StartMinute < ordered[index - 1].EndMinute)
                {
                    problems.Add($"{date}：时间段有重叠。");
                    break;
                }
            }
        }
        return problems;
    }

    private static ExamDisplay BreakDisplay(string planName, ExamDayDto day, DateTime dayStart, string title, DateTime now,
        ExamSegmentDto nextExam, DateTime? segmentStart, DateTime? segmentEnd, DateTime breakStart)
    {
        DateTime nextStart = dayStart.AddMinutes(nextExam.StartMinute);
        return new ExamDisplay(ExamPhase.Break, planName, day.Date, title, nextStart - now,
            segmentStart, segmentEnd, SubjectOf(nextExam), nextStart, nextStart - breakStart);
    }

    private static List<ExamSegmentDto> BuildTimeline(List<ExamSegmentDto>? segments)
    {
        List<ExamSegmentDto> usable = (segments ?? [])
            .Where(IsUsable)
            .OrderBy(x => x.StartMinute)
            .ToList();
        List<ExamSegmentDto> timeline = [];
        foreach (ExamSegmentDto segment in usable)
        {
            // 防御脏数据：与上一段重叠的段直接丢弃。
            if (timeline.Count > 0 && segment.StartMinute < timeline[^1].EndMinute) continue;
            timeline.Add(segment);
        }
        return timeline;
    }

    private static bool IsUsable(ExamSegmentDto? segment) =>
        segment is not null
        && segment.Kind is ExamSegmentKinds.Exam or ExamSegmentKinds.Break
        && segment.StartMinute is >= 0 and < 1440
        && segment.EndMinute is > 0 and <= 1440
        && segment.EndMinute > segment.StartMinute
        && (segment.Kind != ExamSegmentKinds.Exam || !string.IsNullOrWhiteSpace(segment.Subject));

    private static string SubjectOf(ExamSegmentDto segment) =>
        string.IsNullOrWhiteSpace(segment.Subject) ? "考试" : segment.Subject.Trim();

    private static string LabelOf(ExamSegmentDto segment) =>
        string.IsNullOrWhiteSpace(segment.Label) ? DefaultBreakLabel : segment.Label.Trim();

    private static string SegmentName(LocalExamSegment segment) =>
        segment.Kind == ExamSegmentKinds.Exam
            ? (string.IsNullOrWhiteSpace(segment.Subject) ? "考试" : segment.Subject.Trim())
            : (string.IsNullOrWhiteSpace(segment.Label) ? DefaultBreakLabel : segment.Label.Trim());
}
