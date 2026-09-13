#if PANCAKE_UI_TESTS
using Avalonia;
using Avalonia.Controls;
using Pancake.Services;

namespace Pancake.Views;

/// <summary>
/// 验证构建专用：给 Headless 界面自检（tests/UiHeadless）开放必要的界面状态与操作入口。
/// 这些成员只在 EnableUiVerification=true 时编译，正式包不包含它们。
/// 之所以放在界面层，是因为被检查的状态（是否在编辑、控制窗是否隐藏、退出提示是否展开）
/// 都是窗口内部状态，用公开 API 复述一遍等于把断言写在另一份实现上。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>设置页分类标签，供自检遍历所有页面。</summary>
    public static IReadOnlyList<string> SettingsSectionTagsForVerification =>
        [.. SettingsSections.Select(section => section.Tag)];

    /// <summary>切换到指定设置分类。</summary>
    public void ShowSettingsSectionForVerification(string tag) => ShowSettingsSection(tag);

    /// <summary>切换到设置视图（与点击「设置」按钮等效）。</summary>
    public void ShowSettingsForVerification() => ShowSettings();

    /// <summary>回到看板视图。</summary>
    public void ShowBoardForVerification() => ShowBoard();

    /// <summary>按当前设置刷新看板与外观，等价于设置项改动后的处理。</summary>
    public void SettingChangedForVerification() => SettingChanged();

    /// <summary>切换全屏状态。</summary>
    public void SetFullScreenForVerification(bool fullScreen) => SetFullScreen(fullScreen);

    /// <summary>切换无字模式（不写入设置，只作用于当前界面）。</summary>
    public void SetToolbarIconOnlyForVerification(bool iconOnly)
    {
        Settings.ToolbarIconOnly = iconOnly;
        ApplyToolbarAppearance();
    }

    /// <summary>退出提示此刻是否展开。</summary>
    public bool FullScreenHintVisibleForVerification => IsFullScreenHintVisible;

    /// <summary>进入编辑模式。</summary>
    public void EnterEditingForVerification() => EnterEditing();

    /// <summary>结束编辑模式。</summary>
    public void FinishEditingForVerification() => FinishEditing();

    /// <summary>把控制窗空闲计时清零，等价于用户产生了一次操作。</summary>
    public void RegisterActivityForVerification() => RegisterActivity();

    /// <summary>控制窗此刻是否处于自动隐藏状态。</summary>
    public bool ToolbarHiddenForVerification => _toolbarHidden;

    /// <summary>此刻按下的指针数量：用于确认真实输入是否到达窗口。</summary>
    public int PressedPointerCountForVerification => _pressedPointers.Count;

    /// <summary>
    /// 把空闲时间直接推进指定秒数并重新判定自动隐藏。
    /// Headless 环境不推进调度器定时器，用这个入口可以在无头测试里确定性地验证隐藏判定本身；
    /// 定时器驱动的真实隐藏流程由真实窗口的自检覆盖。
    /// </summary>
    public void AdvanceIdleForVerification(double seconds)
    {
        _idleSeconds += seconds;
        CheckAutoHide();
    }

    /// <summary>展开退出全屏提示（真实窗口里用它验证定时器会自动收起）。</summary>
    public void ShowFullScreenHintForVerification() => ShowFullScreenExitHint();

    /// <summary>轻量对话框浮层；自检用它确认对话框是否打开、读取其中的内容。</summary>
    public Control DialogOverlayForVerification => DialogOverlay;

    /// <summary>切换画笔／橡皮擦（与画笔栏上的两个按钮等效）。</summary>
    public void SelectInkToolForVerification(bool eraser) => SelectInkTool(eraser);

    /// <summary>当前画笔设置：自检用它核对落笔是否有颜色与粗细。</summary>
    public (BoardColor Color, double Thickness, bool Eraser) InkSettingsForVerification =>
        (_inkSettings.Color, _inkSettings.Thickness, _inkSettings.Eraser);

    /// <summary>撤销最后一笔（与画笔栏的撤销按钮等效）。</summary>
    public void UndoInkForVerification() => UndoInk_Click(null, new Avalonia.Interactivity.RoutedEventArgs());

    /// <summary>清空笔迹（与画笔栏的垃圾桶按钮等效）。</summary>
    public void ClearInkForVerification() => ClearInk_Click(null, new Avalonia.Interactivity.RoutedEventArgs());

    /// <summary>当前是否处于编辑模式。</summary>
    public bool IsEditingForVerification => _isEditing;

    /// <summary>候选浮层（学科与作业补全共用），自检用它读取候选与开关状态。</summary>
    public Controls.AutofillPopup AutofillPopupForVerification => _autofillPopup;

    /// <summary>触发“新建项目”菜单操作（与点击菜单项等效）。</summary>
    public void NewProjectForVerification() => NewProject_Click(null, new Avalonia.Interactivity.RoutedEventArgs());

    /// <summary>触发“重命名项目”菜单操作。</summary>
    public void RenameProjectForVerification() => RenameProject_Click(null, new Avalonia.Interactivity.RoutedEventArgs());

    /// <summary>触发“删除项目”菜单操作。</summary>
    public void DeleteProjectForVerification() => DeleteProject_Click(null, new Avalonia.Interactivity.RoutedEventArgs());

    /// <summary>切换项目（与点击最近项目列表里的条目等效）。</summary>
    public void SwitchProjectForVerification(ProjectDocument project) => SwitchProject(project);
}
#endif
