using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pancake.Services;
using Pancake.Shared;
using Pancake.ViewModels;

namespace Pancake;

/// <summary>
/// 考试模式：到点后用全屏覆盖层显示日期、科目、班级/考场、带秒的大时钟与胶囊环形倒计时。
/// 覆盖层位于控制窗与设置页之下，考试期间仍可正常操作控制窗。
/// </summary>
public sealed partial class MainWindow
{
    private const double ExamRingThickness = 3;
    private const double ExamRingHeight = 36;
    // 收到 exam 实时通知后短暂合并，再拉取本班的最新安排。
    private readonly DispatcherTimer _examRefreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private double _examRingLength;
    private bool _examOverlayVisible;
    private bool _examForcedFullScreen;

    private void InitializeExamMode()
    {
        InitializeExamRing();
        _examRefreshDebounce.Tick += async (_, _) =>
        {
            _examRefreshDebounce.Stop();
            await CloudSync.RefreshExamAsync(_library);
            UpdateExamDisplay();
        };
        CloudSync.ExamChanged += () => DispatcherQueue.TryEnqueue(UpdateExamDisplay);
        // 启动即恢复缓存：考试进行中重启或断网也要按时全屏。本机安排不依赖集控。
        CloudSync.LoadExamCache(_library);
        UpdateExamDisplay();
    }

    // ------------------------------------------------------------------ 进度环

    /// <summary>胶囊环：高度与右侧时间段文字一致，宽度在每次刷新时按剩余时间文字自适应。</summary>
    private void InitializeExamRing()
    {
        if (ExamRingTrack.Stroke is not null) return;
        ExamRingTrack.Opacity = 0.16;
        ExamRingTrack.Stroke = BoardTheme.TextBrush;
        ExamRingFill.Stroke = SubjectBrush(null);
        UpdateExamRingGeometry(double.IsNaN(ExamRingHost.Width) || ExamRingHost.Width < 12 ? 96 : ExamRingHost.Width);
    }

    private void UpdateExamRingGeometry(double width)
    {
        double rectWidth = Math.Max(12, width - ExamRingThickness);
        double rectHeight = ExamRingHeight - ExamRingThickness;
        double radius = rectHeight / 2;
        ExamRingTrack.Width = ExamRingFill.Width = rectWidth;
        ExamRingTrack.Height = ExamRingFill.Height = rectHeight;
        ExamRingTrack.RadiusX = ExamRingTrack.RadiusY = radius;
        ExamRingFill.RadiusX = ExamRingFill.RadiusY = radius;
        _examRingLength = 2 * (rectWidth + rectHeight) - 8 * radius + 2 * Math.PI * radius;
    }

    /// <summary>
    /// 按当前剩余时间文字测量胶囊宽度：文字与环的上下左右间距相同，胶囊高度与时间段文字一致。
    /// 宽度按 2px 取整，避免每秒数字宽度变化导致抖动。
    /// </summary>
    private void UpdateExamRing(ExamDisplay display)
    {
        ExamCountdownText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        Windows.Foundation.Size text = ExamCountdownText.DesiredSize;
        double gap = Math.Max(2, (ExamRingHeight - 2 * ExamRingThickness - text.Height) / 2);
        double width = Math.Max(64, Math.Ceiling((text.Width + 2 * ExamRingThickness + 2 * gap) / 2) * 2);
        if (double.IsNaN(ExamRingHost.Width) || Math.Abs(ExamRingHost.Width - width) > 0.5)
        {
            ExamRingHost.Width = width;
            UpdateExamRingGeometry(width);
        }
        double fraction = display.Total is { TotalSeconds: > 0 } total
            ? Math.Clamp(display.Remaining.TotalSeconds / total.TotalSeconds, 0, 1)
            : 1;
        if (fraction <= 0.001)
        {
            ExamRingFill.Visibility = Visibility.Collapsed;
            return;
        }
        ExamRingFill.Visibility = Visibility.Visible;
        double length = _examRingLength / ExamRingThickness;
        DoubleCollection dashes = [];
        dashes.Add(fraction * length);
        dashes.Add(length);
        ExamRingFill.StrokeDashArray = dashes;
        ExamRingFill.StrokeDashOffset = 0;
        ExamRingFill.StrokeDashCap = PenLineCap.Round;
    }

    // ------------------------------------------------------------------ 显示

    /// <summary>
    /// 每秒调用（挂在一秒时钟上）：生效顺序为服务器启用的考试计划 &gt; 本机启用的安排 &gt; 正常看板。
    /// 服务器计划用 ServerTime 校正本地时钟；本机安排直接使用本机时间。
    /// </summary>
    private void UpdateExamDisplay()
    {
        if (ExamOverlay is null) return;
        // 设置页或班级页在前台时收起考试画面，避免考试元素透在页面后面；返回看板后自动恢复。
        if (DisplayRoot.Visibility != Visibility.Visible)
        {
            SuspendExamOverlay();
            return;
        }
        ExamScheduleDto? effective = _settings.CloudSync.Enabled ? CloudSync.Exam : null;
        bool localSource = false;
        DateTime now = DateTime.Now;
        if (effective is not null)
            now += CloudSync.ServerClockOffset;
        else if (_settings.LocalExam.Enabled)
        {
            effective = ExamSchedule.FromLocal(_settings.LocalExam);
            localSource = true;
        }
        ExamDisplay? display = ExamSchedule.Evaluate(effective, now);
        if (display is null)
        {
            HideExamOverlay();
            return;
        }
        ShowExamOverlay(display, now, effective!.ShowSeconds, localSource, effective.RoomName);
    }

    private void ShowExamOverlay(ExamDisplay display, DateTime now, bool showSeconds, bool localSource, string? roomName)
    {
        bool wasVisible = _examOverlayVisible;
        _examOverlayVisible = true;
        ExamOverlay.Visibility = Visibility.Visible;
        // 考试期间强制全屏；如果本来就是窗口模式，结束后恢复窗口。
        if (!wasVisible && !_isFullScreen)
        {
            SetFullScreen(true);
            _examForcedFullScreen = true;
        }
        ExamDateText.Text = $"{display.Date:yyyy年M月d日} 星期{GetChineseWeekday(display.Date.DayOfWeek)}";
        ExamTitleText.Text = display.Title;
        ExamSegmentText.Text = SegmentLine(display);
        ExamSegmentText.Visibility = string.IsNullOrEmpty(ExamSegmentText.Text) ? Visibility.Collapsed : Visibility.Visible;
        // 学科名称与胶囊环使用本机该学科配置的颜色（按当前色系解析），未配置时使用色系蓝色。
        string colorSubject = display.Phase == ExamPhase.Break ? display.NextSubject ?? "" : display.Title;
        Brush subjectBrush = SubjectBrush(colorSubject);
        ExamRingFill.Stroke = subjectBrush;
        ExamTitleText.Foreground = display.Phase == ExamPhase.Break ? BoardTheme.TextBrush : subjectBrush;
        ExamRingTrack.Stroke = BoardTheme.TextBrush;
        string className = _settings.CloudSync.ClassName;
        ExamClassText.Text = className;
        ExamClassText.Visibility = string.IsNullOrWhiteSpace(className) ? Visibility.Collapsed : Visibility.Visible;
        ExamRoomText.Text = roomName ?? "";
        ExamRoomText.Visibility = string.IsNullOrWhiteSpace(roomName) ? Visibility.Collapsed : Visibility.Visible;
        ExamClockText.Text = now.ToString(showSeconds ? "HH:mm:ss" : "HH:mm");
        ExamCountdownText.Text = FormatCountdown(display.Remaining, showSeconds);
        UpdateExamRing(display);
        ExamExitButton.Visibility = localSource ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideExamOverlay()
    {
        _examOverlayVisible = false;
        if (ExamOverlay is not null) ExamOverlay.Visibility = Visibility.Collapsed;
        if (_examForcedFullScreen)
        {
            _examForcedFullScreen = false;
            if (_isFullScreen) SetFullScreen(false);
        }
    }

    /// <summary>设置页/班级页打开时暂时收起考试画面；不改变全屏状态，回到看板后由 UpdateExamDisplay 恢复。</summary>
    private void SuspendExamOverlay()
    {
        if (!_examOverlayVisible) return;
        _examOverlayVisible = false;
        if (ExamOverlay is not null) ExamOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>科目右侧的小字：考试段显示本场时间，休息段显示下一场，提前进入时提示即将开始。</summary>
    private static string SegmentLine(ExamDisplay display) => display.Phase switch
    {
        ExamPhase.Upcoming => display is { SegmentStart: { } upcomingStart, SegmentEnd: { } upcomingEnd }
            ? $"{upcomingStart:HH:mm} – {upcomingEnd:HH:mm} · 即将开始" : "即将开始",
        ExamPhase.Examining => display is { SegmentStart: { } start, SegmentEnd: { } end }
            ? $"{start:HH:mm} – {end:HH:mm}" : "",
        _ => display is { NextStart: { } nextStart, NextSubject: { } subject }
            ? $"下一场 {subject} {nextStart:HH:mm} 开始" : "本场结束"
    };

    private static string FormatCountdown(TimeSpan remaining, bool showSeconds)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (!showSeconds) return $"{(int)Math.Ceiling(remaining.TotalMinutes)} 分钟";
        int hours = (int)remaining.TotalHours;
        return hours > 0
            ? $"{hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    /// <summary>学科颜色：本机学科补全里的配置按当前色系解析；未配置时使用色系默认蓝色。</summary>
    private Brush SubjectBrush(string? subject)
    {
        string hex = "#818CF8";
        if (!string.IsNullOrWhiteSpace(subject))
        {
            SubjectSuggestion? entry = _settings.Autofill.Subject.Subjects
                .FirstOrDefault(item => string.Equals(item.Name.Trim(), subject.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                hex = entry.RandomColor ? RandomExamColor(subject) : SafeColorHex(entry.Color);
        }
        return MainViewModel.BrushFromHex(ColorPalette.Resolve(hex));
    }

    /// <summary>随机配色学科在考试画面上取一个稳定的预设色（按学科名散列，不随刷新变化）。</summary>
    private static string RandomExamColor(string subject)
    {
        int hash = 0;
        foreach (char c in subject) hash = unchecked(hash * 31 + c);
        IReadOnlyList<string> presets = ColorPalette.Presets;
        return presets.Count == 0 ? "#818CF8" : presets[(hash & int.MaxValue) % presets.Count];
    }

    // ------------------------------------------------------------------ 本机考试随时退出

    private async void ExamExitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        ContentDialog dialog = new()
        {
            Title = "退出本机考试",
            Content = "退出后本机考试安排会停用，可随时在「设置 → 考试」中重新启用。",
            PrimaryButtonText = "退出考试",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootShell.XamlRoot
        };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return;
        _settings.LocalExam.Enabled = false;
        SettingChanged();
        RefreshSettingControls();
        UpdateExamDisplay();
    }
}
