// 仅在 EnableUiVerification=true 时编译；在隔离的构建目录运行，不访问用户正常运行目录的数据。
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pancake.Controls;
using Pancake.Models;
using Pancake.Services;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Pancake;

public sealed partial class MainWindow
{
    internal void ScheduleFullScreenHintVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS: " + message); }
                await NextLayoutAsync();
                await VerifyFullScreenHintAsync(output, Check);
                evidence.Add("FULLSCREEN_HINT_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("FULLSCREEN_HINT_VERIFICATION_FAILED\n" + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "fullscreen-result.txt"), evidence);
                Close();
            }
        };
    }

    /// <summary>遍历系统字体，找出经 RTF 保存往返后名字被改写、会被误判为缺失字体的家族。</summary>
    internal void ScheduleFontAuditVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                await NextLayoutAsync();
                ExportOverlay.Visibility = Visibility.Visible;
                RichEditBox probe = new() { FontFamily = FontService.DefaultFamily };
                ExportOverlay.Children.Add(probe);
                await NextLayoutAsync();
                List<string> broken = [];
                foreach (string family in FontService.AvailableFamilies)
                {
                    probe.Document.SetText(TextSetOptions.None, "字体审计 0123456789");
                    probe.Document.GetRange(0, 4).CharacterFormat.Name = family;
                    probe.Document.GetRange(0, int.MaxValue).GetText(TextGetOptions.FormatRtf, out string rtf);
                    probe.Document.SetText(TextSetOptions.FormatRtf, FontService.NormalizeRtf(rtf.TrimEnd('\0')));
                    string back = probe.Document.GetRange(0, 1).CharacterFormat.Name;
                    if (!FontService.IsInstalledFamily(back))
                        broken.Add($"{family} => {back}");
                }
                evidence.Add($"AvailableFamilies: {FontService.AvailableFamilies.Count}");
                evidence.Add($"Treated as missing after RTF round trip: {broken.Count}");
                evidence.AddRange(broken.Select(item => "REPLACED " + item));
                evidence.Add("FONT_AUDIT_OK");
            }
            catch (Exception ex) { evidence.Add("FONT_AUDIT_FAILED\n" + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "font-audit-result.txt"), evidence);
                Close();
            }
        };
    }

    internal void ScheduleUiVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS: " + message); }
                evidence.Add("Started " + DateTime.Now.ToString("O"));
                if (Environment.GetCommandLineArgs().Contains("--auto-layout-only"))
                {
                    await NextLayoutAsync();
                    await VerifyAutoLayoutExportAsync(output, Check);
                    evidence.Add("UI_VERIFICATION_OK");
                    return;
                }
                List<Exception> backdropExceptions = [];
                EventHandler<FirstChanceExceptionEventArgs> backdropExceptionHandler = (_, args) =>
                {
                    if (args.Exception is ArgumentException &&
                        args.Exception.StackTrace?.Contains(nameof(PersistentMicaBackdrop), StringComparison.Ordinal) == true)
                    {
                        backdropExceptions.Add(args.Exception);
                    }
                };
                AppDomain.CurrentDomain.FirstChanceException += backdropExceptionHandler;
                try
                {
                    var presenter = _appWindow?.Presenter as OverlappedPresenter
                        ?? throw new Exception("window does not use an overlapped presenter");
                    presenter.Minimize();
                    await Task.Delay(250);
                    presenter.Restore();
                    await Task.Delay(250);
                    await NextLayoutAsync();
                }
                finally
                {
                    AppDomain.CurrentDomain.FirstChanceException -= backdropExceptionHandler;
                }
                Check(backdropExceptions.Count == 0, "Mica configuration does not throw while window focus changes");
                ShowSettings();
                await NextLayoutAsync();
                var initiallySelectedSettingsItems = SettingsRoot.MenuItems.Concat(SettingsRoot.FooterMenuItems)
                    .OfType<NavigationViewItem>()
                    .SelectMany(item => item.MenuItems.OfType<NavigationViewItem>().DefaultIfEmpty(item))
                    .Where(item => item.IsSelected)
                    .ToList();
                Check(ReferenceEquals(SettingsRoot.SelectedItem, AppearanceNavItem) &&
                    initiallySelectedSettingsItems.Count == 1,
                    "opening settings has exactly one selected navigation item with its indicator");
                NavigationViewItem backgroundNavigationItem = AppearanceNavigationGroup.MenuItems
                    .OfType<NavigationViewItem>()
                    .Single(item => item.Tag?.ToString() == "AppearanceBackground");
                SettingsRoot.SelectedItem = backgroundNavigationItem;
                await NextLayoutAsync();
                Check(backgroundNavigationItem.IsSelected && !AppearanceNavItem.IsSelected &&
                    SettingsPageTitle.Text == "背景板",
                    "changing settings page clears the previous selected background");
                SettingsRoot.SelectedItem = AppearanceNavItem;
                await NextLayoutAsync();
                ShowBoard();
                await NextLayoutAsync();
                _settings.AutoUpdateEnabled = false;
                _library = new ProjectLibrary { Settings = _settings };
                ProjectDocument project = ProjectStore.Create(_library, false);
                project.Name = "9月6日"; project.CreatedAt = new DateTime(2026, 9, 6);
                ViewModel.ReplaceSubjects([]);
                SubjectBoard chinese = ViewModel.AddSubject("语文");
                chinese.X = 24; chinese.Y = 24; chinese.TileWidth = 420; chinese.TileHeight = 320;
                chinese.Entries.Add(new HomeworkEntry { Content = "背诵《赤壁赋》第二段\n完成课堂练习，整理作文素材。" });
                chinese.Entries.Add(new HomeworkEntry { Content = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"第 {i} 项：复习课堂知识，记录容易遗漏的细节。")) + "\n最后一行：完整导出验收标记。" });
                InkStrokeData stroke = new() { Color = Windows.UI.Color.FromArgb(255, 248, 113, 113), Thickness = 5 };
                stroke.Points.AddRange([new Point(42, 90), new Point(180, 90), new Point(180, 120), new Point(42, 120)]);
                chinese.InkStrokes.Add(stroke);
                SubjectBoard english = ViewModel.AddSubject("英语"); english.X = 24; english.Y = 360; english.TileWidth = 400; english.TileHeight = 220;
                english.Entries.Add(new HomeworkEntry { Content = "Read Unit 3.\nRemember these words.\nHarmonyOS Sans 1234567890" });
                string imagePath = Path.Combine(output, "attachment.png");
                Grid imageSource = new() { Width = 260, Height = 120, Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue) };
                imageSource.Children.Add(new TextBlock { Text = "图片附件\n旋转与裁切", FontSize = 28, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                ExportOverlay.Visibility = Visibility.Visible; ExportOverlay.Children.Add(imageSource);
                await NextLayoutAsync(); await SaveVisualAsync(imageSource, imagePath, 260, 120);
                ExportOverlay.Children.Clear(); ExportOverlay.Visibility = Visibility.Collapsed;
                english.Entries[0].Attachments.Add(new AttachmentItem { Name = "attachment.png", Kind = "图片", Path = _projectStore.CopyAttachment(project.Id, imagePath), FrameWidth = 260, AspectRatio = 260d / 120, Scale = 1.3, Rotation = 90, OffsetX = 10, PositionY = 8 });
                BuildTiles(); EnterEditing(); await NextLayoutAsync();
                Check(ProjectCommands.Visibility == Visibility.Visible && ProjectNameText.Text == project.Name, "project commands visible in editor");
                var editor = FindVisuals<RichEditBox>(BoardCanvas).Last();
                editor.IsReadOnly = false;
                editor.Document.Selection.SetRange(0, 2);
                editor.Document.Selection.CharacterFormat.Name = FontService.ResourceName;
                editor.Document.Selection.CharacterFormat.Bold = FormatEffect.On;
                editor.Document.Selection.CharacterFormat.ForegroundColor = Microsoft.UI.Colors.Red;
                editor.Document.GetRange(2, 4).CharacterFormat.Name = "Arial";
                editor.Document.GetRange(20, 25).CharacterFormat.Name = "Pancake Missing Typeface";
                var range = editor.Document.GetRange(0, int.MaxValue); range.EndPosition--;
                range.GetText(TextGetOptions.FormatRtf, out string rtf);
                File.WriteAllText(Path.Combine(output, "font-before.rtf"), rtf);
                editor.Document.SetText(TextSetOptions.FormatRtf, FontService.NormalizeRtf(rtf));
                evidence.Add("Before rebind fonts: " + editor.Document.GetRange(0, 1).CharacterFormat.Name + " / " + editor.Document.GetRange(2, 3).CharacterFormat.Name);
                FontService.RebindBundledFont(editor);
                Check(editor.Document.GetRange(0, 1).CharacterFormat.Name.Contains("HarmonyOS"), "bundled font survives RTF reload");
                evidence.Add("After rebind fonts: " + editor.Document.GetRange(0, 1).CharacterFormat.Name + " / " + editor.Document.GetRange(2, 3).CharacterFormat.Name);
                Check(editor.Document.GetRange(2, 3).CharacterFormat.Name == "Arial", "other explicit fonts survive reload");
                evidence.Add("Missing family reload name: " + editor.Document.GetRange(20, 21).CharacterFormat.Name);
                Check(editor.Document.GetRange(0, 1).CharacterFormat.Bold == FormatEffect.On && editor.Document.GetRange(0, 1).CharacterFormat.ForegroundColor.R == 255, "RTF bold and explicit color survive reload");
                editor.Document.Selection.SetRange(5, 5);
                string beforeFont = editor.Document.GetRange(2, 3).CharacterFormat.Name;
                editor.Document.Selection.CharacterFormat.Name = "Times New Roman";
                Check(editor.Document.GetRange(2, 3).CharacterFormat.Name == beforeFont, "collapsed font selection preserves preceding text");
                // 字体保存往返：工具栏选择字体后保存，重建的磁贴必须保留同一字体。
                // 格式工具栏由编辑器的聚焦事件创建，聚焦后需要等它真正挂到宿主上。
                AutoSuggestBox? fontPicker = null;
                for (int attempt = 0; attempt < 10 && fontPicker is null; attempt++)
                {
                    editor.Focus(FocusState.Programmatic);
                    editor.Document.Selection.SetRange(0, 2);
                    await NextLayoutAsync();
                    fontPicker = FindVisuals<AutoSuggestBox>(RichTextToolbarHost).FirstOrDefault();
                }
                Check(fontPicker is not null, "font picker is hosted next to the focused homework editor");
                fontPicker!.Focus(FocusState.Programmatic);
                await NextLayoutAsync();
                evidence.Add("font picker selection: " + editor.Document.Selection.StartPosition + ".." + editor.Document.Selection.EndPosition);
                ((Action<string>)fontPicker.Tag)("Arial");
                await NextLayoutAsync();
                evidence.Add("font after apply: " + editor.Document.GetRange(0, 2).CharacterFormat.Name);
                evidence.Add("captured rtf fonts: " + string.Join(" | ", english.Entries.Concat(chinese.Entries).Select(entry => RtfFonts(entry.RtfContent))));
                FinishEditing();
                await NextLayoutAsync();
                RichEditBox reloaded = FindVisuals<RichEditBox>(BoardCanvas).Last();
                evidence.Add("font after save reload: " + reloaded.Document.GetRange(0, 2).CharacterFormat.Name);
                Check(reloaded.Document.GetRange(0, 2).CharacterFormat.Name == "Arial", "chosen font survives save and tile rebuild");
                EnterEditing();
                await NextLayoutAsync();
                // 中文字体保存进 RTF 后会写成英文别名（微软雅黑 → Microsoft YaHei），别名不能被当成缺失字体。
                foreach (string localized in new[] { "微软雅黑", "宋体" })
                {
                    if (!FontService.AvailableFamilies.Contains(localized, StringComparer.CurrentCultureIgnoreCase))
                    {
                        evidence.Add("Skipped font alias check, not installed: " + localized);
                        continue;
                    }
                    RichEditBox aliasProbe = new() { FontFamily = FontService.DefaultFamily };
                    ExportOverlay.Visibility = Visibility.Visible;
                    ExportOverlay.Children.Add(aliasProbe);
                    await NextLayoutAsync();
                    aliasProbe.Document.SetText(TextSetOptions.None, "字体别名检查");
                    aliasProbe.Document.GetRange(0, 2).CharacterFormat.Name = localized;
                    aliasProbe.Document.GetRange(0, int.MaxValue).GetText(TextGetOptions.FormatRtf, out string aliasRtf);
                    aliasProbe.Document.SetText(TextSetOptions.FormatRtf, FontService.NormalizeRtf(aliasRtf.TrimEnd('\0')));
                    string aliasName = aliasProbe.Document.GetRange(0, 1).CharacterFormat.Name;
                    Check(!aliasName.Contains("HarmonyOS", StringComparison.OrdinalIgnoreCase), localized + " 保存后不应被替换成内置字体：" + aliasName);
                    Check(FontService.IsInstalledFamily(aliasName), localized + " 保存后的字体名仍应识别为已安装字体：" + aliasName);
                    ExportOverlay.Children.Clear();
                    ExportOverlay.Visibility = Visibility.Collapsed;
                }
                var tile = BoardCanvas.Children.OfType<SubjectTileControl>().First();
                Point before = chinese.InkStrokes[0].Points[1];
                chinese.TileWidth = 300; chinese.TileHeight = 180; tile.ApplyModelLayout(); await NextLayoutAsync();
                Check(chinese.InkStrokes[0].Points[1] == before && chinese.InkStrokes[0].Thickness == 5, "resizing tile does not change stored ink");
                chinese.TileWidth = 420; chinese.TileHeight = 320; tile.ApplyModelLayout();
                GlobalPenButton.IsChecked = true; await NextLayoutAsync();
                Check(GlobalInkToolbar.Visibility == Visibility.Visible && !AddSubjectButton.IsEnabled, "global ink mode owns editing input");
                Check(AddSubjectButton.Visibility == Visibility.Collapsed, "ink mode hides disabled add button instead of drawing a gray rectangle");
                Check(GlobalInkToolbar.BorderThickness.Left >= 1, "ink toolbar has a visible outline");
                await SaveVisualAsync(RootShell, Path.Combine(output, "editor.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
                GlobalPenButton.IsChecked = false;
                CaptureCurrentProject();
                string capturedPlain = chinese.Entries[0].Content;
                for (int i = 0; i < 3; i++) { BuildTiles(); await NextLayoutAsync(); }
                Check(chinese.Entries[0].Content == capturedPlain, "rebuilding rich text does not accumulate paragraphs");
                PersistProjects();
                string packagePath = Path.Combine(output, "roundtrip.pch");
                new PchPackageService(_projectStore).Save(project, packagePath);
                ProjectDocument imported = new PchPackageService(_projectStore).Import(packagePath);
                Check(imported.Subjects.Count == 2 && File.Exists(imported.Subjects[1].Entries[0].Attachments[0].Path), "actual rich text and image project package roundtrip");
                ExportImageView export = new(ProjectStore.Clone(project), this, () => { });
                ExportOverlay.Children.Add(export); ExportOverlay.Visibility = Visibility.Visible;
                await export.InitializeAsync(); await NextLayoutAsync();
                evidence.Add(export.VerificationStatus);
                await export.RenderToFileAsync(Path.Combine(output, "export-landscape.png"));
                await NextLayoutAsync();
                var exportEditors = FindVisuals<RichEditBox>(export).ToList();
                var longest = exportEditors.OrderByDescending(e => e.ActualHeight).First();
                evidence.Add($"Long editor actual height: {longest.ActualHeight}");
                Check(longest.ActualHeight > 400, "long rich text fully expands beyond tile viewport");
                for (int ratio = 1; ratio <= 4; ratio++)
                {
                    export.VerificationSetCanvas(ratio, ratio % 3); await NextLayoutAsync();
                    await export.RenderToFileAsync(Path.Combine(output, $"export-ratio-{ratio}.png"));
                }
                Check(chinese.TileWidth == 420 && chinese.TileHeight == 320, "export does not mutate board dimensions");
                RichEditBox missingProbe = new() { FontFamily = FontService.DefaultFamily };
                ExportOverlay.Children.Clear(); ExportOverlay.Children.Add(missingProbe); await NextLayoutAsync();
                missingProbe.Document.SetText(TextSetOptions.FormatRtf, @"{\rtf1\ansi{\fonttbl{\f0\fnil Pancake Missing Typeface;}}\f0 Missing font text}");
                FontService.RebindBundledFont(missingProbe);
                Check(missingProbe.Document.GetRange(0, 1).CharacterFormat.Name.Contains("HarmonyOS"), "missing font uses bundled fallback");
                var fallbacks = FontService.CaptureFallbacks(missingProbe);
                Check(fallbacks.Count == 1 && fallbacks[0].Family == "Pancake Missing Typeface", "missing font original name preserved separately");
                missingProbe.Document.GetText(TextGetOptions.FormatRtf, out string fallbackRtf);
                missingProbe.Document.SetText(TextSetOptions.FormatRtf, fallbackRtf);
                FontService.RebindBundledFont(missingProbe, fallbacks);
                Check(FontService.CaptureFallbacks(missingProbe)[0].Family == "Pancake Missing Typeface", "missing font name survives fallback roundtrip");
                ExportOverlay.Children.Clear(); ExportOverlay.Visibility = Visibility.Collapsed;
                Check(new FluentIcon().Glyph == FluentGlyphs.Info, "default Info icon is initialized without a property change");
                Check(((FluentIcon)AboutNavigationGroup.Icon).Glyph == FluentGlyphs.Info, "settings About item has an Info glyph");
                Check(FindVisuals<FluentIcon>(ProjectPicker).Any(icon => icon.Glyph == FluentGlyphs.Folder), "project name uses the bundled folder icon");
                Check(ReferenceEquals(ToolbarItems.Children.Last(), EditBoardButton), "save remains last in edit mode");
                FinishEditing(); await NextLayoutAsync();
                Check(ReferenceEquals(ToolbarItems.Children.First(), EditBoardButton), "edit is first in viewing mode");
                _library.Projects.Add(new ProjectDocument { Name = "9月6日 (2)" });
                RecentProjects_Opening(ProjectPicker, new object());
                Check(RecentProjectsPanel.Children.Count == 2 && RecentProjectsPanel.Spacing == 8, "recent projects have explicit spacing");
                ProjectPicker.Flyout.ShowAt(ProjectPicker); await NextLayoutAsync();
                await SaveVisualAsync(RootShell, Path.Combine(output, "viewing.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
                ProjectPicker.Flyout.Hide();
                EnterEditing(); GlobalPenButton.IsChecked = true;
                var inkSwatches = _inkColors.Children.Cast<ColorSwatchButton>().ToList();
                Check(inkSwatches.Count(s => s.IsSelected) == 1, "ink palette has exactly one selected swatch");
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(inkSwatches[2]);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await NextLayoutAsync();
                Check(inkSwatches[2].IsSelected && inkSwatches.Count(s => s.IsSelected) == 1, "clicking an ink color moves the checkmark");
                PaletteComboBox.SelectedIndex = 1; await NextLayoutAsync();
                Check(_inkColors.Children.Cast<ColorSwatchButton>().ElementAt(2).Color.Equals(Pancake.ViewModels.MainViewModel.BrushFromHex("#E5B3B3").Color), "ink swatches follow Macaron palette");
                Check(_inkColors.Children.Cast<ColorSwatchButton>().Count(s => s.IsSelected) == 1, "palette changes retain current ink selection");
                await SaveVisualAsync(RootShell, Path.Combine(output, "selected-color.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
                GlobalPenButton.IsChecked = false;
                var themeButton = FindVisuals<Button>(BoardCanvas).First(b => ToolTipService.GetToolTip(b)?.ToString() == "磁贴主题色");
                themeButton.Flyout.ShowAt(themeButton); await NextLayoutAsync();
                var themeSwatches = ((StackPanel)((Flyout)themeButton.Flyout).Content).Children.Cast<ColorSwatchButton>().ToList();
                Check(themeSwatches[0].Color.Equals(Pancake.ViewModels.MainViewModel.BrushFromHex("#A8D5BA").Color), "tile theme swatches follow Macaron palette");
                Check(themeSwatches.Count(s => s.IsSelected) == 1, "current tile theme has a checkmark");
                themeButton.Flyout.Hide();
                RichEditBox paletteEditor = FindVisuals<RichEditBox>(BoardCanvas).First();
                paletteEditor.Focus(FocusState.Programmatic); paletteEditor.Document.Selection.SetRange(0, 2); await NextLayoutAsync();
                var textColorButton = FindVisuals<Button>(RichTextToolbarHost).First(b => ToolTipService.GetToolTip(b)?.ToString() == "文字颜色");
                void InvokeButton(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
                    .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                InvokeButton(textColorButton); await NextLayoutAsync();
                var textSwatches = ((StackPanel)((Flyout)textColorButton.Flyout).Content).Children.Cast<ColorSwatchButton>().ToList();
                Check(textSwatches[0].Color.Equals(Microsoft.UI.Colors.Black) && textSwatches[1].Color.Equals(Microsoft.UI.Colors.White), "text palette always starts with black and white");
                Check(textSwatches[2].Color.Equals(Pancake.ViewModels.MainViewModel.BrushFromHex("#E8D5A5").Color), "other text color swatches follow Macaron palette");
                InvokeButton(textSwatches[1]); await NextLayoutAsync();
                Check(paletteEditor.Document.GetRange(0, 2).CharacterFormat.ForegroundColor.Equals(textSwatches[1].Color), "text swatch applies to preserved text selection");
                InvokeButton(textColorButton); await NextLayoutAsync();
                Check(textSwatches[1].IsSelected && textSwatches.Count(s => s.IsSelected) == 1, "reopened text palette marks current selection color");
                Check(ReferenceEquals(textSwatches[1].BorderBrush, Application.Current.Resources["AccentFillColorDefaultBrush"]), "selected white swatch uses the Windows accent border");
                textColorButton.Flyout.Hide();
                var highlightButton = FindVisuals<Button>(RichTextToolbarHost).First(b => ToolTipService.GetToolTip(b)?.ToString() == "高光颜色");
                InvokeButton(highlightButton); await NextLayoutAsync();
                var highlightSwatches = ((StackPanel)((Flyout)highlightButton.Flyout).Content).Children.Cast<ColorSwatchButton>().ToList();
                Check(highlightSwatches[2].Color.Equals(Pancake.ViewModels.MainViewModel.BrushFromHex("#E8D5A5").Color), "highlight swatches follow Macaron palette");
                InvokeButton(highlightSwatches[2]); await NextLayoutAsync();
                InvokeButton(highlightButton); await NextLayoutAsync();
                Check(highlightSwatches[2].IsSelected, "reopened highlight palette marks current selection color");
                InvokeButton(highlightSwatches[0]); await NextLayoutAsync();
                Check(paletteEditor.Document.GetRange(0, 2).CharacterFormat.BackgroundColor.Equals(TextConstants.AutoColor), "clear highlighting retains automatic background color");
                PaletteComboBox.SelectedIndex = 0;
                ShowSettings(); AppearanceNavigationGroup.IsExpanded = false; AboutNavigationGroup.IsExpanded = true;
                SettingsRoot.SelectedItem = AboutNavigationGroup; await NextLayoutAsync();
                var settingsSplitView = FindVisuals<SplitView>(SettingsRoot).First(view => view.Name == "RootSplitView");
                Check(settingsSplitView.CornerRadius == new CornerRadius(0), "settings navigation pane joins the content with square corners");
                await SaveVisualAsync(RootShell, Path.Combine(output, "about.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
                await VerifyExtendedSettingsAsync(output, Check);
                await VerifyPresentationSettingsAsync(output, Check);
                await VerifyAutoLayoutExportAsync(output, Check);
                evidence.Add("UI_VERIFICATION_OK");
            }
            catch (Exception ex) { evidence.Add("UI_VERIFICATION_FAILED\n" + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "result.txt"), evidence);
                Close();
            }
        };
    }

    // RTF 字体表中的家族名，用于定位字体在哪一步丢失。
    private static string RtfFonts(string rtf) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(rtf, @"\\f\d+\\fnil\s*([^;]+);").Select(m => m.Groups[1].Value.Trim()));

    private async Task VerifyAutoLayoutExportAsync(string output, Action<bool, string> check)
    {
        if (CurrentProject is null) ProjectStore.Create(_library, false);
        ShowBoard(); EnterEditing();
        _settings.InfiniteBoard = true; _settings.AutoLayoutGap = 20;
        _settings.AutoLayoutAlign = true; _settings.AutoLayoutResize = true;
        _settings.GridSnappingEnabled = false;
        ViewModel.ReplaceSubjects([]);
        var subject = ViewModel.AddSubject("自动布局");
        subject.TileWidth = 600; subject.TileHeight = 500;
        subject.Entries.Add(new HomeworkEntry { Content = "完整显示这行文字" });
        var ink = new InkStrokeData { Color = Microsoft.UI.Colors.Red, Thickness = 6 };
        ink.Points.AddRange([new Point(100, 180), new Point(290, 180)]); subject.InkStrokes.Add(ink);
        BuildTiles(); await NextLayoutAsync();
        ArrangeTiles(1100, 780); await NextLayoutAsync();
        check(subject.X == 0 && subject.Y == 0 && subject.TileHeight >= 183 && subject.TileHeight < 500,
            $"auto layout trims empty height while preserving ink and starts at top left ({subject.X},{subject.Y}; {subject.TileWidth} x {subject.TileHeight})");
        check(subject.TileWidth >= 293 && subject.TileWidth < 600, $"auto layout trims empty width while preserving text and ink ({subject.TileWidth} x {subject.TileHeight})");
        double w = subject.TileWidth, h = subject.TileHeight;
        _settings.AutoLayoutResize = false;
        ArrangeTiles(1100, 780); await NextLayoutAsync();
        check(subject.TileWidth == w && subject.TileHeight == h, "disabled auto resize preserves board dimensions");

        ProjectDocument fixture = new() { Name = "导出验证", Subjects =
        [new SubjectState { Name = "裁切笔迹", Width = 300, Height = 220,
            Entries = [new HomeworkState { Content = "文字清晰，不随背景透明" }],
            InkStrokes = [new InkStrokeState { Color = "#FFFF0000", Thickness = 6,
                Points = [new PointState { X = 220, Y = 160 }, new PointState { X = 480, Y = 160 }] }] },
         new SubjectState { Name = "相邻磁贴", Width = 300, Height = 220, Entries = [new HomeworkState { Content = "最小间隔 20" }] }] };
        BoardSettingsState settings = new() { AutoLayoutResize = false, AutoLayoutGap = 20, TileTitleSize = 35,
            TileBackground = new() { ColorOpacity = .4, Glass = true, Blur = 12 } };
        ExportImageView export = new(fixture, this, () => { }, settings, MediaLibraryStore);
        ExportOverlay.Children.Clear(); ExportOverlay.Children.Add(export); ExportOverlay.Visibility = Visibility.Visible;
        await export.InitializeAsync(); await NextLayoutAsync();
        var placements = export.VerificationPlacements;
        check(placements[0].X == 0 && placements[1].X == 320 && placements.All(p => p.Width == 300 && p.Height == 220),
            "export inherits gap and keeps original sizes when resize is disabled");
        var visual = FindVisuals<ExportTileVisual>(export).First();
        check(((RectangleGeometry)visual.Clip).Rect.Width == 300, "export clips overflowing ink at tile boundary");
        var opacity = FindVisuals<Slider>(export).Single(s => s.Header?.ToString() == "磁贴背景透明度（%）");
        check(Math.Abs(opacity.Value - 60) < .01, "export inherits current tile transparency");
        var blur = FindVisuals<Slider>(export).Single(s => s.Header?.ToString() == "磁贴背景模糊");
        check(Math.Abs(blur.Value - 12) < .01, "export inherits current tile blur");
        opacity.Value = 80;
        var titleSize = FindVisuals<NumberBox>(export).Single(n => n.Header?.ToString() == "标题字号");
        titleSize.Value = 60;
        // 生成一张独立于主流程的背景图，保证只跑专项验证时也能覆盖最近图片历史。
        string backgroundPath = Path.Combine(output, "export-background-source.png");
        Grid backgroundSource = new() { Width = 320, Height = 240, Background = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed) };
        ExportOverlay.Children.Clear(); ExportOverlay.Children.Add(backgroundSource);
        await NextLayoutAsync();
        await SaveVisualAsync(backgroundSource, backgroundPath, 320, 240);
        ExportOverlay.Children.Clear(); ExportOverlay.Children.Add(export);
        await NextLayoutAsync();
        await export.VerificationBackgroundAsync(backgroundPath);
        await NextLayoutAsync();
        check(MediaLibraryStore.RecentImages().Count > 0 && FindVisuals<RecentImagesView>(export).Any(), "export background shares recent image history");
        await export.RenderToFileAsync(Path.Combine(output, "export-background-blur.png"));
        await SaveVisualAsync(RootShell, Path.Combine(output, "export-settings.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        check(settings.TileBackground.ColorOpacity == .4 && fixture.Subjects[0].Width == 300, "export adjustments do not mutate original settings or project");
        ExportOverlay.Children.Clear(); ExportOverlay.Visibility = Visibility.Collapsed;

        // 自动排列必须先把一行横排满再换行，而不是竖着排一列。
        _settings.LayoutMode = "Split"; _settings.InfiniteBoard = false;
        ApplyExtendedSettings(); ShowBoard(); await NextLayoutAsync();
        ViewModel.ReplaceSubjects([]);
        for (int i = 0; i < 4; i++)
        {
            SubjectBoard created = ViewModel.AddSubject($"科目{i + 1}");
            created.TileWidth = 430; created.TileHeight = 320;
            created.Entries.Add(new HomeworkEntry { Content = "完成 P30 练习题\n复习二次函数公式" });
        }
        _settings.AutoLayoutResize = true; _settings.AutoLayoutAlign = true; _settings.AutoLayoutGap = 0;
        _settings.GridSnappingEnabled = false; IsGridSnappingEnabled = false;
        BuildTiles(); EnterEditing(); await NextLayoutAsync();
        ArrangeTiles(1100, 780);
        await NextLayoutAsync();
        check(ViewModel.Subjects.GroupBy(subject => subject.Y).Any(row => row.Count() >= 2),
            "auto arrange fills a row horizontally before wrapping: " + string.Join(" | ", ViewModel.Subjects.Select(s => $"{s.X:0},{s.Y:0} {s.TileWidth:0}x{s.TileHeight:0}")));
        check(ViewModel.Subjects.All(subject => subject.X >= 0 && subject.Y >= 0 &&
            subject.TileWidth >= SubjectTileControl.MinimumTileWidth && subject.TileHeight >= SubjectTileControl.MinimumTileHeight),
            "auto arrange keeps rows inside the board above the minimum tile size");

        // 自动排版间隔的步进跟随网格吸附：关闭按 10px，开启按半格。
        GridSnapToggleButton.IsChecked = false;
        await NextLayoutAsync();
        check(Math.Abs(_autoLayoutGapSlider!.StepFrequency - 10) < .01, "gap slider steps by 10px while grid snapping is off");
        GridSnapToggleButton.IsChecked = true;
        await NextLayoutAsync();
        check(Math.Abs(_autoLayoutGapSlider.StepFrequency - GridSize / 2) < .01,
            $"gap slider steps by half a grid cell while snapping is on ({_autoLayoutGapSlider.StepFrequency})");
    }

    private async Task VerifyExtendedSettingsAsync(string output, Action<bool, string> check)
    {
        FinishEditing();
        _appWindow!.Resize(new Windows.Graphics.SizeInt32(2400, 1500));
        await NextLayoutAsync();
        await VerifyFullScreenHintAsync(output, check);
        foreach (string mode in new[] { "Split", "Board", "Clock", "Free", "Split" })
        {
            _settings.LayoutMode = mode; ApplyExtendedSettings(); ShowBoard(); EnterEditing(); await NextLayoutAsync();
            check(ClockPanel.Visibility == (mode is "Board" or "Free" ? Visibility.Collapsed : Visibility.Visible), mode + " clock visibility");
            check(BoardWorkspace.Visibility == (mode == "Clock" ? Visibility.Collapsed : Visibility.Visible), mode + " board visibility");
            check(_freeWidgets.Count == (mode == "Free" ? 3 : 0), mode + " independent component hosts");
            if (mode is "Split" or "Clock")
            {
                string prefix = mode == "Split" ? "Split" : "ClockMode";
                RegionPlacement clock = _settings.Widgets[prefix + "Clock"];
                RegionPlacement components = _settings.Widgets[prefix + "Components"];
                check(Math.Abs(clock.X + clock.Width / 2 - ClockPanel.ActualWidth / 2) < .01, mode + " clock stays on the center axis");
                check(Math.Abs(components.X + components.Width / 2 - ClockPanel.ActualWidth / 2) < .01, mode + " component row stays on the center axis");
                double clockContentAspect = ClockContentView.Child.DesiredSize.Width / ClockContentView.Child.DesiredSize.Height;
                check(Math.Abs(clock.Width / clock.Height - clockContentAspect) < .01,
                    mode + " clock bounds match the actual content ratio without one-sided blank space");
                check(Math.Abs(components.Width / components.Height - DockedComponentsAspectRatio) < .01 && ClockComponents.Orientation == Orientation.Horizontal,
                    mode + " weather and noise scale together in one row");
                int expectedLayers = mode == "Split" ? 3 : 2;
                check(FreeLayoutHandles.Children.Count == expectedLayers, mode + " exposes constrained clock and component interactions while editing");
                var interactions = FreeLayoutHandles.Children.OfType<Grid>().ToList();
                check(interactions.Count == 2 && interactions.All(layer => layer.Children.OfType<Thumb>().Count() == 2),
                    mode + " allows dragging from the full clock and component surfaces");
                check(interactions.SelectMany(layer => layer.Children.OfType<Thumb>()).Count(thumb => thumb.Opacity == 0) == 2,
                    mode + " resize boxes stay hidden until pointer hover");
            }
            if (mode == "Free")
            {
                check(FreeLayoutHandles.Children.OfType<Grid>().Count() == 3, "each free widget has a full-surface move layer and hover resize box");
                WidgetLayout.Move(_settings.Widgets["Clock"], 15, 30, DisplayRoot.ActualWidth, DisplayRoot.ActualHeight);
                WidgetLayout.Resize(_settings.Widgets["Weather"], 15, 20, DisplayRoot.ActualWidth, DisplayRoot.ActualHeight);
                ApplyExtendedSettings(); await NextLayoutAsync(); SaveStateNow();
                var persisted = _projectStore.Load().Settings;
                check(persisted.Widgets["Clock"].X == _settings.Widgets["Clock"].X && persisted.Widgets["Weather"].Height == _settings.Widgets["Weather"].Height, "widget movement and resizing reach actual persisted settings");
                check(_freeWidgets["Clock"].Width == _settings.Widgets["Clock"].Width, "widget host follows placement dimensions");
            }
            await SaveVisualAsync(RootShell, Path.Combine(output, "layout-" + mode + ".png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        }
        _settings.InfiniteBoard = true; _settings.GridSize = 64; _renderedGridWidth = 0; ApplyExtendedSettings(); await NextLayoutAsync();
        check(BoardScroller.ZoomMode == ZoomMode.Enabled && BoardSurface.Width > BoardScroller.ActualWidth, "infinite board extends beyond viewport");
        BoardScroller.ChangeView(120, 80, .75f, true); await NextLayoutAsync();
        check(Math.Abs(BoardScroller.ZoomFactor - .75) < .01, "infinite board actually zooms");
        var gridLines = GridCanvas.Children.OfType<Microsoft.UI.Xaml.Shapes.Line>().Where(l => l.X1 == l.X2).ToList();
        check(gridLines.Count > 1 && Math.Abs(gridLines[1].X1 - gridLines[0].X1 - 64) < .01, "grid uses configured spacing");
        _settings.ShowGridWhileEditing = false; _settings.GridStyle = "Dots"; _renderedGridAppearance = string.Empty; ApplyExtendedSettings(); await NextLayoutAsync();
        check(GridCanvas.Children.OfType<Microsoft.UI.Xaml.Shapes.Ellipse>().Any() && !GridCanvas.Children.OfType<Microsoft.UI.Xaml.Shapes.Line>().Any(), "dot grid changes only the rendered appearance");
        _settings.GridStyle = "None"; _renderedGridAppearance = string.Empty; ApplyExtendedSettings(); await NextLayoutAsync();
        check(GridCanvas.Children.Count == 0 && IsGridSnappingEnabled, "hidden grid keeps snapping enabled");
        _settings.ShowGridWhileEditing = true; _renderedGridAppearance = string.Empty; ApplyExtendedSettings(); await NextLayoutAsync();
        check(GridCanvas.Children.OfType<Microsoft.UI.Xaml.Shapes.Line>().Any(), "editing override displays the regular grid");
        FinishEditing(); await NextLayoutAsync();
        check(GridCanvas.Children.Count == 0, "leaving edit mode restores the configured hidden grid");
        EnterEditing(); _settings.GridStyle = "Grid"; _settings.ShowGridWhileEditing = true; _renderedGridAppearance = string.Empty;
        _settings.InfiniteBoard = false; _settings.GridSize = 48; _renderedGridWidth = 0;
        _settings.SharedBackgroundEnabled = true;
        _settings.SharedBackground.ImagePath = Path.Combine(output, "attachment.png");
        _settings.ClockBackground.Glass = true; _settings.ClockBackground.Blur = 35;
        _settings.BoardBackground.Glass = true; _settings.BoardBackground.Blur = 8;
        _settings.TileBackground.Glass = true; _settings.TileBackground.Blur = 15; _settings.TileTitleSize = 36;
        ApplyExtendedSettings(); await NextLayoutAsync();
        check(Math.Abs(BoardScroller.ZoomFactor - 1) < .01 && BoardScroller.HorizontalOffset == 0 && BoardScroller.VerticalOffset == 0, $"disabling infinite board restores scale and position ({BoardScroller.ZoomFactor}, {BoardScroller.HorizontalOffset}, {BoardScroller.VerticalOffset})");
        check(UseSharedBackground && FindVisuals<Image>(SharedBackgroundVisual).Any(), "shared background image is a single cross-region layer");
        Image sharedImage = FindVisuals<Image>(SharedBackgroundVisual).Single();
        _settings.SplitRatio = .46; ApplyExtendedSettings(); await NextLayoutAsync();
        check(ReferenceEquals(sharedImage, FindVisuals<Image>(SharedBackgroundVisual).Single()), "split changes reuse the loaded background image without a black reload frame");
        check(FindVisuals<Border>(ClockBackgroundVisual).Any(b => b.Background is BlurBackdropBrush) && FindVisuals<Border>(BoardBackgroundVisual).Any(b => b.Background is BlurBackdropBrush), "regions retain independent real blur layers");
        await SaveVisualAsync(RootShell, Path.Combine(output, "background-glass.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        _settings.SharedBackgroundEnabled = false; _settings.ClockBackground.Glass = _settings.BoardBackground.Glass = _settings.TileBackground.Glass = false;
        _settings.ToolbarIconOnly = false;
        GlobalPenButton.IsChecked = true;
        _inkEraser.IsChecked = true;
        check(_inkSettings.Eraser && _inkPen.IsChecked == false, "eraser icon selects erasing exclusively");
        _inkPen.IsChecked = true;
        check(!_inkSettings.Eraser && _inkEraser.IsChecked == false, "pen icon returns to drawing exclusively");
        List<InkWidthPresetButton> thicknessPresets = [.. _inkWidths.Children.OfType<InkWidthPresetButton>()];
        check(thicknessPresets.Select(preset => preset.Thickness).SequenceEqual(new[] { 2d, 5d, 12d }),
            "ink thickness offers thin/medium/thick presets only");
        check(!GlobalInkTools.Children.OfType<Slider>().Any(),
            "ink toolbar no longer exposes a thickness slider");
        check(thicknessPresets.Count(preset => preset.IsSelected) == 1 &&
            Math.Abs(thicknessPresets.Single(preset => preset.IsSelected).Thickness - _inkSettings.Thickness) < .01,
            "ink thickness preset highlights the active thickness");
        check(thicknessPresets[0].Content is Microsoft.UI.Xaml.Shapes.Ellipse thin &&
            thicknessPresets[1].Content is Microsoft.UI.Xaml.Shapes.Ellipse medium &&
            thicknessPresets[2].Content is Microsoft.UI.Xaml.Shapes.Ellipse thick &&
            thin.Width < medium.Width && medium.Width < thick.Width,
            "ink thickness presets grow from thin to thick");
        foreach (ToggleButton button in new[] { _inkPen, _inkEraser })
        {
            Rect vectorBounds = button.Content is Viewbox { Child: IconSourceElement { IconSource: PathIconSource path } } ? path.Data.Bounds : default;
            bool usesInsetVector = button.Content is Viewbox { Child: IconSourceElement { IconSource: PathIconSource source } } viewbox &&
                viewbox.Width >= 20 && viewbox.Height >= 20 &&
                source.Data.Bounds.Left > 0 && source.Data.Bounds.Top > 0 &&
                source.Data.Bounds.Right < 20 && source.Data.Bounds.Bottom < 20;
            check(usesInsetVector, "ink tool vector stays inside its unclipped 20 DIP viewport");
            check(true, $"ink tool vector bounds {vectorBounds}, inset left {vectorBounds.Left:0.##} right {20 - vectorBounds.Right:0.##}");
        }
        // 画笔与橡皮按钮：图标必须在按钮内水平、垂直居中且完整可见，圆角必须跟随控制窗圆角。
        double originalScale = _settings.ToolbarScale, originalRadius = _settings.ToolbarRadius;
        foreach (double scale in new[] { .6, 1, 2 })
        {
            _settings.ToolbarScale = scale; ApplyToolbarSettings(); await NextLayoutAsync();
            foreach (ToggleButton button in new[] { _inkPen, _inkEraser })
            {
                Viewbox icon = (Viewbox)button.Content;
                Rect iconBounds = icon.TransformToVisual(button).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                double horizontalGap = Math.Abs(iconBounds.Left - (button.ActualWidth - iconBounds.Right));
                double verticalGap = Math.Abs(iconBounds.Top - (button.ActualHeight - iconBounds.Bottom));
                check(horizontalGap <= .5 && verticalGap <= .5,
                    $"ink icon is centered in its button at scale {scale} (offset {horizontalGap:0.##}/{verticalGap:0.##})");
                check(iconBounds.Left >= 8 * scale && button.ActualWidth - iconBounds.Right >= 8 * scale,
                    $"ink icon keeps an even inset inside its button at scale {scale} ({iconBounds.Left:0.##})");
            }
            check(true, $"ink toolbar geometry at scale {scale}: padding {GlobalInkToolbar.Padding.Left:0.##}, " +
                $"button {_inkPen.ActualWidth:0.##}, corner {_inkPen.CornerRadius.TopLeft:0.##}");
        }
        foreach (double radius in new[] { 0d, 6d, 14d, 30d, 60d })
        {
            _settings.ToolbarScale = 1;
            _settings.ToolbarRadius = radius; ApplyToolbarSettings(); await NextLayoutAsync();
            double expected = Math.Clamp(radius - GlobalInkToolbar.Padding.Left, 0, 20);
            check(new[] { _inkPen, _inkEraser }.All(button => Math.Abs(button.CornerRadius.TopLeft - expected) < .01),
                $"ink icon button corners follow the control window radius {radius} ({_inkPen.CornerRadius.TopLeft:0.##})");
        }
        _settings.ToolbarRadius = 30; ApplyToolbarSettings(); await NextLayoutAsync();
        check(_inkPen.CornerRadius.TopLeft == _inkEraser.CornerRadius.TopLeft && _inkPen.CornerRadius.TopLeft > 4,
            "pen and eraser share one window-following corner instead of the stock button corner");
        _settings.ToolbarScale = originalScale; _settings.ToolbarRadius = originalRadius;
        ApplyToolbarSettings(); await NextLayoutAsync();
        await SaveVisualAsync(GlobalInkToolbar, Path.Combine(output, "ink-toolbar.png"),
            (int)GlobalInkToolbar.ActualWidth, (int)GlobalInkToolbar.ActualHeight);
        foreach (string position in new[] { "BottomLeft", "BottomCenter", "BottomRight", "TopCenter", "TopLeft", "TopRight", "CenterLeft", "CenterRight" })
        {
            _settings.ToolbarPosition = position; ApplyExtendedSettings(); await NextLayoutAsync();
            bool verticalToolbar = position.StartsWith("Center");
            check(ToolbarItems.Orientation == (verticalToolbar ? Orientation.Vertical : Orientation.Horizontal), position + " toolbar orientation");
            check(_inkWidths.Orientation == (verticalToolbar ? Orientation.Vertical : Orientation.Horizontal), position + " ink thickness preset orientation");
            check(verticalToolbar ? _inkWidths.ActualHeight > _inkWidths.ActualWidth : _inkWidths.ActualWidth > _inkWidths.ActualHeight,
                position + " ink thickness presets follow the toolbar axis");
            check(_toolbarLabels[EditBoardButton].Label.Visibility == Visibility.Visible, position + " button names visible");
            // 竖置时画笔栏可能出现滚动条，按钮不能被挤压，图标仍须居中且完整。
            foreach (ToggleButton button in new[] { _inkPen, _inkEraser })
            {
                Viewbox icon = (Viewbox)button.Content;
                Rect iconBounds = icon.TransformToVisual(button).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                check(Math.Abs(iconBounds.Left - (button.ActualWidth - iconBounds.Right)) <= .5 &&
                    Math.Abs(iconBounds.Top - (button.ActualHeight - iconBounds.Bottom)) <= .5,
                    position + " ink icon stays centered in its button");
            }
            if (verticalToolbar)
            {
                foreach (var labeledButton in new[] { BackToBoardButton, FullScreenButton, EditBoardButton })
                {
                    var pair = _toolbarLabels[labeledButton];
                    StackPanel content = (StackPanel)labeledButton.Content;
                    check(content.Orientation == Orientation.Vertical &&
                        ReferenceEquals(content.Children[0], pair.Icon) &&
                        ReferenceEquals(content.Children[1], pair.Label),
                        position + " button name is directly below its icon");
                    check(Math.Abs(labeledButton.Width - 64) < .01 && Math.Abs(pair.Label.FontSize - 8) < .01,
                        position + " labeled button stays narrow with smaller text");
                }
            }
            var rect = FloatingToolbar.TransformToVisual(RootShell).TransformBounds(new Rect(0, 0, FloatingToolbar.ActualWidth, FloatingToolbar.ActualHeight));
            var penRect = GlobalInkToolbar.TransformToVisual(RootShell).TransformBounds(new Rect(0, 0, GlobalInkToolbar.ActualWidth, GlobalInkToolbar.ActualHeight));
            check(rect.Right <= RootShell.ActualWidth + 1 && rect.Bottom <= RootShell.ActualHeight + 1, position + " toolbar fits window");
            check(rect.Right <= penRect.Left || rect.Left >= penRect.Right || rect.Bottom <= penRect.Top || rect.Top >= penRect.Bottom, position + " pen and main toolbar do not overlap");
            if (position == "CenterLeft") await SaveVisualAsync(RootShell, Path.Combine(output, "toolbar-vertical.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        }
        GlobalPenButton.IsChecked = false;
        _settings.ToolbarIconOnly = true;
        _settings.ToolbarPosition = "BottomCenter"; ApplyExtendedSettings();
        FinishEditing(); ShowSettings();
        var settingsPages = SettingsRoot.MenuItems.Concat(SettingsRoot.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .SelectMany(item => item.MenuItems.OfType<NavigationViewItem>().DefaultIfEmpty(item))
            .Where(item => item.Tag is not null)
            .ToList();
        check(settingsPages.Count == _settingsPages.Count, "each settings content page has a navigation item");
        check(SettingsRoot.MenuItems.OfType<NavigationViewItem>().Count(item => item.MenuItems.Count > 0) == 2,
            "settings subpages live in expandable navigation groups");
        check(ReferenceEquals(SettingsRoot.SelectedItem, AboutNavigationGroup) &&
            settingsPages.Count(item => item.IsSelected) == 1,
            "reopening settings preserves exactly one selected navigation item");
        foreach (NavigationViewItem nav in settingsPages)
        {
            foreach (NavigationViewItem group in SettingsRoot.MenuItems.OfType<NavigationViewItem>().Where(item => item.MenuItems.Count > 0))
                group.IsExpanded = group.MenuItems.Contains(nav);
            SettingsRoot.SelectedItem = nav; await NextLayoutAsync();
            SettingsPage page = _settingsPages[nav.Tag!.ToString()!];
            check(page.Container.Visibility == Visibility.Visible && page.Content.Visibility == Visibility.Visible, nav.Tag + " navigation page has content");
            check(SettingsPageTitle.Text == page.Title, nav.Tag + " navigation page updates the title");
            check(nav.IsSelected, nav.Tag + " navigation item shows the selected state");
            check(settingsPages.Count(item => item.IsSelected) == 1,
                nav.Tag + " navigation clears the previous selected background");
        }
        AppearanceNavigationGroup.IsExpanded = true;
        ComponentsNavigationGroup.IsExpanded = AboutNavigationGroup.IsExpanded = false;
        SettingsRoot.SelectedItem = AppearanceNavItem;
        await NextLayoutAsync();
        await Task.Delay(250);
        await SaveVisualAsync(RootShell, Path.Combine(output, "settings-v203.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        // 小窗口和最大尺寸组合仍能通过滚动访问全部按钮。
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        _settings.ToolbarScale = 2; _settings.ToolbarIconOnly = false;
        ShowBoard(); EnterEditing(); ApplyExtendedSettings(); await NextLayoutAsync();
        check(MainToolbarScroll.ScrollableWidth > 0, "oversized toolbar remains horizontally scrollable in a narrow window");
        _settings.ToolbarScale = 1; _settings.ToolbarIconOnly = true; ApplyExtendedSettings();
    }

    private async Task VerifyFullScreenHintAsync(string output, Action<bool, string> check)
    {
        ShowBoard(); FinishEditing();
        _settings.ToolbarIconOnly = true;
        _settings.ToolbarScale = 1;
        _settings.ToolbarRadius = 60;
        SetFullScreen(true); await NextLayoutAsync();
        foreach (string position in new[] { "CenterLeft", "BottomCenter" })
        {
            _settings.ToolbarPosition = position; ApplyExtendedSettings();
            bool verticalToolbar = position.StartsWith("Center");
            var (icon, exitLabel) = _toolbarLabels[FullScreenButton];
            double iconFontSize = ((FluentIcon)icon).FontSize;
            ShowFullScreenExitHint(); await NextLayoutAsync();
            StackPanel content = (StackPanel)FullScreenButton.Content;
            check(exitLabel.Visibility == Visibility.Visible && FindVisuals<TextBlock>(FullScreenButton).Count(label =>
                    label.Visibility == Visibility.Visible && label.Text.Replace("\n", string.Empty) == "退出全屏") == 1,
                position + " fullscreen hint has one visible label");
            check(exitLabel.Text == "退出全屏", position + " fullscreen hint keeps text horizontal");
            check(content.Orientation == (verticalToolbar ? Orientation.Vertical : Orientation.Horizontal) &&
                ReferenceEquals(content.Children[0], icon) && ReferenceEquals(content.Children[1], exitLabel),
                position + " fullscreen hint extends after the unchanged icon");
            check(Math.Abs(((FluentIcon)icon).FontSize - iconFontSize) < .01,
                position + " fullscreen hint keeps the icon size");
            Rect iconRect = icon.TransformToVisual(FullScreenButton).TransformBounds(new Rect(0, 0, icon.ActualSize.X, icon.ActualSize.Y));
            Rect labelRect = exitLabel.TransformToVisual(FullScreenButton).TransformBounds(new Rect(0, 0, exitLabel.ActualWidth, exitLabel.ActualHeight));
            check(labelRect.Left >= 0 && labelRect.Top >= 0 && labelRect.Right <= FullScreenButton.ActualWidth && labelRect.Bottom <= FullScreenButton.ActualHeight,
                position + " fullscreen hint label fits inside its button");
            check(verticalToolbar
                    ? Math.Abs(FullScreenButton.ActualWidth - 44) < 1 && FullScreenButton.ActualHeight > 44 && labelRect.Top >= iconRect.Bottom
                    : Math.Abs(FullScreenButton.ActualHeight - 44) < 1 && FullScreenButton.ActualWidth > 44 && labelRect.Left >= iconRect.Right,
                position + " fullscreen hint grows only along the toolbar axis");
            double expectedRadius = Math.Min(60 - 7, Math.Min(FullScreenButton.ActualWidth, FullScreenButton.ActualHeight) / 2);
            check(Math.Abs(FullScreenButton.CornerRadius.TopLeft - expectedRadius) < 1,
                position + " fullscreen hint corner follows the maximum toolbar radius without clipping");
            await SaveVisualAsync(RootShell, Path.Combine(output, $"fullscreen-hint-{position}.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
            HideFullScreenExitHint();
        }
        _settings.ToolbarIconOnly = false;
        foreach (string position in new[] { "CenterLeft", "BottomCenter" })
        {
            _settings.ToolbarPosition = position; ApplyExtendedSettings(); await NextLayoutAsync();
            var (_, exitLabel) = _toolbarLabels[FullScreenButton];
            StackPanel content = (StackPanel)FullScreenButton.Content;
            double width = FullScreenButton.ActualWidth, height = FullScreenButton.ActualHeight, fontSize = exitLabel.FontSize;
            Orientation orientation = content.Orientation;
            ShowFullScreenExitHint(); await NextLayoutAsync();
            check(exitLabel.Text == "退出全屏" && content.Orientation == orientation &&
                    Math.Abs(exitLabel.FontSize - fontSize) < .01 && Math.Abs(FullScreenButton.ActualWidth - width) < 1 &&
                    Math.Abs(FullScreenButton.ActualHeight - height) < 1,
                position + " labeled fullscreen hint only changes color");
            check(ReferenceEquals(FullScreenButton.Background, FullScreenHintBrush), position + " labeled fullscreen hint turns red");
            double expectedRadius = Math.Min(60 - 7, Math.Min(width, height) / 2);
            check(Math.Abs(FullScreenButton.CornerRadius.TopLeft - expectedRadius) < 1,
                position + " labeled fullscreen hint corner follows the maximum toolbar radius without clipping");
            await SaveVisualAsync(RootShell, Path.Combine(output, $"fullscreen-hint-labeled-{position}.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
            HideFullScreenExitHint();
        }
        SetFullScreen(false); _settings.ToolbarPosition = "BottomCenter"; _settings.ToolbarRadius = 14; ApplyExtendedSettings();
    }

    private async Task VerifyPresentationSettingsAsync(string output, Action<bool, string> check)
    {
        FinishEditing(); SetFullScreen(false); ShowSettings();
        SettingsRoot.SelectedItem = AboutNavigationGroup;
        await NextLayoutAsync();
        check(AboutNavigationGroup.MenuItems.Count == 0 && SettingsPageTitle.Text == "关于" &&
            FindVisuals<Border>(_settingsPages["About"].Content).Contains(VersionSettingsCard) &&
            FindVisuals<Border>(_settingsPages["About"].Content).Contains(UpdateSettingsCard) &&
            FindVisuals<Grid>(_settingsPages["About"].Content).Contains(RepositorySettingsCard), "About combines version repositories and updates on one page");
        foreach (string page in new[] { "AppearanceTile", "AppearanceBackground", "AppearanceGrid", "AppearanceToolbar" })
        {
            ShowSettingsPage(page); await NextLayoutAsync();
            var previews = FindVisuals<Border>(_settingsPages[page].Content).Where(border => border.Name.EndsWith("AppearancePreview")).ToList();
            check(previews.Count == (page == "AppearanceBackground" ? 3 : 1) && previews.All(preview => preview.ActualWidth > 0 && preview.ActualHeight > 0),
                page + " has all requested live previews");
            await SaveVisualAsync(RootShell, Path.Combine(output, page + "-preview.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        }
        ShowSettingsPage("Layout");
        await NextLayoutAsync();
        check(FindVisuals<Slider>(_settingsPages["Layout"].Content).Any(slider => Equals(slider.Header, "自动排版磁贴间隔")) &&
            FindVisuals<ToggleSwitch>(_settingsPages["Layout"].Content).Any(toggle => Equals(toggle.Header, "自动对齐") && toggle.IsOn) &&
            FindVisuals<ToggleSwitch>(_settingsPages["Layout"].Content).Any(toggle => Equals(toggle.Header, "自动调整磁贴大小") && toggle.IsOn),
            "layout page exposes auto layout gap, alignment and resize defaults");
        check(FindVisuals<Border>(_settingsPages["Layout"].Content).Any(border => border.Name == "LayoutAppearancePreview" &&
                border.ActualWidth > 0 && border.ActualHeight > 0) &&
            FindVisuals<TextBlock>(_appearancePreviews["Layout"]).Any(text => text.Text.StartsWith("网格 ")) &&
            FindVisuals<TextBlock>(_appearancePreviews["Layout"]).Any(text => text.Text.StartsWith("自动排列")),
            "layout page previews grid size and auto arrangement in one scene");
        // 预览必须是真实磁贴控件排在看板同款网格上，而不是画出来的示意图。
        var layoutTiles = FindVisuals<SubjectTileControl>(_appearancePreviews["Layout"]).ToList();
        check(layoutTiles.Count == 4 && layoutTiles.All(tile => tile.ActualWidth > 0 && tile.ActualHeight > 0),
            "layout preview uses real subject tiles");
        check(FindVisuals<Microsoft.UI.Xaml.Shapes.Line>(_appearancePreviews["Layout"]).Any() &&
            layoutTiles.Select(tile => Canvas.GetTop(tile)).Distinct().Count() >= 2,
            "layout preview arranges the real tiles on the board grid");
        // 网格大小变化必须重画预览网格，且磁贴与网格是同一套缩放，数量随大小单调变化。
        string originalGridStyle = _settings.GridStyle;
        _settings.GridStyle = "Grid";
        int GridLineCount() => FindVisuals<Microsoft.UI.Xaml.Shapes.Line>(_appearancePreviews["Layout"]).Count();
        double layoutPreviewGrid = _settings.GridSize;
        _settings.GridSize = 16; ApplyExtendedSettings(); await NextLayoutAsync();
        int denseLines = GridLineCount();
        _settings.GridSize = 160; ApplyExtendedSettings(); await NextLayoutAsync();
        int sparseLines = GridLineCount();
        check(denseLines > sparseLines && sparseLines > 0, "layout preview redraws the grid for the configured grid size");
        _settings.GridSize = layoutPreviewGrid; _settings.GridStyle = originalGridStyle;
        ApplyExtendedSettings(); await NextLayoutAsync();
        // 网格大小按整数调节；改变网格大小后已有磁贴必须重新吸附到新网格。
        Slider gridSizeSlider = FindVisuals<Slider>(_settingsPages["Layout"].Content).Single(slider => Equals(slider.Header, "网格大小"));
        Slider gapSlider = FindVisuals<Slider>(_settingsPages["Layout"].Content).Single(slider => Equals(slider.Header, "自动排版磁贴间隔"));
        check(Math.Abs(gridSizeSlider.StepFrequency - 1) < .01, "grid size slider steps in whole pixels");
        bool originalSnapping = IsGridSnappingEnabled;
        _settings.GridSnappingEnabled = IsGridSnappingEnabled = true;
        gridSizeSlider.Value = 64; await NextLayoutAsync();
        check(ViewModel.Subjects.Count > 0 && ViewModel.Subjects.All(subject => subject.X % 64 == 0 && subject.Y % 64 == 0) &&
            ViewModel.Subjects.All(subject => subject.TileWidth % 64 == 0),
            "changing grid size re-snaps tiles to the new grid: " +
            string.Join(" | ", ViewModel.Subjects.Select(s => $"{s.X:0.#},{s.Y:0.#} {s.TileWidth:0.#}x{s.TileHeight:0.#}")));
        // 拖动自动排版间隔不能改变网格大小，也不能让预览里的网格跟着缩放。
        int gridLinesBeforeGapDrag = GridLineCount();
        gapSlider.Value = 48; await NextLayoutAsync();
        check(Math.Abs(_settings.GridSize - 64) < .01 && GridLineCount() == gridLinesBeforeGapDrag,
            "dragging the auto layout gap leaves grid size and preview grid unchanged");
        // 两个滑块都必须跟手：拖动过程不能触发整窗重刷。
        var gridDrag = System.Diagnostics.Stopwatch.StartNew();
        for (int size = 16; size <= 64; size++) gridSizeSlider.Value = size;
        gridDrag.Stop();
        var gapDrag = System.Diagnostics.Stopwatch.StartNew();
        for (int value = 0; value <= 48; value++) gapSlider.Value = value;
        gapDrag.Stop();
        check(gridDrag.ElapsedMilliseconds < 1500, "dragging grid size stays responsive: " + gridDrag.ElapsedMilliseconds + "ms/49 updates");
        check(gapDrag.ElapsedMilliseconds < 600, "dragging auto layout gap stays responsive: " + gapDrag.ElapsedMilliseconds + "ms/49 updates");
        _settings.GridSnappingEnabled = IsGridSnappingEnabled = originalSnapping;
        gridSizeSlider.Value = layoutPreviewGrid; gapSlider.Value = 0;
        ApplyExtendedSettings(); await NextLayoutAsync();
        // 自动布局里的“吸附到网格”与编辑工具栏的吸附按钮共用同一份设置，双向同步。
        ToggleSwitch snapToggle = FindVisuals<ToggleSwitch>(_settingsPages["Layout"].Content)
            .Single(toggle => Equals(toggle.Header, "吸附到网格"));
        snapToggle.IsOn = false; await NextLayoutAsync();
        check(!IsGridSnappingEnabled && GridSnapToggleButton.IsChecked == false && !_settings.GridSnappingEnabled,
            "auto layout snap switch turns grid snapping off for the whole app");
        snapToggle.IsOn = true; await NextLayoutAsync();
        check(IsGridSnappingEnabled && GridSnapToggleButton.IsChecked == true && _settings.GridSnappingEnabled &&
            Math.Abs(gapSlider.StepFrequency - Math.Max(1, GridSize) / 2) < .01,
            "auto layout snap switch enables snapping and half cell gap steps");
        GridSnapToggleButton.IsChecked = false; await NextLayoutAsync();
        check(!snapToggle.IsOn && Math.Abs(gapSlider.StepFrequency - 10) < .01, "board snap button turns the auto layout snap switch off");
        GridSnapToggleButton.IsChecked = true; await NextLayoutAsync();
        check(snapToggle.IsOn, "board snap button turns the auto layout snap switch back on");
        await SaveVisualAsync(_appearancePreviews["Layout"], Path.Combine(output, "layout-preview.png"), 640, 300);
        ShowSettingsPage("AppearanceTile");
        await NextLayoutAsync();
        var originalTile = FindVisuals<SubjectTileControl>(_appearancePreviews["Tile"]).Single();
        var titleSlider = FindVisuals<Slider>(_settingsPages["AppearanceTile"].Content).Single(slider => Equals(slider.Header, "标题大小"));
        var originalEditors = FindVisuals<RichEditBox>(_appearancePreviews["Tile"]).ToArray();
        var originalBackground = SharedBackgroundVisual.Children.ToArray();
        var titleTiming = System.Diagnostics.Stopwatch.StartNew();
        for (int size = 16; size <= 72; size++) titleSlider.Value = size;
        titleTiming.Stop();
        await NextLayoutAsync();
        check(ReferenceEquals(originalTile, FindVisuals<SubjectTileControl>(_appearancePreviews["Tile"]).Single()),
            "title slider retains the preview tile and its rich text editors");
        check(FindVisuals<TextBox>(_appearancePreviews["Tile"]).Any(text => text.Text == "这是标题~"), "tile appearance preview uses the fixed sample");
        check(titleTiming.ElapsedMilliseconds < 1000, "57 title slider updates take less than one second: " + titleTiming.ElapsedMilliseconds + "ms");
        check(originalEditors.SequenceEqual(FindVisuals<RichEditBox>(_appearancePreviews["Tile"])), "title slider preserves rich text editors");
        check(originalBackground.SequenceEqual(SharedBackgroundVisual.Children), "title slider preserves window background resources");
        check(originalEditors.Length == 2, "fixed preview contains exactly two entries");
        originalEditors[0].Document.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out string sampleItalic);
        originalEditors[1].Document.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out string sampleLink);
        check(sampleItalic.Contains("\\i") && sampleItalic.Contains("Through adversity to the stars."), "sample preserves italic text");
        var sampleColor = originalEditors[0].Document.GetRange(0, 1).CharacterFormat.ForegroundColor;
        check(BoardTheme.IsLight ? sampleColor.R == 0 : sampleColor.R == 247, "sample text follows theme foreground color");
        check(sampleLink.Contains("HYPERLINK") && sampleLink.Contains("https://en.wikipedia.org/wiki/Per_ardua_ad_astra"), "sample preserves hyperlink target");
        var backdrop = FindVisuals<Image>(_appearancePreviews["Tile"]).First();
        check(backdrop.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap && bitmap.PixelWidth > 0, "tile preview background asset loads");
        _settings.TileBackground.Glass = true;
        _settings.TileBackground.Blur = 15;
        _settings.TileBackground.Color = "";
        _settings.TileTitleSize = 29;
        ApplyExtendedSettings(); await NextLayoutAsync();
        await SaveVisualAsync(_appearancePreviews["Tile"], Path.Combine(output, "tile-sample-glass.png"), 640, 300);
        _settings.TileTitleSize = 47; ApplyExtendedSettings(); await NextLayoutAsync();
        check(FindVisuals<TextBox>(_appearancePreviews["Tile"]).Any(text => Math.Abs(text.FontSize - 47) < .01), "tile preview applies title size immediately");
        _settings.TileTitleSize = 29;
        ShowSettingsPage("AppearanceBackground");
        _settings.SharedBackgroundEnabled = false; _settings.LayoutMode = "Split";
        _settings.ClockBackground.Color = "#123456"; ApplyExtendedSettings(); await NextLayoutAsync();
        check(FindVisuals<BackgroundVisual>(_appearancePreviews["Clock"]).Any(background => background.Background is SolidColorBrush brush && brush.Color.R == 0x12),
            "clock preview applies background color immediately");
        check(FindVisuals<TextBlock>(_appearancePreviews["Clock"]).Any(text => text.Text == MainTimeText.Text), "clock preview uses the live time");
        _settings.ClockBackground.Color = "";
        ShowSettingsPage("AppearanceToolbar");
        var positionButtons = FindVisuals<RadioButton>(_settingsPages["AppearanceToolbar"].Content)
            .Where(button => button.GroupName == "ToolbarPosition").ToList();
        check(positionButtons.Count == 8, "toolbar screen picker retains all eight positions");
        foreach (RadioButton positionButton in positionButtons)
        {
            positionButton.IsChecked = true; await NextLayoutAsync();
            check(_settings.ToolbarPosition == (string)positionButton.Tag && positionButtons.Count(button => button.IsChecked == true) == 1,
                "toolbar screen picker selects " + positionButton.Tag);
        }
        // 极限尺寸下检查局部裁剪完整包含控制窗，而不是只检查控件属性。
        double originalToolbarScale = _settings.ToolbarScale;
        bool originalIconOnly = _settings.ToolbarIconOnly;
        _settings.ToolbarScale = 2; _settings.ToolbarIconOnly = false;
        foreach (string position in new[] { "TopLeft", "TopCenter", "TopRight", "CenterLeft", "CenterRight", "BottomLeft", "BottomCenter", "BottomRight" })
        {
            _settings.ToolbarPosition = position; ApplyExtendedSettings(); await NextLayoutAsync();
            Grid scene = _appearancePreviews["Toolbar"];
            Border preview = FindVisuals<Border>(scene).Single(border => border.Name == "ToolbarPreviewFrame");
            Point origin = preview.TransformToVisual(scene).TransformPoint(new Point());
            Point end = preview.TransformToVisual(scene).TransformPoint(new Point(preview.ActualWidth, preview.ActualHeight));
            check(origin.X >= -.5 && origin.Y >= -.5 && end.X <= scene.Width + .5 && end.Y <= scene.Height + .5,
                "zoomed toolbar fits preview at " + position);
            scene.StartBringIntoView(); await NextLayoutAsync();
            await SaveVisualAsync(scene, Path.Combine(output, "toolbar-crop-" + position + ".png"), 640, 300);
        }
        Border positionPicker = FindVisuals<Border>(_settingsPages["AppearanceToolbar"].Content).Single(border => border.Name == "ToolbarPositionPicker");
        positionPicker.StartBringIntoView(); await NextLayoutAsync();
        await SaveVisualAsync(positionPicker, Path.Combine(output, "toolbar-position-picker.png"), 640, 226);
        _settings.ToolbarScale = originalToolbarScale; _settings.ToolbarIconOnly = originalIconOnly;
        _settings.ToolbarPosition = "CenterLeft"; _settings.ToolbarRadius = 37; ApplyExtendedSettings(); await NextLayoutAsync();
        var toolbarPreview = FindVisuals<Border>(_appearancePreviews["Toolbar"]).Single(border => border.Name == "ToolbarPreviewFrame");
        check(toolbarPreview.HorizontalAlignment == HorizontalAlignment.Left && toolbarPreview.CornerRadius.TopLeft == 37,
            "toolbar preview follows position and corner radius");
        _settings.ToolbarRadius = 14; _settings.ToolbarPosition = "BottomCenter";
        _settings.ToolbarAutoHide = true; _settings.ToolbarAutoHideSeconds = 1; _settings.ToolbarHideAnimation = "Fade";
        ApplyExtendedSettings(); ShowBoard(); await NextLayoutAsync();
        _lastToolbarActivity = Environment.TickCount64 - 2000;
        await Task.Delay(650);
        check(_toolbarHidden && FloatingToolbar.Opacity < .01 && !FloatingToolbar.IsHitTestVisible, "idle timer fades the toolbar and disables invisible hit targets");
        RecordToolbarActivity(); await Task.Delay(400);
        check(!_toolbarHidden && FloatingToolbar.Opacity > .99 && FloatingToolbar.IsHitTestVisible, "activity restores toolbar opacity and interaction");
        _settings.ToolbarHideAnimation = "Fly"; ApplyExtendedSettings(); await NextLayoutAsync();
        _lastToolbarActivity = Environment.TickCount64 - 2000; await Task.Delay(650);
        check(_toolbarHidden && _toolbarTranslation.Y > 0 && FloatingToolbar.Opacity > .99 &&
            FloatingToolbar.TransformToVisual(RootShell).TransformPoint(new Point()).Y >= RootShell.ActualHeight,
            "fly animation moves the entire toolbar beyond the nearest bottom edge");
        RecordToolbarActivity(); await Task.Delay(400);
        check(!_toolbarHidden && Math.Abs(_toolbarTranslation.Y) < .01, "fly animation returns to the original toolbar position");
        EnterEditing(); _lastToolbarActivity = Environment.TickCount64 - 2000; await Task.Delay(200);
        check(!_toolbarHidden, "editing mode keeps the toolbar visible");
        FinishEditing(); ShowSettings(); _lastToolbarActivity = Environment.TickCount64 - 2000; await Task.Delay(200);
        check(!_toolbarHidden, "settings mode keeps the toolbar visible");
        _settings.ToolbarAutoHide = false;
        _settings.PauseNoiseWhenMinimized = true;
        var presenter = (OverlappedPresenter)_appWindow!.Presenter;
        presenter.Minimize(); await Task.Delay(400);
        check(_noiseSuspended && NoiseText.Text == "监测已暂停", "minimizing the real window suspends noise monitoring");
        UpdateNoiseDisplay(100); ProcessNoiseAlert(100);
        check(NoiseText.Text == "监测已暂停", "queued samples cannot overwrite suspended noise state");
        presenter.Restore(); await Task.Delay(400);
        check(!_noiseSuspended, "restoring the window clears noise suspension");
        _settings.PauseNoiseWhenMinimized = false;
        presenter.Minimize(); await Task.Delay(300);
        check(!_noiseSuspended, "minimizing with pause disabled leaves monitoring active");
        presenter.Restore(); ApplyExtendedSettings(); await NextLayoutAsync();
    }

    private async Task NextLayoutAsync()
    {
        TaskCompletionSource ready = new();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { RootShell.UpdateLayout(); ready.TrySetResult(); });
        await ready.Task;
        await Task.Delay(150);
    }

    private static async Task SaveVisualAsync(UIElement visual, string path, int width, int height)
    {
        RenderTargetBitmap bitmap = new(); await bitmap.RenderAsync(visual, width, height);
        using var memory = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync(); memory.Seek(0);
        using Stream source = memory.AsStreamForRead(); using FileStream target = File.Create(path); await source.CopyToAsync(target);
    }

    private static IEnumerable<T> FindVisuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisuals<T>(child)) yield return descendant;
        }
    }
}
