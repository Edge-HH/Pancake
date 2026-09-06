// 仅在 EnableUiVerification=true 时编译；在隔离的构建目录运行，不访问用户正常运行目录的数据。
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
                Check(ProjectCommands.Visibility == Visibility.Visible && ProjectPicker.Content.ToString() == project.Name, "project commands visible in editor");
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
                var tile = BoardCanvas.Children.OfType<SubjectTileControl>().First();
                Point before = chinese.InkStrokes[0].Points[1];
                chinese.TileWidth = 300; chinese.TileHeight = 180; tile.ApplyModelLayout(); await NextLayoutAsync();
                Check(chinese.InkStrokes[0].Points[1] == before && chinese.InkStrokes[0].Thickness == 5, "resizing tile does not change stored ink");
                chinese.TileWidth = 420; chinese.TileHeight = 320; tile.ApplyModelLayout();
                GlobalPenButton.IsChecked = true; await NextLayoutAsync();
                Check(GlobalInkToolbar.Visibility == Visibility.Visible && !AddSubjectButton.IsEnabled, "global ink mode owns editing input");
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
