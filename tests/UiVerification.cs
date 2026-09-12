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
                await VerifyFloatingIslandsAsync(output, Check);
                PaletteComboBox.SelectedIndex = 0;
                ShowSettings(); AppearanceNavigationGroup.IsExpanded = false; AboutNavigationGroup.IsExpanded = true;
                SettingsRoot.SelectedItem = AboutNavigationGroup; await NextLayoutAsync();
                var settingsSplitView = FindVisuals<SplitView>(SettingsRoot).First(view => view.Name == "RootSplitView");
                Check(settingsSplitView.CornerRadius == new CornerRadius(0), "settings navigation pane joins the content with square corners");
                await SaveVisualAsync(RootShell, Path.Combine(output, "about.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
                await VerifyExtendedSettingsAsync(output, Check);
                await VerifyPresentationSettingsAsync(output, Check);
                await VerifyAutofillAsync(output, Check);
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

    /// <summary>
    /// 补全候选的真实按键验收：上下键切换、Tab/回车采纳、Esc 关闭。
    /// 真实按键要经系统输入队列投递，窗口必须在前台，因此单独作为可选的验证模式运行。
    /// </summary>
    internal void ScheduleAutofillKeyVerification()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS: " + message); }
                _settings.AutoUpdateEnabled = false;
                _settings.Autofill.Subject.Enabled = true;
                _settings.Autofill.Subject.MatchLevel = "Normal";
                _library = new ProjectLibrary { Settings = _settings };
                ProjectStore.Create(_library, false).Name = "补全探针";
                ViewModel.ReplaceSubjects([]);
                SubjectBoard probeSubject = ViewModel.AddSubject("语文");
                probeSubject.X = 24;
                probeSubject.Y = 24;
                probeSubject.TileWidth = 420;
                probeSubject.TileHeight = 320;
                probeSubject.Entries.Add(new HomeworkEntry { Content = "完成 " });
                BuildTiles();
                ShowBoard();
                EnterEditing();
                await NextLayoutAsync();
                SubjectTileControl tile = BoardCanvas.Children.OfType<SubjectTileControl>().First();
                TextBox box = FindVisuals<TextBox>(tile).First();
                nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ForceForeground(hwnd);
                await Task.Delay(600);
                Check(GetForegroundWindow() == hwnd, "the verification window holds the foreground so real keys reach it");

                string State(string label) =>
                    $"{label}: popup={_autofillPopup.IsOpen} count={_autofillPopup.Count} highlight={_autofillPopup.Highlighted?.Text} text='{box.Text}' caret={box.SelectionStart} focus={Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootShell.XamlRoot)?.GetType().Name}";

                async Task ArmAsync(string typed)
                {
                    box.Focus(FocusState.Programmatic);
                    box.Text = string.Empty;
                    await Task.Delay(60);
                    box.SelectedText = typed;
                    box.SelectionStart = box.Text.Length;
                    box.SelectionLength = 0;
                    await NextLayoutAsync();
                }

                // 上下键一次按键只能切换一格：曾出现同一处理函数同时挂在 Preview 与冒泡事件上，
                // 一次按键先加一又立刻减一，看起来完全切不动，这里逐次核对。
                await ArmAsync("sh");
                evidence.Add(State("arm"));
                Check(_autofillPopup.IsOpen && _autofillPopup.Count == 2, "typing 'sh' lists two subject candidates");
                string first = _autofillPopup.Highlighted?.Text ?? string.Empty;
                SendKey(Windows.System.VirtualKey.Down);
                await Task.Delay(400);
                evidence.Add(State("down"));
                string second = _autofillPopup.Highlighted?.Text ?? string.Empty;
                Check(first.Length > 0 && second != first, "Down moves the highlight to the next candidate");
                SendKey(Windows.System.VirtualKey.Down);
                await Task.Delay(400);
                evidence.Add(State("down2"));
                Check(_autofillPopup.Highlighted?.Text == first, "Down again cycles to the first candidate");
                SendKey(Windows.System.VirtualKey.Up);
                await Task.Delay(400);
                evidence.Add(State("up"));
                Check(_autofillPopup.Highlighted?.Text == second, "Up moves the highlight back");
                SendKey(Windows.System.VirtualKey.Escape);
                await Task.Delay(400);
                evidence.Add(State("escape"));
                Check(!_autofillPopup.IsOpen && box.Text == "sh", "Escape closes the popup without touching the typed text");

                await ArmAsync("sh");
                string tabTarget = _autofillPopup.Highlighted?.Text ?? string.Empty;
                SendKey(Windows.System.VirtualKey.Tab);
                await Task.Delay(400);
                evidence.Add(State("tab"));
                Check(!_autofillPopup.IsOpen && box.Text == tabTarget, $"Tab accepts the highlighted candidate (text='{box.Text}')");
                await Task.Delay(1600);

                await ArmAsync("sh");
                string enterTarget = _autofillPopup.Highlighted?.Text ?? string.Empty;
                SendKey(Windows.System.VirtualKey.Enter);
                await Task.Delay(400);
                evidence.Add(State("enter"));
                Check(!_autofillPopup.IsOpen && box.Text == enterTarget, $"Enter accepts the highlighted candidate (text='{box.Text}')");
                await Task.Delay(1600);

                _settings.Autofill.Homework.Enabled = true;
                _settings.Autofill.Homework.Items.Add(new HomeworkSuggestion
                {
                    Text = "同步练习册", Source = AutofillService.ManualSource, IsGlobal = true, Promoted = true,
                    Count = 3, FirstSeenAt = DateTime.Now, LastSeenAt = DateTime.Now
                });
                await NextLayoutAsync();
                tile = BoardCanvas.Children.OfType<SubjectTileControl>().First();
                evidence.Add($"homework setup: editing={_isEditing} tiles={BoardCanvas.Children.OfType<SubjectTileControl>().Count()} editors={FindVisuals<RichEditBox>(tile).Count()} entries={ViewModel.Subjects.First().Entries.Count}");
                RichEditBox editor = FindVisuals<RichEditBox>(tile).First();
                string EditorText()
                {
                    editor.Document.GetText(TextGetOptions.None, out string content);
                    return content.Replace("\r", "|");
                }
                async Task ArmEditorAsync(string typed)
                {
                    editor.Focus(FocusState.Programmatic);
                    editor.Document.SetText(TextSetOptions.None, "完成 ");
                    editor.Document.Selection.SetRange(3, 3);
                    editor.Document.Selection.TypeText(typed);
                    editor.Document.Selection.SetRange(3 + typed.Length, 3 + typed.Length);
                    await NextLayoutAsync();
                }
                string EditorState(string label) =>
                    $"{label}: popup={_autofillPopup.IsOpen} count={_autofillPopup.Count} highlight={_autofillPopup.Highlighted?.Text} text='{EditorText()}' focus={Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootShell.XamlRoot)?.GetType().Name}";

                await ArmEditorAsync("同步");
                evidence.Add(EditorState("hw-arm"));
                Check(_autofillPopup.IsOpen, "typing a recorded homework name lists candidates");
                SendKey(Windows.System.VirtualKey.Tab);
                await Task.Delay(400);
                evidence.Add(EditorState("hw-tab"));
                Check(!_autofillPopup.IsOpen && EditorText() == "完成 同步练习册|", $"Tab accepts a homework candidate (text='{EditorText()}')");
                await Task.Delay(1600);
                await ArmEditorAsync("同步");
                SendKey(Windows.System.VirtualKey.Enter);
                await Task.Delay(400);
                evidence.Add(EditorState("hw-enter"));
                Check(!_autofillPopup.IsOpen && EditorText() == "完成 同步练习册|", $"Enter accepts a homework candidate (text='{EditorText()}')");
                await Task.Delay(1600);

                evidence.Add("AUTOFILL_KEY_VERIFICATION_OK");
            }
            catch (Exception ex)
            {
                evidence.Add("AUTOFILL_KEY_VERIFICATION_FAILED\n" + ex);
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "autofill-key-result.txt"), evidence);
                Close();
            }
        };
    }

    /// <summary>
    /// 输入法组合态诊断：切到中文输入法后真实投递拼音按键，逐项记录组合事件、输入框文本、
    /// 候选浮层状态以及按键是否到达应用，用来确定组合态下应用实际能看到什么。
    /// </summary>
    internal void ScheduleAutofillImeDiagnostic()
    {
        RootShell.Loaded += async (_, _) =>
        {
            string output = Path.Combine(AppContext.BaseDirectory, "verification");
            Directory.CreateDirectory(output);
            List<string> evidence = [];
            try
            {
                _settings.AutoUpdateEnabled = false;
                _settings.Autofill.Subject.Enabled = true;
                _settings.Autofill.Subject.MatchLevel = "Normal";
                _settings.Autofill.Homework.Enabled = false;
                _library = new ProjectLibrary { Settings = _settings };
                ProjectStore.Create(_library, false).Name = "输入法探针";
                ViewModel.ReplaceSubjects([]);
                SubjectBoard probeSubject = ViewModel.AddSubject("语文");
                probeSubject.X = 24;
                probeSubject.Y = 24;
                probeSubject.TileWidth = 420;
                probeSubject.TileHeight = 320;
                BuildTiles();
                ShowBoard();
                EnterEditing();
                await NextLayoutAsync();

                SubjectTileControl tile = BoardCanvas.Children.OfType<SubjectTileControl>().First();
                TextBox box = FindVisuals<TextBox>(tile).First();
                nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                void Check(bool condition, string message)
                {
                    if (!condition) throw new Exception(message);
                    evidence.Add("PASS: " + message);
                }

                int started = 0, ended = 0, changed = 0;
                string Text() => (box.Text ?? string.Empty).Replace("\r", "|").Replace("\n", "|");
                box.TextCompositionStarted += (_, args) => { started++; evidence.Add($"event composition started start={args.StartIndex} length={args.Length}"); };
                box.TextCompositionChanged += (_, args) => { changed++; evidence.Add($"event composition changed start={args.StartIndex} length={args.Length} text='{Text()}'"); };
                box.TextCompositionEnded += (_, args) => { ended++; evidence.Add($"event composition ended start={args.StartIndex} length={args.Length} text='{Text()}'"); };
                box.AddHandler(UIElement.PreviewKeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) => evidence.Add($"box PreviewKeyDown key={e.Key}({(int)e.Key}) handled={e.Handled}")), true);
                box.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) => evidence.Add($"box KeyDown key={e.Key}({(int)e.Key}) handled={e.Handled}")), true);
                RootShell.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) => evidence.Add($"root KeyDown key={e.Key}({(int)e.Key}) handled={e.Handled}")), true);
                box.TextChanged += (_, _) => evidence.Add($"event text changed text='{Text()}' caret={box.SelectionStart} popup={_autofillPopup.IsOpen} count={_autofillPopup.Count}");
                void State(string label) => evidence.Add(
                    $"{label}: text='{Text()}' caret={box.SelectionStart} popup={_autofillPopup.IsOpen} count={_autofillPopup.Count} " +
                    $"highlight='{_autofillPopup.Highlighted?.Text}' focus={Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootShell.XamlRoot)?.GetType().Name} " +
                    $"foreground={GetForegroundWindow() == hwnd} layout=0x{GetKeyboardLayout(0):X8} composition={started}/{changed}/{ended}");

                // 每次投递按键前都重新置顶，避免按键落到当时的其它窗口里。
                async Task<bool> ReadyAsync()
                {
                    ForceForeground(hwnd);
                    await Task.Delay(250);
                    if (GetForegroundWindow() != hwnd)
                    {
                        evidence.Add("skip: window lost foreground before sending keys");
                        return false;
                    }

                    // 先置顶再把焦点交回输入框：Win32 层的 SetFocus 会打断 XAML 的编辑焦点。
                    box.Focus(FocusState.Programmatic);
                    await Task.Delay(150);
                    return true;
                }

                // 中文输入法的组合内容是否落在窗口的 IMM 上下文里，决定应用能不能读到拼音。
                void LogComposition(string label)
                {
                    var description = new System.Text.StringBuilder(260);
                    ImmGetDescription(GetKeyboardLayout(0), description, 260);
                    nint imc = ImmGetContext(hwnd);
                    if (imc == nint.Zero)
                    {
                        evidence.Add($"{label}: no IMM context, ime='{description}'");
                        return;
                    }

                    var composition = new System.Text.StringBuilder(260);
                    int length = ImmGetCompositionString(imc, 0x0008 /* GCS_COMPSTR */, composition, 260 * 2);
                    var result = new System.Text.StringBuilder(260);
                    int resultLength = ImmGetCompositionString(imc, 0x8005 /* GCS_RESULTSTR */, result, 260 * 2);
                    evidence.Add(
                        $"{label}: ime='{description}' open={ImmGetOpenStatus(imc)} composing='{composition}' ({length}) result='{result}' ({resultLength})");
                    ImmReleaseContext(hwnd, imc);
                }

                // 组合期间窗口消息里是否还有 WM_KEYDOWN / WM_IME_*，决定应用层能不能截到被输入法占用的按键。
                nint previousProcedure = GetWindowLongPtr(hwnd, -4 /* GWLP_WNDPROC */);
                WindowProcedure procedure = (window, message, wparam, lparam) =>
                {
                    if (message is 0x0100 or 0x0101 or 0x0104 or 0x0105 or 0x010D or 0x010E or 0x010F or 0x0281)
                    {
                        evidence.Add($"window message=0x{message:X4} wparam=0x{wparam:X} lparam=0x{lparam:X}");
                    }

                    return CallWindowProc(previousProcedure, window, message, wparam, lparam);
                };
                SetWindowLongPtr(hwnd, -4, System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(procedure));

                // 先在不带输入法的状态下确认按键投递本身可用（对照组）。
                box.Text = string.Empty;
                if (await ReadyAsync())
                {
                    SendLetter('a');
                    SendLetter('b');
                    await Task.Delay(300);
                    State("plain-typing");
                }

                nint original = GetKeyboardLayout(0);
                nint chinese = LoadKeyboardLayout("00000804", 1);
                PostMessage(hwnd, 0x0050, nint.Zero, chinese);
                await Task.Delay(600);
                nint context = ImmGetContext(hwnd);
                if (context != nint.Zero)
                {
                    ImmGetConversionStatus(context, out uint conversion, out uint sentence);
                    evidence.Add($"ime: layout=0x{GetKeyboardLayout(0):X8} conversion=0x{conversion:X4} sentence=0x{sentence:X4}");
                    ImmSetConversionStatus(context, 0x0001, sentence);
                    ImmGetConversionStatus(context, out conversion, out _);
                    evidence.Add($"ime: native mode conversion=0x{conversion:X4}");
                    ImmReleaseContext(hwnd, context);
                }
                else
                {
                    evidence.Add("ime: ImmGetContext returned null");
                }

                // 空白标题会列出推荐学科，先看输入法已激活但还没开始组合时按键能否到达应用。
                box.Text = string.Empty;
                await NextLayoutAsync();
                if (await ReadyAsync())
                {
                    State("ime-idle");
                    SendKey(Windows.System.VirtualKey.Down);
                    await Task.Delay(400);
                    State("ime-idle-down");
                    SendKey(Windows.System.VirtualKey.Tab);
                    await Task.Delay(400);
                    State("ime-idle-tab");
                }

                box.Text = string.Empty;
                await NextLayoutAsync();
                if (await ReadyAsync())
                {
                    State("armed");
                    foreach (char letter in "yuwen")
                    {
                        SendLetter(letter);
                        await Task.Delay(300);
                        State($"typed-{letter}");
                    }

                    LogComposition("after-typing");
                    Check(_autofillPopup.IsOpen && _autofillPopup.Highlighted?.Text is { Length: > 0 },
                        "输入法组合期间按拼音给出学科候选");
                    SendKey(Windows.System.VirtualKey.Down);
                    await Task.Delay(400);
                    State("down");
                    SendKey(Windows.System.VirtualKey.Tab);
                    await Task.Delay(400);
                    State("tab");

                    string highlighted = _autofillPopup.Highlighted?.Text ?? string.Empty;
                    if (_autofillPopup.IsOpen) _autofillPopup.ChooseHighlighted();
                    await Task.Delay(400);
                    State($"after-choose('{highlighted}')");

                    Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(
                        Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next,
                        new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = RootShell });
                    await Task.Delay(800);
                    State("after-blur");
                    await Task.Delay(1500);
                    State("settled");
                    Check(highlighted.Length > 0 && Text() == highlighted,
                        $"输入法把组合内容上屏后仍保留采纳的候选（text='{Text()}'）");
                }

                // 输入法上屏后再用键盘采纳：空格上屏学科名后，浮层应给出同名候选，Tab 直接采纳。
                box.Text = string.Empty;
                await NextLayoutAsync();
                if (await ReadyAsync())
                {
                    foreach (char letter in "yuwen")
                    {
                        SendLetter(letter);
                        await Task.Delay(120);
                    }

                    await Task.Delay(300);
                    SendKey(Windows.System.VirtualKey.Space);
                    await Task.Delay(700);
                    State("committed");
                    bool named = _settings.Autofill.Subject.Subjects.Any(item => item.Name == Text());
                    Check(!named || _autofillPopup.IsOpen, "输入法上屏学科名后仍然给出该候选");
                    if (_autofillPopup.IsOpen)
                    {
                        SendKey(Windows.System.VirtualKey.Tab);
                        await Task.Delay(500);
                        State("committed-tab");
                        Check(!_autofillPopup.IsOpen &&
                            Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(RootShell.XamlRoot) is TextBox,
                            "上屏后用 Tab 采纳学科候选并留在输入框里");
                    }
                }

                PostMessage(hwnd, 0x0050, nint.Zero, original);
                SetWindowLongPtr(hwnd, -4, previousProcedure);
                GC.KeepAlive(procedure);
                evidence.Add("AUTOFILL_IME_DIAGNOSTIC_OK");
            }
            catch (Exception ex)
            {
                evidence.Add("AUTOFILL_IME_DIAGNOSTIC_FAILED\n" + ex);
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "ime-result.txt"), evidence);
                Close();
            }
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attach1);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint LoadKeyboardLayout(string layout, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern nint ImmGetContext(nint window);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(nint window, nint context);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmGetConversionStatus(nint context, out uint conversion, out uint sentence);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmSetConversionStatus(nint context, uint conversion, uint sentence);

    [System.Runtime.InteropServices.DllImport("imm32.dll")]
    private static extern bool ImmGetOpenStatus(nint context);

    [System.Runtime.InteropServices.DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ImmGetCompositionString(nint context, int index, System.Text.StringBuilder? buffer, int length);

    [System.Runtime.InteropServices.DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint ImmGetDescription(nint layout, System.Text.StringBuilder? buffer, uint length);

    private delegate nint WindowProcedure(nint window, uint message, nint wparam, nint lparam);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint previous, nint window, uint message, nint wparam, nint lparam);

    // 前台锁定会拒绝后台进程的置顶请求，先挂到当前前台线程再激活，否则按键不会进本窗口。
    private static void ForceForeground(nint window)
    {
        uint current = GetCurrentThreadId();
        nint foreground = GetForegroundWindow();
        uint foregroundThread = foreground == nint.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        if (foregroundThread != 0 && foregroundThread != current) AttachThreadInput(current, foregroundThread, true);
        ShowWindow(window, 9);
        SetForegroundWindow(window);
        SetFocus(window);
        if (foregroundThread != 0 && foregroundThread != current) AttachThreadInput(current, foregroundThread, false);
    }

    // 只描述按键输入，字段偏移与 native INPUT/KEYBDINPUT 在 x64 下的布局一致。
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [System.Runtime.InteropServices.FieldOffset(0)] public uint Type;
        [System.Runtime.InteropServices.FieldOffset(8)] public ushort VirtualKey;
        [System.Runtime.InteropServices.FieldOffset(10)] public ushort ScanCode;
        [System.Runtime.InteropServices.FieldOffset(12)] public uint Flags;
    }

    private static void SendKey(Windows.System.VirtualKey key)
    {
        SendInput(1, [new INPUT { Type = 1, VirtualKey = (ushort)key }], 40);
        Thread.Sleep(30);
        SendInput(1, [new INPUT { Type = 1, VirtualKey = (ushort)key, Flags = 2 }], 40);
        Thread.Sleep(30);
    }

    /// <summary>按真实键盘那样投递一个字母（带扫描码），输入法才会把它当成正常按键处理。</summary>
    private static void SendLetter(char letter)
    {
        ushort key = (ushort)char.ToUpperInvariant(letter);
        ushort scan = (ushort)MapVirtualKey(key, 0);
        SendInput(1, [new INPUT { Type = 1, VirtualKey = key, ScanCode = scan }], 40);
        Thread.Sleep(20);
        SendInput(1, [new INPUT { Type = 1, VirtualKey = key, ScanCode = scan, Flags = 2 }], 40);
        Thread.Sleep(20);
    }

    // RTF 字体表中的家族名，用于定位字体在哪一步丢失。
    private static string RtfFonts(string rtf) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(rtf, @"\\f\d+\\fnil\s*([^;]+);").Select(m => m.Groups[1].Value.Trim()));

    /// <summary>
    /// 悬浮岛验收：编辑模式下富文本工具贴在控制窗左侧、画笔模式换成画笔控件，竖版控制窗时改到上方，
    /// 两块窗口与控制窗等高（竖版等宽）；缩放岛在另一侧并能真正改动作业板缩放比例。
    /// </summary>
    private async Task VerifyFloatingIslandsAsync(string output, Action<bool, string> check)
    {
        Rect Bounds(FrameworkElement element) => element.TransformToVisual(RootShell)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        static void Invoke(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new
            Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        // 富文本工具条由编辑器的聚焦事件提供，聚焦后需要等它真正挂到悬浮岛上。
        async Task FocusEditorAsync()
        {
            RichEditBox box = FindVisuals<RichEditBox>(BoardCanvas).First();
            for (int attempt = 0; attempt < 8 && RichTextIsland.Visibility != Visibility.Visible; attempt++)
            {
                box.Focus(FocusState.Programmatic);
                box.Document.Selection.SetRange(0, 1);
                await NextLayoutAsync();
            }
        }

        _settings.LayoutMode = "Split"; _settings.ToolbarPosition = "BottomCenter";
        _settings.ToolbarScale = 1; _settings.ToolbarIconOnly = true; _settings.ToolbarRadius = 14;
        // 悬浮岛按教室大屏尺寸验收：窗口太小时浮层会按设计收窄并在岛内滚动，判据要与真实使用场景一致。
        DisplayArea islandWorkArea = DisplayArea.GetFromWindowId(_appWindow!.Id, DisplayAreaFallback.Primary);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(islandWorkArea.WorkArea.Width, islandWorkArea.WorkArea.Height));
        ShowBoard(); EnterEditing(); ApplyExtendedSettings(); await NextLayoutAsync();
        await FocusEditorAsync();
        Rect toolbar = Bounds(FloatingToolbar);
        Rect island = Bounds(RichTextIsland);
        check(RichTextIsland.Visibility == Visibility.Visible, "editing mode hosts the rich text island");
        check(island.Right <= toolbar.Left + .5,
            $"rich text island sits left of the control window ({island.Right:0.#} <= {toolbar.Left:0.#})");
        check(Math.Abs(island.Height - toolbar.Height) < .5,
            $"rich text island matches the control window height ({island.Height:0.#} / {toolbar.Height:0.#})");
        check(RichTextIsland.CornerRadius.TopLeft == FloatingToolbar.CornerRadius.TopLeft &&
            Math.Abs(RichTextIsland.Padding.Left - FloatingToolbar.Padding.Left) < .01 && RichTextIsland.Background is not null,
            "rich text island inherits the control window corner, padding and background");
        check(RichTextIsland.BorderThickness.Left >= 1, "rich text island keeps a visible outline");
        await SaveVisualAsync(RootShell, Path.Combine(output, "islands-editor.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);

        // 关闭无字模式后按钮名称必须排在图标下方，并保持与控制窗相同的高度。
        _settings.ToolbarIconOnly = false; ApplyExtendedSettings(); await NextLayoutAsync();
        Button boldButton = FindVisuals<Button>(RichTextToolbarHost).First(b => ToolTipService.GetToolTip(b)?.ToString() == "加粗");
        StackPanel boldContent = (StackPanel)boldButton.Content;
        TextBlock boldLabel = boldContent.Children.OfType<TextBlock>().First();
        Rect iconBounds = Bounds((FrameworkElement)boldContent.Children[0]);
        Rect labelBounds = Bounds(boldLabel);
        check(boldLabel.Visibility == Visibility.Visible && boldLabel.Text == "加粗", "shown island button carries its name");
        check(labelBounds.Top >= iconBounds.Bottom - .5,
            $"island button name stays below its icon ({labelBounds.Top:0.#} >= {iconBounds.Bottom:0.#})");
        check(Math.Abs(boldButton.ActualHeight - 64) < .5 && Math.Abs(RichTextIsland.ActualHeight - FloatingToolbar.ActualHeight) < .5,
            "labeled island keeps the control window height");
        _settings.ToolbarIconOnly = true; ApplyExtendedSettings(); await NextLayoutAsync();

        // 画笔模式：富文本岛让位给画笔控件，位置和高度保持一致。
        GlobalPenButton.IsChecked = true; ApplyExtendedSettings(); await NextLayoutAsync();
        Rect penToolbar = Bounds(GlobalInkToolbar);
        toolbar = Bounds(FloatingToolbar);
        check(RichTextIsland.Visibility == Visibility.Collapsed && GlobalInkToolbar.Visibility == Visibility.Visible,
            "pen mode swaps the rich text island for the pen toolbar");
        check(penToolbar.Right <= toolbar.Left + .5, "pen toolbar takes over the slot left of the control window");
        check(Math.Abs(penToolbar.Height - toolbar.Height) < .5,
            $"pen toolbar matches the control window height ({penToolbar.Height:0.#} / {toolbar.Height:0.#})");
        await SaveVisualAsync(RootShell, Path.Combine(output, "islands-pen.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        GlobalPenButton.IsChecked = false; ApplyExtendedSettings(); await NextLayoutAsync();

        // 缩放岛：只在编辑模式出现，滑条与档位按钮都要真正改动作业板缩放。
        List<Button> zoomButtons = ZoomIslandItems.Children.OfType<Button>().ToList();
        Slider zoomSlider = ZoomIslandItems.Children.OfType<Slider>().Single();
        toolbar = Bounds(FloatingToolbar);
        Rect zoom = Bounds(ZoomIsland);
        Rect slider = Bounds(zoomSlider);
        check(ZoomIsland.Visibility == Visibility.Visible && zoomButtons.Count == 3 && zoomSlider.Visibility == Visibility.Visible,
            "editing mode offers the board zoom island with three buttons and a slider");
        check(zoom.Left >= toolbar.Right - .5, $"zoom island sits right of the control window ({zoom.Left:0.#} >= {toolbar.Right:0.#})");
        check(Math.Abs(zoom.Height - toolbar.Height) < .5,
            $"zoom island matches the control window height ({zoom.Height:0.#} / {toolbar.Height:0.#})");
        check(slider.Width > 0 && ZoomIslandItems.ActualWidth >= slider.Width - .5,
            $"zoom slider is laid out inside the island content ({slider.Width:0.#} / {ZoomIslandItems.ActualWidth:0.#})");
        // 窗口过窄时浮层按既有规则收窄并在岛内滚动，这里只在放得下时要求滑条完整可见。
        if (zoom.Width >= ZoomIslandItems.ActualWidth - .5)
        {
            check(slider.Left >= zoom.Left - .5 && slider.Right <= zoom.Right + .5,
                $"wide window keeps the slider inside the visible island ({slider.Left:0.#}..{slider.Right:0.#} in {zoom.Left:0.#}..{zoom.Right:0.#})");
        }
        double initialZoom = BoardScroller.ZoomFactor;
        Invoke(zoomButtons[2]); await NextLayoutAsync();
        check(BoardScroller.ZoomFactor > initialZoom + .001,
            $"zoom in enlarges the board ({initialZoom:0.##} -> {BoardScroller.ZoomFactor:0.##})");
        double zoomed = BoardScroller.ZoomFactor;
        Invoke(zoomButtons[0]); await NextLayoutAsync();
        check(BoardScroller.ZoomFactor < zoomed - .001, "zoom out shrinks the board again");
        Invoke(zoomButtons[1]); await NextLayoutAsync();
        check(Math.Abs(BoardScroller.ZoomFactor - 1) < .001 && Math.Abs(BoardZoom - 1) < .001,
            "the percentage button restores 100%");
        check(_zoomLevelText?.Text == "100%", "the zoom island shows the current percentage");
        // 拖动滑条连续缩放，比例文字与档位按钮同步；点比例按钮时滑条也要回到 100%。
        zoomSlider.Value = 220; await NextLayoutAsync();
        check(Math.Abs(BoardScroller.ZoomFactor - 2.2) < .01 && Math.Abs(BoardZoom - 2.2) < .01 && _zoomLevelText?.Text == "220%",
            $"the zoom slider scales the board ({BoardScroller.ZoomFactor:0.##} / {_zoomLevelText?.Text})");
        Invoke(zoomButtons[1]); await NextLayoutAsync();
        check(Math.Abs(zoomSlider.Value - 100) < .01 && Math.Abs(BoardScroller.ZoomFactor - 1) < .001,
            "restoring 100% also moves the slider back");
        // 查看模式：缩放岛收起，但缩放结果保留，作业板仍可平移。
        SetBoardZoom(1.5); await NextLayoutAsync();
        FinishEditing(); await NextLayoutAsync();
        check(ZoomIsland.Visibility == Visibility.Collapsed, "view mode hides the zoom island");
        check(Math.Abs(BoardScroller.ZoomFactor - 1.5) < .01 && BoardScroller.ZoomMode == ZoomMode.Enabled,
            $"view mode keeps the applied zoom and stays pannable ({BoardScroller.ZoomFactor:0.##})");
        EnterEditing(); ApplyExtendedSettings(); await NextLayoutAsync();
        await FocusEditorAsync();
        foreach (string mode in new[] { "Split", "Board", "Free" })
        {
            _settings.LayoutMode = mode; ApplyExtendedSettings(); await NextLayoutAsync();
            check(ZoomIsland.Visibility == Visibility.Visible, mode + " keeps the board zoom island");
            await FocusEditorAsync();
            Rect modeToolbar = Bounds(FloatingToolbar), modeIsland = Bounds(RichTextIsland);
            check(RichTextIsland.Visibility == Visibility.Visible && modeIsland.Right <= modeToolbar.Left + .5,
                mode + " keeps the rich text island left of the control window");
        }
        _settings.LayoutMode = "Clock"; ApplyExtendedSettings(); await NextLayoutAsync();
        check(ZoomIsland.Visibility == Visibility.Collapsed, "clock-only layout hides the board zoom island");
        check(RichTextIsland.Visibility == Visibility.Collapsed, "clock-only layout hides the rich text island");

        // 竖版控制窗：浮岛改到控制窗上方、缩放岛改到下方，宽度与控制窗一致。
        // 屏幕高度不足时按设计退回左右，此时只要求浮岛同样贴边、尺寸与控制窗对齐。
        DisplayArea workArea = DisplayArea.GetFromWindowId(_appWindow!.Id, DisplayAreaFallback.Primary);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(workArea.WorkArea.Width, workArea.WorkArea.Height));
        await NextLayoutAsync();
        _settings.LayoutMode = "Split"; _settings.ToolbarPosition = "CenterLeft";
        ApplyExtendedSettings(); await NextLayoutAsync(); await FocusEditorAsync();
        toolbar = Bounds(FloatingToolbar); island = Bounds(RichTextIsland); zoom = Bounds(ZoomIsland);
        check(RichTextIsland.Visibility == Visibility.Visible && GlobalInkToolbar.Visibility == Visibility.Collapsed,
            "vertical editing mode keeps the rich text island on screen");
        bool islandAlignedVertically = island.Bottom <= toolbar.Top + .5 || island.Top >= toolbar.Bottom - .5;
        bool zoomAlignedVertically = zoom.Top >= toolbar.Bottom - .5 || zoom.Bottom <= toolbar.Top + .5;
        double horizontalCross = ToolbarContentSize(1) + 7 * 2 + IslandBorderThickness * 2;
        check(islandAlignedVertically
                ? Math.Abs(island.Width - toolbar.Width) < .5
                : Math.Abs(island.Height - horizontalCross) < .5,
            $"vertical island matches the control window and stays clear of it (island {island.X:0.#},{island.Y:0.#} {island.Width:0.#}x{island.Height:0.#}" +
            $" / toolbar {toolbar.X:0.#},{toolbar.Y:0.#} {toolbar.Width:0.#}x{toolbar.Height:0.#} / root {RootShell.ActualWidth:0.#}x{RootShell.ActualHeight:0.#})");
        check(islandAlignedVertically || island.Right <= toolbar.Left + .5 || island.Left >= toolbar.Right + .5,
            "vertical island never overlaps the control window");
        check(zoomAlignedVertically || zoom.Right <= toolbar.Left + .5 || zoom.Left >= toolbar.Right + .5,
            "vertical zoom island stays clear of the control window");
        check(!islandAlignedVertically || island.Bottom <= toolbar.Top + .5,
            "vertical editing tools stay above the control window");
        check(!zoomAlignedVertically || zoom.Top >= toolbar.Bottom - .5,
            "vertical zoom island stays below the control window");
        check(zoomAlignedVertically ? Math.Abs(zoom.Width - toolbar.Width) < .5 : Math.Abs(zoom.Height - horizontalCross) < .5,
            $"vertical zoom island matches the control window cross size ({zoom.Width:0.#}x{zoom.Height:0.#})");
        check(zoomAlignedVertically ? zoomSlider.Orientation == Orientation.Vertical : zoomSlider.Orientation == Orientation.Horizontal,
            $"zoom slider follows the island direction ({zoomSlider.Orientation})");
        // 竖版靠挪动控制窗让位，浮岛保持完整尺寸，不应再出现岛内上下翻页的滚动条。
        check(!islandAlignedVertically || RichTextToolbar.ScrollableHeight <= 1.5,
            $"vertical editing tools fit without a pager ({RichTextToolbar.ScrollableHeight:0.#})");
        check(!zoomAlignedVertically || ZoomIslandScroll.ScrollableHeight <= 1.5,
            $"vertical zoom island fits without a pager ({ZoomIslandScroll.ScrollableHeight:0.#})");
        // 窗口高度不足时按设计退回左右排布，此时字体选择框仍是内联输入框，只在竖排时要求收成图标按钮。
        check(!islandAlignedVertically ||
            (FindVisuals<Button>(RichTextToolbarHost).Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "字体") &&
                !FindVisuals<AutoSuggestBox>(RichTextToolbarHost).Any()),
            "vertical island collapses the font picker into an icon button");
        // 画笔栏同样排在控制窗上方，缩放岛仍在下方。
        GlobalPenButton.IsChecked = true; ApplyExtendedSettings(); await NextLayoutAsync();
        Rect penIsland = Bounds(GlobalInkToolbar);
        toolbar = Bounds(FloatingToolbar); zoom = Bounds(ZoomIsland);
        check(penIsland.Bottom <= toolbar.Top + .5 && zoom.Top >= toolbar.Bottom - .5 && ZoomIslandScroll.ScrollableHeight <= 1.5,
            $"vertical pen toolbar stays above the control window with zoom below ({penIsland.Bottom:0.#} <= {toolbar.Top:0.#}, {zoom.Top:0.#} >= {toolbar.Bottom:0.#})" +
            $" [pen {penIsland.X:0.#},{penIsland.Y:0.#} {penIsland.Width:0.#}x{penIsland.Height:0.#} / toolbar {toolbar.X:0.#},{toolbar.Y:0.#} {toolbar.Width:0.#}x{toolbar.Height:0.#}" +
            $" / zoom {zoom.X:0.#},{zoom.Y:0.#} {zoom.Width:0.#}x{zoom.Height:0.#} / root {RootShell.ActualWidth:0.#}x{RootShell.ActualHeight:0.#} / pager {ZoomIslandScroll.ScrollableHeight:0.#}]");
        GlobalPenButton.IsChecked = false; ApplyExtendedSettings(); await NextLayoutAsync();
        await SaveVisualAsync(RootShell, Path.Combine(output, "islands-vertical.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);

        // 窗口高度不足时（例如 768p 的屏幕）竖版上下两块浮岛都放不下，必须退回左右排布：
        // 编辑工具不能掉到控制窗下方，浮岛也不能被挤出窗口。
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1024, 700));
        ApplyExtendedSettings(); await NextLayoutAsync(); await FocusEditorAsync();
        toolbar = Bounds(FloatingToolbar); island = Bounds(RichTextIsland); zoom = Bounds(ZoomIsland);
        check(island.Bottom <= toolbar.Top + .5 || island.Right <= toolbar.Left + .5 || island.Left >= toolbar.Right + .5,
            $"short window keeps the editing tools above or beside the control window (island {island.X:0.#},{island.Y:0.#} {island.Width:0.#}x{island.Height:0.#}" +
            $" / toolbar {toolbar.X:0.#},{toolbar.Y:0.#} {toolbar.Width:0.#}x{toolbar.Height:0.#})");
        check(zoom.Right <= RootShell.ActualWidth + .5 && zoom.Bottom <= RootShell.ActualHeight + .5,
            $"short window keeps the zoom island inside the window (zoom {zoom.X:0.#},{zoom.Y:0.#} {zoom.Width:0.#}x{zoom.Height:0.#}" +
            $" / root {RootShell.ActualWidth:0.#}x{RootShell.ActualHeight:0.#})");
        _appWindow.Resize(new Windows.Graphics.SizeInt32(workArea.WorkArea.Width, workArea.WorkArea.Height));
        ApplyExtendedSettings(); await NextLayoutAsync();

        SetBoardZoom(1);
        _settings.LayoutMode = "Split"; _settings.ToolbarPosition = "BottomCenter";
        _settings.ToolbarIconOnly = true; _settings.ToolbarScale = 1; _settings.ToolbarRadius = 14;
        ApplyExtendedSettings(); await NextLayoutAsync();
        check(FindVisuals<AutoSuggestBox>(RichTextToolbarHost).Any(),
            "horizontal island restores the inline font picker");
    }

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
                // 分屏与仅时钟都保留分隔滑条，再各加时钟、组件两层交互。
                int expectedLayers = 3;
                check(FreeLayoutHandles.Children.Count == expectedLayers, mode + " exposes constrained clock and component interactions while editing");
                var interactions = FreeLayoutHandles.Children.OfType<Grid>().ToList();
                check(interactions.Count == 2 && interactions.All(layer => layer.Children.OfType<Thumb>().Count() == 2),
                    mode + " allows dragging from the full clock and component surfaces");
                check(interactions.SelectMany(layer => layer.Children.OfType<Thumb>()).Count(thumb => thumb.Opacity == 0) == 2,
                    mode + " resize boxes stay hidden until pointer hover");
                check(interactions.All(layer => layer.Children.OfType<Border>().Count() == 1 && layer.Children.OfType<Border>().All(border => border.Opacity == 0)),
                    mode + " component selection borders stay hidden until pointer hover");
                var resizeThumbs = interactions.SelectMany(layer => layer.Children.OfType<Thumb>())
                    .Where(thumb => thumb.HorizontalAlignment == HorizontalAlignment.Right && thumb.VerticalAlignment == VerticalAlignment.Bottom).ToList();
                check(resizeThumbs.Count == interactions.Count && resizeThumbs.All(thumb => FindVisuals<FontIcon>(thumb).Any(icon => icon.Glyph == FluentGlyphs.ArrowDownRight)),
                    mode + " resize boxes render the down-right arrow glyph");
                // 秒数占位不能把大号时间挤偏：时间数字要和日期、组件栏共用同一条中轴线。
                Rect timeBounds = MainTimeText.TransformToVisual(ClockPanel)
                    .TransformBounds(new Rect(0, 0, MainTimeText.ActualWidth, MainTimeText.ActualHeight));
                check(Math.Abs(timeBounds.X + timeBounds.Width / 2 - ClockPanel.ActualWidth / 2) < 3,
                    mode + " time digits stay on the same center axis as the component row");
            }
            if (mode is "Clock" or "Board")
            {
                // 单区模式保留贴边分隔条，往回流方向拖动即可恢复分屏。
                Thumb edge = FreeLayoutHandles.Children.OfType<Thumb>().Single();
                check(mode == "Clock" ? Canvas.GetLeft(edge) > DisplayRoot.ActualWidth - 11 : Canvas.GetLeft(edge) == 0,
                    mode + " keeps an edge split handle for dragging back to split");
                BeginSplitDrag();
                DragSplitHandle(false, (mode == "Clock" ? -1 : 1) * DisplayRoot.ActualWidth * .4, 0);
                CompleteSplitDrag();
                await NextLayoutAsync();
                check(_settings.LayoutMode == "Split" && Math.Abs(_settings.SplitRatio - (mode == "Clock" ? .6 : .4)) < .01,
                    mode + " returns to split when the edge handle is dragged back");
                _settings.SplitRatio = .4; _settings.LayoutMode = mode;
                ApplyExtendedSettings(); ShowBoard(); EnterEditing(); await NextLayoutAsync();
            }
            if (mode == "Clock")
            {
                // 仅时钟模式没有作业板：编辑工具栏只留画笔等必要入口，画笔可在整块屏幕书写并写进项目。
                check(GlobalPenButton.Visibility == Visibility.Visible && AddSubjectButton.Visibility == Visibility.Collapsed &&
                    AutoArrangeButton.Visibility == Visibility.Collapsed && GridSnapToggleButton.Visibility == Visibility.Collapsed &&
                    DiscardEditButton.Visibility == Visibility.Visible,
                    "clock mode keeps only the pen and save/discard actions in the editing toolbar");
                check(FreeLayoutHandles.IsHitTestVisible && FreeLayoutHandles.Children.OfType<Thumb>().Count() == 1,
                    "clock mode keeps the edge split handle usable while drawing");
                GlobalPenButton.IsChecked = true;
                await NextLayoutAsync();
                check(ClockInkLayer.Visibility == Visibility.Visible && ClockInkLayer.IsHitTestVisible,
                    "clock mode exposes a full-screen ink layer while the pen is on");
                int inkBefore = ClockInkLayer.StrokeCount;
                ClockInkLayer.VerificationDrawStroke(
                [
                    new Point(30, 140), new Point(DisplayRoot.ActualWidth / 2, DisplayRoot.ActualHeight / 2),
                    new Point(DisplayRoot.ActualWidth - 40, DisplayRoot.ActualHeight - 80)
                ]);
                check(ClockInkLayer.StrokeCount == inkBefore + 1 && ClockInkLayer.Children.Count == inkBefore + 1,
                    "clock ink accepts a stroke across the whole screen and renders it");
                SaveStateNow();
                check(CurrentProject!.ClockInkStrokes.Count == inkBefore + 1,
                    "clock ink is written into the current project when saving");
                ClockInkLayer.ClearStrokes();
                check(ClockInkLayer.StrokeCount == 0 && ClockInkLayer.Children.Count == 0, "clearing clock ink empties the full-screen layer");
                GlobalPenButton.IsChecked = false;
                await NextLayoutAsync();
                check(!ClockInkLayer.IsHitTestVisible && AddSubjectButton.Visibility == Visibility.Collapsed,
                    "closing the pen ends clock writing without restoring board-only tools");
            }
            else
            {
                check(ClockInkLayer.Visibility == Visibility.Collapsed, mode + " hides the clock-only ink layer");
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
        check(SettingsRoot.MenuItems.OfType<NavigationViewItem>().Count(item => item.MenuItems.Count > 0) == 3,
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
        ComponentsNavigationGroup.IsExpanded = AutofillNavigationGroup.IsExpanded = AboutNavigationGroup.IsExpanded = false;
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
        // 退出全屏后窗口可能落在另一块缩放比例不同的屏幕上；先按当前屏幕工作区把窗口放到最大，
        // 让设置页有足够宽度，磁贴预览的右侧固定区才会生效（窄窗口按设计回退到页内预览）。
        DisplayArea area = DisplayArea.GetFromWindowId(_appWindow!.Id, DisplayAreaFallback.Primary);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(area.WorkArea.Width, area.WorkArea.Height));
        await Task.Delay(300);
        SettingsRoot.SelectedItem = AboutNavigationGroup;
        await NextLayoutAsync();
        check(AboutNavigationGroup.MenuItems.Count == 0 && SettingsPageTitle.Text == "关于" &&
            FindVisuals<Border>(_settingsPages["About"].Content).Contains(VersionSettingsCard) &&
            FindVisuals<Border>(_settingsPages["About"].Content).Contains(UpdateSettingsCard) &&
            FindVisuals<Grid>(_settingsPages["About"].Content).Contains(RepositorySettingsCard), "About combines version repositories and updates on one page");
        foreach (string page in new[] { "AppearanceTile", "AppearanceBackground", "AppearanceGrid", "AppearanceToolbar" })
        {
            ShowSettingsPage(page); await NextLayoutAsync();
            // 磁贴预览在宽窗口里固定在设置页右侧，向下滚动编辑选项时始终可见；其余预览仍在可滚动内容中。
            var previews = page == "AppearanceTile"
                ? FindVisuals<Border>(SettingsPreviewHost).Where(border => border.Name == "TileStickyAppearancePreview").ToList()
                : FindVisuals<Border>(_settingsPages[page].Content).Where(border => border.Name.EndsWith("AppearancePreview")).ToList();
            // 失败时给出窗口与预览区的实际尺寸，便于判断是固定区还是页内回退没有生效。
            check(SettingsPreviewHost.Visibility == (page == "AppearanceTile" ? Visibility.Visible : Visibility.Collapsed) &&
                previews.Count == (page == "AppearanceBackground" ? 3 : 1) &&
                previews.All(preview => preview.ActualWidth > 0 && preview.ActualHeight > 0),
                page + " has all requested live previews [" + SettingsPreviewHost.Visibility + " host=" + SettingsContentHost.ActualWidth +
                " root=" + RootShell.ActualWidth + "x" + RootShell.ActualHeight + " scale=" + (RootShell.XamlRoot?.RasterizationScale ?? 0) +
                " panel=" + SettingsPreviewHost.ActualWidth + " count=" + previews.Count +
                " sizes=" + string.Join(",", previews.Select(preview => $"{preview.Name}:{preview.ActualWidth}x{preview.ActualHeight}")) + "]");
            await SaveVisualAsync(RootShell, Path.Combine(output, page + "-preview.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        }
        // 窄窗口必须回退到页内预览：右侧区域放不下时，预览仍要出现在“标题大小”上方。
        double physicalPerDip = Math.Max(1, _appWindow.Size.Width / Math.Max(1, RootShell.ActualWidth));
        _appWindow.Resize(new Windows.Graphics.SizeInt32((int)(800 * physicalPerDip), (int)(600 * physicalPerDip)));
        await Task.Delay(200);
        ShowSettingsPage("AppearanceTile");
        await NextLayoutAsync();
        check(SettingsPreviewHost.Visibility == Visibility.Collapsed &&
            FindVisuals<Border>(_settingsPages["AppearanceTile"].Content).Any(border =>
                border.Name == "TileAppearancePreview" && border.ActualWidth > 0 && border.ActualHeight > 0),
            "narrow settings page falls back to the inline tile preview");
        await SaveVisualAsync(RootShell, Path.Combine(output, "tile-preview-inline.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(area.WorkArea.Width, area.WorkArea.Height));
        await Task.Delay(200);
        await NextLayoutAsync();
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
        check(originalEditors.Length == 3, "fixed preview contains the Chinese opening line and both Latin lines");
        // 第一行中文排在原本的两行之前，原本两行整体后移。
        originalEditors[0].Document.GetText(TextGetOptions.None, out string sampleFirstLine);
        var sampleFirstFormat = originalEditors[0].Document.GetRange(0, 1).CharacterFormat;
        check(sampleFirstFormat.Bold == FormatEffect.On && sampleFirstFormat.Italic == FormatEffect.Off &&
            sampleFirstFormat.Underline == UnderlineType.None &&
            sampleFirstLine.TrimEnd('\r', '\n') == "当有一天你不再纠结于答案 当我们又重逢于天涯或沧海",
            "sample opens with the bold Chinese line");
        originalEditors[1].Document.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out string sampleText);
        originalEditors[2].Document.GetText(Microsoft.UI.Text.TextGetOptions.FormatRtf, out string sampleLink);
        var sampleTextFormat = originalEditors[1].Document.GetRange(0, 1).CharacterFormat;
        originalEditors[1].Document.GetText(TextGetOptions.None, out string samplePlainText);
        check(sampleTextFormat.Bold == FormatEffect.On && sampleTextFormat.Italic == FormatEffect.Off &&
            sampleTextFormat.Underline == UnderlineType.None &&
            samplePlainText.TrimEnd('\r', '\n') == "Through hardships to the stars." && sampleText.Length > 0,
            "sample text is bold, upright and not underlined");
        check(sampleTextFormat.Name.Contains("HarmonyOS"), "sample text uses the app default font");
        var sampleColor = sampleTextFormat.ForegroundColor;
        check(BoardTheme.IsLight ? sampleColor.R == 0 : sampleColor.R == 247, "sample text follows theme foreground color");
        // RichEdit 会给 HYPERLINK 域强制加下划线，示例正文改成普通文本才符合“无下划线”；这里守住这个前提。
        originalEditors[2].Document.GetText(TextGetOptions.None, out string sampleLinkText);
        check(sampleLinkText.TrimEnd('\r', '\n') == "Per ardua ad astra." && !sampleLink.Contains("HYPERLINK"),
            "sample keeps the last line as plain text without a hyperlink underline");
        var sampleLinkFormat = originalEditors[2].Document.GetRange(0, 1).CharacterFormat;
        check(sampleLinkFormat.Underline == UnderlineType.None && sampleLinkFormat.ForegroundColor.Equals(sampleColor),
            "sample last line is white without underline");
        // 高光色使用当前色系的预设色，和磁贴编辑器高光色卡里的颜色一致。
        int sampleAstraStart = "Per ardua ad ".Length;
        var sampleHighlight = originalEditors[2].Document.GetRange(sampleAstraStart, sampleAstraStart + "astra".Length).CharacterFormat.BackgroundColor;
        check(sampleHighlight.Equals(ViewModels.MainViewModel.BrushFromHex(ColorPalette.Resolve("#F472B6")).Color),
            "sample highlight follows the current palette");
        var sampleSubject = (SubjectBoard)originalTile.DataContext;
        check(sampleSubject.AccentBrush.Color.Equals(ViewModels.MainViewModel.BrushFromHex(
                ColorPalette.ResolveAccent("#FBBF24", false, ColorPalette.IsMacaron)).Color),
            "tile appearance sample uses the yellow theme color");
        // 三行正文必须完整落在示例磁贴里，不能靠磁贴内部的滚动条才看得到。
        check(originalTile.MeasureContentSize().Height <= sampleSubject.TileHeight &&
            FindVisuals<ScrollViewer>(originalTile).Where(viewer => viewer.Content is StackPanel).All(viewer => viewer.ScrollableHeight <= 0),
            "three sample lines fit the fixed preview tile without scrolling [content=" +
            originalTile.MeasureContentSize().Height.ToString("0.#") + " tile=" + sampleSubject.TileHeight +
            " scrollers=" + string.Join("|", FindVisuals<ScrollViewer>(originalTile)
                .Select(viewer => $"{viewer.Content?.GetType().Name}:{viewer.ScrollableHeight:0.#}")) + "]");
        var backdrop = FindVisuals<Image>(_appearancePreviews["Tile"]).First();
        check(backdrop.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap && bitmap.PixelWidth > 0, "tile preview background asset loads");
        check(backdrop.Stretch == Stretch.UniformToFill, "tile preview background fills the whole preview area");
        var lightOverlay = FindVisuals<Border>(_appearancePreviews["Tile"]).Single(border => border.Name == "TilePreviewLightOverlay");
        check(Math.Abs(lightOverlay.Opacity - (BoardTheme.IsLight ? .55 : 0)) < .01, "tile preview background brightens in light mode");
        // 切到浅色模式后底图必须明显提亮，切回深色恢复原图。
        ElementTheme originalRequestedTheme = RootShell.RequestedTheme;
        RootShell.RequestedTheme = ElementTheme.Light;
        await Task.Delay(150);
        await NextLayoutAsync();
        check(FindVisuals<Border>(_appearancePreviews["Tile"]).Single(border => border.Name == "TilePreviewLightOverlay").Opacity > .5,
            "tile preview background is much brighter in light mode");
        RootShell.RequestedTheme = originalRequestedTheme;
        await Task.Delay(150);
        await NextLayoutAsync();
        _settings.TileBackground.Glass = true;
        _settings.TileBackground.Blur = 15;
        _settings.TileBackground.Color = "";
        _settings.TileTitleSize = 29;
        ApplyExtendedSettings(); await NextLayoutAsync();
        await SaveVisualAsync(_appearancePreviews["Tile"], Path.Combine(output, "tile-sample-glass.png"),
            Math.Max(1, (int)_appearancePreviews["Tile"].ActualWidth), Math.Max(1, (int)_appearancePreviews["Tile"].ActualHeight));
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

    /// <summary>自动填充：两个设置页的控件、拼音候选浮层、采纳上色与作业补全插入。</summary>
    private async Task VerifyAutofillAsync(string output, Action<bool, string> check)
    {
        FinishEditing(); ShowSettings();
        ShowSettingsPage("AutofillSubject");
        await NextLayoutAsync();
        check(_settings.Autofill.Subject.Subjects.Count >= 16 &&
            FindVisuals<ToggleSwitch>(_settingsPages["AutofillSubject"].Content).Any(toggle => Equals(toggle.Header, "学科补全")) &&
            FindVisuals<ComboBox>(_settingsPages["AutofillSubject"].Content).Any(combo => Equals(combo.Header, "匹配程度")),
            "subject autofill page exposes the master switch, match level and subject list");
        check(FindVisuals<ToggleSwitch>(_settingsPages["AutofillSubject"].Content).Count() == _settings.Autofill.Subject.Subjects.Count + 1,
            "every subject row keeps its own enable switch");
        check(FindVisuals<Button>(_settingsPages["AutofillSubject"].Content).Any(button => Equals(button.Content, "刷新列表")),
            "subject page offers a list refresh button");
        await SaveVisualAsync(RootShell, Path.Combine(output, "autofill-subject.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);

        // 行内色卡必须显示当前色系下的学科颜色，切换色系后立即重算，而不是停留在构建时的颜色。
        Button? SubjectColorButton(string name) => FindVisuals<Grid>(_settingsPages["AutofillSubject"].Content)
            .Where(row => FindVisuals<TextBlock>(row).Any(text => text.Text == name))
            .Select(row => FindVisuals<Button>(row).FirstOrDefault(button =>
                FindVisuals<TextBlock>(button).Any(text => text.Text is "默认颜色" or "随机颜色")))
            .FirstOrDefault(button => button is not null);
        Border SubjectSwatch(string name) => FindVisuals<Border>(SubjectColorButton(name)!)
            .First(border => border.Width == 18 && border.Height == 18);
        SubjectSuggestion math = _settings.Autofill.Subject.Subjects.First(item => item.Name == "数学");
        int paletteIndex = PaletteComboBox.SelectedIndex;
        check(SubjectSwatch("数学").Background is SolidColorBrush vividSwatch &&
            vividSwatch.Color.Equals(ViewModels.MainViewModel.BrushFromHex(ColorPalette.Resolve(math.Color)).Color),
            "subject color swatch shows the subject color of the current palette");
        PaletteComboBox.SelectedIndex = paletteIndex == 1 ? 0 : 1;
        await NextLayoutAsync();
        check(SubjectSwatch("数学").Background is SolidColorBrush macaronSwatch &&
            macaronSwatch.Color.Equals(ViewModels.MainViewModel.BrushFromHex(ColorPalette.Resolve(math.Color)).Color),
            "switching the palette refreshes the subject color swatch");
        PaletteComboBox.SelectedIndex = paletteIndex;
        await NextLayoutAsync();

        // 「自定义颜色」右侧的随机按钮：开启后该学科改用随机预设色，色卡显示预设色渐变。
        SubjectSuggestion physics = _settings.Autofill.Subject.Subjects.First(item => item.Name == "物理");
        Button physicsColor = SubjectColorButton("物理")!;
        Flyout physicsColors = (Flyout)physicsColor.Flyout;
        physicsColors.ShowAt(physicsColor);
        await NextLayoutAsync();
        StackPanel flyoutContent = (StackPanel)physicsColors.Content;
        List<Button> flyoutButtons = FindVisuals<Button>(flyoutContent).ToList();
        check(flyoutButtons.Any(button => Equals(button.Content, "自定义颜色")),
            "subject color flyout keeps the custom color entry");
        ToggleButton randomToggle = FindVisuals<ToggleButton>(flyoutContent).Single(toggle => Equals(toggle.Content, "随机"));
        check(!randomToggle.IsChecked.GetValueOrDefault(), "random colors stay off until the button is used");
        randomToggle.IsChecked = true;
        await NextLayoutAsync();
        check(physics.RandomColor, "random button switches the subject to random colors");
        check(SubjectSwatch("物理").Background is LinearGradientBrush, "random subject swatch previews the preset colors");
        randomToggle.IsChecked = false;
        physicsColors.Hide();
        await NextLayoutAsync();
        check(!physics.RandomColor && SubjectSwatch("物理").Background is SolidColorBrush,
            "turning the random button off restores the fixed subject color");

        ShowSettingsPage("AutofillHomework");
        await NextLayoutAsync();
        check(FindVisuals<ToggleSwitch>(_settingsPages["AutofillHomework"].Content).Any(toggle => Equals(toggle.Header, "作业补全")) &&
            FindVisuals<ComboBox>(_settingsPages["AutofillHomework"].Content).Any(combo => Equals(combo.Header, "记录隔离")) &&
            FindVisuals<ComboBox>(_settingsPages["AutofillHomework"].Content).Any(combo => Equals(combo.Header, "自动记录阈值")),
            "homework autofill page exposes the switch, recording threshold and isolation");
        await SaveVisualAsync(RootShell, Path.Combine(output, "autofill-homework.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);

        // 标题输入拼音 → 浮层候选 → 采纳后写入标题并套用学科颜色。
        _settings.Autofill.Subject.Enabled = true;
        _settings.Autofill.Subject.MatchLevel = "Normal";
        _settings.Autofill.Homework.Enabled = true;
        ShowBoard(); EnterEditing(); await NextLayoutAsync();
        SubjectTileControl tile = BoardCanvas.Children.OfType<SubjectTileControl>().First();
        TextBox titleEditor = FindVisuals<TextBox>(tile).First();
        titleEditor.Focus(FocusState.Programmatic);
        titleEditor.Text = string.Empty;
        titleEditor.SelectionStart = 0;
        titleEditor.SelectedText = "yuwe";
        // 程序化插入后光标停在选区起点，这里收拢到末尾，等同真实输入完最后一个字母。
        titleEditor.SelectionStart = titleEditor.Text.Length;
        titleEditor.SelectionLength = 0;
        await NextLayoutAsync();
        check(_autofillPopup.IsOpen,
            $"typing pinyin in a tile title opens the completion popup (text='{titleEditor.Text}', caret={titleEditor.SelectionStart}, enabled={_settings.Autofill.Subject.Enabled}, subjects={_settings.Autofill.Subject.Subjects.Count}, matches={_autofill.MatchSubjects(AutofillInputText.CurrentWord(titleEditor.Text, titleEditor.SelectionStart)).Count})");
        check(_autofillPopup.ChooseHighlighted(), "completion popup accepts the highlighted candidate");
        await NextLayoutAsync();
        SubjectSuggestion suggestion = _settings.Autofill.Subject.Subjects.First(item => item.Name == "语文");
        SolidColorBrush expectedAccent = ViewModels.MainViewModel.BrushFromHex(ColorPalette.ResolveAccent(suggestion.Color, true, ColorPalette.IsMacaron));
        check(ViewModel.Subjects.First().Name == "语文" && ViewModel.Subjects.First().AccentHex == suggestion.Color,
            "accepting a subject candidate writes the title and applies the subject color");
        check(tile.Children.OfType<Border>().Any(border => border.BorderBrush is SolidColorBrush brush && brush.Color.Equals(expectedAccent.Color)),
            "tile border follows the completed subject color");
        // 模拟输入法在补全之后又把拼音上屏一次：观察窗口必须把候选写回去，不能要求再点一次。
        titleEditor.Text = "yuwe";
        titleEditor.SelectionStart = titleEditor.Text.Length;
        titleEditor.SelectionLength = 0;
        await Task.Delay(500);
        check(titleEditor.Text == "语文" && ViewModel.Subjects.First().Name == "语文",
            $"subject completion survives an input method re-commit (text='{titleEditor.Text}', attached={titleEditor.XamlRoot is not null}, name='{ViewModel.Subjects.First().Name}', tiles={BoardCanvas.Children.OfType<SubjectTileControl>().Count()})");

        // 随机配色：同一学科再次采纳时套用的颜色一定来自预设色板。
        // 先等上一个采纳的输入法观察窗口结束，否则它会把这里的输入写回成上一次的结果。
        await Task.Delay(1600);
        suggestion.RandomColor = true;
        titleEditor.Text = string.Empty;
        titleEditor.SelectionStart = 0;
        titleEditor.SelectedText = "yuwe";
        titleEditor.SelectionStart = titleEditor.Text.Length;
        titleEditor.SelectionLength = 0;
        await NextLayoutAsync();
        check(_autofillPopup.ChooseHighlighted(), "completion popup accepts the highlighted candidate in random color mode");
        await NextLayoutAsync();
        check(ColorPalette.Presets.Any(preset => preset.Equals(ViewModel.Subjects.First().AccentHex, StringComparison.OrdinalIgnoreCase)),
            $"random subject colors apply one of the presets (applied={ViewModel.Subjects.First().AccentHex})");
        suggestion.RandomColor = false;

        // 作业正文补全：手动添加的类型立即参与候选，采纳后只替换光标左侧正在输入的一段。
        _settings.Autofill.Homework.Items.Add(new HomeworkSuggestion
        {
            Text = "同步练习册", Source = AutofillService.ManualSource, IsGlobal = true, Promoted = true,
            Count = 3, FirstSeenAt = DateTime.Now, LastSeenAt = DateTime.Now
        });
        RichEditBox editor = FindVisuals<RichEditBox>(tile).First();
        editor.Focus(FocusState.Programmatic);
        editor.Document.SetText(TextSetOptions.None, "完成 ");
        editor.Document.Selection.SetRange(3, 3);
        editor.Document.Selection.TypeText("同步");
        // 光标移动会重新计算候选，这里固定一次位置让弹窗状态可预期。
        editor.Document.Selection.SetRange(5, 5);
        await NextLayoutAsync();
        check(_autofillPopup.IsOpen, "typing a recorded homework name opens the completion popup");
        check(_autofillPopup.ChooseHighlighted(), "homework completion popup accepts the highlighted candidate");
        await NextLayoutAsync();
        editor.Document.GetText(TextGetOptions.None, out string completed);
        check(completed.Replace("\r", string.Empty) == "完成 同步练习册", "accepted homework candidate replaces only the word being typed");
        editor.Document.SetText(TextSetOptions.None, "完成 同步");
        editor.Document.Selection.SetRange(5, 5);
        await Task.Delay(700);
        editor.Document.GetText(TextGetOptions.None, out string restored);
        check(restored.Replace("\r", string.Empty) == "完成 同步练习册",
            $"homework completion survives an input method re-commit (restored='{restored.Replace("\r", "|")}')");

        // 默认标题聚焦后清空，并直接给出全部推荐学科。
        SubjectBoard fresh = ViewModel.AddSubject("新科目");
        AddTile(fresh);
        await NextLayoutAsync();
        SubjectTileControl freshTile = FindTile(fresh)!;
        TextBox freshTitle = FindVisuals<TextBox>(freshTile).First();
        freshTitle.Focus(FocusState.Programmatic);
        await NextLayoutAsync();
        check(freshTitle.Text.Length == 0 && _autofillPopup.IsOpen && _autofillPopup.Count > 1,
            "focusing the default title clears it and lists the recommended subjects");

        // 候选再长也一次只露出 5 行，其余在浮层内部滚动；浮层同时放宽到能一行放下常见学科名。
        Border? recommendationFrame = FindVisuals<Popup>(RootShell)
            .Where(popup => popup.IsOpen)
            .Select(popup => popup.Child)
            .OfType<Border>()
            .FirstOrDefault(border => border.Child is ScrollViewer);
        ScrollViewer? recommendationScroll = recommendationFrame?.Child as ScrollViewer;
        // 浮层内容是 Popup.Child，不挂在窗口可视树上，必须从浮层自身往下找候选行。
        List<Border> recommendationRows = recommendationFrame is null
            ? []
            : FindVisuals<Border>(recommendationFrame).Where(border => border.Tag is AutofillEntry).ToList();
        check(recommendationScroll is not null &&
            recommendationRows.Count > AutofillPopup.VisibleRowLimit &&
            recommendationScroll.ScrollableHeight > 0 &&
            recommendationScroll.ActualHeight < recommendationRows.Sum(row => row.ActualHeight) &&
            recommendationFrame!.ActualWidth >= AutofillPopup.MinimumWidth - 1,
            $"completion popup shows at most {AutofillPopup.VisibleRowLimit} rows at once and stays wide enough " +
            $"(rows={recommendationRows.Count}, viewport={recommendationScroll?.ActualHeight:0.#}, " +
            $"scrollable={recommendationScroll?.ScrollableHeight:0.#}, width={recommendationFrame?.ActualWidth:0.#})");

        // 候选浮层是代码创建的控件，必须跟着主题换色：浅色模式曾保留深色底又把文字换成黑色，完全看不清。
        Border? AutofillFrame() => FindVisuals<Popup>(RootShell)
            .Where(popup => popup.IsOpen)
            .Select(popup => popup.Child)
            .OfType<Border>()
            .FirstOrDefault(border => border.Child is ScrollViewer);
        async Task CheckPopupThemeAsync(ElementTheme theme, Windows.UI.Color background, Windows.UI.Color text)
        {
            RootShell.RequestedTheme = theme;
            await NextLayoutAsync();
            TextBox editor = FindVisuals<TextBox>(BoardCanvas.Children.OfType<SubjectTileControl>().First()).First();
            editor.Focus(FocusState.Programmatic);
            editor.Text = string.Empty;
            editor.SelectedText = "sh";
            editor.SelectionStart = editor.Text.Length;
            editor.SelectionLength = 0;
            await NextLayoutAsync();
            Border frame = AutofillFrame() ?? throw new Exception("completion popup is not open while checking theme colors");
            // 只取候选行自身的文字：浮层内部的滚动条模板也带 TextBlock，不参与配色。
            List<Windows.UI.Color> rows = FindVisuals<Border>(frame)
                .Select(border => border.Child)
                .OfType<Grid>()
                .SelectMany(grid => grid.Children.OfType<TextBlock>())
                .Select(block => (block.Foreground as SolidColorBrush)?.Color ?? Windows.UI.Color.FromArgb(0, 0, 0, 0))
                .ToList();
            check((frame.Background as SolidColorBrush)?.Color == background && rows.Count > 0 && rows.All(row => row == text),
                $"completion popup follows the {theme} theme (background={(frame.Background as SolidColorBrush)?.Color}, rows={rows.Count}, colors={string.Join("/", rows)})");
        }
        ElementTheme popupTheme = RootShell.ActualTheme;
        await CheckPopupThemeAsync(ElementTheme.Light, Windows.UI.Color.FromArgb(255, 255, 255, 255), Windows.UI.Color.FromArgb(255, 0, 0, 0));
        await CheckPopupThemeAsync(ElementTheme.Dark, Windows.UI.Color.FromArgb(255, 37, 37, 50), Windows.UI.Color.FromArgb(255, 240, 240, 245));
        RootShell.RequestedTheme = popupTheme;
        await NextLayoutAsync();

        ShowSettingsPage("AutofillHomework");
        await NextLayoutAsync();
        List<Button> homeworkButtons = FindVisuals<Button>(_settingsPages["AutofillHomework"].Content).ToList();
        check(homeworkButtons.Any(button => Equals(button.Content, "刷新列表")), "homework page offers a list refresh button");
        check(homeworkButtons.Any(button => Equals(button.Content, "编辑")) && homeworkButtons.Any(button => Equals(button.Content, "屏蔽")),
            "recorded homework rows expose edit and block buttons");
        // 行内按钮必须各占一列，不能因为列定义缺失而叠在一起。
        List<Rect> actionBounds = homeworkButtons
            .Where(button => button.Content is "生效范围" or "编辑" or "屏蔽" or "删除")
            .Select(button => button.TransformToVisual(RootShell).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight)))
            .ToList();
        bool overlapping = false;
        for (int first = 0; first < actionBounds.Count; first++)
            for (int second = first + 1; second < actionBounds.Count; second++)
                overlapping |= actionBounds[first].X < actionBounds[second].X + actionBounds[second].Width &&
                    actionBounds[second].X < actionBounds[first].X + actionBounds[first].Width &&
                    actionBounds[first].Y < actionBounds[second].Y + actionBounds[second].Height &&
                    actionBounds[second].Y < actionBounds[first].Y + actionBounds[first].Height;
        check(actionBounds.Count >= 4 && !overlapping, "recorded homework row buttons keep separate columns");
        await SaveVisualAsync(RootShell, Path.Combine(output, "autofill-homework-list.png"), (int)RootShell.ActualWidth, (int)RootShell.ActualHeight);

        _settings.Autofill.Subject.Enabled = false;
        _settings.Autofill.Homework.Enabled = false;
        FinishEditing();
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
