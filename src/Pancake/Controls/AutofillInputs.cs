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
    /// 采纳候选后的守护。真实中文输入法下，应用写入候选会让输入法立刻结束组合，
    /// 紧接着输入法又把这次组合的内容按“原始拼音”写回文档，覆盖刚写入的候选；
    /// 这次上屏也可能拖到输入框失焦时发生。两种时机都不是用户的新输入，因此：
    /// 组合结束后、或输入框失焦后，只要文本变得不是采纳结果，就把被改掉的那一段补写回候选。
    /// 用户重新开始组合时解除守护；补写不抢回焦点。
    /// </summary>
    internal sealed class ApplyGuard
    {
        public const int IntervalMilliseconds = 150;
        /// <summary>定时核对上限：覆盖组合结束后稍晚才上屏、以及失焦提交两种时机。</summary>
        public const int MaximumChecks = 20;
        /// <summary>“整体退回采纳前文本”的核对上限：沿用旧版对输入法再次上屏的观察窗口。</summary>
        public const int RevertChecks = 10;
        /// <summary>与输入法互相覆盖时的最大补写次数，超过就交回用户处理。</summary>
        public const int MaximumRestores = 2;

        private readonly DispatcherQueueTimer _timer;
        private Func<string>? _text;
        private Func<bool>? _focused;
        private Func<bool>? _looksReverted;
        private Action? _restore;
        private string _previous = string.Empty;
        private string _expected = string.Empty;
        private bool _compositionEnded;
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
        /// <param name="focused">输入框当前是否持有焦点。</param>
        /// <param name="previous">采纳前的整段文本。</param>
        /// <param name="expected">采纳完成后应得的整段文本。</param>
        /// <param name="looksReverted">
        /// 光标附近是否又变回采纳时被替换的那一段（老版判据），用于识别整段文本之外的覆盖方式。
        /// </param>
        /// <param name="restore">把被输入法改掉的那一段补写回采纳结果，且不改变焦点。</param>
        public void Start(Func<string> text, Func<bool> focused, string previous, string expected, Func<bool> looksReverted, Action restore)
        {
            _text = text;
            _focused = focused;
            _looksReverted = looksReverted;
            _previous = previous;
            _expected = expected;
            _restore = restore;
            _compositionEnded = false;
            _checks = 0;
            _restores = 0;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _text = null;
            _focused = null;
            _looksReverted = null;
            _restore = null;
        }

        /// <summary>文本变化、失焦或组合结束时核对一次。</summary>
        public void Verify()
        {
            if (_text is not null) Check();
        }

        /// <summary>组合结束：这次组合的内容随时可能被输入法上屏，之后按“已上屏”判断。</summary>
        public void CompositionEnded()
        {
            if (_text is null) return;
            _compositionEnded = true;
            Check();
        }

        private void OnTick(DispatcherQueueTimer sender, object args)
        {
            if (++_checks > MaximumChecks)
            {
                // 到点后停止轮询，文本变化与失焦事件仍会触发核对。
                _timer.Stop();
                return;
            }

            Check();
        }

        private void Check()
        {
            if (_text is null || _focused is null || _restore is null)
            {
                Stop();
                return;
            }

            string text = _text();
            if (text == _expected) return;   // 候选还在，继续守着等输入法可能的上屏

            // 组合结束或输入框失焦后文本发生变化，只可能是输入法把组合内容上屏。
            // 没有组合时整体退回采纳前的文本，同样按输入法再次上屏处理（观察窗口内有效）。
            bool committed = _compositionEnded || !_focused();
            bool reverted = !committed && _checks <= RevertChecks &&
                (string.Equals(text, _previous, StringComparison.Ordinal) || (_looksReverted?.Invoke() ?? false));
            if (!committed && !reverted)
            {
                // 组合还在进行且文本不是退回原样，说明是用户自己的输入，不再干预。
                Stop();
                return;
            }

            if (++_restores > MaximumRestores)
            {
                Stop();
                return;
            }

            _restore();
            Stop();
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
    private bool _justApplied;
    private bool _afterComposition;

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
            // 输入法刚刚上屏时（例如按空格确认候选）也给出同名学科候选，方便接着用键盘套色；
            // 刚采纳过（这次组合是被我们写入结束的）就不重复弹。
            bool allowExact = _afterComposition && !_justApplied;
            _justApplied = false;
            _guard.Verify();
            Refresh(allowExact: allowExact);
        };
        // 组合期间输入法用自己的候选窗接管上下键与 Tab（应用收不到这些按键），
        // 这里只记录状态：刷新候选时保留已移动的选中项，并解除上一轮的采纳守护。
        box.TextCompositionStarted += (_, _) =>
        {
            _composing = true;
            _justApplied = false;
            _afterComposition = false;
            _guard.Stop();
        };
        box.TextCompositionEnded += (_, _) =>
        {
            _composing = false;
            _afterComposition = true;
            // 组合结束是输入法上屏的时机：先核对采纳结果是否被覆盖，
            // 再按上屏文字重新给候选。刚采纳过（组合是被写入结束的）就不再重复弹同名候选。
            _guard.CompositionEnded();
            Refresh(allowExact: !_justApplied);
        };
        box.SelectionChanged += (_, _) =>
        {
            if (!_suppress) Refresh(allowExact: _afterComposition && !_justApplied);
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

    /// <param name="allowExact">
    /// 输入法上屏的文字本身就是某个学科时也给出该候选，方便接着用键盘套用学科颜色。
    /// 只有组合结束这条路径会打开，普通输入仍然不提示已经输入完整的学科。
    /// </param>
    private void Refresh(bool allowExact = false)
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

        IReadOnlyList<SubjectSuggestion> matches = _service.MatchSubjects(typed, includeExact: allowExact);
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
        _justApplied = true;
        // 输入法会在采纳后把这次组合的内容按原始拼音再上屏一次（也可能拖到失焦时），
        // 组合结束或失焦后核对并把被覆盖的内容补写回候选，避免要再点第二下。
        _guard.Start(
            () => _box.Text ?? string.Empty,
            () => _box.FocusState != FocusState.Unfocused,
            text,
            expected,
            () => AutofillInputText.CurrentWord(_box.Text ?? string.Empty, _box.SelectionStart) == replaced,
            () =>
            {
                Hide();
                // 补写属于回滚输入法上屏：不要再按“输入法刚上屏”重新弹出同名候选。
                _afterComposition = false;
                _justApplied = true;
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
        // 组合期间输入法用自己的候选窗接管上下键与 Tab（应用收不到这些按键），
        // 这里只记录状态：刷新候选时保留选中项，并解除上一轮的采纳守护。
        editor.TextCompositionStarted += (_, _) =>
        {
            _composing = true;
            _guard.Stop();
        };
        editor.TextCompositionEnded += (_, _) =>
        {
            _composing = false;
            // 组合结束是输入法上屏的时机：先核对采纳结果是否被覆盖，再按上屏文字重新给候选。
            _guard.CompositionEnded();
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
        string previous = DocumentText();
        WriteText(suggestion, caret, typed, focus: true);
        _service.NotifyHomeworkCompleted(suggestion);
        string expected = DocumentText();
        // 输入法会在采纳后把这次组合的内容按原始拼音再上屏一次（也可能拖到失焦时），
        // 组合结束或失焦后核对并把被覆盖的那一段补写回候选。
        _guard.Start(
            DocumentText,
            () => _editor.FocusState != FocusState.Unfocused,
            previous,
            expected,
            () => CurrentInput().Typed == typed,
            () =>
            {
                Hide();
                RestoreApplied(expected);
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
    /// 输入法把组合内容上屏、覆盖了刚采纳的候选时，只重写与采纳结果不一致的那一段：
    /// 取两者的公共前后缀，中间不一致的部分按采纳结果恢复，其余文字与富文本格式保持不动。
    /// </summary>
    private void RestoreApplied(string expected)
    {
        string text = DocumentText();
        if (string.Equals(text, expected, StringComparison.Ordinal)) return;

        int limit = Math.Min(text.Length, expected.Length);
        int prefix = 0;
        while (prefix < limit && text[prefix] == expected[prefix]) prefix++;
        int suffix = 0;
        while (suffix < limit - prefix && text[text.Length - 1 - suffix] == expected[expected.Length - 1 - suffix]) suffix++;

        // 区间必须非空：RichEdit 不接受对空区间的写入，纯插入时向前借一个字符一起重写。
        int start = Math.Max(0, Math.Min(prefix, text.Length - suffix - 1));
        int end = Math.Max(start, text.Length - suffix);
        _suppress = true;
        if (end > start)
        {
            _editor.Document.GetRange(start, end).Text = expected[start..Math.Max(start, expected.Length - suffix)];
        }
        else
        {
            // 整条作业被输入法清空时只能整段写回。
            _editor.Document.SetText(TextSetOptions.None, expected);
        }
        _suppress = false;
    }

    /// <summary>
    /// 读取作业正文的纯文本。RichEdit 文档的最后一个字符是控件维护的段落标记，不属于内容，
    /// 这里与保存时一样把它排除掉（旧数据可能多出若干换行，一并收尾），
    /// 保证下标能直接当作文档区间位置使用。
    /// </summary>
    private string DocumentText()
    {
        ITextRange range = _editor.Document.GetRange(0, int.MaxValue);
        if (range.EndPosition > 0) range.EndPosition -= 1;
        range.GetText(TextGetOptions.None, out string text);
        return text.TrimEnd('\r', '\n');
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
