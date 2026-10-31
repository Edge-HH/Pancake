using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pancake.Services;
using Pancake.Shared;

namespace Pancake;

/// <summary>
/// 考试模式：到点后用全屏覆盖层显示日期、科目、班级/考场、带秒的大时钟与胶囊环形倒计时。
/// 覆盖层位于控制窗与设置页之下，考试期间仍可正常操作控制窗。
/// </summary>
public sealed partial class MainWindow
{
    private const double ExamRingThickness = 10;
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

    /// <summary>胶囊进度环：750×110 的圆角矩形居中放在 760×120 的格子里，描边不会超出外框。</summary>
    private void InitializeExamRing()
    {
        if (ExamRingTrack.Stroke is not null) return;
        ExamRingTrack.Stroke = (Brush)Application.Current.Resources["BoardTextBrush"];
        ExamRingTrack.Opacity = 0.16;
        ExamRingFill.Stroke = (Brush)Application.Current.Resources["BoardBrandBrush"];
        // 圆角矩形周长 = 2×(宽+高) − 8×圆角半径 + 2π×圆角半径。
        _examRingLength = 2 * (750 + 110) - 8 * 55 + 2 * Math.PI * 55;
    }

    /// <summary>按当前阶段剩余比例绘制胶囊环；剩余时间不变时环保持满格。</summary>
    private void UpdateExamRing(ExamDisplay display)
    {
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
        if (!_examOverlayVisible) return;
        _examOverlayVisible = false;
        if (ExamOverlay is not null) ExamOverlay.Visibility = Visibility.Collapsed;
        if (_examForcedFullScreen)
        {
            _examForcedFullScreen = false;
            if (_isFullScreen) SetFullScreen(false);
        }
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
