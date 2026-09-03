using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Pancake.Models;

namespace Pancake.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly string[] _accentPalette = ["#4ADE80", "#818CF8", "#60A5FA", "#FBBF24", "#F472B6", "#2DD4BF"];
    private ObservableCollection<SubjectBoard> _subjects;
    private ObservableCollection<SubjectBoard>? _editSnapshot;
    private SubjectBoard? _selectedSubject;
    private HomeworkEntry? _selectedHomework;

    public MainViewModel()
    {
        _subjects = CreateSampleSubjects();
        SelectedSubject = _subjects.FirstOrDefault();
        SelectedHomework = SelectedSubject?.Entries.FirstOrDefault();
    }

    public ObservableCollection<SubjectBoard> Subjects
    {
        get => _subjects;
        private set => SetProperty(ref _subjects, value);
    }

    public SubjectBoard? SelectedSubject
    {
        get => _selectedSubject;
        set
        {
            if (SetProperty(ref _selectedSubject, value))
            {
                SelectedHomework = value?.Entries.FirstOrDefault();
            }
        }
    }

    public HomeworkEntry? SelectedHomework
    {
        get => _selectedHomework;
        set => SetProperty(ref _selectedHomework, value);
    }

    public void BeginEditing() => _editSnapshot = CloneSubjects(Subjects);

    public void PublishEditing() => _editSnapshot = null;

    public void ReplaceSubjects(IEnumerable<SubjectBoard> subjects)
    {
        Subjects = new ObservableCollection<SubjectBoard>(subjects);
        SelectedSubject = Subjects.FirstOrDefault();
    }

    public void DiscardEditing()
    {
        if (_editSnapshot is null)
        {
            return;
        }

        Subjects = CloneSubjects(_editSnapshot);
        _editSnapshot = null;
        SelectedSubject = Subjects.FirstOrDefault();
    }

    public SubjectBoard AddSubject(string name)
    {
        string hex = _accentPalette[Subjects.Count % _accentPalette.Length];
        int index = Subjects.Count;
        SubjectBoard subject = new()
        {
            Name = name,
            AccentHex = hex,
            AccentBrush = BrushFromHex(hex),
            X = 36 + (index % 2) * 470,
            Y = 36 + (index / 2) * 360
        };
        Subjects.Add(subject);
        SelectedSubject = subject;
        return subject;
    }

    public HomeworkEntry? AddHomework()
    {
        if (SelectedSubject is null)
        {
            return null;
        }

        HomeworkEntry homework = new() { Content = "在这里输入作业内容" };
        SelectedSubject.Entries.Add(homework);
        SelectedSubject.NotifyEntriesChanged();
        SelectedHomework = homework;
        return homework;
    }

    public static SolidColorBrush BrushFromHex(string hex)
    {
        string value = hex.TrimStart('#');
        return new SolidColorBrush(Windows.UI.Color.FromArgb(
            255,
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16)));
    }

    private static ObservableCollection<SubjectBoard> CloneSubjects(IEnumerable<SubjectBoard> source) =>
        new(source.Select(subject => subject.Clone()));

    private static ObservableCollection<SubjectBoard> CreateSampleSubjects()
    {
        SubjectBoard math = CreateSubject("数学", "#65D46E", 36, 36, 430, 360);
        math.Entries.Add(CreateHomework("完成 P30 练习题\n复习二次函数公式", true));
        math.Entries.Add(CreateHomework("整理课堂错题，写出三种解法", false));

        SubjectBoard english = CreateSubject("英语", "#7567FF", 500, 36, 430, 250);
        english.Entries.Add(CreateHomework("背诵 Unit 3 单词\n完成阅读理解", false));

        SubjectBoard physics = CreateSubject("物理", "#60A5FA", 500, 320, 520, 300);
        physics.Entries.Add(CreateHomework("完成力学练习第 1—8 题\n预习串并联电路", true));

        SubjectBoard chinese = CreateSubject("语文", "#FBBF24", 36, 430, 430, 280);
        chinese.Entries.Add(CreateHomework("背诵《赤壁赋》第二段\n整理作文素材", false));

        return [math, english, physics, chinese];
    }

    private static SubjectBoard CreateSubject(
        string name,
        string accentHex,
        double x,
        double y,
        double width,
        double height) => new()
    {
        Name = name,
        AccentHex = accentHex,
        AccentBrush = BrushFromHex(accentHex),
        X = x,
        Y = y,
        TileWidth = width,
        TileHeight = height
    };

    private static HomeworkEntry CreateHomework(string content, bool hasHandwriting, params AttachmentItem[] attachments)
    {
        HomeworkEntry homework = new() { Content = content, HasHandwriting = hasHandwriting };
        foreach (AttachmentItem attachment in attachments)
        {
            homework.Attachments.Add(attachment);
        }

        return homework;
    }
}
