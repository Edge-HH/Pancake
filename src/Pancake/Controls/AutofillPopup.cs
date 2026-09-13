using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Pancake.Services;

namespace Pancake.Controls;

/// <summary>
/// 自动填充候选浮层：贴在输入控件下方，最多显示五条，可滚动。
/// 鼠标点击与键盘（上下键、Tab、回车、Esc）都由调用方通过本类的方法驱动。
/// </summary>
public sealed class AutofillPopup
{
    /// <summary>与旧版一致：一次最多显示五条候选。</summary>
    public const int MaxVisibleItems = 5;

    private readonly ListBox _list = new();
    private readonly Popup _popup = new();
    private readonly Border _frame;

    public AutofillPopup()
    {
        _list.MaxHeight = 240;
        _list.MinWidth = 240;
        _list.Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor());
        _list.BorderThickness = new Thickness(0);
        _list.PointerReleased += (_, _) => AcceptSelected();
        _frame = new Border
        {
            Child = _list,
            Background = new SolidColorBrush(BoardTheme.SurfaceColor.ToColor()),
            BorderBrush = new SolidColorBrush(BoardTheme.LineColor.ToColor()),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            MaxHeight = 260
        };
        _popup.Child = _frame;
        _popup.Placement = PlacementMode.Bottom;
        _popup.IsLightDismissEnabled = false;
    }

    /// <summary>候选被采纳时触发，参数为选中的候选项。</summary>
    public event Action<object>? Accepted;

    public bool IsOpen => _popup.IsOpen;

    public object? SelectedItem => _list.SelectedItem;

    /// <summary>验证脚本用：当前候选数量。</summary>
    internal int ItemCountForVerification => _list.ItemCount;

    /// <summary>把浮层挂到窗口的宿主画布上；Avalonia 的弹出层需要处于可视树中。</summary>
    public void Attach(Panel host)
    {
        if (!host.Children.Contains(_popup)) host.Children.Add(_popup);
    }

    public void Show(Control anchor, IReadOnlyList<object> items)
    {
        if (items.Count == 0)
        {
            Hide();
            return;
        }

        _list.ItemsSource = items;
        _list.SelectedIndex = 0;
        _popup.PlacementTarget = anchor;
        _popup.IsOpen = true;
        _frame.MinWidth = Math.Clamp(anchor.Bounds.Width, 240, 520);
    }

    public void Hide() => _popup.IsOpen = false;

    /// <summary>上下移动选中项并保持可见。</summary>
    public void MoveSelection(int delta)
    {
        int count = _list.ItemCount;
        if (count == 0) return;
        int index = Math.Clamp(_list.SelectedIndex + delta, 0, count - 1);
        _list.SelectedIndex = index;
        if (_list.ContainerFromIndex(index) is Control container) container.BringIntoView();
    }

    public void AcceptSelected()
    {
        object? selected = _list.SelectedItem;
        Hide();
        if (selected is not null) Accepted?.Invoke(selected);
    }
}

/// <summary>
/// 把自动填充挂到输入框上：只在编辑态、有输入时给出候选，
/// 键盘由隧道阶段拦截，保证上下键、Tab 与回车优先用于选择候选而不是移动光标。
/// </summary>
public sealed class AutofillController
{
    private readonly AutofillService _service;
    private readonly TextBox _input;
    private readonly AutofillPopup _popup;
    private readonly bool _isSubject;
    private readonly Func<string?> _subjectName;
    private readonly Action<SubjectSuggestion>? _applySubject;
    private readonly Action? _contentChanged;
    private readonly Func<bool> _ready;
    private bool _suppress;
    private string _lastRecorded = string.Empty;

    private AutofillController(
        AutofillService service,
        TextBox input,
        AutofillPopup popup,
        bool isSubject,
        Func<string?> subjectName,
        Func<bool> ready,
        Action<SubjectSuggestion>? applySubject,
        Action? contentChanged)
    {
        _service = service;
        _input = input;
        _popup = popup;
        _isSubject = isSubject;
        _subjectName = subjectName;
        _ready = ready;
        _applySubject = applySubject;
        _contentChanged = contentChanged;
        _popup.Accepted += OnAccepted;
        _input.TextChanged += (_, _) => Refresh();
        _input.LostFocus += (_, _) => { _popup.Hide(); Settle(); };
        _input.AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>挂接学科标题补全；采纳后同时套用该学科的颜色。</summary>
    public static AutofillController AttachSubject(
        AutofillService service,
        TextBox input,
        AutofillPopup popup,
        Func<bool> ready,
        Action<SubjectSuggestion> applySubject) =>
        new(service, input, popup, isSubject: true, () => null, ready, applySubject, null);

    /// <summary>挂接作业内容补全；内容稳定后按阈值统计是否收录。</summary>
    public static AutofillController AttachHomework(
        AutofillService service,
        TextBox input,
        AutofillPopup popup,
        Func<string?> subjectName,
        Func<bool> ready,
        Action contentChanged) =>
        new(service, input, popup, isSubject: false, subjectName, ready, null, contentChanged);

    /// <summary>按当前输入刷新候选；没有候选时收起浮层。</summary>
    public void Refresh()
    {
        if (_suppress || !_ready()) return;
        string typed = _input.Text ?? string.Empty;
        if (typed.Length == 0 || typed.Length > AutofillService.MaxQueryLength)
        {
            _popup.Hide();
            return;
        }

        if (_isSubject)
        {
            IReadOnlyList<SubjectSuggestion> matches = _service.MatchSubjects(typed, AutofillPopup.MaxVisibleItems);
            _popup.Show(_input, matches.Cast<object>().ToArray());
            return;
        }

        IReadOnlyList<HomeworkSuggestion> homework = _service.MatchHomework(typed, _subjectName(), AutofillPopup.MaxVisibleItems);
        _popup.Show(_input, homework.Cast<object>().ToArray());
    }

    /// <summary>结算一次输入：作业内容按阈值统计，学科标题不参与收录。</summary>
    public void Settle()
    {
        if (_isSubject || !_ready()) return;
        string content = _input.Text ?? string.Empty;
        if (content == _lastRecorded || content.Length == 0) return;
        _lastRecorded = content;
        if (_service.RecordHomework(content, _subjectName())) _contentChanged?.Invoke();
    }

    public void Hide() => _popup.Hide();

    private void OnAccepted(object item)
    {
        _suppress = true;
        try
        {
            switch (item)
            {
                case SubjectSuggestion subject:
                    _input.Text = subject.Name;
                    _input.CaretIndex = subject.Name.Length;
                    _service.NotifySubjectCompleted(subject);
                    _applySubject?.Invoke(subject);
                    break;
                case HomeworkSuggestion homework:
                    _input.Text = homework.Text;
                    _input.CaretIndex = homework.Text.Length;
                    _service.NotifyHomeworkCompleted(homework);
                    _contentChanged?.Invoke();
                    break;
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>隧道阶段拦截导航键：有候选时上下键、Tab、回车与 Esc 都先交给浮层。</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen) return;
        switch (e.Key)
        {
            case Key.Down:
                _popup.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _popup.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Tab:
            case Key.Enter:
                _popup.AcceptSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                _popup.Hide();
                e.Handled = true;
                break;
        }
    }
}
