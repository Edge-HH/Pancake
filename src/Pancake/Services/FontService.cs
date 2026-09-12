using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pancake.Services;

/// <summary>字体显示名和应用资源路径集中解析；RTF 对外保存标准家族名。</summary>
public static class FontService
{
    public const string FamilyName = "HarmonyOS Sans SC";
    public const string ResourceName = "ms-appx:///Assets/Fonts/HarmonyOS_Sans_SC_Regular.ttf#HarmonyOS Sans SC";
    public static FontFamily DefaultFamily { get; } = new(ResourceName);
    private static readonly Lazy<string[]> Fonts = new(EnumerateFonts);
    private static readonly ConditionalWeakTable<RichEditBox, List<(ITextRange Range, string Family)>> Fallbacks = new();

    public static IReadOnlyList<string> AvailableFamilies => Fonts.Value;

    public static string NormalizeRtf(string rtf) => rtf.Replace(ResourceName, FamilyName, StringComparison.OrdinalIgnoreCase);

    public static void RebindBundledFont(RichEditBox editor, IReadOnlyList<FontFallbackState>? savedFallbacks = null)
    {
        editor.FontFamily = DefaultFamily;
        Fallbacks.Remove(editor);
        var fallbacks = Fallbacks.GetOrCreateValue(editor);
        ITextRange all = editor.Document.GetRange(0, int.MaxValue);
        int end = all.EndPosition;
        foreach (FontFallbackState saved in savedFallbacks ?? [])
        {
            if (saved.Start < 0 || saved.Length <= 0 || saved.Start >= end - 1) continue;
            ITextRange range = editor.Document.GetRange(saved.Start, Math.Min(end - 1, saved.Start + saved.Length));
            if (Fonts.Value.Contains(saved.Family, StringComparer.CurrentCultureIgnoreCase)) range.CharacterFormat.Name = saved.Family;
            else { range.CharacterFormat.Name = ResourceName; fallbacks.Add((range, saved.Family)); }
        }
        int position = 0;
        while (position < end)
        {
            ITextRange run = editor.Document.GetRange(position, position);
            run.MoveEnd(TextRangeUnit.CharacterFormat, 1);
            if (run.EndPosition <= position) run.EndPosition = position + 1;
            string name = run.CharacterFormat.Name;
            if (name.Contains("HarmonyOS", StringComparison.OrdinalIgnoreCase))
                run.CharacterFormat.Name = ResourceName;
            else if (!string.IsNullOrWhiteSpace(name) && !Fonts.Value.Contains(name, StringComparer.CurrentCultureIgnoreCase))
            {
                fallbacks.Add((editor.Document.GetRange(run.StartPosition, run.EndPosition), name));
                run.CharacterFormat.Name = ResourceName;
            }
            position = run.EndPosition;
        }
    }

    public static List<FontFallbackState> CaptureFallbacks(RichEditBox editor)
    {
        int end = editor.Document.GetRange(0, int.MaxValue).EndPosition - 1;
        return Fallbacks.GetOrCreateValue(editor).Where(item => item.Range.EndPosition > item.Range.StartPosition && item.Range.StartPosition < end)
            .Select(item => new FontFallbackState { Start = item.Range.StartPosition, Length = Math.Min(end, item.Range.EndPosition) - item.Range.StartPosition, Family = item.Family }).ToList();
    }

    private static void ForgetFallbacks(RichEditBox editor, int start, int end)
    {
        var originals = Fallbacks.GetOrCreateValue(editor);
        foreach (var item in originals.ToArray())
        {
            int left = item.Range.StartPosition, right = item.Range.EndPosition;
            if (right <= start || left >= end) continue;
            originals.Remove(item);
            if (left < start) originals.Add((editor.Document.GetRange(left, start), item.Family));
            if (right > end) originals.Add((editor.Document.GetRange(end, right), item.Family));
        }
    }

    public static FrameworkElement CreateFontPicker(RichEditBox editor, Action changed)
    {
        AutoSuggestBox input = new() { Width = 180, PlaceholderText = "字体", Text = FamilyName, ItemsSource = Fonts.Value, MaxSuggestionListHeight = 300 };
        int start = 0, end = 0;
        void Remember() { start = editor.Document.Selection.StartPosition; end = editor.Document.Selection.EndPosition; }
        Remember();
        editor.SelectionChanged += (_, _) => { if (editor.FocusState != FocusState.Unfocused) Remember(); };
        input.GettingFocus += (_, _) => Remember();
        input.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                input.ItemsSource = Fonts.Value.Where(f => f.Contains(input.Text, StringComparison.CurrentCultureIgnoreCase)).Take(80).ToArray();
        };
        void Apply(string name)
        {
            if (!Fonts.Value.Contains(name, StringComparer.CurrentCultureIgnoreCase)) return;
            editor.Focus(FocusState.Programmatic);
            editor.Document.Selection.SetRange(start, end);
            ForgetFallbacks(editor, start, end);
            // 折叠选区设置插入格式，不扩展到整条作业。
            editor.Document.Selection.CharacterFormat.Name = name == FamilyName ? ResourceName : name;
            changed();
        }
        input.QuerySubmitted += (_, args) => Apply(args.ChosenSuggestion as string ?? args.QueryText);
        input.SuggestionChosen += (_, args) => { input.Text = (string)args.SelectedItem; Apply(input.Text); };
        return input;
    }

    private static string[] EnumerateFonts()
    {
        HashSet<string> names = new(StringComparer.CurrentCultureIgnoreCase) { FamilyName };
        nint dc = CreateCompatibleDC(0);
        if (dc != 0)
        {
            try
            {
                LogFont request = new() { CharSet = 1, FaceName = "" };
                FontCallback callback = (font, _, _, _) =>
                {
                    string? name = Marshal.PtrToStringUni(font + 28);
                    if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) names.Add(name);
                    return 1;
                };
                EnumFontFamiliesEx(dc, ref request, callback, 0, 0);
                GC.KeepAlive(callback);
            }
            finally { DeleteDC(dc); }
        }
        return names.OrderBy(n => n != FamilyName).ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }
    private delegate int FontCallback(nint logFont, nint metric, uint type, nint parameter);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "EnumFontFamiliesExW")]
    private static extern int EnumFontFamiliesEx(nint dc, ref LogFont font, FontCallback callback, nint parameter, uint flags);
}
