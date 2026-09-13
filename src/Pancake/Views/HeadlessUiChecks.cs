#if PANCAKE_UI_TESTS
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Platforms.Abstraction;
using Pancake.Platforms.Abstraction.Services;
using Pancake.RichText;
using Pancake.Services;
using Pancake.ViewModels;

namespace Pancake.Views;

/// <summary>
/// Headless 界面自检使用的输入通道。实现在测试宿主里（Avalonia.Headless 的真实输入管线），
/// 界面层只声明需要哪些输入，不依赖 Headless 包本身。
/// </summary>
public interface IHeadlessInput
{
    /// <summary>绑定要操作的窗口。</summary>
    void Attach(TopLevel window);

    /// <summary>等待一次界面空闲：处理已排队的布局、渲染与输入。</summary>
    void Idle();

    /// <summary>按下并拖动（移动过程中保持按下），最后抬起。</summary>
    void Drag(Point from, Point to);

    /// <summary>按下后不抬起，用于验证“按住指针”相关的行为。</summary>
    void Press(Point point);

    /// <summary>在按下过的位置抬起。</summary>
    void Release(Point point);

    /// <summary>等待指定时长，期间继续驱动界面消息循环。</summary>
    void Wait(TimeSpan duration);

    /// <summary>向当前焦点控件输入文本（等价于键盘输入字符）。</summary>
    void TypeText(string text);

    /// <summary>按下并抬起一个按键（不输入字符）。</summary>
    void PressKey(Key key);
}

/// <summary>
/// Headless 界面自检：不需要显示设备，三个平台都能跑。
/// 覆盖方案里约定的几类检查——主窗口与设置页构建、主题切换、磁贴与图片控件布局与命中区域、
/// 全屏退出提示、按住指针时的自动隐藏例外——输入全部走真实的事件路由。
/// </summary>
public static class HeadlessUiChecks
{
    /// <summary>运行全部检查，返回逐条结果；任一条失败会以 FAILED 前缀写在第一行。</summary>
    public static IReadOnlyList<string> Run(IHeadlessInput input, string dataRoot, string evidenceDirectory)
    {
        List<string> lines = [];
        MainWindow? window = null;
        try
        {
            // 与正式启动一致：注册随包字体并接上系统字体目录，字体相关检查才有真实候选。
            FontService.RegisterBundledFonts();
            MainViewModel viewModel = new();
            viewModel.LoadFromDisk(dataRoot);
            // 自检要用真实项目：没有当前项目时回到看板会显示空状态引导面板，看板内容被盖住，
            // 与正式使用的状态不一致（命中测试、手写都会因此失效）。
            viewModel.CreateProject(keepLayout: true);
            // 预备两个科目，用来检查磁贴布局、命中区域与手写。
            viewModel.ReplaceSubjects(
            [
                CreateSubject("语文", 20, 20, 430, 320, ["背诵《赤壁赋》第二段", "完成课堂练习。"]),
                CreateSubject("数学", 480, 20, 360, 260, ["完成 P30 练习题"])
            ]);
            window = new MainWindow(viewModel, new AppLaunchOptions(StartFullScreen: false, View: "display"));
            window.Show();
            input.Attach(window);
            input.Idle();

            lines.Add(CheckWindowAndSettings(window, viewModel));
            lines.Add(CheckThemeSwitching(window, viewModel, input));
            lines.Add(CheckTileLayoutAndHitTesting(window, viewModel, input));
            // 手写检查紧跟磁贴命中检查：两者都依赖看板可命中，顺序相邻便于定位问题。
            lines.Add(CheckInkDrawing(window, viewModel, input));
            lines.Add(CheckBoardInteractions(window, viewModel, input));
            lines.Add(CheckFreeLayoutWidgetInteractions(window, viewModel, input));
            lines.Add(CheckAttachmentInteractions(window, viewModel, input, dataRoot));
            lines.Add(CheckAutofillKeyboard(window, viewModel, input));
            lines.Add(CheckScalingAndHitTargets(window, viewModel, input, evidenceDirectory));
            lines.Add(CheckRichTextFonts(window, viewModel, input));
            lines.Add(CheckWallpaperEngineImportDialog(window, dataRoot, input, evidenceDirectory));
            lines.Add(CheckFullScreenHint(window, input));
            lines.Add(CheckPressedPointerKeepsToolbar(window, viewModel, input));
            // 项目操作会新建项目（清空作业内容）并删除项目，放在最后，避免影响依赖作业内容的检查。
            lines.Add(CheckProjectOperations(window, viewModel, input));
            lines.Insert(0, "HEADLESS_OK");
        }
        catch (Exception ex)
        {
            string trace = ex.StackTrace ?? string.Empty;
            lines.Insert(0, $"HEADLESS_FAILED {ex.GetType().Name} {ex.Message}\n{trace[..Math.Min(trace.Length, 1500)]}");
        }
        finally
        {
            window?.Close();
        }

        return lines;
    }

    /// <summary>主窗口与设置页：窗口能构建，12 个设置分类都能真实建成页面。</summary>
    private static string CheckWindowAndSettings(MainWindow window, MainViewModel viewModel)
    {
        if (window.FindControl<Grid>("RootShell") is null) throw new InvalidOperationException("主窗口没有构建出来。");
        int sections = 0;
        window.ShowSettingsForVerification();
        foreach (string tag in SettingsSectionTags())
        {
            window.ShowSettingsSectionForVerification(tag);
            StackPanel content = window.FindControl<StackPanel>("SettingsContent")!;
            if (content.Children.Count == 0) throw new InvalidOperationException($"设置分类 {tag} 没有内容。");
            sections++;
        }

        window.ShowBoardForVerification();
        if (viewModel.Subjects.Count == 0) throw new InvalidOperationException("看板没有科目。");
        return $"window+sections:ok({sections} 个分类,{viewModel.Subjects.Count} 个科目)";
    }

    /// <summary>主题切换：深色/浅色来回切换后界面仍是可用的，主题色与画刷同步更新。</summary>
    private static string CheckThemeSwitching(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        bool original = BoardTheme.IsLight;
        List<string> states = [];
        foreach (bool light in new[] { true, false, true })
        {
            viewModel.Settings.Theme = light ? "Light" : "Dark";
            window.SettingChangedForVerification();
            input.Idle();
            if (BoardTheme.IsLight != light) throw new InvalidOperationException("主题状态没有切换到预期值。");
            Border topBar = window.FindControl<Border>("TopBar")!;
            if (topBar.BorderBrush is not ISolidColorBrush line) throw new InvalidOperationException("主题切换后边框画刷丢失。");
            states.Add(light ? $"light:{line.Color}" : $"dark:{line.Color}");
        }

        viewModel.Settings.Theme = original ? "Light" : "Dark";
        window.SettingChangedForVerification();
        input.Idle();
        return "theme:" + string.Join("->", states);
    }

    /// <summary>
    /// 磁贴布局与命中区域：模型尺寸与控件尺寸一致，磁贴不越出看板，
    /// 命中测试在磁贴内部命中的是磁贴自身、在磁贴外部不命中该磁贴。
    /// </summary>
    private static string CheckTileLayoutAndHitTesting(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        Canvas canvas = window.FindControl<Canvas>("BoardCanvas")!;
        List<SubjectTileControl> tiles = canvas.Children.OfType<SubjectTileControl>().ToList();
        if (tiles.Count != viewModel.Subjects.Count) throw new InvalidOperationException("看板上的磁贴数量与科目数量不一致。");
        for (int index = 0; index < tiles.Count; index++)
        {
            SubjectBoard subject = viewModel.Subjects[index];
            SubjectTileControl tile = tiles[index];
            if (Math.Abs(tile.Width - subject.TileWidth) > 0.5 || Math.Abs(tile.Height - subject.TileHeight) > 0.5)
            {
                throw new InvalidOperationException($"磁贴尺寸与模型不一致：{tile.Width}x{tile.Height} vs {subject.TileWidth}x{subject.TileHeight}。");
            }

            if (Canvas.GetLeft(tile) + tile.Width > canvas.Width + 1 || Canvas.GetTop(tile) + tile.Height > canvas.Height + 1)
            {
                throw new InvalidOperationException("磁贴超出了作业板范围。");
            }

            // 命中区域：磁贴中心必须命中磁贴内部的控件，而磁贴之外不能命中它。
            Window host = window;
            Point center = tile.TranslatePoint(new Point(tile.Width / 2, tile.Height / 2), host) ?? default;
            IInputElement? inside = host.InputHitTest(center);
            if (inside is null || !IsInsideTile(inside, tile))
            {
                throw new InvalidOperationException(
                    $"磁贴中心的命中测试没有落在磁贴内部（tile={tile.Bounds} canvasAt={Canvas.GetLeft(tile):0},{Canvas.GetTop(tile):0} " +
                    $"center={center:0.#},{center:0.#} hit={inside?.GetType().Name ?? "null"} " +
                    $"tileVisible={tile.IsVisible}/{tile.IsHitTestVisible} tileTopLeft={tile.TranslatePoint(default, host)} " +
                    $"localHit={tile.InputHitTest(new Point(tile.Width / 2, tile.Height / 2))?.GetType().Name ?? "null"} " +
                    $"canvasHit={canvas.InputHitTest(new Point(Canvas.GetLeft(tile) + tile.Width / 2, Canvas.GetTop(tile) + tile.Height / 2))?.GetType().Name ?? "null"} " +
                    $"hitChain={DescribeChain(inside)} tileChain={DescribeChain(tile)} " +
                    $"offset={window.FindControl<ScrollViewer>("BoardScroller")?.Offset} " +
                    $"canvasTopLeft={canvas.TranslatePoint(default, host)} board={canvas.Bounds} " +
                    $"window={window.Bounds.Width:0}x{window.Bounds.Height:0}）。");
            }

            Point outside = tile.TranslatePoint(new Point(-12, -12), host) ?? default;
            IInputElement? other = host.InputHitTest(outside);
            if (other is not null && IsInsideTile(other, tile))
            {
                throw new InvalidOperationException("磁贴外侧的命中测试落到了这块磁贴上。");
            }
        }

        return $"tiles:layout+hit:ok({tiles.Count} 块,{canvas.Width:0}x{canvas.Height:0})";
    }

    /// <summary>
    /// 缩放与命中区域：
    /// 1) 同一份 DIP 布局在 100%/125%/150%/200% 像素密度下渲染，像素尺寸按比例变化而布局尺寸不变；
    /// 2) 命中测试始终按 DIP 坐标工作，同一坐标在各缩放档位命中同一个控件；
    /// 3) 控制窗按钮的命中区域不小于 40 DIP（触屏可用性下限）。
    /// 无头平台没有渲染缩放开关，这里用渲染目标位图的 DPI 表达缩放，正是合成器在不同缩放下的做法。
    /// </summary>
    private static string CheckScalingAndHitTargets(
        MainWindow window,
        MainViewModel viewModel,
        IHeadlessInput input,
        string evidenceDirectory)
    {
        Control board = window.FindControl<Grid>("DisplayRoot")!;
        Border toolbar = window.FindControl<Border>("FloatingToolbar")!;
        List<Border> buttons =
        [
            .. toolbar.GetLogicalDescendants().OfType<Border>().Where(border => border.Child is StackPanel)
        ];
        // 命中区域：控制窗按钮在布局尺寸下不小于 40 DIP（触屏可用性的常用下限）。
        // 查看模式下有一部分按钮是收起的（Bounds 为 0），这里只看真实显示出来的按钮。
        List<Button> toolbarButtons =
        [
            .. toolbar.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.IsVisible && button.Bounds.Width > 0 && button.Bounds.Height > 0)
        ];
        if (toolbarButtons.Count < 3)
        {
            throw new InvalidOperationException($"查看模式下控制窗的可见按钮过少（{toolbarButtons.Count} 个）。");
        }

        double minTarget = toolbarButtons.Min(button => Math.Min(button.Bounds.Width, button.Bounds.Height));
        if (minTarget < 40)
        {
            throw new InvalidOperationException($"控制窗按钮的命中区域过小（最小 {minTarget:0.#} DIP）。");
        }

        // 取一个稳定的命中点：第一个控制窗按钮的中心。
        Button probe = toolbarButtons[0];
        Point probePoint = probe.TranslatePoint(new Point(probe.Bounds.Width / 2, probe.Bounds.Height / 2), window) ?? default;
        List<string> sizes = [];
        double? layoutWidth = null;
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            input.Idle();
            Size dipSize = board.Bounds.Size;
            if (layoutWidth is null) layoutWidth = dipSize.Width;
            else if (Math.Abs(layoutWidth.Value - dipSize.Width) > 0.5)
            {
                throw new InvalidOperationException($"缩放档位之间布局宽度发生了变化（{layoutWidth:0.#} → {dipSize.Width:0.#}）。");
            }

            // 以该像素密度渲染一帧：像素尺寸应当约等于 DIP 尺寸 × 缩放倍率。
            RenderTargetBitmap frame = new(
                new PixelSize(Math.Max(1, (int)Math.Ceiling(dipSize.Width * scale)), Math.Max(1, (int)Math.Ceiling(dipSize.Height * scale))),
                new Vector(96 * scale, 96 * scale));
            frame.Render(board);
            double expectedWidth = dipSize.Width * scale;
            if (Math.Abs(frame.PixelSize.Width - expectedWidth) > 2)
            {
                throw new InvalidOperationException(
                    $"缩放 {scale:0.##} 下的渲染像素宽度不符合预期（{frame.PixelSize.Width} vs {expectedWidth:0.#}）。");
            }

            // 证据截图：看板与设置页各存一份，便于人工比对缩放观感。
            string boardPath = Path.Combine(evidenceDirectory, $"board-{scale * 100:0}pct.png");
            frame.Save(boardPath);
            if (new FileInfo(boardPath).Length < 2048)
            {
                throw new InvalidOperationException($"缩放 {scale:0.##} 下的看板截图几乎是空白。");
            }

            // 命中测试按 DIP 坐标进行：同一坐标在各档位必须命中同一个按钮。
            IInputElement? hit = window.InputHitTest(probePoint);
            if (hit is not Visual hitVisual || !(ReferenceEquals(hitVisual, probe) || hitVisual.GetVisualAncestors().Contains(probe)))
            {
                throw new InvalidOperationException(
                    $"缩放 {scale:0.##} 下的命中测试没有命中控制窗按钮（hit={hit?.GetType().Name ?? "null"}）。");
            }

            sizes.Add($"{scale * 100:0}%:{frame.PixelSize.Width}x{frame.PixelSize.Height}");
        }

        // 设置页也留一张截图：它是唯一透出窗口材质、带实时预览的页面。
        window.ShowSettingsForVerification();
        window.ShowSettingsSectionForVerification("AppearanceTile");
        input.Idle();
        input.Idle();
        Control settings = window.FindControl<Grid>("SettingsRoot")!;
        Size settingsSize = settings.Bounds.Size;
        if (settingsSize.Width < 100 || settingsSize.Height < 100)
        {
            throw new InvalidOperationException($"设置页没有完成布局（{settingsSize.Width:0}x{settingsSize.Height:0}）。");
        }

        RenderTargetBitmap settingsFrame = new(
            new PixelSize(Math.Max(1, (int)settingsSize.Width), Math.Max(1, (int)settingsSize.Height)),
            new Vector(96, 96));
        settingsFrame.Render(settings);
        string settingsPath = Path.Combine(evidenceDirectory, "settings-tile.png");
        settingsFrame.Save(settingsPath);
        long settingsBytes = new FileInfo(settingsPath).Length;
        if (settingsBytes < 2048)
        {
            throw new InvalidOperationException(
                $"设置页截图几乎是空白（{settingsSize.Width:0}x{settingsSize.Height:0}，{settingsBytes} 字节）。");
        }

        window.ShowBoardForVerification();
        input.Idle();
        return $"scaling+hitTargets:minTarget={minTarget:0.#}dip frames={string.Join(",", sizes)} settingsShot=ok";
    }

    /// <summary>
    /// 富文本字体：给选区套用系统字体后，
    /// 1) 模型（RTF）里记录了该家族名；
    /// 2) 显示层对相应片段使用了该字体；
    /// 3) 本机缺失的字体在显示时退回随包字体（不出现缺字方框），但原始家族名仍保留；
    /// 4) 输入层的字体跟随光标所在片段（否则透明输入层的光标会与文字错位）。
    /// </summary>
    private static string CheckRichTextFonts(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        SubjectTileControl tile = window.FindControl<Canvas>("BoardCanvas")!.Children
            .OfType<SubjectTileControl>()
            .First(tileControl => tileControl.SubjectForVerification.Entries.Count > 0);
        HomeworkEntry entry = tile.SubjectForVerification.Entries[0];
        RichTextEditor editor = tile.FirstEditorForVerification
            ?? throw new InvalidOperationException("磁贴里没有可编辑的作业条目。");

        // 挑一个真实安装的系统字体（没有系统字体时用随包字体，同样能验证链路）。
        string installed = FontService.SelectableFamilies()
            .FirstOrDefault(name => !name.Equals(FontService.FamilyName, StringComparison.OrdinalIgnoreCase))
            ?? FontService.FamilyName;

        // 第 1、2 条：套用字体后 RTF 与显示层都要带上它。
        int length = Math.Min(2, Math.Max(1, editor.Document.Text.Length));
        editor.ApplyFormat(0, length, format => format with { FontFamily = installed });
        input.Idle();
        // 非 ASCII 家族名在 RTF 里会写成 \uN? 转义，因此按“重新解析后仍然是这个名字”来断言。
        string roundTripped = RtfCodec.Parse(entry.RtfContent, entry.Content).GetFormatAt(0).FontFamily ?? string.Empty;
        if (!roundTripped.Equals(installed, StringComparison.CurrentCultureIgnoreCase))
        {
            throw new InvalidOperationException($"套用字体后 RTF 往返丢失了家族名（期望 {installed}，实际 {roundTripped}）。");
        }

        RichTextPresenter presenter = window.GetVisualDescendants()
            .OfType<RichTextPresenter>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Document, editor.Document))
            ?? throw new InvalidOperationException("找不到与编辑器对应的显示层。");
        string expectedSource = installed.Equals(FontService.FamilyName, StringComparison.OrdinalIgnoreCase)
            ? FontService.DefaultFamily.Name
            : installed;
        IReadOnlyList<Run> runs = presenter.Inlines?.OfType<Run>().ToList() ?? [];
        Run first = runs.FirstOrDefault() ?? throw new InvalidOperationException("显示层没有生成任何文字片段。");
        string? actualSource = first.FontFamily?.Name;
        if (!string.Equals(actualSource, expectedSource, StringComparison.CurrentCultureIgnoreCase))
        {
            throw new InvalidOperationException($"显示层没有使用选中的字体（期望 {expectedSource}，实际 {actualSource ?? "默认"}）。");
        }

        // 第 3 条：本机缺失的字体显示时退回随包字体，但原始家族名仍写进 RTF。
        const string missingFamily = "NotInstalled Classroom Font";
        editor.ApplyFormat(0, length, format => format with { FontFamily = missingFamily });
        input.Idle();
        runs = presenter.Inlines?.OfType<Run>().ToList() ?? [];
        string missingSource = runs.FirstOrDefault()?.FontFamily?.Name ?? FontService.FamilyName;
        if (!missingSource.Contains("HarmonyOS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"缺失字体的片段没有退回随包字体（实际 {missingSource}）。");
        }

        string missingRoundTrip = RtfCodec.Parse(entry.RtfContent, entry.Content).GetFormatAt(0).FontFamily ?? string.Empty;
        if (!missingRoundTrip.Equals(missingFamily, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"缺失字体的原始家族名没有保留在 RTF 里（实际 {missingRoundTrip}）。");
        }

        // 第 4 条：输入层跟随光标所在片段。
        editor.Input.CaretIndex = 0;
        input.Idle();
        string inputSource = editor.Input.FontFamily?.Name ?? string.Empty;
        if (!inputSource.Contains("HarmonyOS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"输入层没有跟随光标片段退回随包字体（实际 {inputSource}）。");
        }

        // 还原成默认字体，避免影响后续检查。
        editor.ApplyFormat(0, length, format => format with { FontFamily = null });
        input.Idle();
        return $"richTextFonts:installed={installed}+missingFallback+inputFollows+rtfRoundTrip";
    }

    /// <summary>
    /// 全屏退出提示：全屏查看模式下拖动看板会短暂展开「退出全屏」并高亮，
    /// 提示会自动收起；编辑模式与窗口模式下都保持安静。
    /// </summary>
    /// <summary>
    /// Wallpaper Engine 导入窗口：用假的安装目录驱动真实入口，
    /// 验证列表按旧版显示预览图、标题与类型标签，并给没有预览图的项目占位。
    /// 这条路径平时只在装有 Wallpaper Engine 的 Windows 上出现，这里用假数据在任意平台验证。
    /// </summary>
    /// <summary>
    /// 手写链路：进入编辑并开启画笔后，用真实指针在磁贴上写两笔，
    /// 断言笔迹进入模型（颜色与粗细沿用画笔设置）、越界部分不落笔、
    /// 橡皮擦能按命中半径删掉整条笔迹、撤销与清空按预期生效。
    /// </summary>
    private static string CheckInkDrawing(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        SubjectTileControl probeTile = window.FindControl<Canvas>("BoardCanvas")!.Children.OfType<SubjectTileControl>().First();
        // 看板必须处于可命中状态：没有当前项目时空状态面板会盖住看板，这里先挡住这种状态。
        Point probePoint = probeTile.TranslatePoint(new Point(40, 60), window) ?? default;
        bool boardHitTestable = probeTile.InputHitTest(new Point(40, 60)) is not null &&
                                IsInside(probeTile, window.InputHitTest(probePoint));
        if (!boardHitTestable) throw new InvalidOperationException("看板上的磁贴此刻不可命中，后续手写无法进行。");
        window.EnterEditingForVerification();
        input.Idle();
        SubjectTileControl tile = window.FindControl<Canvas>("BoardCanvas")!.Children
            .OfType<SubjectTileControl>()
            .First();
        SubjectBoard subject = tile.SubjectForVerification;
        int before = subject.InkStrokes.Count;
        if (before != 0) throw new InvalidOperationException($"示例科目一开始就带有 {before} 条笔迹。");

        // 开启画笔：画笔栏出现，磁贴的笔迹层开始接收指针。
        ToggleButton pen = window.FindControl<ToggleButton>("GlobalPenButton")!;
        pen.IsChecked = true;
        input.Idle();
        (BoardColor color, double thickness, bool eraser) = window.InkSettingsForVerification;
        if (eraser) throw new InvalidOperationException("画笔模式一开始处于橡皮擦状态。");

        // 第一笔：磁贴内部横向划一条。
        Point origin = tile.TranslatePoint(new Point(0, 0), window) ?? default;
        Point start = new(origin.X + 40, origin.Y + 60);
        input.Drag(start, start + new Point(120, 0));
        input.Idle();
        if (subject.InkStrokes.Count != 1)
        {
            throw new InvalidOperationException(
                $"第一笔没有落进模型（{subject.InkStrokes.Count} 条；pen={pen.IsChecked} " +
                $"layer={tile.InkLayerForVerification.IsVisible}/{tile.InkLayerForVerification.IsHitTestVisible} " +
                $"point={start:0.#} hit={DescribeChain(window.InputHitTest(start))}）。");
        }
        InkStrokeData stroke = subject.InkStrokes[0];
        if (!stroke.Color.Equals(color) || Math.Abs(stroke.Thickness - thickness) > 0.01)
        {
            throw new InvalidOperationException("笔迹没有沿用画笔栏设置的颜色与粗细。");
        }

        if (stroke.Points.Count < 2) throw new InvalidOperationException($"一笔只有 {stroke.Points.Count} 个采样点。");
        // 越界不落笔：所有采样点都必须落在磁贴范围内（允许 1 像素的边界误差）。
        bool insideTile = stroke.Points.All(point =>
            point.X >= -1 && point.Y >= -1 && point.X <= tile.Width + 1 && point.Y <= tile.Height + 1);
        if (!insideTile) throw new InvalidOperationException("笔迹出现了越出磁贴的采样点。");

        // 第二笔：从磁贴内划出边界，越界后当前笔段结束，笔迹仍然只落在磁贴内。
        input.Drag(new Point(origin.X + 40, origin.Y + 90), new Point(origin.X + tile.Width + 200, origin.Y + 90));
        input.Idle();
        if (subject.InkStrokes.Count != 2) throw new InvalidOperationException("第二笔没有落进模型。");

        // 撤销：只移除最后一笔。
        window.UndoInkForVerification();
        input.Idle();
        if (subject.InkStrokes.Count != 1) throw new InvalidOperationException("撤销没有移除最后一笔。");

        // 橡皮擦：在剩下那笔上扫一下，整条笔迹被删除。
        window.SelectInkToolForVerification(eraser: true);
        input.Idle();
        input.Drag(start, start + new Point(60, 0));
        input.Idle();
        if (subject.InkStrokes.Count != 0) throw new InvalidOperationException("橡皮擦没有删除命中的笔迹。");

        // 清空：再写一笔后整体清空。
        window.SelectInkToolForVerification(eraser: false);
        input.Drag(start, start + new Point(80, 20));
        input.Idle();
        int redrawn = subject.InkStrokes.Count;
        window.ClearInkForVerification();
        input.Idle();
        if (subject.InkStrokes.Count != 0) throw new InvalidOperationException("清空笔迹没有生效。");

        pen.IsChecked = false;
        window.FinishEditingForVerification();
        input.Idle();

        // 回归守卫：从设置页回到看板后磁贴必须仍然可命中（空状态面板曾经在这条路径上盖住看板）。
        window.ShowSettingsForVerification();
        input.Idle();
        window.ShowBoardForVerification();
        input.Idle();
        SubjectTileControl afterReturn = window.FindControl<Canvas>("BoardCanvas")!.Children.OfType<SubjectTileControl>().First();
        Point afterPoint = afterReturn.TranslatePoint(new Point(40, 60), window) ?? default;
        bool stillHitTestable = IsInside(afterReturn, window.InputHitTest(afterPoint));
        if (!stillHitTestable) throw new InvalidOperationException("从设置页回到看板后磁贴不可命中。");

        return $"ink:draw+inBounds+undo+erase+clear(redrawn={redrawn})+boardHitAfterSettings";
    }

    /// <summary>命中到的元素是否位于指定控件内部（含控件自身）。</summary>
    private static bool IsInside(Visual owner, IInputElement? hit) =>
        hit is Visual visual && (ReferenceEquals(visual, owner) || visual.GetVisualAncestors().Contains(owner));

    /// <summary>
    /// 自由布局的组件交互：拖动时钟组件的移动层、再拖右下角缩放手柄，
    /// 断言设置里的位置与尺寸确实跟着变（这两个把手同样是 Thumb，曾因缺少模板而失效）。
    /// </summary>
    private static string CheckFreeLayoutWidgetInteractions(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        viewModel.Settings.LayoutMode = "Free";
        window.SettingChangedForVerification();
        input.Idle();
        window.EnterEditingForVerification();
        input.Idle();
        if (!viewModel.Settings.Widgets.TryGetValue("Clock", out RegionPlacement? clock))
        {
            throw new InvalidOperationException("自由布局里没有时钟组件的布局记录。");
        }

        Canvas handles = window.FindControl<Canvas>("FreeLayoutHandles")!;
        Thumb? move = handles.GetLogicalDescendants().OfType<Thumb>()
            .FirstOrDefault(thumb => (ToolTip.GetTip(thumb) as string)?.Contains("时钟", StringComparison.Ordinal) == true &&
                                     (ToolTip.GetTip(thumb) as string)?.Contains("拖动", StringComparison.Ordinal) == true);
        Thumb? resize = handles.GetLogicalDescendants().OfType<Thumb>()
            .FirstOrDefault(thumb => (ToolTip.GetTip(thumb) as string)?.Contains("缩放时钟", StringComparison.Ordinal) == true);
        if (move is null || resize is null) throw new InvalidOperationException("自由布局里没有时钟组件的拖动/缩放入口。");

        double startX = clock.X, startY = clock.Y, startWidth = clock.Width;
        Point movePoint = move.TranslatePoint(new Point(Math.Max(4, move.Bounds.Width / 2), Math.Max(4, move.Bounds.Height / 2)), window) ?? default;
        input.Drag(movePoint, movePoint + new Point(0, -36));
        input.Idle();
        bool moved = Math.Abs(clock.Y - startY) > 1 || Math.Abs(clock.X - startX) > 1;
        if (!moved) throw new InvalidOperationException($"拖动时钟组件后布局记录没有变化（{startX},{startY}）。");

        Point resizePoint = resize.TranslatePoint(new Point(Math.Max(4, resize.Bounds.Width / 2), Math.Max(4, resize.Bounds.Height / 2)), window) ?? default;
        input.Drag(resizePoint, resizePoint + new Point(48, 0));
        input.Idle();
        bool resized = clock.Width > startWidth + 1 || clock.Height > 0 && Math.Abs(clock.Width - startWidth) > 1;
        if (!resized) throw new InvalidOperationException($"拖动缩放手柄后组件尺寸没有变化（{startWidth} → {clock.Width}）。");

        window.FinishEditingForVerification();
        viewModel.Settings.LayoutMode = "Split";
        window.SettingChangedForVerification();
        input.Idle();
        return $"freeWidgets:move+resize({startWidth:0}→{clock.Width:0})";
    }

    /// <summary>
    /// 自动填充的键盘导航：在标题输入框里输入全拼后，
    /// 候选浮层要出现，上下键切换选中，Tab 与回车采纳（采纳后标题与主题色一起更新），Esc 只收起浮层不改文字。
    /// </summary>
    /// <summary>
    /// 项目与文件菜单：新建（保留布局）、重命名、切换、删除四条链路，
    /// 每一步都通过真实的对话框按钮完成，并核对视图模型里的项目集合与当前项目。
    /// 导入/保存依赖系统文件选择器，无头环境没有选择器，因此由核心层的 ProjectLogic 覆盖。
    /// </summary>
    private static string CheckProjectOperations(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        int startCount = viewModel.Projects.Count;
        if (viewModel.CurrentProject is null) throw new InvalidOperationException("自检开始时没有当前项目。");
        SubjectBoard before = viewModel.Subjects.First();
        string previousName = viewModel.CurrentProject.Name;

        // 新建（重置作业）：项目数 +1，新项目成为当前项目并保留科目布局、清空作业内容。
        window.NewProjectForVerification();
        input.Idle();
        if (!window.DialogOverlayForVerification.IsVisible) throw new InvalidOperationException("新建项目没有弹出选择对话框。");
        ClickDialogButton(window, "DialogPrimaryButton");
        input.Idle();
        if (viewModel.Projects.Count != startCount + 1) throw new InvalidOperationException("新建项目后项目数量没有增加。");
        if (viewModel.CurrentProject?.Name == previousName) throw new InvalidOperationException("新建后当前项目没有切换。");
        bool layoutKept = viewModel.Subjects.Count == 2 && viewModel.Subjects.All(subject => subject.Entries.Count == 0);
        if (!layoutKept) throw new InvalidOperationException("「重置作业」没有保留科目与布局并清空内容。");
        _ = before;

        // 重命名：对话框里改文本后点保存。
        window.RenameProjectForVerification();
        input.Idle();
        if (!window.DialogOverlayForVerification.IsVisible) throw new InvalidOperationException("重命名没有弹出输入对话框。");
        TextBox? renameInput = window.DialogOverlayForVerification.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault();
        if (renameInput is null) throw new InvalidOperationException("重命名对话框里没有输入框。");
        renameInput.Text = "重命名后的项目";
        ClickDialogButton(window, "DialogPrimaryButton");
        input.Idle();
        if (viewModel.CurrentProject?.Name != "重命名后的项目")
        {
            throw new InvalidOperationException($"重命名没有生效（{viewModel.CurrentProject?.Name}）。");
        }

        // 切换：切回上一个项目；两个项目的主题/色系可能不同，会先弹出「应用项目外观」。
        ProjectDocument target = viewModel.Projects.First(project => project.Name == previousName);
        window.SwitchProjectForVerification(target);
        input.Idle();
        if (window.DialogOverlayForVerification.IsVisible) ClickDialogButton(window, "DialogCloseButton");
        input.Idle();
        if (viewModel.CurrentProject?.Id != target.Id) throw new InvalidOperationException("切换项目没有生效。");

        // 删除：确认后当前项目被移除，并自动切到剩余项目。
        window.DeleteProjectForVerification();
        input.Idle();
        if (!window.DialogOverlayForVerification.IsVisible) throw new InvalidOperationException("删除项目没有弹出确认对话框。");
        ClickDialogButton(window, "DialogPrimaryButton");
        input.Idle();
        if (viewModel.Projects.Count != startCount) throw new InvalidOperationException("删除后项目数量没有回到起始值。");
        if (viewModel.Projects.Any(project => project.Name == previousName)) throw new InvalidOperationException("被删除的项目仍在列表里。");
        if (viewModel.CurrentProject is null) throw new InvalidOperationException("删除当前项目后没有切到剩余项目。");
        return $"projects:new(keepLayout)+rename+switch+delete(count={viewModel.Projects.Count})";
    }

    /// <summary>点击轻量对话框里的指定按钮（按名称取控件，与用户点击等效）。</summary>
    private static void ClickDialogButton(MainWindow window, string name)
    {
        Button button = window.DialogOverlayForVerification.GetLogicalDescendants().OfType<Button>()
            .FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new InvalidOperationException($"对话框里找不到按钮 {name}。");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>
    /// 自动填充的键盘导航：在标题输入框里输入全拼后，
    private static string CheckAutofillKeyboard(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        AutofillService? autofill = viewModel.Autofill ?? throw new InvalidOperationException("项目数据里没有自动填充服务。");
        autofill.Subject.Enabled = true;
        autofill.EnsureBuiltIns();
        window.EnterEditingForVerification();
        input.Idle();

        SubjectTileControl tile = window.FindControl<Canvas>("BoardCanvas")!.Children.OfType<SubjectTileControl>().First();
        SubjectBoard subject = tile.SubjectForVerification;
        TextBox editor = tile.TitleEditorForVerification;
        string originalName = subject.Name;
        editor.Text = string.Empty;
        editor.Focus();
        input.Idle();

        AutofillPopup popup = window.AutofillPopupForVerification;
        input.TypeText("yu");
        input.Idle();
        if (!popup.IsOpen) throw new InvalidOperationException("输入全拼后候选浮层没有打开。");
        if (popup.SelectedItem is not SubjectSuggestion first)
        {
            throw new InvalidOperationException($"候选浮层里没有学科候选项（{popup.SelectedItem?.GetType().Name ?? "null"}）。");
        }

        // 上下键切换：候选多于一条时选中项必须变化。
        object? before = popup.SelectedItem;
        input.PressKey(Key.Down);
        input.Idle();
        object? after = popup.SelectedItem;
        bool movedSelection = popup.SelectedItem is not null;
        if (popup.ItemCountForVerification > 1 && ReferenceEquals(before, after))
        {
            throw new InvalidOperationException("候选多于一条时按下键没有切换选中项。");
        }

        // Esc：只收起浮层，不改动文字。
        input.PressKey(Key.Escape);
        input.Idle();
        bool closedByEscape = !popup.IsOpen && editor.Text == "yu";
        if (!closedByEscape) throw new InvalidOperationException("Esc 没有按预期收起候选浮层。");

        // 回车采纳：标题与主题色一起更新。
        input.TypeText("wen");
        input.Idle();
        if (!popup.IsOpen) throw new InvalidOperationException("继续输入后候选浮层没有重新打开。");
        SubjectSuggestion accepted = popup.SelectedItem as SubjectSuggestion
            ?? throw new InvalidOperationException("候选浮层里没有可采纳的学科候选。");
        input.PressKey(Key.Enter);
        input.Idle();
        bool acceptedByEnter = subject.Name == accepted.Name && !popup.IsOpen;
        bool accentApplied = subject.IsAccentExplicit;
        if (!acceptedByEnter) throw new InvalidOperationException($"回车没有采纳候选（{subject.Name} ≠ {accepted.Name}）。");
        if (!accentApplied) throw new InvalidOperationException("采纳候选后没有套用学科主题色。");

        // Tab 采纳：再来一次，换一个候选。
        editor.Text = string.Empty;
        editor.Focus();
        input.Idle();
        input.TypeText("shu");
        input.Idle();
        if (!popup.IsOpen) throw new InvalidOperationException("输入第二个学科的拼音后候选浮层没有打开。");
        SubjectSuggestion second = popup.SelectedItem as SubjectSuggestion
            ?? throw new InvalidOperationException("候选浮层里没有可采纳的学科候选。");
        input.PressKey(Key.Tab);
        input.Idle();
        bool acceptedByTab = subject.Name == second.Name && !popup.IsOpen;
        if (!acceptedByTab) throw new InvalidOperationException($"Tab 没有采纳候选（{subject.Name} ≠ {second.Name}）。");

        // 还原：标题与主题色恢复原值，关闭补全。
        subject.Name = originalName;
        autofill.Subject.Enabled = false;
        window.FinishEditingForVerification();
        input.Idle();
        return $"autofillKeys:open+down({movedSelection})+esc+enter({accepted.Name})+tab({second.Name})";
    }

    /// <summary>
    /// 图片附件交互：点选附件后拖右下角手柄缩放、拖动画面移动、点工具栏旋转，
    /// 断言附件的框架宽度、位置与旋转角度都写回了模型（手柄同样是 Thumb）。
    /// </summary>
    private static string CheckAttachmentInteractions(
        MainWindow window,
        MainViewModel viewModel,
        IHeadlessInput input,
        string dataRoot)
    {
        string imagePath = Path.Combine(dataRoot, "attachment-sample.png");
        WriteTinyPng(imagePath, 64, 48);

        SubjectTileControl tile = window.FindControl<Canvas>("BoardCanvas")!.Children.OfType<SubjectTileControl>().First();
        HomeworkEntry entry = tile.SubjectForVerification.Entries.First();
        entry.Attachments.Clear();
        // 故意让取景框比例（1:1）与图片比例（4:3）不同，裁切模式下才有多余画面可以移动。
        entry.Attachments.Add(new AttachmentItem
        {
            Name = "sample.png",
            Kind = "图片",
            Path = imagePath,
            AspectRatio = 1
        });
        entry.NotifyAttachmentsChanged();
        tile.RebuildEntries();
        input.Idle();
        window.EnterEditingForVerification();
        input.Idle();

        AttachmentImageControl image = tile.GetVisualDescendants().OfType<AttachmentImageControl>().FirstOrDefault()
            ?? throw new InvalidOperationException("作业里没有生成图片附件控件。");
        AttachmentItem attachment = entry.Attachments[0];
        double startWidth = attachment.FrameWidth;

        // 点选附件（按下即抬起，位置不变）后手柄与工具条出现。
        Point center = image.TranslatePoint(new Point(image.Width / 2, 20), window) ?? default;
        input.Press(center);
        input.Release(center);
        input.Idle();
        // 附件可能比磁贴的作业区更高，底部手柄会被滚动区裁掉；这里挑一个当前真的能点到的把手
        // （优先靠右下，尽量走到角上的缩放分支）。
        Thumb? handle = image.GetVisualDescendants().OfType<Thumb>()
            .Where(thumb => thumb.IsVisible && thumb.Bounds.Width > 0)
            .Select(thumb => (Thumb: thumb, Center: thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window) ?? default))
            .Where(pair => IsInside(image, window.InputHitTest(pair.Center)))
            .OrderByDescending(pair => pair.Center.X + pair.Center.Y)
            .Select(pair => pair.Thumb)
            .FirstOrDefault();
        if (handle is null)
        {
            List<string> handleStates = [.. image.GetVisualDescendants().OfType<Thumb>()
                .Select(thumb => $"{thumb.Bounds.Width:0}x{thumb.Bounds.Height:0}/{thumb.IsVisible}")];
            throw new InvalidOperationException(
                $"选中附件后没有出现缩放手柄（image={image.Bounds} at={image.TranslatePoint(default, window)} " +
                $"click={center:0.#} hit={DescribeChain(window.InputHitTest(center))} " +
                $"state={image.SelectionStateForVerification} editing={window.IsEditingForVerification} " +
                $"handles=[{string.Join(",", handleStates)}]）。");
        }

        Point handlePoint = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window) ?? default;
        input.Drag(handlePoint, handlePoint + new Point(60, 0));
        input.Idle();
        bool resized = attachment.FrameWidth > startWidth + 1;
        if (!resized)
        {
            throw new InvalidOperationException(
                $"拖动附件手柄后框架宽度没有变化（{startWidth} → {attachment.FrameWidth}；" +
                $"handle={handle.Bounds} at={handle.TranslatePoint(default, window)} " +
                $"point={handlePoint:0.#} hit={DescribeChain(window.InputHitTest(handlePoint))}）。");
        }

        // 拖动画面本身：位置偏移写回模型。
        double startX = attachment.PositionX, startY = attachment.PositionY;
        Point dragFrom = image.TranslatePoint(new Point(image.Width / 2, 20), window) ?? default;
        input.Drag(dragFrom, dragFrom + new Point(28, 16));
        input.Idle();
        bool moved = Math.Abs(attachment.PositionX - startX) > 0.5 || Math.Abs(attachment.PositionY - startY) > 0.5;
        if (!moved) throw new InvalidOperationException("拖动附件后位置偏移没有写回模型。");

        // 工具栏旋转：顺时针 90°。
        Button? rotate = image.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => (ToolTip.GetTip(button) as string) == "顺时针旋转");
        if (rotate is null) throw new InvalidOperationException("附件工具条里没有顺时针旋转按钮。");
        rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        input.Idle();
        bool rotated = Math.Abs(attachment.Rotation - 90) < 0.01;
        if (!rotated) throw new InvalidOperationException($"旋转后角度不是 90°，实际 {attachment.Rotation}。");

        // 裁切：进入裁切模式后拖动画面改的是取景偏移，图片框位置不动；再点一次退出裁切。
        Button? cropToggle = image.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => (ToolTip.GetTip(button) as string) == "裁切");
        if (cropToggle is null) throw new InvalidOperationException("附件工具条里没有裁切按钮。");
        cropToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        input.Idle();
        if (!image.IsCroppingForVerification) throw new InvalidOperationException("点击裁切后没有进入裁切模式。");
        double offsetX = attachment.OffsetX, offsetY = attachment.OffsetY;
        double positionX = attachment.PositionX, positionY = attachment.PositionY;
        Point cropFrom = image.TranslatePoint(new Point(image.Width / 2, 20), window) ?? default;
        input.Drag(cropFrom, cropFrom + new Point(24, 12));
        input.Idle();
        bool cropMoved = Math.Abs(attachment.OffsetX - offsetX) > 0.5 || Math.Abs(attachment.OffsetY - offsetY) > 0.5;
        bool positionUnchanged = Math.Abs(attachment.PositionX - positionX) < 0.5 && Math.Abs(attachment.PositionY - positionY) < 0.5;
        cropToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        input.Idle();
        bool cropExited = !image.IsCroppingForVerification;
        if (!cropMoved)
        {
            throw new InvalidOperationException(
                $"裁切模式下拖动画面没有改变取景偏移（offset={offsetX:0.#},{offsetY:0.#} → {attachment.OffsetX:0.#},{attachment.OffsetY:0.#}）。");
        }
        if (!positionUnchanged) throw new InvalidOperationException("裁切模式下拖动画面不应移动图片框。");
        if (!cropExited) throw new InvalidOperationException("再点裁切没有退出裁切模式。");

        // 清理：移除附件并恢复原状。
        entry.Attachments.Clear();
        entry.NotifyAttachmentsChanged();
        tile.RebuildEntries();
        window.FinishEditingForVerification();
        input.Idle();
        return $"attachment:select+resize({startWidth:0}→{attachment.FrameWidth:0})+move+rotate(90°)+crop";
    }

    /// <summary>
    /// 看板交互：编辑模式下用真实指针拖动磁贴、拖右边缘缩放、拖动分隔条，
    /// 断言模型坐标与尺寸跟着变、开启网格吸附时位置落在网格线上、分隔条拖动会改分屏比例并能在拖到端点时切换单区。
    /// </summary>
    private static string CheckBoardInteractions(MainWindow window, MainViewModel viewModel, IHeadlessInput input)
    {
        viewModel.Settings.LayoutMode = "Split";
        viewModel.Settings.GridSnappingEnabled = true;
        viewModel.Settings.GridSize = 48;
        viewModel.Settings.SplitRatio = 0.4;
        window.SettingChangedForVerification();
        input.Idle();
        window.EnterEditingForVerification();
        input.Idle();

        SubjectTileControl Tile() => window.FindControl<Canvas>("BoardCanvas")!.Children.OfType<SubjectTileControl>().First();
        SubjectTileControl tile = Tile();
        SubjectBoard subject = tile.SubjectForVerification;
        double startX = subject.X, startY = subject.Y, startWidth = subject.TileWidth;

        // 1. 拖动：按住磁贴顶部拖动条往右下走。
        Point origin = tile.TranslatePoint(default, window) ?? default;
        input.Drag(
            new Point(origin.X + tile.Width / 2, origin.Y + 11),
            new Point(origin.X + tile.Width / 2 + 130, origin.Y + 11 + 70));
        input.Idle();
        bool moved = Math.Abs(subject.X - startX) > 1 || Math.Abs(subject.Y - startY) > 1;
        bool snapped = Math.Abs(subject.X % 48) < 0.01 && Math.Abs(subject.Y % 48) < 0.01;
        bool viewSynced = Math.Abs(Canvas.GetLeft(tile) - subject.X) < 0.01 && Math.Abs(Canvas.GetTop(tile) - subject.Y) < 0.01;
        if (!moved)
        {
            Point pressPoint = new(origin.X + tile.Width / 2, origin.Y + 11);
            // 失败时把命中链与把手状态一并带出来：把手没模板时正是「看得见、点不到」。
            throw new InvalidOperationException(
                $"拖动磁贴后模型坐标没有变化（tile={tile.Bounds} " +
                $"hit={DescribeChain(window.InputHitTest(pressPoint))} " +
                $"thumb={tile.HeaderThumbForVerification.Bounds}/{tile.HeaderThumbForVerification.IsVisible}/" +
                $"{tile.HeaderThumbForVerification.IsHitTestVisible}）。");
        }
        if (!snapped) throw new InvalidOperationException($"开启网格吸附后磁贴没有落在网格线上（{subject.X},{subject.Y}）。");
        if (!viewSynced) throw new InvalidOperationException("拖动后控件位置与模型坐标不一致。");

        // 2. 缩放：拖右边缘把手横向拉宽。
        tile = Tile();
        origin = tile.TranslatePoint(default, window) ?? default;
        input.Drag(
            new Point(origin.X + tile.Width - 3, origin.Y + tile.Height / 2),
            new Point(origin.X + tile.Width - 3 + 100, origin.Y + tile.Height / 2));
        input.Idle();
        bool resized = subject.TileWidth > startWidth + 1;
        if (!resized) throw new InvalidOperationException($"拖动右边缘后磁贴宽度没有变化（{startWidth} → {subject.TileWidth}）。");
        if (Math.Abs(subject.TileWidth % 48) > 0.01) throw new InvalidOperationException("缩放后的宽度没有吸附到网格。");

        // 3. 分隔条：拖动改比例，拖到端点切到仅时钟，再拖回来恢复分屏。
        double ratio = viewModel.Settings.SplitRatio;
        Canvas handles = window.FindControl<Canvas>("FreeLayoutHandles")!;
        Thumb? splitter = handles.Children.OfType<Thumb>().FirstOrDefault();
        if (splitter is null) throw new InvalidOperationException("分屏模式下没有分隔条。");
        Point splitterPoint = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window) ?? default;
        input.Drag(splitterPoint, splitterPoint + new Point(-60, 0));
        input.Idle();
        bool ratioChanged = Math.Abs(viewModel.Settings.SplitRatio - ratio) > 0.005;
        if (!ratioChanged) throw new InvalidOperationException($"拖动分隔条后分屏比例没有变化（{ratio}）。");

        // 拖到最右端：按旧版规则切换成仅时钟。
        splitter = handles.Children.OfType<Thumb>().FirstOrDefault();
        if (splitter is null) throw new InvalidOperationException("拖动比例后分隔条消失了。");
        splitterPoint = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window) ?? default;
        input.Drag(splitterPoint, new Point(window.Bounds.Width - 2, splitterPoint.Y));
        input.Idle();
        bool switchedToClock = viewModel.Settings.LayoutMode == "Clock";
        if (!switchedToClock) throw new InvalidOperationException($"分隔条拖到端点后没有切换成仅时钟（{viewModel.Settings.LayoutMode}）。");

        // 从仅时钟拖回去：恢复分屏。
        splitter = handles.Children.OfType<Thumb>().FirstOrDefault();
        if (splitter is null) throw new InvalidOperationException("仅时钟模式下没有分隔条。");
        splitterPoint = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window) ?? default;
        input.Drag(splitterPoint, splitterPoint + new Point(-400, 0));
        input.Idle();
        bool restored = viewModel.Settings.LayoutMode == "Split";
        if (!restored) throw new InvalidOperationException($"从仅时钟拖回后没有恢复分屏（{viewModel.Settings.LayoutMode}）。");

        window.FinishEditingForVerification();
        input.Idle();
        viewModel.Settings.LayoutMode = "Split";
        viewModel.Settings.SplitRatio = 0.4;
        window.SettingChangedForVerification();
        input.Idle();
        return $"board:drag(snap)+resize(edge)+splitter(ratio→clock→split)";
    }

    /// <summary>
    /// Wallpaper Engine 导入窗口：用假的安装目录驱动真实入口，
    private static string CheckWallpaperEngineImportDialog(
        MainWindow window,
        string dataRoot,
        IHeadlessInput input,
        string evidenceDirectory)
    {
        string install = Path.Combine(dataRoot, "wallpaper_engine");
        string projectsRoot = Path.Combine(install, "projects", "myprojects");
        Directory.CreateDirectory(projectsRoot);
        File.WriteAllText(Path.Combine(install, "wallpaper64.exe"), string.Empty);
        // 一个带预览图的项目 + 一个没有预览图的项目。
        string withPreview = Path.Combine(projectsRoot, "demo-with-preview");
        string withoutPreview = Path.Combine(projectsRoot, "demo-without-preview");
        WriteFakeWallpaperProject(withPreview, "带预览的壁纸", withPreviewImage: true, evidenceDirectory);
        WriteFakeWallpaperProject(withoutPreview, "无预览的壁纸", withPreviewImage: false, evidenceDirectory);

        IPlatformFeatures originalFeatures = PlatformServices.Features;
        IWallpaperEnginePathSource originalPaths = PlatformServices.WallpaperEnginePaths;
        try
        {
            PlatformServices.Features = new FakePlatformFeatures();
            PlatformServices.WallpaperEnginePaths = new FakeWallpaperEnginePaths(install);
            window.ShowSettingsForVerification();
            window.ShowSettingsSectionForVerification("AppearanceBackground");
            input.Idle();
            Button? import = window.GetLogicalDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => (button.Content as string)?.Contains("Wallpaper Engine", StringComparison.Ordinal) == true);
            if (import is null) throw new InvalidOperationException("背景板页没有出现 Wallpaper Engine 导入入口。");

            import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // 对话框在等用户选择之前就把浮层显示出来了，因此这里排队跑完即可。
            input.Idle();
            input.Idle();
            if (!window.DialogOverlayForVerification.IsVisible) throw new InvalidOperationException("导入对话框没有打开。");
            List<string> texts = [.. window.DialogOverlayForVerification.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)];
            bool hasVideoLabel = texts.Any(text => text == "视频壁纸");
            bool hasTitles = texts.Any(text => text.Contains("带预览的壁纸", StringComparison.Ordinal)) &&
                             texts.Any(text => text.Contains("无预览的壁纸", StringComparison.Ordinal));
            int thumbnails = window.DialogOverlayForVerification.GetLogicalDescendants().OfType<Image>().Count();
            int placeholders = texts.Count(text => text == "无预览图");
            if (!hasVideoLabel) throw new InvalidOperationException("导入列表没有显示类型标签。");
            if (!hasTitles) throw new InvalidOperationException("导入列表没有显示项目标题。");
            if (thumbnails == 0) throw new InvalidOperationException("导入列表没有加载任何预览图。");
            if (placeholders == 0) throw new InvalidOperationException("没有预览图的项目没有显示占位文字。");

            // 关掉对话框：点击取消（关闭按钮）并把排队的任务跑完。
            Button close = window.DialogOverlayForVerification.GetLogicalDescendants().OfType<Button>()
                .Last(button => (button.Content as string) == "取消");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            input.Idle();
            if (window.DialogOverlayForVerification.IsVisible) throw new InvalidOperationException("导入对话框没有关闭。");
            return $"wallpaperEngineDialog:rows=2 thumbs={thumbnails} placeholders={placeholders} typeLabel=视频壁纸";
        }
        finally
        {
            PlatformServices.Features = originalFeatures;
            PlatformServices.WallpaperEnginePaths = originalPaths;
            window.ShowBoardForVerification();
            input.Idle();
        }
    }

    /// <summary>假 Wallpaper Engine 项目：project.json + 视频占位文件（+ 可选预览图）。</summary>
    private static void WriteFakeWallpaperProject(string folder, string title, bool withPreviewImage, string evidenceDirectory)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "clip.mp4"), string.Empty);
        Dictionary<string, string> json = new()
        {
            ["type"] = "video",
            ["file"] = "clip.mp4",
            ["title"] = title
        };
        if (withPreviewImage)
        {
            string preview = Path.Combine(folder, "preview.png");
            WriteTinyPng(preview);
            json["preview"] = "preview.png";
        }

        File.WriteAllText(Path.Combine(folder, "project.json"), JsonSerializer.Serialize(json));
        _ = evidenceDirectory;
    }

    /// <summary>写一张纯色 PNG，作为假项目的预览图或自检用的图片附件。</summary>
    private static void WriteTinyPng(string path, int width = 8, int height = 8)
    {
        Border source = new()
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Colors.OrangeRed)
        };
        RenderTargetBitmap bitmap = new(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(source);
        bitmap.Save(path);
    }

    /// <summary>假能力开关：让 Wallpaper Engine 与网页壁纸入口在无头环境里出现。</summary>
    private sealed class FakePlatformFeatures : IPlatformFeatures
    {
        public bool WallpaperEngine => true;

        public bool WebWallpaper => true;

        public bool SelfUpdate => false;
    }

    /// <summary>假安装位置：指向无头测试临时目录里的假 Wallpaper Engine。</summary>
    private sealed class FakeWallpaperEnginePaths(string install) : IWallpaperEnginePathSource
    {
        public string? GetSteamRoot() => null;

        public IReadOnlyList<string> GetInstallLocations() => [install];
    }

    /// <summary>
    /// 全屏退出提示：全屏查看模式下拖动看板会短暂展开「退出全屏」并高亮，
    /// 提示会自动收起；编辑模式与窗口模式下都保持安静。
    /// </summary>
    private static string CheckFullScreenHint(MainWindow window, IHeadlessInput input)
    {
        window.SetFullScreenForVerification(true);
        window.SetToolbarIconOnlyForVerification(true);
        input.Idle();
        Point start = new(window.Bounds.Width / 2 - 60, window.Bounds.Height / 2);
        input.Drag(start, start + new Point(40, 0));
        input.Idle();
        Button button = window.FindControl<Button>("FullScreenButton")!;
        bool captionShown = button.Content is StackPanel { Children.Count: 2 } stack &&
                            stack.Children[1] is TextBlock { IsVisible: true } caption &&
                            caption.Text == "退出全屏";
        bool highlighted = button.Background is ISolidColorBrush;
        if (!window.FullScreenHintVisibleForVerification) throw new InvalidOperationException("全屏下滑动看板没有显示退出提示。");
        if (!captionShown) throw new InvalidOperationException("无字模式下没有展开「退出全屏」文字。");
        if (!highlighted) throw new InvalidOperationException("退出提示没有高亮控制窗按钮。");

        // 编辑模式：拖动磁贴是正常编辑，不弹提示。
        window.EnterEditingForVerification();
        input.Drag(start, start + new Point(40, 0));
        input.Idle();
        bool silentWhileEditing = !window.FullScreenHintVisibleForVerification;
        window.FinishEditingForVerification();
        if (!silentWhileEditing) throw new InvalidOperationException("编辑模式下也弹出了退出提示。");

        // 窗口模式：可以直接点按钮退出全屏，不需要提示。
        window.SetFullScreenForVerification(false);
        input.Drag(start, start + new Point(40, 0));
        input.Idle();
        if (window.FullScreenHintVisibleForVerification) throw new InvalidOperationException("窗口模式下也弹出了退出提示。");
        // 定时器自动收起在真实窗口的自检里验证：无头环境不推进调度器定时器。
        return "fullScreenHint:shownByDrag+silent(editing,windowed)";
    }

    /// <summary>
    /// 按住指针时不自动隐藏控制窗：按下后即使超过自动隐藏时间也保持可见，
    /// 抬起后恢复自动隐藏。
    /// </summary>
    private static string CheckPressedPointerKeepsToolbar(
        MainWindow window,
        MainViewModel viewModel,
        IHeadlessInput input)
    {
        viewModel.Settings.ToolbarAutoHide = true;
        viewModel.Settings.ToolbarAutoHideSeconds = 1;
        viewModel.Settings.ToolbarHideAnimation = "Fade";
        Point point = new(window.Bounds.Width / 2, window.Bounds.Height - 40);
        input.Press(point);
        input.Idle();
        // 真实输入必须真的到达窗口：按下的指针数量由窗口的隧道事件维护。
        if (window.PressedPointerCountForVerification != 1)
        {
            throw new InvalidOperationException(
                $"按下指针后窗口没有记录到按下的指针（count={window.PressedPointerCountForVerification}）。");
        }

        // 空闲时间超过阈值：按住指针时不能隐藏。
        window.AdvanceIdleForVerification(viewModel.Settings.ToolbarAutoHideSeconds + 1);
        bool keptByPress = !window.ToolbarHiddenForVerification &&
                           window.FindControl<Border>("FloatingToolbar")!.Opacity == 1;
        input.Release(point);
        input.Idle();
        if (window.PressedPointerCountForVerification != 0)
        {
            throw new InvalidOperationException("抬起指针后窗口仍然记录着按下的指针。");
        }

        // 抬起后同样的空闲时间就能触发隐藏。
        window.AdvanceIdleForVerification(viewModel.Settings.ToolbarAutoHideSeconds + 1);
        bool hiddenAfterRelease = window.ToolbarHiddenForVerification;
        viewModel.Settings.ToolbarAutoHide = false;
        if (!keptByPress) throw new InvalidOperationException("按住指针时控制窗被自动隐藏了。");
        if (!hiddenAfterRelease) throw new InvalidOperationException("抬起指针后控制窗没有恢复自动隐藏判定。");
        return "pressedPointer:keptByPress+hiddenAfterRelease";
    }

    private static bool IsInsideTile(IInputElement element, SubjectTileControl tile) =>
        element is Visual visual && (ReferenceEquals(visual, tile) || visual.GetVisualAncestors().Contains(tile));

    /// <summary>命中链：从命中的元素往上列出类型与名称，便于定位遮挡或树结构问题。</summary>
    private static string DescribeChain(IInputElement? element) =>
        element is not Visual visual
            ? "null"
            : string.Join(" < ", new[] { visual }.Concat(visual.GetVisualAncestors())
                .Take(8)
                .Select(item => item is Control { Name: { Length: > 0 } name } ? $"{item.GetType().Name}#{name}" : item.GetType().Name));

    /// <summary>检查用的科目：内容与尺寸固定，便于断言布局结果。</summary>
    private static SubjectBoard CreateSubject(
        string name,
        double x,
        double y,
        double width,
        double height,
        string[] entries)
    {
        SubjectBoard subject = new()
        {
            Name = name,
            TileWidth = width,
            TileHeight = height,
            AccentHex = "#818CF8",
            IsAccentExplicit = true,
            X = x,
            Y = y
        };
        foreach (string entry in entries) subject.Entries.Add(new HomeworkEntry { Content = entry });
        return subject;
    }

    /// <summary>设置页分类标签；与设置页导航使用的是同一份清单。</summary>
    private static IEnumerable<string> SettingsSectionTags() => MainWindow.SettingsSectionTagsForVerification;
}
#endif
