using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pancake.Services;
using Windows.Foundation;

namespace Pancake.Controls;

/// <summary>
/// 补全输入的共用规则：候选只替换光标左侧正在输入的一段，词边界与作业切词保持一致。
/// 键盘操作统一由窗口根面板处理，见 MainWindow 的补全按键拦截。
/// </summary>
internal static class AutofillInputText
{
    /// <summary>默认标题，聚焦后自动清除并展示全部推荐学科。</summary>
    public const string DefaultSubjectTitle = "新科目";

    public static int WordStart(string text, int caret)
    {
        int start = Math.Clamp(caret, 0, text.Length);
        while (start > 0 && !AutofillService.IsWordSeparator(text[start - 1])) start--;
        return start;
    }

    public static string CurrentWord(string text, int caret)
    {
        int start = WordStart(text, caret);
        return text[start..Math.Clamp(caret, 0, text.Length)];
    }

    /// <summary>
    /// 采纳候选后的守护。输入法组合态下，应用写好候选之后输入法仍可能把组合文本上屏一次：
    /// 整段回退成拼音，或把拼音补在候选前后。只靠“写入后短时间自检”会漏掉这种情况，
    /// 因为这次上屏往往发生在用户稍后点到别处、输入框失焦的时候。
    /// 因此守护同时盯住定时自检、输入法组合结束和输入框失焦三个时机：
    /// 只要整段文本里又出现了当次被替换的拼音，就把整段文本补写回采纳结果（补写不抢回焦点）。
    /// </summary>
    internal sealed class ApplyGuard
    {
        public const int IntervalMilliseconds = 150;
        /// <summary>定时自检上限；到点后停止轮询，组合结束与失焦时仍会各核对一次。</summary>
        public const int MaximumChecks = 40;
        /// <summary>与输入法互相覆盖时的最大补写次数，超过就交回用户处理。</summary>
        public const int MaximumRestores = 3;

        private readonly DispatcherQueueTimer _timer;
        private Func<string>? _text;
        private Func<bool>? _composing;
        private Action? _restore;
        private string _typed = string.Empty;
        private string _expected = string.Empty;
        private int _checks;
        private int _restores;

        public ApplyGuard(DispatcherQueue dispatcher)
        {
            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(IntervalMilliseconds);
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
        }

        /// <param name="text">输入框的整段文本。</param>
        /// <param name="composing">输入法是否正在组合。</param>
        /// <param name="typed">本次采纳替换掉的那一段（拼音原文）。</param>
        /// <param name="expected">采纳完成后应得的整段文本。</param>
        /// <param name="restore">把整段文本补写回采纳结果，且不改变焦点。</param>
        public void Start(Func<string> text, Func<bool> composing, string typed, string expected, Action restore)
        {
            _text = text;
            _composing = composing;
            _typed = typed;
            _expected = expected;
            _restore = restore;
            _checks = 0;
            _restores = 0;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _text = null;
            _composing = null;
            _restore = null;
        }

        /// <summary>组合结束、输入框失焦或文本变化时主动核对一次，覆盖定时窗口之后才上屏的输入法。</summary>
        public void Verify()
        {
            if (_text is not null) Check();
        }

        private void OnTick(DispatcherQueueTimer sender, object args)
        {
            if (++_checks > MaximumChecks)
            {
                // 组合迟迟不结束时不再轮询，改由组合结束、失焦等事件触发核对。
                _timer.Stop();
                return;
            }

            Check();
        }

        private void Check()
        {
            if (_text is null || _composing is null || _restore is null)
            {
                Stop();
                return;
            }

            string text = _text();
            if (text == _expected)
            {
                // 候选已经就位：组合结束后输入法不会再上屏，可以收工。
                if (!_composing()) Stop();
                return;
            }

            // 只有“整段文本里又出现了当次替换掉的拼音”才认定是输入法上屏覆盖；
            // 长度超过采纳结果加一段拼音，说明用户已经继续输入，不再干预。
            bool overwritten = _typed.Length > 0 &&
                text.Contains(_typed, StringComparison.Ordinal) &&
                text.Length <= _expected.Length + _typed.Length;
            if (!overwritten)
            {
                Stop();
                return;
            }

            if (++_restores > MaximumRestores)
            {
                Stop();
                return;
            }

            _restore();
        }
    }
}

/// <summary>磁贴标题的学科补全。</summary>
internal sealed class SubjectAutofillInput
{
    private readonly AutofillService _service;
    private readonly AutofillPopup _popup;
    private readonly TextBox _box;
    private readonly Action<SubjectSuggestion> _applied;
    private readonly AutofillInputText.ApplyGuard _guard;
    private bool _suppress;
    private bool _composing;

    public SubjectAutofillInput(AutofillService service, AutofillPopup popup, TextBox box, Action<SubjectSuggestion> applied)
    {
        _service = service;
        _popup = popup;
        _box = box;
        _applied = applied;
        _guard = new AutofillInputText.ApplyGuard(box.DispatcherQueue);
        box.TextChanged += (_, _) =>
        {
            if (_suppress) return;
            _guard.Verify();
            Refresh();
        };
        // 组合态下输入法会用自己的候选窗接管上下键与 Tab，这里只记录状态：
        // 组合期间刷新候选时保留用户已经移动过的选中项，避免选中被刷新重置。
        box.TextCompositionStarted += (_, _) => _composing = true;
        box.TextCompositionEnded += (_, _) =>
        {
            _composing = false;
            // 组合结束是输入法上屏的时机，先核对采纳结果，再按上屏文字重新给候选。
            _guard.Verify();
            Refresh();
        };
        box.SelectionChanged += (_, _) =>
        {
            if (!_suppress) Refresh();
        };
        box.GotFocus += (_, _) => ClearDefaultTitle();
        // 失焦会让输入法把还没上屏的组合文本一次性提交，可能覆盖刚采纳的候选：
        // 这里只收起浮层并核对一次，守护继续留着等这次提交落地。
        box.LostFocus += (_, _) =>
        {
            Hide();
            _guard.Verify();
        };
        box.Unloaded += (_, _) =>
        {
            Hide();
            _guard.Stop();
        };
    }

    public void Hide() => _popup.HideIfOwnedBy(this);

    /// <summary>默认标题只用于占位，点击后清空并直接给出全部推荐学科。</summary>
    private void ClearDefaultTitle()
    {
        if (!_box.Text.Equals(AutofillInputText.DefaultSubjectTitle, StringComparison.Ordinal)) return;
        _suppress = true;
        _box.Text = string.Empty;
        _box.SelectionStart = 0;
        _suppress = false;
        ShowRecommendations();
    }

    private void Refresh()
    {
        if (_box.IsReadOnly || !_box.IsHitTestVisible)
        {
            Hide();
            return;
        }

        string text = _box.Text ?? string.Empty;
        int caret = Math.Clamp(_box.SelectionStart, 0, text.Length);
        string typed = AutofillInputText.CurrentWord(text, caret);
        if (typed.Length == 0)
        {
            ShowRecommendations();
            return;
        }

        IReadOnlyList<SubjectSuggestion> matches = _service.MatchSubjects(typed);
        if (matches.Count == 0)
        {
            Hide();
            return;
        }

        Show(matches);
    }

    private void ShowRecommendations() => Show(_service.RecommendSubjects(AutofillPopup.RecommendationLimit));

    private void Show(IReadOnlyList<SubjectSuggestion> subjects)
    {
        if (subjects.Count == 0)
        {
            Hide();
            return;
        }

        List<AutofillEntry> entries = subjects
            .Select(item => new AutofillEntry { Text = item.Name, ColorHex = item.Color, Payload = item })
            .ToList();
        _popup.Show(this, _box, entries, Apply, preserveHighlight: _composing);
    }

    private void Apply(AutofillEntry entry)
    {
        if (entry.Payload is not SubjectSuggestion suggestion) return;
        string text = _box.Text ?? string.Empty;
        int caret = Math.Clamp(_box.SelectionStart, 0, text.Length);
        string replaced = AutofillInputText.CurrentWord(text, caret);
        WriteText(suggestion, focus: true);
        _service.NotifySubjectCompleted(suggestion);
        _applied(suggestion);
        string expected = _box.Text ?? string.Empty;
        int caretAfterApply = Math.Clamp(_box.SelectionStart, 0, expected.Length);
        // 输入法随后可能再上屏一次拼音，组合结束或失焦时把候选补写回去，避免要再点第二下。
        _guard.Start(
            () => _box.Text ?? string.Empty,
            () => _composing,
            replaced,
            expected,
            () =>
            {
                Hide();
                // 标题是纯文本，整段写回采纳结果即可覆盖“整段回退”和“拼音补在候选前后”两种情况。
                WriteText(expected, caretAfterApply);
            });
    }

    /// <summary>
    /// 把候选写进标题。写入不改变焦点，由调用方决定是否把焦点交回输入框，
    /// 这样补写（回滚输入法上屏的拼音）时不会把用户已经点到别处的焦点抢回来。
    /// </summary>
    private void WriteText(SubjectSuggestion suggestion, bool focus)
    {
        string text = _box.Text ?? string.Empty;
        int caret = Math.Clamp(_box.SelectionStart, 0, text.Length);
        int start = AutofillInputText.WordStart(text, caret);
        WriteText(string.Concat(text.AsSpan(0, start), suggestion.Name, text.AsSpan(caret)), start + suggestion.Name.Length);
        if (focus) _box.Focus(FocusState.Programmatic);
    }

    /// <summary>整段写入标题并定位光标；学科统计与配色只在本轮采纳时结算一次，补写不再重复触发。</summary>
    private void WriteText(string text, int caret)
    {
        _suppress = true;
        _box.Text = text;
        _box.SelectionStart = Math.Clamp(caret, 0, text.Length);
        _box.SelectionLength = 0;
        _suppress = false;
    }
}

/// <summary>作业正文的补全，并在一条作业的文本真正变化后统计一次。</summary>
internal sealed class HomeworkAutofillInput
{
    private readonly AutofillService _service;
    private readonly AutofillPopup _popup;
    private readonly RichEditBox _editor;
    private readonly Func<string?> _subjectName;
    private readonly Func<bool> _ready;
    private readonly Func<string> _content;
    private readonly Action _changed;
    private readonly AutofillInputText.ApplyGuard _guard;
    private bool _suppress;
    private bool _composing;
    private string _counted;

    public HomeworkAutofillInput(
        AutofillService service,
        AutofillPopup popup,
        RichEditBox editor,
        Func<string?> subjectName,
        Func<bool> ready,
        Func<string> content,
        Action changed)
    {
        _service = service;
        _popup = popup;
        _editor = editor;
        _subjectName = subjectName;
        _ready = ready;
        _content = content;
        _changed = changed;
        _guard = new AutofillInputText.ApplyGuard(editor.DispatcherQueue);
        // 以打开时的文本为基准，反复进出同一条作业不会重复计数。
        _counted = content();
        editor.TextChanged += (_, _) =>
        {
            if (!_ready() || _suppress) return;
            _guard.Verify();
            Refresh();
        };
        // 组合态下输入法用自己的候选窗接管上下键与 Tab，这里只记录状态并让刷新保留选中项。
        editor.TextCompositionStarted += (_, _) => _composing = true;
        editor.TextCompositionEnded += (_, _) =>
        {
            _composing = false;
            // 组合结束是输入法上屏的时机，先核对采纳结果，再按上屏文字重新给候选。
            _guard.Verify();
            Refresh();
        };
        // 光标移动到另一个词上时重新计算候选，避免沿用上一次的列表。
        editor.SelectionChanged += (_, _) =>
        {
            if (!_suppress && _ready()) Refresh();
        };
        editor.Loaded += (_, _) =>
        {
            // 文档装载（含旧数据修正）结束后重新取基准，避免把加载差异当成一次输入。
            if (_ready()) _counted = _content();
        };
        // 失焦会让输入法把还没上屏的组合文本一次性提交，可能覆盖刚采纳的候选：
        // 先核对并把候选补写回来，再按补写后的文本结算统计。
        editor.LostFocus += (_, _) =>
        {
            Hide();
            _guard.Verify();
            Flush();
        };
        editor.Unloaded += (_, _) =>
        {
            Hide();
            _guard.Stop();
        };
    }

    public void Hide() => _popup.HideIfOwnedBy(this);

    /// <summary>文本相比上次结算发生变化时才计数一次；只做格式化或反复打开不计数。</summary>
    public void Flush()
    {
        string content = _content();
        if (string.Equals(content, _counted, StringComparison.Ordinal)) return;
        _counted = content;
        if (_service.RecordHomework(content, _subjectName())) _changed();
    }

    private void Refresh()
    {
        if (_editor.IsReadOnly)
        {
            Hide();
            return;
        }

        (int caret, string typed) = CurrentInput();
        if (typed.Length == 0)
        {
            Hide();
            return;
        }

        IReadOnlyList<HomeworkSuggestion> matches = _service.MatchHomework(typed, _subjectName());
        if (matches.Count == 0)
        {
            Hide();
            return;
        }

        List<AutofillEntry> entries = matches
            .Select(item => new AutofillEntry { Text = item.Text, Payload = item })
            .ToList();
        _popup.Show(this, _editor, entries, Apply, CaretRect(caret), preserveHighlight: _composing);
    }

    private void Apply(AutofillEntry entry)
    {
        if (entry.Payload is not HomeworkSuggestion suggestion) return;
        (int caret, string typed) = CurrentInput();
        if (typed.Length == 0) return;
        WriteText(suggestion, caret, typed, focus: true);
        _service.NotifyHomeworkCompleted(suggestion);
        string expected = _content();
        // 输入法随后可能再上屏一次拼音，组合结束或失焦时把被覆盖的那一段补写回候选。
        _guard.Start(
            _content,
            () => _composing,
            typed,
            expected,
            () =>
            {
                Hide();
                RestoreApplied(suggestion, typed, expected);
            });
    }

    /// <summary>按光标位置替换正在输入的那一段；补写（回滚输入法上屏）时不抢回焦点。</summary>
    private void WriteText(HomeworkSuggestion suggestion, int caret, string typed, bool focus)
    {
        int start = caret - typed.Length;
        if (start < 0) return;
        _suppress = true;
        ITextRange range = _editor.Document.GetRange(start, caret);
        range.Text = suggestion.Text;
        int next = start + suggestion.Text.Length;
        _editor.Document.Selection.SetRange(next, next);
        _suppress = false;
        if (focus) _editor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 输入法把组合文本上屏时会覆盖刚采纳的候选，这里只改被覆盖的那一段：
    /// 正在输入的那段拼音还在就按词替换，被补在候选前后就直接删掉，
    /// 其余文字与富文本格式保持不动。
    /// </summary>
    private void RestoreApplied(HomeworkSuggestion suggestion, string typed, string expected)
    {
        (int caret, string word) = CurrentInput();
        if (word == typed)
        {
            WriteText(suggestion, caret, typed, focus: false);
            return;
        }

        string text = _content();
        int index = text.IndexOf(typed, StringComparison.Ordinal);
        while (index >= 0)
        {
            // 替换或删除这一处后正好回到采纳结果，才认定它是输入法补进来的拼音。
            if (text.Remove(index, typed.Length).Insert(index, suggestion.Text) == expected)
            {
                WriteText(suggestion, index + typed.Length, typed, focus: false);
                return;
            }

            if (text.Remove(index, typed.Length) == expected)
            {
                _suppress = true;
                _editor.Document.GetRange(index, index + typed.Length).Text = string.Empty;
                _suppress = false;
                return;
            }

            index = text.IndexOf(typed, index + 1, StringComparison.Ordinal);
        }
    }

    private (int Caret, string Typed) CurrentInput()
    {
        int length = _editor.Document.GetRange(0, int.MaxValue).EndPosition;
        int caret = Math.Clamp(_editor.Document.Selection.EndPosition, 0, Math.Max(0, length));
        ITextRange range = _editor.Document.GetRange(0, caret);
        range.GetText(TextGetOptions.None, out string prefix);
        return (caret, AutofillInputText.CurrentWord(prefix, prefix.Length));
    }

    /// <summary>光标矩形按编辑框客户区返回，滚动到可视区外时按边界收拢。</summary>
    private Rect? CaretRect(int caret)
    {
        try
        {
            _editor.Document.Selection.SetRange(caret, caret);
            _editor.Document.Selection.GetRect(PointOptions.ClientCoordinates, out Rect rect, out _);
            double height = Math.Max(20, rect.Height);
            double top = Math.Clamp(rect.Y, 0, Math.Max(0, _editor.ActualHeight - height));
            return new Rect(rect.X, top, Math.Max(1, rect.Width), height);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
